"use client";

import { useEffect, useState } from "react";

interface JsonState<T> {
  key: string;
  data: T | null;
  error: string | null;
}

/**
 * Fetches JSON from `url` whenever it (or `reloadToken`) changes, cancelling stale requests.
 * The previous data stays visible while the next request is loading.
 */
export function useJson<T>(url: string, reloadToken = 0) {
  const key = `${reloadToken}|${url}`;
  const [state, setState] = useState<JsonState<T>>({ key: "", data: null, error: null });

  useEffect(() => {
    const controller = new AbortController();

    fetch(url, { signal: controller.signal, cache: "no-store" })
      .then(async (response) => {
        const payload: unknown = await response.json().catch(() => null);

        if (response.ok) {
          setState({ key, data: payload as T, error: null });
          return;
        }

        const message =
          typeof payload === "object" && payload !== null && typeof (payload as { error?: unknown }).error === "string"
            ? (payload as { error: string }).error
            : `Request failed (${response.status}).`;

        setState({ key, data: null, error: message });
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setState({ key, data: null, error: "Network error. Check your connection and try again." });
        }
      });

    return () => controller.abort();
  }, [url, key]);

  return {
    data: state.data,
    error: state.key === key ? state.error : null,
    loading: state.key !== key,
  };
}
