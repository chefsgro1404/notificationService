import { withAdmin } from "@/lib/auth/guard";
import { pickQuery, proxyJson } from "@/lib/backend";

/** Proxy: GET /api/notification-logs → Azure Function GET /api/notification-logs (filters + paging). */
const ALLOWED = ["channel", "status", "recipient", "from", "to", "page", "pageSize"] as const;

export const GET = withAdmin(async (request: Request): Promise<Response> => {
  const { searchParams } = new URL(request.url);

  return proxyJson(`/api/notification-logs${pickQuery(searchParams, ALLOWED)}`);
});
