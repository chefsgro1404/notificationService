// @vitest-environment node
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { sessionCookie, stubAuthEnv } from "@/test/auth";
import { POST } from "./route";

let cookie = "";

beforeAll(async () => {
  cookie = await sessionCookie();
});

const audio = {
  id: 7,
  notificationId: "00000000-0000-0000-0000-000000000007",
  blobName: "tts/2026/09/30/7b1c9c0e.wav",
  audioUrl: "https://acct.blob.core.windows.net/notifications/tts/a.wav?sp=r&sig=x",
  expiresAtUtc: "2026-09-30T13:00:00+00:00",
  contentType: "audio/wav",
  sizeBytes: 1234,
  characterCount: 5,
};

function request(body: unknown): Request {
  return new Request("http://localhost/api/tts", {
    method: "POST",
    headers: { "Content-Type": "application/json", cookie },
    body: typeof body === "string" ? body : JSON.stringify(body),
  });
}

describe("POST /api/tts", () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    stubAuthEnv();
    vi.stubEnv("FUNCTIONS_BASE_URL", "http://functions.test/");
    vi.stubEnv("FUNCTIONS_KEY", "");
    vi.spyOn(console, "error").mockImplementation(() => {});
    fetchMock.mockReset();
  });

  it("forwards trimmed text to the function and returns the audio", async () => {
    fetchMock.mockResolvedValue(Response.json(audio));

    const response = await POST(request({ text: "  Hello  " }), undefined);

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual(audio);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://functions.test/api/tts");
    expect(init?.body).toBe(JSON.stringify({ text: "Hello" }));
    expect((init?.headers as Record<string, string>)["x-functions-key"]).toBeUndefined();
  });

  it("sends the function key when configured", async () => {
    vi.stubEnv("FUNCTIONS_KEY", "secret");
    fetchMock.mockResolvedValue(Response.json(audio));

    await POST(request({ text: "Hello" }), undefined);

    const [, init] = fetchMock.mock.calls[0];
    expect((init?.headers as Record<string, string>)["x-functions-key"]).toBe("secret");
  });

  it("rejects invalid JSON", async () => {
    const response = await POST(request("{not json"), undefined);

    expect(response.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([{}, { text: 42 }, { text: "   " }, null, { text: "a".repeat(151) }])(
    "rejects invalid text %j without calling the backend",
    async (body) => {
      const response = await POST(request(body), undefined);

      expect(response.status).toBe(400);
      expect(fetchMock).not.toHaveBeenCalled();
    },
  );

  it("returns 500 when the backend URL is not configured", async () => {
    vi.stubEnv("FUNCTIONS_BASE_URL", "");

    const response = await POST(request({ text: "Hello" }), undefined);

    expect(response.status).toBe(500);
  });

  it("passes backend validation messages through", async () => {
    fetchMock.mockResolvedValue(Response.json({ error: "Text is required." }, { status: 400 }));

    const response = await POST(request({ text: "Hello" }), undefined);

    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ error: "Text is required." });
  });

  it("uses a generic message for a backend 400 without an error field", async () => {
    fetchMock.mockResolvedValue(new Response("oops", { status: 400 }));

    const response = await POST(request({ text: "Hello" }), undefined);

    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ error: "The text could not be converted." });
  });

  it.each([
    () => Response.json({ error: "speech down" }, { status: 502 }),
    () => new Response("Unauthorized", { status: 401 }),
    () => Response.json({ audioUrl: "javascript:alert(1)", expiresAtUtc: "x" }),
  ])("returns 502 for backend failures or unexpected payloads", async (make) => {
    fetchMock.mockResolvedValue(make());

    const response = await POST(request({ text: "Hello" }), undefined);

    expect(response.status).toBe(502);
  });

  it("returns 502 when the backend is unreachable", async () => {
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));

    const response = await POST(request({ text: "Hello" }), undefined);

    expect(response.status).toBe(502);
  });

  it("returns 504 when the backend times out", async () => {
    fetchMock.mockRejectedValue(new DOMException("timed out", "TimeoutError"));

    const response = await POST(request({ text: "Hello" }), undefined);

    expect(response.status).toBe(504);
  });
});
