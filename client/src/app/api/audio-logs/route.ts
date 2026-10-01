import { withAdmin } from "@/lib/auth/guard";
import { pickQuery, proxyJson } from "@/lib/backend";

/** Proxy: GET /api/audio-logs → Azure Function GET /api/tts/logs (status filter, search, paging). */
const ALLOWED = ["status", "search", "page", "pageSize"] as const;

export const GET = withAdmin(async (request: Request): Promise<Response> => {
  const { searchParams } = new URL(request.url);

  return proxyJson(`/api/tts/logs${pickQuery(searchParams, ALLOWED)}`);
});
