import { PAGE_SIZES } from "@/lib/logs";
import styles from "./logs.module.css";

interface PaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  disabled?: boolean;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
}

/** "Showing 21–40 of 132", page size selector, and First / Previous / Next / Last buttons. */
export default function Pagination({
  page,
  pageSize,
  totalCount,
  totalPages,
  disabled = false,
  onPageChange,
  onPageSizeChange,
}: PaginationProps) {
  const first = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);
  const atStart = disabled || page <= 1;
  const atEnd = disabled || page >= totalPages;

  return (
    <nav className={styles.pagination} aria-label="Pagination">
      <span className={styles.muted}>
        {totalCount === 0 ? "No results" : `Showing ${first}–${last} of ${totalCount}`}
      </span>

      <label className={styles.pageSize}>
        Rows
        <select
          value={pageSize}
          disabled={disabled}
          onChange={(event) => onPageSizeChange(Number(event.target.value))}
        >
          {PAGE_SIZES.map((size) => (
            <option key={size} value={size}>
              {size}
            </option>
          ))}
        </select>
      </label>

      <div className={styles.pageButtons}>
        <button type="button" onClick={() => onPageChange(1)} disabled={atStart} aria-label="First page">
          «
        </button>
        <button type="button" onClick={() => onPageChange(page - 1)} disabled={atStart}>
          Previous
        </button>
        <span className={styles.pageLabel}>
          Page {totalPages === 0 ? 0 : page} of {totalPages}
        </span>
        <button type="button" onClick={() => onPageChange(page + 1)} disabled={atEnd}>
          Next
        </button>
        <button type="button" onClick={() => onPageChange(totalPages)} disabled={atEnd} aria-label="Last page">
          »
        </button>
      </div>
    </nav>
  );
}
