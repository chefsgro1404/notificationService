"use client";

import { useCallback, useState, type FormEvent } from "react";
import LinkCategoryDialog from "@/components/LinkCategoryDialog";
import Pagination from "@/components/Pagination";
import { categoriesForAudio, type AudioCategory } from "@/lib/categories";
import {
  AUDIO_STATUS_FILTERS,
  AUDIO_STATUS_LABELS,
  AUDIO_STATUSES,
  formatBytes,
  formatDateTime,
  statusKey,
  toQueryString,
  type AudioLogItem,
  type AudioStatus,
  type PagedResult,
} from "@/lib/logs";
import { useJson } from "@/lib/use-json";
import styles from "./logs.module.css";

interface Filters {
  status: string;
  search: string;
}

const EMPTY_FILTERS: Filters = { status: "", search: "" };

type Notice = { kind: "success" | "error"; message: string } | null;

/**
 * Actions offered for a record: [label, target status]. Audio that was never generated (no file) can only
 * be deleted, and a deleted record without a file cannot be restored.
 */
function actionsFor(item: AudioLogItem): [string, AudioStatus][] {
  switch (item.status) {
    case "Active":
      return [
        ["Deactivate", "Inactive"],
        ["Delete", "Deleted"],
      ];
    case "Inactive":
      return [
        ["Activate", "Active"],
        ["Delete", "Deleted"],
      ];
    case "Deleted":
      return item.blobName ? [["Restore", "Active"]] : [];
    default:
      return [["Delete", "Deleted"]];
  }
}

