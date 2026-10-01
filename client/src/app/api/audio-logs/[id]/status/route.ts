import { withAdmin } from "@/lib/auth/guard";
import { errorResponse, proxyJson } from "@/lib/backend";
import { AUDIO_TARGET_STATUSES } from "@/lib/logs";

/** Proxy: PATCH /api/audio-logs/{id}/status → Azure Function PATCH /api/tts/logs/{id}/status. */
export const PATCH = withAdmin(async (
  request: Request,
  context: { params: Promise<{ id: string }> },
): Promise<Response> => {
  const { id } = await context.params;

  if (!/^[1-9]\d{0,9}$/.test(id)) {
    return errorResponse(400, "Audio id must be a positive whole number.");
  }

  const body: unknown = await request.json().catch(() => null);
  const status = typeof body === "object" && body !== null ? (body as { status?: unknown }).status : undefined;

  if (typeof status !== "string" || !(AUDIO_TARGET_STATUSES as readonly string[]).includes(status)) {
    return errorResponse(400, `status must be one of: ${AUDIO_TARGET_STATUSES.join(", ")}.`);
  }

  return proxyJson(`/api/tts/logs/${id}/status`, { method: "PATCH", body: { status } });
});
