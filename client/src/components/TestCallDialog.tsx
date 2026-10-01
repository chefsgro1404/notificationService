"use client";

import Link from "next/link";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import type { AudioCategory } from "@/lib/categories";
import { isE164, normalizePhone } from "@/lib/phone";
import styles from "./LinkCategoryDialog.module.css";

interface TestCallDialogProps {
  category: AudioCategory;
  initialNumber: string;
  onClose: () => void;
  onNumberUsed: (number: string) => void;
}

type Result = { kind: "success"; notificationId: string; number: string } | { kind: "error"; message: string } | null;

/**
 * Modal that places a real test voice call playing the category's linked audio
 * (POST /api/audio-categories/{id}/test-call → Azure Function POST /api/voice-notifications).
 */
export default function TestCallDialog({ category, initialNumber, onClose, onNumberUsed }: TestCallDialogProps) {
  const [number, setNumber] = useState(initialNumber);
  const [calling, setCalling] = useState(false);
  const [result, setResult] = useState<Result>(null);
  const titleId = useId();
  const inputId = useId();
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

  async function call(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const recipient = normalizePhone(number);

    if (!isE164(recipient)) {
      setResult({ kind: "error", message: "Enter one phone number in international format, for example +18005551234." });
      return;
    }

    setCalling(true);
    setResult(null);

    try {
      const response = await fetch(`/api/audio-categories/${category.id}/test-call`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ recipient }),
      });

      const payload: unknown = await response.json().catch(() => null);
      const notificationId =
        typeof payload === "object" && payload !== null ? (payload as { notificationId?: unknown }).notificationId : null;

      if (response.ok && typeof notificationId === "string") {
        onNumberUsed(recipient);
        setResult({ kind: "success", notificationId, number: recipient });
        return;
      }

      setResult({
        kind: "error",
        message:
          typeof payload === "object" && payload !== null && typeof (payload as { error?: unknown }).error === "string"
            ? (payload as { error: string }).error
            : "Could not start the test call.",
      });
    } catch {
      setResult({ kind: "error", message: "Network error. Check your connection and try again." });
    } finally {
      setCalling(false);
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
        <form onSubmit={call} noValidate>
          <header className={styles.header}>
            <h2 id={titleId}>Test call: {category.name}</h2>
            <p className={styles.text}>
              Places a real phone call that plays audio #{category.audioId}, the audio linked to this category.
            </p>
          </header>

          <label htmlFor={inputId} className={styles.legend}>
            Phone number
          </label>
          <input
            id={inputId}
            className={styles.input}
            type="tel"
            inputMode="tel"
            autoComplete="tel"
            placeholder="+18005551234"
            value={number}
            onChange={(e) => setNumber(e.target.value)}
            disabled={calling}
          />
          <p className={styles.hint}>International format with country code, for example +4368864748566.</p>

          {result?.kind === "success" && (
            <p role="status" className={styles.success}>
              Calling {result.number} now. Track it in the <Link href="/notification-logs">Notification Log</Link>{" "}
              (id {result.notificationId}).
            </p>
          )}

          {result?.kind === "error" && (
            <p role="alert" className={styles.error}>
              {result.message}
            </p>
          )}

          <footer className={styles.footer}>
            <button type="button" onClick={onClose} disabled={calling}>
              {result?.kind === "success" ? "Close" : "Cancel"}
            </button>
            <button type="submit" className={styles.primary} disabled={calling || number.trim() === ""}>
              {calling ? "Calling…" : "Call now"}
            </button>
          </footer>
        </form>
      </div>
    </div>
  );
}
