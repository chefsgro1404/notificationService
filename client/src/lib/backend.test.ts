// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from "vitest";
import { callBackend, errorMessageOf, pickQuery, proxyJson } from "./backend";

describe("backend helpers", () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    vi.stubEnv("FUNCTIONS_BASE_URL", "http://functions.test");
    vi.stubEnv("FUNCTIONS_KEY", "k");
    vi.spyOn(console, "error").mockImplementation(() => {});
    fetchMock.mockReset();
  });

  it("pickQuery keeps only allowed, non-empty keys", () => {
    const params = new URLSearchParams("status=Failed&evil=1&recipient=%20&page=2");

    expect(pickQuery(params, ["status", "recipient", "page"])).toBe("?status=Failed&page=2");
    expect(pickQuery(new URLSearchParams(), ["status"])).toBe("");
  });

  it("errorMessageOf reads error strings only", () => {
    expect(errorMessageOf({ error: "x" })).toBe("x");
    expect(errorMessageOf({ error: 1 })).toBeNull();
    expect(errorMessageOf(null)).toBeNull();
  });

  it("callBackend sends GET without a body or content type, with the function key", async () => {
    fetchMock.mockResolvedValue(Response.json({ ok: 1 }));

    const result = await callBackend("/api/x");

    expect(result).toEqual({ ok: true, status: 200, payload: { ok: 1 } });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://functions.test/api/x");
    expect(init?.body).toBeUndefined();
    expect(init?.headers).toEqual({ "x-functions-key": "k" });
  });

  it("callBackend returns a null payload for non-JSON bodies", async () => {
    fetchMock.mockResolvedValue(new Response("plain", { status: 200 }));

    expect(await callBackend("/api/x")).toEqual({ ok: true, status: 200, payload: null });
  });

  it("proxyJson passes through success, 400 and 404", async () => {
    fetchMock.mockResolvedValueOnce(Response.json({ items: [] }));
    fetchMock.mockResolvedValueOnce(Response.json({ error: "bad page" }, { status: 400 }));
    fetchMock.mockResolvedValueOnce(new Response("", { status: 404 }));

    const ok = await proxyJson("/api/a");
    const bad = await proxyJson("/api/b");
    const missing = await proxyJson("/api/c");

    expect(ok.status).toBe(200);
    expect(ok.headers.get("Cache-Control")).toBe("no-store");
    expect(await bad.json()).toEqual({ error: "bad page" });
    expect(missing.status).toBe(404);
    expect(await missing.json()).toEqual({ error: "The request could not be completed." });
  });

  it("proxyJson hides other backend failures behind 502", async () => {
    fetchMock.mockResolvedValue(Response.json({ error: "stack trace" }, { status: 500 }));

    const response = await proxyJson("/api/a");

    expect(response.status).toBe(502);
    expect(await response.json()).toEqual({ error: "The backend could not complete the request. Please try again." });
  });

  it("proxyJson returns configuration and network errors", async () => {
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));
    expect((await proxyJson("/api/a")).status).toBe(502);

    vi.stubEnv("FUNCTIONS_BASE_URL", "");
    expect((await proxyJson("/api/a")).status).toBe(500);
  });
});
