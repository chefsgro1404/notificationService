/**
 * Server-only helpers for calling the NotificationService Azure Functions app.
 * FUNCTIONS_BASE_URL and FUNCTIONS_KEY are read at request time and never sent to the browser.
 */

const BACKEND_TIMEOUT_MS = 45_000;

export type BackendResult =
  | { ok: true; status: number; payload: unknown }
  | { ok: false; response: Response };

/** Builds a JSON error response: { "error": message }. */
export function errorResponse(status: number, message: string): Response {
  return Response.json({ error: message }, { status });
}

/** Reads the "error" string from a backend payload, if there is one. */
export function errorMessageOf(payload: unknown): string | null {
  return typeof payload === "object" && payload !== null && typeof (payload as { error?: unknown }).error === "string"
    ? (payload as { error: string }).error
    : null;
}

/**
 * Calls the backend and parses its JSON body. Configuration errors, timeouts and network failures are
 * turned into ready-to-return error responses (500, 504, 502).
 */
export async function callBackend(
  path: string,
  init: { method: string; body?: unknown } = { method: "GET" },
): Promise<BackendResult> {
  const baseUrl = process.env.FUNCTIONS_BASE_URL?.replace(/\/+$/, "");

  if (!baseUrl) {
    console.error("FUNCTIONS_BASE_URL is not configured.");
    return { ok: false, response: errorResponse(500, "The server is not configured to reach the backend.") };
  }

  const headers: Record<string, string> = {};

  if (init.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  if (process.env.FUNCTIONS_KEY) {
    headers["x-functions-key"] = process.env.FUNCTIONS_KEY;
  }

  let response: Response;

  try {
    response = await fetch(`${baseUrl}${path}`, {
      method: init.method,
      headers,
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      cache: "no-store",
      signal: AbortSignal.timeout(BACKEND_TIMEOUT_MS),
    });
  } catch (err) {
    const timedOut = err instanceof Error && err.name === "TimeoutError";
    console.error(`Backend request ${init.method} ${path} failed.`, err);
    return {
      ok: false,
      response: timedOut
        ? errorResponse(504, "The backend took too long to respond. Please try again.")
        : errorResponse(502, "Could not reach the backend. Please try again."),
    };
  }

  const payload: unknown = await response.json().catch(() => null);

  return { ok: true, status: response.status, payload };
}

/**
 * Forwards a backend call to the browser: 2xx, 400, 404 and 409 responses pass through (with a clean error
 * body); anything else becomes a generic 502 so backend details are not leaked.
 */
export async function proxyJson(
  path: string,
  init?: { method: string; body?: unknown },
): Promise<Response> {
  const result = await callBackend(path, init);

  if (!result.ok) {
    return result.response;
  }

  const { status, payload } = result;

  if (status >= 200 && status < 300) {
    return Response.json(payload, { status, headers: { "Cache-Control": "no-store" } });
  }

  if (status === 400 || status === 404 || status === 409) {
    return errorResponse(status, errorMessageOf(payload) ?? "The request could not be completed.");
  }

  console.error(`Backend ${path} returned ${status}.`, payload);
  return errorResponse(502, "The backend could not complete the request. Please try again.");
}

/** Copies only the allowed, non-empty query parameters into a query string ("?a=1&b=2" or ""). */
export function pickQuery(searchParams: URLSearchParams, allowed: readonly string[]): string {
  const picked = new URLSearchParams();

  for (const key of allowed) {
    const value = searchParams.get(key)?.trim();
    if (value) {
      picked.set(key, value);
    }
  }

  const query = picked.toString();
  return query ? `?${query}` : "";
}
