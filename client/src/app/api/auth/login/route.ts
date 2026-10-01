import { CryptoProvider, ResponseMode } from "@azure/msal-node";
import { NextResponse } from "next/server";
import { getAuthConfig, LOGIN_SCOPES } from "@/lib/auth/config";
import { getMsalClient } from "@/lib/auth/msal";
import { AUTH_FLOW_COOKIE, cookieOptions, safeReturnTo, seal } from "@/lib/auth/session";

/** How long the user has to finish signing in at Microsoft. */
const FLOW_MAX_AGE_SECONDS = 10 * 60;

/**
 * GET /api/auth/login?returnTo=/path — starts the Microsoft Entra sign-in (authorization code + PKCE).
 * The PKCE verifier, state, nonce and return path are kept in a short-lived encrypted cookie.
 */
export async function GET(request: Request): Promise<Response> {
  let config;

  try {
    config = getAuthConfig();
  } catch (err) {
    console.error(err);
    return NextResponse.redirect(new URL("/login?error=config", request.url));
  }

  const returnTo = safeReturnTo(new URL(request.url).searchParams.get("returnTo"));
  const cryptoProvider = new CryptoProvider();
  const { verifier, challenge } = await cryptoProvider.generatePkceCodes();
  const state = cryptoProvider.createNewGuid();
  const nonce = cryptoProvider.createNewGuid();

  let authorizeUrl: string;

  try {
    authorizeUrl = await getMsalClient(config).getAuthCodeUrl({
      scopes: LOGIN_SCOPES,
      redirectUri: config.redirectUri,
      codeChallenge: challenge,
      codeChallengeMethod: "S256",
      responseMode: ResponseMode.QUERY,
      prompt: "select_account",
      state,
      nonce,
    });
  } catch (err) {
    console.error("Could not build the Microsoft sign-in URL.", err);
    return NextResponse.redirect(new URL("/login?error=signin_failed", config.baseUrl));
  }

  const flow = await seal(
    { state, nonce, codeVerifier: verifier, returnTo },
    config.sessionSecret,
    FLOW_MAX_AGE_SECONDS,
  );

  const response = NextResponse.redirect(authorizeUrl);
  response.cookies.set(AUTH_FLOW_COOKIE, flow, cookieOptions(config.secureCookies, FLOW_MAX_AGE_SECONDS));
  return response;
}
