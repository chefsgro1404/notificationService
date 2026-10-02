import { checkWebApp, healthResponse } from "@/lib/health";

/**
 * Public: GET /api/health/live → the web app only (no backend call). For App Service "Health check",
 * which replaces instances that fail: a Functions outage must not make App Service restart the web app.
 */
export async function GET(): Promise<Response> {
  return healthResponse({ webApp: checkWebApp() });
}
