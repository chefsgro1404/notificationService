"use client";

import { useState, type FormEvent } from "react";
import Pagination from "@/components/Pagination";
import {
  endOfLocalDayUtc,
  formatDateTime,
  NOTIFICATION_CHANNELS,
  NOTIFICATION_STATUSES,
  startOfLocalDayUtc,
  statusKey,
  toQueryString,
  type NotificationLogItem,
  type PagedResult,
} from "@/lib/logs";
import { useJson } from "@/lib/use-json";
import styles from "./logs.module.css";

interface Filters {
  channel: string;
  status: string;
  recipient: string;
  from: string;
  to: string;
}

const EMPTY_FILTERS: Filters = { channel: "", status: "", recipient: "", from: "", to: "" };

/** Filterable, paginated notification audit log with rows colored by delivery status. */
export default function NotificationLog() {
  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [reloadToken, setReloadToken] = useState(0);
  const [filterError, setFilterError] = useState<string | null>(null);

  const url = `/api/notification-logs?${toQueryString({
    channel: applied.channel,
    status: applied.status,
    recipient: applied.recipient,
    from: applied.from ? startOfLocalDayUtc(applied.from) : undefined,
    to: applied.to ? endOfLocalDayUtc(applied.to) : undefined,
    page,
    pageSize,
  })}`;

  const { data, error, loading } = useJson<PagedResult<NotificationLogItem>>(url, reloadToken);

  function update(field: keyof Filters, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
  }

  function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (draft.from && draft.to && draft.from > draft.to) {
      setFilterError("The From date must be on or before the To date.");
      return;
    }

    setFilterError(null);
    setApplied(draft);
    setPage(1);
  }

  function reset() {
    setFilterError(null);
    setDraft(EMPTY_FILTERS);
    setApplied(EMPTY_FILTERS);
    setPage(1);
  }

  return (
    <div className={styles.panel}>
      <form className={styles.filters} onSubmit={apply} aria-label="Notification log filters">
        <label>
          Channel
          <select value={draft.channel} onChange={(e) => update("channel", e.target.value)}>
            <option value="">All channels</option>
            {NOTIFICATION_CHANNELS.map((channel) => (
              <option key={channel} value={channel}>
                {channel}
              </option>
            ))}
          </select>
        </label>

        <label>
          Status
          <select value={draft.status} onChange={(e) => update("status", e.target.value)}>
            <option value="">All statuses</option>
            {NOTIFICATION_STATUSES.map((status) => (
              <option key={status} value={status}>
                {status}
              </option>
            ))}
          </select>
        </label>

        <label className={styles.grow}>
          Recipient
          <input
            type="search"
            value={draft.recipient}
            placeholder="Contains…"
            onChange={(e) => update("recipient", e.target.value)}
          />
        </label>

        <label>
          From
          <input type="date" value={draft.from} onChange={(e) => update("from", e.target.value)} />
        </label>

        <label>
          To
          <input type="date" value={draft.to} onChange={(e) => update("to", e.target.value)} />
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

      <ul className={styles.legend} aria-label="Status colors">
        {NOTIFICATION_STATUSES.map((status) => (
          <li key={status}>
            <span className={`${styles.badge} ${styles[`status-${status.toLowerCase()}`]}`}>{status}</span>
          </li>
        ))}
      </ul>

      <div aria-live="polite">
        {filterError && (
          <p role="alert" className={styles.error}>
            {filterError}
          </p>
        )}
        {error && (
          <p role="alert" className={styles.error}>
            {error}
          </p>
        )}
        {data?.truncated && (
          <p className={styles.warning}>
            Only the first {data.totalCount} matching records were read. Narrow the filters (for example a date range) to
            see older records.
          </p>
        )}
      </div>

      <div className={styles.tableWrap} aria-busy={loading}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Status</th>
              <th scope="col">Channel</th>
              <th scope="col">Recipient</th>
              <th scope="col">Created</th>
              <th scope="col">Updated</th>
              <th scope="col">Retries</th>
              <th scope="col">Details</th>
            </tr>
          </thead>
          <tbody>
            {data?.items.map((item) => (
              <tr
                key={`${item.channel}-${item.id}`}
                className={`${styles.row} ${styles[`status-${statusKey(item.status, NOTIFICATION_STATUSES)}`]}`}
                data-status={item.status}
              >
                <td>
                  <span className={styles.badge}>{item.status}</span>
                </td>
                <td>{item.channel}</td>
                <td className={styles.wrap}>{item.recipient}</td>
                <td className={styles.nowrap}>{formatDateTime(item.createdAtUtc)}</td>
                <td className={styles.nowrap}>{formatDateTime(item.updatedAtUtc)}</td>
                <td>{item.retryCount}</td>
                <td className={styles.wrap}>
                  {item.errorMessage ? (
                    <span className={styles.errorText}>
                      {item.errorCode ? `${item.errorCode}: ` : ""}
                      {item.errorMessage}
                    </span>
                  ) : (
                    <span className={styles.muted}>{item.providerMessageId ?? "—"}</span>
                  )}
                  {item.hasAttachment && <span className={styles.tag}>Attachment</span>}
                </td>
              </tr>
            ))}

            {data && data.items.length === 0 && (
              <tr>
                <td colSpan={7} className={styles.empty}>
                  No notifications match these filters.
                </td>
              </tr>
            )}

            {!data && loading && (
              <tr>
                <td colSpan={7} className={styles.empty}>
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
    </div>
  );
}
