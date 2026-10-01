"use client";

import Link from "next/link";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import type { AudioCategory } from "@/lib/categories";
import styles from "./LinkCategoryDialog.module.css";

interface LinkCategoryDialogProps {
  audio: { id: number; text: string };
  categories: AudioCategory[] | null;
  loadError: string | null;
  onClose: () => void;
  onLinked: (category: AudioCategory) => void;
}

/**
 * Modal that lists the audio categories; the chosen category is linked to this audio
 * (PUT /api/audio-categories/{id}/audio), replacing the category's previous audio.
 */
export default function LinkCategoryDialog({ audio, categories, loadError, onClose, onLinked }: LinkCategoryDialogProps) {
  const [selected, setSelected] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const titleId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    dialogRef.current?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (selected === null) {
      return;
    }

    setSaving(true);
    setError(null);

    try {
      const response = await fetch(`/api/audio-categories/${selected}/audio`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ audioId: audio.id }),
      });

      const payload: unknown = await response.json().catch(() => null);

      if (response.ok) {
        onLinked(payload as AudioCategory);
        return;
      }

      setError(
        typeof payload === "object" && payload !== null && typeof (payload as { error?: unknown }).error === "string"
          ? (payload as { error: string }).error
          : "Could not link the category.",
      );
    } catch {
      setError("Network error. Check your connection and try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className={styles.backdrop} onClick={onClose}>
      <div
        ref={dialogRef}
        className={styles.dialog}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <form onSubmit={save}>
          <header className={styles.header}>
            <h2 id={titleId}>Link audio #{audio.id} to a category</h2>
            <p className={styles.text}>&ldquo;{audio.text}&rdquo;</p>
          </header>

          {loadError && (
            <p role="alert" className={styles.error}>
              {loadError}
            </p>
          )}

          {!loadError && categories === null && <p className={styles.muted}>Loading categories…</p>}

          {categories?.length === 0 && (
            <p className={styles.muted}>
              There are no categories yet. <Link href="/audio-categories">Add a category</Link> first.
            </p>
          )}

          {categories && categories.length > 0 && (
            <fieldset className={styles.list}>
              <legend className={styles.legend}>Category</legend>
              {categories.map((category) => (
                <label key={category.id} className={styles.option}>
                  <input
                    type="radio"
                    name="category"
                    value={category.id}
                    checked={selected === category.id}
                    onChange={() => setSelected(category.id)}
                  />
                  <span className={styles.name}>{category.name}</span>
                  <span className={styles.current}>
                    {category.audioId === null
                      ? "No audio linked"
                      : category.audioId === audio.id
                        ? "Linked to this audio"
                        : `Currently audio #${category.audioId}`}
                  </span>
                </label>
              ))}
            </fieldset>
          )}

          {error && (
            <p role="alert" className={styles.error}>
              {error}
            </p>
          )}

          <footer className={styles.footer}>
            <button type="button" onClick={onClose} disabled={saving}>
              Cancel
            </button>
            <button type="submit" className={styles.primary} disabled={selected === null || saving}>
              {saving ? "Saving…" : "Save"}
            </button>
          </footer>
        </form>
      </div>
    </div>
  );
}
