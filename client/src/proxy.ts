import { NextResponse, type NextRequest } from "next/server";
import { isAdmin, readSession, SESSION_COOKIE } from "@/lib/auth/session";

/**
 * Optimistic auth check for every request (pages and API routes) except the public ones listed in
 * `config.matcher`. Signed-out users are sent to /login (API: 401); signed-in users without the Admin
 * role are sent to /unauthorized (API: 403). Pages and route handlers check again on the server.
 */
export async function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const isApi = pathname.startsWith("/api/");
  const secret = process.env.AUTH_SESSION_SECRET ?? "";

  const user = secret.length >= 32 ? await readSession(request.cookies.get(SESSION_COOKIE)?.value, secret) : null;

  if (!user) {
    if (isApi) {
      return NextResponse.json({ error: "Sign in to continue." }, { status: 401 });
    }

    const login = new URL("/login", request.url);
    if (pathname !== "/") {
      login.searchParams.set("returnTo", `${pathname}${search}`);
    }
    return NextResponse.redirect(login);
  }

  if (!isAdmin(user) && pathname !== "/unauthorized") {
    return isApi
      ? NextResponse.json({ error: "You do not have permission to do this." }, { status: 403 })
      : NextResponse.redirect(new URL("/unauthorized", request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: [
    // Everything except the sign-in flow, the login page, static files and the logo.
    "/((?!api/auth/|login$|_next/static|_next/image|favicon\\.ico|chefsrhere-logo\\.png).*)",
  ],
};
