// @vitest-environment node
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { GUEST_USER, sessionCookie, stubAuthEnv } from "@/test/auth";
import { PUT as linkAudio } from "./audio-categories/[id]/audio/route";
import { GET as listCategories, POST as createCategory } from "./audio-categories/route";

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
  fetchMock.mockResolvedValue(Response.json([]));
});

function json(method: string, url: string, body: unknown, withCookie = cookie) {
  return new Request(url, {
    method,
    headers: { "Content-Type": "application/json", cookie: withCookie },
    body: typeof body === "string" ? body : JSON.stringify(body),
  });
}

describe("/api/audio-categories", () => {
  it("lists categories through the backend", async () => {
    const response = await listCategories(new Request("http://localhost/api/audio-categories", { headers: { cookie } }), undefined);

    expect(response.status).toBe(200);
    expect(fetchMock.mock.calls[0][0]).toBe("http://functions.test/api/audio-categories");
  });

  it("requires an admin", async () => {
    const guest = await sessionCookie(GUEST_USER);

    expect((await listCategories(new Request("http://localhost/api/audio-categories"), undefined)).status).toBe(401);
    expect((await createCategory(json("POST", "http://localhost/api/audio-categories", { name: "A" }, guest), undefined)).status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("creates a category with a trimmed name", async () => {
    fetchMock.mockResolvedValue(Response.json({ id: 1, name: "Support" }, { status: 201 }));

    const response = await createCategory(json("POST", "http://localhost/api/audio-categories", { name: " Support " }), undefined);

    expect(response.status).toBe(201);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://functions.test/api/audio-categories");
    expect(init?.body).toBe(JSON.stringify({ name: "Support" }));
  });

  it.each([{ name: 5 }, {}, "{bad", { name: "  " }, { name: "a".repeat(51) }])("rejects invalid body %j", async (body) => {
    const response = await createCategory(json("POST", "http://localhost/api/audio-categories", body), undefined);

    expect(response.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("passes a duplicate-name conflict through", async () => {
    fetchMock.mockResolvedValue(Response.json({ error: "A category named \"Support\" already exists." }, { status: 409 }));

    const response = await createCategory(json("POST", "http://localhost/api/audio-categories", { name: "Support" }), undefined);

    expect(response.status).toBe(409);
  });
});

describe("/api/audio-categories/[id]/audio", () => {
  const put = (id: string, body: unknown) =>
    linkAudio(json("PUT", `http://localhost/api/audio-categories/${id}/audio`, body), { params: Promise.resolve({ id }) });

  it("links audio through the backend", async () => {
    fetchMock.mockResolvedValue(Response.json({ id: 2, name: "Support", audioId: 7 }));

    const response = await put("2", { audioId: 7 });

    expect(response.status).toBe(200);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("http://functions.test/api/audio-categories/2/audio");
    expect(init?.method).toBe("PUT");
    expect(init?.body).toBe(JSON.stringify({ audioId: 7 }));
  });

  it.each(["0", "abc", "-1"])("rejects invalid category id %s", async (id) => {
    expect((await put(id, { audioId: 7 })).status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([{ audioId: "7" }, { audioId: 1.5 }, { audioId: 0 }, {}, null])("rejects invalid body %j", async (body) => {
    expect((await put("2", body)).status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("passes 404 and 409 through", async () => {
    fetchMock.mockResolvedValueOnce(Response.json({ error: "Category 2 was not found." }, { status: 404 }));
    fetchMock.mockResolvedValueOnce(Response.json({ error: "Only active audio can be linked." }, { status: 409 }));

    expect((await put("2", { audioId: 7 })).status).toBe(404);
    expect((await put("2", { audioId: 7 })).status).toBe(409);
  });
});
