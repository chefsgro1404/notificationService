import { withAdmin } from "@/lib/auth/guard";
import { errorResponse, proxyJson } from "@/lib/backend";

const POSITIVE_ID = /^[1-9]\d{0,9}$/;

/** Proxy: PUT /api/audio-categories/{id}/audio { audioId } → Azure Function PUT /api/audio-categories/{id}/audio. */
export const PUT = withAdmin(async (
  request: Request,
  context: { params: Promise<{ id: string }> },
): Promise<Response> => {
  const { id } = await context.params;

  if (!POSITIVE_ID.test(id)) {
    return errorResponse(400, "Category id must be a positive whole number.");
  }

  const body: unknown = await request.json().catch(() => null);
  const audioId = typeof body === "object" && body !== null ? (body as { audioId?: unknown }).audioId : undefined;

  if (typeof audioId !== "number" || !Number.isInteger(audioId) || audioId < 1) {
    return errorResponse(400, "Body must be JSON like { \"audioId\": 12 }.");
  }

  return proxyJson(`/api/audio-categories/${id}/audio`, { method: "PUT", body: { audioId } });
});
