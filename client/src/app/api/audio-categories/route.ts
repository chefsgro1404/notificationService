import { withAdmin } from "@/lib/auth/guard";
import { errorResponse, proxyJson } from "@/lib/backend";
import { validateCategoryName } from "@/lib/categories";

/** Proxy: GET /api/audio-categories → Azure Function GET /api/audio-categories. */
export const GET = withAdmin(async (): Promise<Response> => proxyJson("/api/audio-categories"));

/** Proxy: POST /api/audio-categories { name } → Azure Function POST /api/audio-categories. */
export const POST = withAdmin(async (request: Request): Promise<Response> => {
  const body: unknown = await request.json().catch(() => null);
  const name = typeof body === "object" && body !== null ? (body as { name?: unknown }).name : undefined;

  if (typeof name !== "string") {
    return errorResponse(400, "Body must be JSON like { \"name\": \"Support\" }.");
  }

  const error = validateCategoryName(name);

  if (error) {
    return errorResponse(400, error);
  }

  return proxyJson("/api/audio-categories", { method: "POST", body: { name: name.trim() } });
});
