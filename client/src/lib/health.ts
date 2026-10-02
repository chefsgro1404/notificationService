import { getAuthConfig } from "@/lib/auth/config";

/**
 * Health checks for the web app (public, unauthenticated routes /api/health and /api/health/live).
 * The web app checks its own configuration and asks the Functions app for its health; it never calls
 * Service Bus, Storage or ACS itself. The Functions app caches its result, so frequent probes stay free.
 * Responses never contain secret values, only the names of missing settings.
 */

export type HealthStatus = "Healthy" | "Degraded" | "Unhealthy";

export interface HealthBlock {
  status: HealthStatus;
  description: string;
  /** For the Functions block: the Functions app's own blocks (serviceBus, storage, acs). */
  checks?: Record<string, unknown>;
}

export interface HealthBody {
  status: HealthStatus;
  checkedAtUtc: string;
  checks: Record<string, HealthBlock>;
}

const FUNCTIONS_HEALTH_TIMEOUT_MS = 15_000;
const STATUS_ORDER: readonly HealthStatus[] = ["Healthy", "Degraded", "Unhealthy"];

/** The worst of the given statuses ("Healthy" when there are none). */
export function worstStatus(statuses: HealthStatus[]): HealthStatus {
  return statuses.reduce<HealthStatus>(
    (worst, status) => (STATUS_ORDER.indexOf(status) > STATUS_ORDER.indexOf(worst) ? status : worst),
    "Healthy",
  );
}

function isHealthStatus(value: unknown): value is HealthStatus {
  return STATUS_ORDER.includes(value as HealthStatus);
}

/** Checks the web app's own settings: sign-in (Entra ID) and the backend URL / key. */
export function checkWebApp(): HealthBlock {
  const problems: string[] = [];

  try {
    getAuthConfig();
  } catch (err) {
    problems.push(err instanceof Error ? err.message : "Authentication is not configured.");
  }

  if (!process.env.FUNCTIONS_BASE_URL?.trim()) {
    problems.push("FUNCTIONS_BASE_URL is not configured.");
  }

  if (process.env.NODE_ENV === "production" && !process.env.FUNCTIONS_KEY?.trim()) {
    problems.push("FUNCTIONS_KEY is not configured.");
  }

  return problems.length > 0
    ? { status: "Unhealthy", description: problems.join(" ") }
    : { status: "Healthy", description: "Sign-in and backend settings are configured." };
}

/** Calls the Functions app's GET /api/health (anonymous) and turns the answer into one block. */
export async function checkFunctions(): Promise<HealthBlock> {
  const baseUrl = process.env.FUNCTIONS_BASE_URL?.trim().replace(/\/+$/, "");

  if (!baseUrl) {
    return { status: "Unhealthy", description: "FUNCTIONS_BASE_URL is not configured." };
  }

  let response: Response;

  try {
    response = await fetch(`${baseUrl}/api/health`, {
      method: "GET",
      cache: "no-store",
      signal: AbortSignal.timeout(FUNCTIONS_HEALTH_TIMEOUT_MS),
    });
  } catch (err) {
    const timedOut = err instanceof Error && err.name === "TimeoutError";
    return {
      status: "Unhealthy",
      description: timedOut ? "The Functions app did not answer in time." : "Could not reach the Functions app.",
    };
  }

  const payload = (await response.json().catch(() => null)) as { status?: unknown; checks?: unknown } | null;

  if (!payload || !isHealthStatus(payload.status)) {
    return { status: "Unhealthy", description: `The Functions app health endpoint returned HTTP ${response.status}.` };
  }

  const checks =
    typeof payload.checks === "object" && payload.checks !== null
      ? (payload.checks as Record<string, unknown>)
      : {};

  const notHealthy = Object.entries(checks)
    .filter(([, block]) => (block as { status?: unknown } | null)?.status !== "Healthy")
    .map(([name]) => name);

  return {
    status: payload.status,
    description:
      notHealthy.length > 0
        ? `The Functions app is ${payload.status}: ${notHealthy.join(", ")}.`
        : "The Functions app and its dependencies are healthy.",
    checks,
  };
}

/** Builds the JSON health response: 200 for Healthy / Degraded, 503 for Unhealthy. */
export function healthResponse(checks: Record<string, HealthBlock>): Response {
  const status = worstStatus(Object.values(checks).map((block) => block.status));
  const body: HealthBody = { status, checkedAtUtc: new Date().toISOString(), checks };

  return Response.json(body, {
    status: status === "Unhealthy" ? 503 : 200,
    headers: { "Cache-Control": "no-store" },
  });
}
