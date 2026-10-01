import { withAdmin } from "@/lib/auth/guard";
import { errorResponse, proxyJson } from "@/lib/backend";
import { isE164, normalizePhone } from "@/lib/phone";

const POSITIVE_ID = /^[1-9]\d{0,9}$/;

/**
 * POST /api/audio-categories/{id}/test-call { recipient } — places a real voice call that plays the category's
 * linked audio, through the existing Azure Function POST /api/voice-notifications (form fields recipient and
 * categoryId). The call is audited like any other voice notification.
 */
export const POST = withAdmin(async (
  request: Request,
  context: { params: Promise<{ id: string }> },
): Promise<Response> => {
  const { id } = await context.params;

  if (!POSITIVE_ID.test(id)) {
    return errorResponse(400, "Category id must be a positive whole number.");
  }

  const body: unknown = await request.json().catch(() => null);
  const raw = typeof body === "object" && body !== null ? (body as { recipient?: unknown }).recipient : undefined;
  const recipient = typeof raw === "string" ? normalizePhone(raw) : "";

  if (!isE164(recipient)) {
    return errorResponse(400, "Enter one phone number in international format, for example +18005551234.");
  }

  const form = new FormData();
  form.set("recipient", recipient);
  form.set("categoryId", id);
  form.set("text", `Test call for audio category ${id}`);

  return proxyJson("/api/voice-notifications", { method: "POST", form });
});
