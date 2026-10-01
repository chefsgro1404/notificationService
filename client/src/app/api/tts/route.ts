import { withAdmin } from "@/lib/auth/guard";
import { callBackend, errorMessageOf, errorResponse } from "@/lib/backend";
import { isGeneratedAudio, validateText } from "@/lib/text-to-wav";

/**
 * Server-side proxy to the Azure Function (POST {FUNCTIONS_BASE_URL}/api/tts).
 *
 * The browser only ever talks to this Next.js route, so the Azure Functions key
 * (FUNCTIONS_KEY) never reaches the client and no CORS configuration is needed.
 */
export const POST = withAdmin(async (request: Request): Promise<Response> => {
  let body: unknown;

  try {
    body = await request.json();
  } catch {
    return errorResponse(400, "Request body must be JSON, for example { \"text\": \"Hello\" }.");
  }

  const text =
    typeof body === "object" && body !== null && typeof (body as { text?: unknown }).text === "string"
      ? (body as { text: string }).text
      : "";

  const validationError = validateText(text);

  if (validationError) {
    return errorResponse(400, validationError);
  }

  const result = await callBackend("/api/tts", { method: "POST", body: { text: text.trim() } });

  if (!result.ok) {
    return result.response;
  }

  const { status, payload } = result;

  if (status === 400) {
    return errorResponse(400, errorMessageOf(payload) ?? "The text could not be converted.");
  }

  if (status < 200 || status >= 300 || !isGeneratedAudio(payload)) {
    console.error(`Text-to-speech backend returned ${status}.`, payload);
    return errorResponse(502, "The audio service could not generate audio. Please try again.");
  }

  return Response.json(payload, { headers: { "Cache-Control": "no-store" } });
});
