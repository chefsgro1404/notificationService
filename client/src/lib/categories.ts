/** Audio categories: a named slot linked to the audio that voice notifications in that category play. */

export interface AudioCategory {
  id: number;
  name: string;
  audioId: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

/** Maximum category name length (matches the backend). */
export const MAX_CATEGORY_NAME = 50;

/** Returns a user-facing error for a category name, or null when it is valid. */
export function validateCategoryName(name: string): string | null {
  const trimmed = name.trim();

  if (trimmed.length === 0) {
    return "Enter a category name.";
  }

  return trimmed.length > MAX_CATEGORY_NAME ? `Category name must be ${MAX_CATEGORY_NAME} characters or fewer.` : null;
}

/** Names of the categories linked to an audio id, sorted. */
export function categoriesForAudio(categories: AudioCategory[] | null, audioId: number): string[] {
  return (categories ?? [])
    .filter((category) => category.audioId === audioId)
    .map((category) => category.name)
    .sort((a, b) => a.localeCompare(b));
}
