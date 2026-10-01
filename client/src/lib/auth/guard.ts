import "server-only";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getAuthConfig } from "./config";
import { cookieFromHeader, isAdmin, readSession, SESSION_COOKIE, type SessionUser } from "./session";

/**
 * Server-side authorization (the "data access layer" checks). The proxy only redirects optimistically;
 * every protected page and API route calls one of these as well.
 */

/** Current user from the session cookie, or null. For server components. */
export async function getSessionUser(): Promise<SessionUser | null> {
  const token = (await cookies()).get(SESSION_COOKIE)?.value;
  return readSession(token, getAuthConfig().sessionSecret);
}

/** Requires a signed-in Admin for a page: redirects to /login or /unauthorized otherwise. */
export async function requireAdminPage(returnTo = "/"): Promise<SessionUser> {
  const user = await getSessionUser();

  if (!user) {
    redirect(`/login?returnTo=${encodeURIComponent(returnTo)}`);
  }

  if (!isAdmin(user)) {
    redirect("/unauthorized");
  }

  return user;
}

type RouteHandler<TContext> = (request: Request, context: TContext) => Promise<Response>;

/** Wraps an API route handler so it only runs for a signed-in Admin (401 / 403 JSON otherwise). */
export function withAdmin<TContext>(handler: RouteHandler<TContext>): RouteHandler<TContext> {
  return async (request, context) => {
    const token = cookieFromHeader(request.headers.get("cookie"), SESSION_COOKIE);
    const user = await readSession(token, getAuthConfig().sessionSecret);

    if (!user) {
      return Response.json({ error: "Sign in to continue." }, { status: 401 });
    }

    if (!isAdmin(user)) {
      return Response.json({ error: "You do not have permission to do this." }, { status: 403 });
    }

    return handler(request, context);
  };
}
