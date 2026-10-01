"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { MAX_CATEGORY_NAME, validateCategoryName, type AudioCategory } from "@/lib/categories";
import { formatDateTime } from "@/lib/logs";
import { useJson } from "@/lib/use-json";
import styles from "./logs.module.css";

type Notice = { kind: "success" | "error"; message: string } | null;

/** Lists audio categories and adds new ones. Audio is linked to a category from the Audio Log. */
export default function AudioCategories() {
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<Notice>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const { data, error, loading } = useJson<AudioCategory[]>("/api/audio-categories", reloadToken);
  const rows = Array.isArray(data) ? data : null;

  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const validationError = validateCategoryName(name);
    if (validationError) {
      setNotice({ kind: "error", message: validationError });
      return;
    }

    setSaving(true);
    setNotice(null);

    try {
      const response = await fetch("/api/audio-categories", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: name.trim() }),
      });

      const payload: unknown = await response.json().catch(() => null);

      if (response.ok) {
        const created = payload as AudioCategory;
        setNotice({ kind: "success", message: `Category “${created.name}” was added.` });
        setName("");
        setReloadToken((t) => t + 1);
      } else {
        setNotice({
          kind: "error",
          message:
            typeof payload === "object" && payload !== null && typeof (payload as { error?: unknown }).error === "string"
              ? (payload as { error: string }).error
              : "Could not add the category.",
        });
      }
    } catch {
      setNotice({ kind: "error", message: "Network error. Check your connection and try again." });
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className={styles.panel}>
      <form className={styles.filters} onSubmit={add} aria-label="Add category" noValidate>
        <label className={styles.grow}>
          New category
          <input
            value={name}
            maxLength={MAX_CATEGORY_NAME}
            placeholder="For example Support or Order updates"
            onChange={(e) => setName(e.target.value)}
            disabled={saving}
          />
        </label>
        <div className={styles.filterButtons}>
          <button type="submit" className={styles.primary} disabled={saving || name.trim() === ""}>
            {saving ? "Adding…" : "Add category"}
          </button>
        </div>
      </form>

      <div aria-live="polite">
        {notice && (
          <p role={notice.kind === "error" ? "alert" : "status"} className={styles[notice.kind]}>
            {notice.message}
          </p>
        )}
        {error && (
          <p role="alert" className={styles.error}>
            {error}
          </p>
        )}
      </div>

      <div className={styles.tableWrap} aria-busy={loading}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Id</th>
              <th scope="col">Name</th>
              <th scope="col">Linked audio</th>
              <th scope="col">Updated</th>
            </tr>
          </thead>
          <tbody>
            {rows?.map((category) => (
              <tr key={category.id}>
                <td className={styles.nowrap}>#{category.id}</td>
                <td className={styles.wrap}>{category.name}</td>
                <td>
                  {category.audioId === null ? (
                    <span className={styles.muted}>Not linked</span>
                  ) : (
                    <Link href="/audio-logs">Audio #{category.audioId}</Link>
                  )}
                </td>
                <td className={styles.nowrap}>{formatDateTime(category.updatedAtUtc)}</td>
              </tr>
            ))}

            {rows && rows.length === 0 && (
              <tr>
                <td colSpan={4} className={styles.empty}>
                  No categories yet. Add one above, then link audio to it from the Audio Log.
                </td>
              </tr>
            )}

            {!rows && loading && (
              <tr>
                <td colSpan={4} className={styles.empty}>
                  Loading…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
