/**
 * Phone number rules for voice test calls. Matches the backend (VoiceRecipients.IsValid): E.164, a "+" and
 * 8–15 digits with no leading zero, for example +18005551234.
 */

const E164 = /^\+[1-9]\d{7,14}$/;

/** Removes spaces, dashes, dots and brackets people type for readability ("+43 688 647-48566"). */
export function normalizePhone(value: string): string {
  return value.replace(/[\s\-().]/g, "");
}

/** Whether the (normalized) number is valid E.164. */
export function isE164(value: string): boolean {
  return E164.test(value);
}
