"use client";

import Link from "next/link";
import { useId, useState, type FormEvent } from "react";
import {
  countCharacters,
  isGeneratedAudio,
  limitCharacters,
  MAX_CHARACTERS,
  validateText,
  type GeneratedAudio,
} from "@/lib/text-to-wav";
import styles from "./TextToWav.module.css";

type Status =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "success"; audio: GeneratedAudio }
  | { kind: "error"; message: string };

function formatExpiry(isoDate: string): string {
  const date = new Date(isoDate);
  return Number.isNaN(date.getTime())
    ? isoDate
    : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

/** Text box, live character counter, Generate Audio button, and the resulting audio player. */
export default function TextToWav() {
  const [text, setText] = useState("");
  const [status, setStatus] = useState<Status>({ kind: "idle" });
  const inputId = useId();
  const counterId = useId();

  const count = countCharacters(text);
  const isLoading = status.kind === "loading";
  const canSubmit = !isLoading && text.trim().length > 0;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const validationError = validateText(text);
    if (validationError) {
      setStatus({ kind: "error", message: validationError });
      return;
    }

    setStatus({ kind: "loading" });

    try {
      const response = await fetch("/api/tts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text }),
      });

      const payload: unknown = await response.json().catch(() => null);

      if (response.ok && isGeneratedAudio(payload)) {
        setStatus({ kind: "success", audio: payload });
        return;
      }

      const message =
        typeof payload === "object" && payload !== null && typeof (payload as { error?: unknown }).error === "string"
          ? (payload as { error: string }).error
          : "Something went wrong while generating audio.";

      setStatus({ kind: "error", message });
    } catch {
      setStatus({ kind: "error", message: "Network error. Check your connection and try again." });
    }
  }

  return (
    <div className={styles.card}>
      <form onSubmit={handleSubmit} noValidate>
        <label htmlFor={inputId} className={styles.label}>
          Text to convert
        </label>

        <textarea
          id={inputId}
          className={styles.textarea}
          value={text}
          onChange={(event) => setText(limitCharacters(event.target.value))}
          placeholder="Type up to 150 characters…"
          rows={4}
          aria-describedby={counterId}
          disabled={isLoading}
        />

        <div className={styles.row}>
          <span
            id={counterId}
            className={count >= MAX_CHARACTERS ? `${styles.counter} ${styles.counterFull}` : styles.counter}
          >
            {count} / {MAX_CHARACTERS}
          </span>

          <button type="submit" className={styles.button} disabled={!canSubmit}>
            {isLoading ? "Generating…" : "Generate Audio"}
          </button>
        </div>
      </form>

      <div aria-live="polite">
        {status.kind === "error" && (
          <p role="alert" className={`${styles.message} ${styles.error}`}>
            {status.message}
          </p>
        )}

        {status.kind === "success" && (
          <section className={styles.result} aria-label="Generated audio">
            <p className={`${styles.message} ${styles.success}`}>
              Audio generated successfully. Saved as{" "}
              <Link href="/audio-logs" className={styles.logLink}>
                audio #{status.audio.id}
              </Link>
              .
            </p>

            <audio
              key={status.audio.audioUrl}
              className={styles.player}
              controls
              preload="auto"
              src={status.audio.audioUrl}
            >
              Your browser does not support the audio element.
            </audio>

            <p className={styles.meta}>
              Link valid until {formatExpiry(status.audio.expiresAtUtc)} (60 minutes).
            </p>

            <a
              className={styles.url}
              href={status.audio.audioUrl}
              target="_blank"
              rel="noopener noreferrer"
            >
              {status.audio.audioUrl}
            </a>
          </section>
        )}
      </div>
    </div>
  );
}
