/**
 * Types and constants shared by the Notification Log and Audio Log pages. They mirror the backend
 * models (PagedResult, NotificationLogItem, TextToSpeechLogItem).
 */

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  truncated: boolean;
}

export interface NotificationLogItem {
  id: string;
  channel: string;
  recipient: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  providerMessageId: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  retryCount: number;
  hasAttachment: boolean;
}

export interface AudioLogItem {
  id: number;
  /** Notification id: the table row key and the file name ("T2A/{notificationId}.wav"). */
  notificationId: string;
  text: string;
  blobName: string;
  contentType: string;
  sizeBytes: number;
  characterCount: number;
  status: AudioStatus;
  errorMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  audioUrl: string | null;
  audioUrlExpiresAtUtc: string | null;
}

export const NOTIFICATION_CHANNELS = ["Email", "Sms", "Telegram"] as const;

/** Notification statuses in lifecycle order (backend NotificationStatus enum). */
export const NOTIFICATION_STATUSES = ["Accepted", "Queued", "Processing", "Sent", "Retrying", "Failed"] as const;

export const AUDIO_STATUSES = ["AudioNotGenerated", "Active", "Inactive", "Deleted"] as const;
export type AudioStatus = (typeof AUDIO_STATUSES)[number];

/** Statuses a user can choose; AudioNotGenerated is only set by the backend. */
export const AUDIO_TARGET_STATUSES = ["Active", "Inactive", "Deleted"] as const;

/** Display names for audio statuses. */
export const AUDIO_STATUS_LABELS: Record<AudioStatus, string> = {
  AudioNotGenerated: "Audio not generated",
  Active: "Active",
  Inactive: "Inactive",
  Deleted: "Deleted",
};

/** Audio log status filter values; "" (default) means everything except deleted. */
export const AUDIO_STATUS_FILTERS = [
  { value: "", label: "All except deleted" },
  { value: "AudioNotGenerated", label: "Audio not generated" },
  { value: "Active", label: "Active" },
  { value: "Inactive", label: "Inactive" },
  { value: "Deleted", label: "Deleted" },
  { value: "All", label: "All" },
] as const;

export const PAGE_SIZES = [10, 20, 50, 100] as const;

/** Lower-case CSS modifier for a status ("Sent" → "sent"); unknown statuses get "unknown". */
export function statusKey(status: string, known: readonly string[]): string {
  return known.includes(status) ? status.toLowerCase() : "unknown";
}

/** Converts a yyyy-mm-dd date input (local time) to the UTC instant of that day's start. */
export function startOfLocalDayUtc(date: string): string {
  return new Date(`${date}T00:00:00`).toISOString();
}

/** Converts a yyyy-mm-dd date input (local time) to the UTC instant of the next day's start, so the day is included. */
export function endOfLocalDayUtc(date: string): string {
  const next = new Date(`${date}T00:00:00`);
  next.setDate(next.getDate() + 1);
  return next.toISOString();
}

/** Formats an ISO timestamp for display in the viewer's locale. */
export function formatDateTime(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? iso
    : date.toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
}

/** Formats a byte count as B / KB / MB. */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Builds a query string from non-empty values. */
export function toQueryString(values: Record<string, string | number | undefined>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && `${value}`.trim() !== "") {
      params.set(key, `${value}`.trim());
    }
  }
  return params.toString();
}
