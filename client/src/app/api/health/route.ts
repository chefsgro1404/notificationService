import { checkFunctions, checkWebApp, healthResponse } from "@/lib/health";

/**
 * Public: GET /api/health → the web app's own settings plus the Functions app's health (Service Bus,
 * Storage, ACS). For availability tests and alerts. 503 when either side is Unhealthy.
 */
export async function GET(): Promise<Response> {
  return healthResponse({
    webApp: checkWebApp(),
    functions: await checkFunctions(),
  });
}
