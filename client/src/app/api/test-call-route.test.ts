// @vitest-environment node
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { callBackend } from "@/lib/backend";
import { isE164, normalizePhone } from "@/lib/phone";
import { GUEST_USER, sessionCookie, stubAuthEnv } from "@/test/auth";
import { POST as testCall } from "./audio-categories/[id]/test-call/route";

const fetchMock = vi.fn<typeof fetch>();
let cookie = "";

beforeAll(async () => {
  cookie = await sessionCookie();
});

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  stubAuthEnv();
  vi.stubEnv("FUNCTIONS_BASE_URL", "http://functions.test");
  vi.stubEnv("FUNCTIONS_KEY", "fkey");
  vi.spyOn(console, "error").mockImplementation(() => {});
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(
    Response.json({ notificationId: "91ab7746", status: "Accepted", recipients: 1, categoryId: 1 }, { status: 202 }),
  );
});

function call(id: string, body: unknown, withCookie = cookie) {
  return testCall(
    new Request(`http://localhost/api/audio-categories/${id}/test-call`, {
      method: "POST",
      headers: { "Content-Type": "application/json", cookie: withCookie },
      body: typeof body === "string" ? body : JSON.stringify(body),
    }),
    { params: Promise.resolve({ id }) },
  );
}

describe("phone helpers", () => {
  it("normalizes and validates E.164 numbers", () => {
    expect(normalizePhone(" +43 (688) 647-485.66 ")).toBe("+4368864748566");
    expect(isE164("+4368864748566")).toBe(true);
    expect(isE164("+18005551234")).toBe(true);
    expect(isE164("004368864748566")).toBe(false);
    expect(isE164("+0123456789")).toBe(false);
    expect(isE164("+1234567")).toBe(false);
    expect(isE164("+1234567890123456")).toBe(false);
  });
});

describe("callBackend with form data", () => {
  it("sends the form without forcing a JSON content type", async () => {
    const form = new FormData();
    form.set("a", "1");

    await callBackend("/api/x", { method: "POST", form });

    const [, init] = fetchMock.mock.calls[0];
    expect(init?.body).toBe(form);
    expect(init?.headers).toEqual({ "x-functions-key": "fkey" });
  });
});

describe("POST /api/audio-categories/[id]/test-call", () => {
  it("calls the existing voice-notifications function with recipient and categoryId", async () => {
    const response = await call("1", { recipient: "+43 688 64748566" });

    expect(response.status).toBe(202);
    expect(await response.json()).toMatchObject({ notificationId: "91ab7746", categoryId: 1 });

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://functions.test/api/voice-notifications");
    expect(init?.method).toBe("POST");
    const form = init?.body as FormData;
    expect(form.get("recipient")).toBe("+4368864748566");
    expect(form.get("categoryId")).toBe("1");
    expect(form.get("text")).toBe("Test call for audio category 1");
  });

  it("requires an admin", async () => {
    expect((await call("1", { recipient: "+18005551234" }, "")).status).toBe(401);
    expect((await call("1", { recipient: "+18005551234" }, await sessionCookie(GUEST_USER))).status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each(["0", "abc", "-1"])("rejects invalid category id %s", async (id) => {
    expect((await call(id, { recipient: "+18005551234" })).status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([{ recipient: "12345" }, { recipient: 18005551234 }, {}, null, "{bad", { recipient: "+1 800 555 1234, +1 800 555 9999" }])(
    "rejects invalid recipient %j",
    async (body) => {
      const response = await call("1", body);

      expect(response.status).toBe(400);
      expect(await response.json()).toEqual({
        error: "Enter one phone number in international format, for example +18005551234.",
      });
      expect(fetchMock).not.toHaveBeenCalled();
    },
  );

  it("passes the function's validation errors through", async () => {
    fetchMock.mockResolvedValue(Response.json({ error: "Category \"Sales\" has no linked audio." }, { status: 400 }));

    const response = await call("2", { recipient: "+18005551234" });

    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ error: "Category \"Sales\" has no linked audio." });
  });

  it("hides other backend failures behind 502", async () => {
    fetchMock.mockResolvedValue(Response.json({ error: "bus down" }, { status: 500 }));

    expect((await call("1", { recipient: "+18005551234" })).status).toBe(502);
  });
});
