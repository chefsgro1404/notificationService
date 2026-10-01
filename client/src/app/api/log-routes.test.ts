// @vitest-environment node
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { sessionCookie, stubAuthEnv } from "@/test/auth";
import { GET as getAudioLogs } from "./audio-logs/route";
import { PATCH as patchAudioStatus } from "./audio-logs/[id]/status/route";
import { GET as getNotificationLogs } from "./notification-logs/route";

const fetchMock = vi.fn<typeof fetch>();
let cookie = "";

beforeAll(async () => {
  cookie = await sessionCookie();
});

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  stubAuthEnv();
  vi.stubEnv("FUNCTIONS_BASE_URL", "http://functions.test");
  vi.stubEnv("FUNCTIONS_KEY", "");
  vi.spyOn(console, "error").mockImplementation(() => {});
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(Response.json({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }));
});

function patch(id: string, body: unknown) {
  return patchAudioStatus(
    new Request(`http://localhost/api/audio-logs/${id}/status`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json", cookie },
      body: typeof body === "string" ? body : JSON.stringify(body),
    }),
    { params: Promise.resolve({ id }) },
  );
}

describe("GET /api/notification-logs", () => {
  it("forwards only the supported filters", async () => {
    const response = await getNotificationLogs(
      new Request("http://localhost/api/notification-logs?channel=Email&status=Failed&recipient=a&from=x&to=y&page=2&pageSize=50&code=secret", {
        headers: { cookie },
      }),
      undefined,
    );

    expect(response.status).toBe(200);
    expect(fetchMock.mock.calls[0][0]).toBe(
      "http://functions.test/api/notification-logs?channel=Email&status=Failed&recipient=a&from=x&to=y&page=2&pageSize=50",
    );
  });
});

describe("GET /api/audio-logs", () => {
  it("forwards to the text-to-speech log function", async () => {
    await getAudioLogs(new Request("http://localhost/api/audio-logs?status=All&search=hi&page=3&other=1", { headers: { cookie } }), undefined);

    expect(fetchMock.mock.calls[0][0]).toBe("http://functions.test/api/tts/logs?status=All&search=hi&page=3");
  });
});

describe("PATCH /api/audio-logs/[id]/status", () => {
  it("forwards a valid status change", async () => {
    fetchMock.mockResolvedValue(Response.json({ id: 5, status: "Inactive" }));

    const response = await patch("5", { status: "Inactive" });

    expect(response.status).toBe(200);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://functions.test/api/tts/logs/5/status");
    expect(init?.method).toBe("PATCH");
    expect(init?.body).toBe(JSON.stringify({ status: "Inactive" }));
  });

  it.each(["0", "-1", "abc", "1.5", "12345678901"])("rejects invalid id %s", async (id) => {
    const response = await patch(id, { status: "Active" });

    expect(response.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([{ status: "Archived" }, { status: "AudioNotGenerated" }, { status: 1 }, {}, null, "{bad"])("rejects invalid body %j", async (body) => {
    const response = await patch("5", body);

    expect(response.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("passes a backend 409 (change not allowed) through", async () => {
    fetchMock.mockResolvedValue(
      Response.json({ error: "Audio 5 was never generated, so it can only be deleted." }, { status: 409 }),
    );

    const response = await patch("5", { status: "Active" });

    expect(response.status).toBe(409);
    expect(await response.json()).toEqual({ error: "Audio 5 was never generated, so it can only be deleted." });
  });

  it("passes a backend 404 through", async () => {
    fetchMock.mockResolvedValue(Response.json({ error: "Audio 5 was not found." }, { status: 404 }));

    const response = await patch("5", { status: "Deleted" });

    expect(response.status).toBe(404);
    expect(await response.json()).toEqual({ error: "Audio 5 was not found." });
  });
});
