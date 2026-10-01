/**
 * Shared rules for the Text to WAV feature. The limits mirror the backend
 * (NotificationService.Application.Validation.SpeechText): characters are counted as
 * Unicode code points, so an emoji counts as one character on both sides.
 */

/** Maximum number of characters that can be converted in one request. */
export const MAX_CHARACTERS = 150;

/** Successful response returned by POST /api/tts (and by the Azure Function behind it). */
export interface GeneratedAudio {
  /** Sequential integer id (key in the audio log). */
  id: number;
  /** Notification id; the file is stored as "T2A/{notificationId}.wav". */
  notificationId: string;
  blobName: string;
  audioUrl: string;
  expiresAtUtc: string;
  contentType: string;
  sizeBytes: number;
  characterCount: number;
}

/** Error response returned by POST /api/tts. */
export interface ApiError {
  error: string;
}

/** Counts characters as Unicode code points. */
export function countCharacters(text: string): number {
  return Array.from(text).length;
}

/** Cuts text down to at most {@link MAX_CHARACTERS} code points without splitting a surrogate pair. */
export function limitCharacters(text: string): string {
  const characters = Array.from(text);
  return characters.length > MAX_CHARACTERS
    ? characters.slice(0, MAX_CHARACTERS).join("")
    : text;
}

/** Returns a user-facing error message, or null when the text can be submitted. */
export function validateText(text: string): string | null {
  const trimmed = text.trim();

  if (trimmed.length === 0) {
    return "Please enter some text.";
  }

  const length = countCharacters(trimmed);

  return length > MAX_CHARACTERS
    ? `Text must be ${MAX_CHARACTERS} characters or fewer (currently ${length}).`
    : null;
}

/** Type guard for a successful response body. Only http(s) audio URLs are accepted. */
export function isGeneratedAudio(value: unknown): value is GeneratedAudio {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;

  if (typeof candidate.audioUrl !== "string" || typeof candidate.expiresAtUtc !== "string") {
    return false;
  }

  try {
    const { protocol } = new URL(candidate.audioUrl);
    return protocol === "https:" || protocol === "http:";
  } catch {
    return false;
  }
}
