import { NextResponse } from "next/server";
import { getAuthConfig } from "@/lib/auth/config";
import { SESSION_COOKIE } from "@/lib/auth/session";

/**
 * POST /api/auth/logout — clears the session cookie and signs the user out of Microsoft Entra,
 * which then returns them to /login.
 */
export async function POST(request: Request): Promise<Response> {
  let target: URL;

  try {
    const config = getAuthConfig();
    target = new URL(`${config.authority}/oauth2/v2.0/logout`);
    target.searchParams.set("post_logout_redirect_uri", config.postLogoutRedirectUri);
  } catch {
    target = new URL("/login", request.url);
  }

  const response = NextResponse.redirect(target, 303);
  response.cookies.delete(SESSION_COOKIE);
  return response;
}