/** Paginated log of text-to-speech (T2A) records with activate / deactivate / delete / restore actions. */
export default function AudioLog() {
  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [reloadToken, setReloadToken] = useState(0);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [notice, setNotice] = useState<Notice>(null);
  const [linking, setLinking] = useState<AudioLogItem | null>(null);
  const [categoriesToken, setCategoriesToken] = useState(0);

  const url = `/api/audio-logs?${toQueryString({ ...applied, page, pageSize })}`;
  const { data, error, loading } = useJson<PagedResult<AudioLogItem>>(url, reloadToken);
  const categories = useJson<AudioCategory[]>("/api/audio-categories", categoriesToken);
  const categoryList = Array.isArray(categories.data) ? categories.data : null;

  const closeDialog = useCallback(() => setLinking(null), []);

  function linked(category: AudioCategory) {
    setNotice({ kind: "success", message: `Audio #${category.audioId} is now linked to “${category.name}”.` });
    setLinking(null);
    setCategoriesToken((t) => t + 1);
  }

  function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setApplied(draft);
    setPage(1);
  }

  function reset() {
    setDraft(EMPTY_FILTERS);
    setApplied(EMPTY_FILTERS);
    setPage(1);
  }

  async function changeStatus(item: AudioLogItem, status: AudioStatus) {
    if (status === "Deleted" && !window.confirm(`Delete audio #${item.id}? It can be restored later.`)) {
      return;
    }

    setBusyId(item.id);
    setNotice(null);

    try {
      const response = await fetch(`/api/audio-logs/${item.id}/status`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ status }),
      });

      const payload: unknown = await response.json().catch(() => null);

      if (response.ok) {
        setNotice({ kind: "success", message: `Audio #${item.id} is now ${AUDIO_STATUS_LABELS[status].toLowerCase()}.` });
        setReloadToken((t) => t + 1);
      } else {
        const message =
          typeof payload === "object" && payload !== null && typeof (payload as { error?: unknown }).error === "string"
            ? (payload as { error: string }).error
            : "Could not update the audio status.";
        setNotice({ kind: "error", message });
      }
    } catch {
      setNotice({ kind: "error", message: "Network error. Check your connection and try again." });
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className={styles.panel}>
      <form className={styles.filters} onSubmit={apply} aria-label="Audio log filters">
        <label>
          Status
          <select value={draft.status} onChange={(e) => setDraft({ ...draft, status: e.target.value })}>
            {AUDIO_STATUS_FILTERS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <label className={styles.grow}>
          Text
          <input
            type="search"
            value={draft.search}
            placeholder="Contains…"
            onChange={(e) => setDraft({ ...draft, search: e.target.value })}
          />
        </label>

        <div className={styles.filterButtons}>
          <button type="submit" className={styles.primary}>
            Apply
          </button>
          <button type="button" onClick={reset}>
            Reset
          </button>
          <button type="button" onClick={() => setReloadToken((t) => t + 1)} disabled={loading}>
            Refresh
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
        {data?.truncated && (
          <p className={styles.warning}>Only the first {data.totalCount} matching records were read.</p>
        )}
      </div>

      <div className={styles.tableWrap} aria-busy={loading}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Id</th>
              <th scope="col">Text</th>
              <th scope="col">Status</th>
              <th scope="col">Created</th>
              <th scope="col">Size</th>
              <th scope="col">Audio</th>
              <th scope="col">Categories</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {data?.items.map((item) => (
              <tr
                key={item.id}
                className={`${styles.row} ${styles[`status-${statusKey(item.status, AUDIO_STATUSES)}`]}`}
                data-status={item.status}
              >
                <td className={styles.nowrap}>
                  #{item.id}
                  <div className={styles.subtle} title="Notification id (T2A file name)">
                    {item.notificationId}
                  </div>
                </td>
                <td className={styles.wrap}>{item.text}</td>
                <td>
                  <span className={styles.badge}>{AUDIO_STATUS_LABELS[item.status] ?? item.status}</span>
                </td>
                <td className={styles.nowrap}>{formatDateTime(item.createdAtUtc)}</td>
                <td className={styles.nowrap}>{item.sizeBytes > 0 ? formatBytes(item.sizeBytes) : "—"}</td>
                <td>
                  {item.audioUrl ? (
                    <audio
                      className={styles.player}
                      controls
                      preload="none"
                      src={item.audioUrl}
                      aria-label={`Play audio #${item.id}`}
                    />
                  ) : item.errorMessage ? (
                    <span className={styles.errorText}>{item.errorMessage}</span>
                  ) : (
                    <span className={styles.muted}>{item.blobName ? "Not playable" : "No audio file"}</span>
                  )}
                </td>
                <td className={styles.wrap}>
                  {categoriesForAudio(categoryList, item.id).map((name) => (
                    <span key={name} className={styles.tag}>
                      {name}
                    </span>
                  ))}
                </td>
                <td>
                  <div className={styles.actions}>
                    {item.status === "Active" && item.blobName && (
                      <button
                        type="button"
                        disabled={busyId !== null}
                        onClick={() => setLinking(item)}
                        aria-label={`Link audio #${item.id} to a category`}
                      >
                        Link
                      </button>
                    )}
                    {actionsFor(item).map(([label, target]) => (
                      <button
                        key={label}
                        type="button"
                        className={target === "Deleted" ? styles.danger : undefined}
                        disabled={busyId !== null}
                        onClick={() => changeStatus(item, target)}
                        aria-label={`${label} audio #${item.id}`}
                      >
                        {busyId === item.id ? "…" : label}
                      </button>
                    ))}
                  </div>
                </td>
              </tr>
            ))}

            {data && data.items.length === 0 && (
              <tr>
                <td colSpan={8} className={styles.empty}>
                  No audio matches these filters.
                </td>
              </tr>
            )}

            {!data && loading && (
              <tr>
                <td colSpan={8} className={styles.empty}>
                  Loading…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {data && (
        <Pagination
          page={data.page}
          pageSize={pageSize}
          totalCount={data.totalCount}
          totalPages={data.totalPages}
          disabled={loading}
          onPageChange={setPage}
          onPageSizeChange={(size) => {
            setPageSize(size);
            setPage(1);
          }}
        />
      )}

      {linking && (
        <LinkCategoryDialog
          audio={{ id: linking.id, text: linking.text }}
          categories={categoryList}
          loadError={categories.error}
          onClose={closeDialog}
          onLinked={linked}
        />
      )}
    </div>
  );
}
