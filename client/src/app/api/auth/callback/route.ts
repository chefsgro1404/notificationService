import { NextResponse } from "next/server";
import { getAuthConfig, LOGIN_SCOPES, SESSION_MAX_AGE_SECONDS } from "@/lib/auth/config";
import { getMsalClient } from "@/lib/auth/msal";
import {
  AUTH_FLOW_COOKIE,
  cookieFromHeader,
  cookieOptions,
  isAdmin,
  seal,
  SESSION_COOKIE,
  unseal,
  type AuthFlowState,
  type SessionUser,
} from "@/lib/auth/session";

/**
 * GET /api/auth/callback — Microsoft Entra redirects here with an authorization code.
 * Verifies state, redeems the code (with the PKCE verifier and client secret), checks the ID token's
 * nonce, audience and tenant, then creates the encrypted session cookie with the user's app roles.
 */
export async function GET(request: Request): Promise<Response> {
  let config;

  try {
    config = getAuthConfig();
  } catch (err) {
    console.error(err);
    return NextResponse.redirect(new URL("/login?error=config", request.url));
  }

  const fail = (code: string) => {
    const response = NextResponse.redirect(new URL(`/login?error=${code}`, config.baseUrl));
    response.cookies.delete(AUTH_FLOW_COOKIE);
    return response;
  };

  const url = new URL(request.url);
  const error = url.searchParams.get("error");

  if (error) {
    console.warn(`Microsoft sign-in returned ${error}: ${url.searchParams.get("error_description") ?? ""}`);
    return fail(error === "access_denied" ? "cancelled" : "signin_failed");
  }

  const flow = await unseal<AuthFlowState>(
    cookieFromHeader(request.headers.get("cookie"), AUTH_FLOW_COOKIE),
    config.sessionSecret,
  );
  const code = url.searchParams.get("code");
  const state = url.searchParams.get("state");

  if (!flow || !code || !state || state !== flow.state) {
    return fail("expired");
  }

  const msal = getMsalClient(config);
  let claims: Record<string, unknown>;

  try {
    const result = await msal.acquireTokenByCode({
      code,
      scopes: LOGIN_SCOPES,
      redirectUri: config.redirectUri,
      codeVerifier: flow.codeVerifier,
      // MSAL Node rejects an ID token whose nonce it was not told to expect.
      nonce: flow.nonce,
      state: flow.state,
    });

    claims = (result.idTokenClaims ?? {}) as Record<string, unknown>;

    // Tokens are not needed after sign-in; keep MSAL's in-memory cache from growing.
    if (result.account) {
      await msal.getTokenCache().removeAccount(result.account);
    }
  } catch (err) {
    console.error("Could not redeem the Microsoft authorization code.", err);
    return fail("signin_failed");
  }

  if (claims.nonce !== flow.nonce || claims.aud !== config.clientId || claims.tid !== config.tenantId) {
    console.error("The ID token failed nonce, audience or tenant validation.");
    return fail("signin_failed");
  }

  const user: SessionUser = {
    oid: typeof claims.oid === "string" ? claims.oid : "",
    name: typeof claims.name === "string" ? claims.name : "",
    username: typeof claims.preferred_username === "string" ? claims.preferred_username : "",
    roles: Array.isArray(claims.roles) ? claims.roles.filter((role): role is string => typeof role === "string") : [],
  };

  if (!user.oid) {
    return fail("signin_failed");
  }

  const session = await seal(user, config.sessionSecret, SESSION_MAX_AGE_SECONDS);
  const response = NextResponse.redirect(new URL(isAdmin(user) ? flow.returnTo : "/unauthorized", config.baseUrl));

  response.cookies.set(SESSION_COOKIE, session, cookieOptions(config.secureCookies, SESSION_MAX_AGE_SECONDS));
  response.cookies.delete(AUTH_FLOW_COOKIE);
  return response;
}
