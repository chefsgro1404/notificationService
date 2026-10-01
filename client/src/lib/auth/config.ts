/**
 * Microsoft Entra ID (Azure AD) settings for sign-in. All values are read from server-only environment
 * variables (client/.env.local locally, App Service app settings in Azure); none reach the browser.
 */

/** App role that can use every page and action. */
export const ADMIN_ROLE = "Admin";

/** App role for guests (signed in, but no access to the current pages yet). */
export const GUEST_ROLE = "Guest";

/** Scopes requested at sign-in: an ID token with the user's name, username and app roles. */
export const LOGIN_SCOPES = ["openid", "profile", "email"];

/** How long a sign-in lasts before the user must sign in again. */
export const SESSION_MAX_AGE_SECONDS = 8 * 60 * 60;

export interface AuthConfig {
  tenantId: string;
  clientId: string;
  clientSecret: string;
  sessionSecret: string;
  baseUrl: string;
  authority: string;
  redirectUri: string;
  postLogoutRedirectUri: string;
  secureCookies: boolean;
}

/** Reads and validates the auth settings; throws with the names of any missing variables. */
export function getAuthConfig(): AuthConfig {
  const tenantId = process.env.AZURE_AD_TENANT_ID?.trim() ?? "";
  const clientId = process.env.AZURE_AD_CLIENT_ID?.trim() ?? "";
  const clientSecret = process.env.AZURE_AD_CLIENT_SECRET?.trim() ?? "";
  const sessionSecret = process.env.AUTH_SESSION_SECRET?.trim() ?? "";
  const baseUrl = (process.env.APP_BASE_URL?.trim() ?? "").replace(/\/+$/, "");

  const missing = Object.entries({
    AZURE_AD_TENANT_ID: tenantId,
    AZURE_AD_CLIENT_ID: clientId,
    AZURE_AD_CLIENT_SECRET: clientSecret,
    APP_BASE_URL: baseUrl,
  })
    .filter(([, value]) => !value)
    .map(([name]) => name);

  if (sessionSecret.length < 32) {
    missing.push("AUTH_SESSION_SECRET (at least 32 characters)");
  }

  if (missing.length > 0) {
    throw new Error(`Authentication is not configured. Missing: ${missing.join(", ")}.`);
  }

  return {
    tenantId,
    clientId,
    clientSecret,
    sessionSecret,
    baseUrl,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: `${baseUrl}/api/auth/callback`,
    postLogoutRedirectUri: `${baseUrl}/login`,
    secureCookies: baseUrl.startsWith("https://"),
  };
}
