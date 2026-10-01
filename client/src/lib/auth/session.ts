import { EncryptJWT, jwtDecrypt } from "jose";
import { ADMIN_ROLE } from "./config";

/**
 * Encrypted session cookies (JWE, A256GCM). The cookie holds only what the app needs to authorize
 * requests — the user's object id, display name, username and app roles — never Entra tokens.
 * Used by the proxy, server components and route handlers.
 */

export const SESSION_COOKIE = "t2w_session";
export const AUTH_FLOW_COOKIE = "t2w_auth_flow";

/** Signed-in user, taken from the ID token. */
export interface SessionUser {
  oid: string;
  name: string;
  username: string;
  roles: string[];
}

/** Short-lived state kept between the redirect to Microsoft and the callback. */
export interface AuthFlowState {
  state: string;
  nonce: string;
  codeVerifier: string;
  returnTo: string;
}

async function keyFrom(secret: string): Promise<Uint8Array> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(secret));
  return new Uint8Array(digest);
}

/** Encrypts a payload into a compact JWE that expires after `maxAgeSeconds`. */
export async function seal(payload: object, secret: string, maxAgeSeconds: number): Promise<string> {
  return new EncryptJWT({ ...payload })
    .setProtectedHeader({ alg: "dir", enc: "A256GCM" })
    .setIssuedAt()
    .setExpirationTime(`${maxAgeSeconds}s`)
    .encrypt(await keyFrom(secret));
}

/** Decrypts a JWE made by {@link seal}; returns null when it is missing, tampered with or expired. */
export async function unseal<T>(token: string | undefined, secret: string): Promise<T | null> {
  if (!token) {
    return null;
  }

  try {
    const { payload } = await jwtDecrypt(token, await keyFrom(secret));
    return payload as T;
  } catch {
    return null;
  }
}

/** Reads the session user from a sealed cookie value, validating its shape. */
export async function readSession(token: string | undefined, secret: string): Promise<SessionUser | null> {
  const payload = await unseal<Partial<SessionUser>>(token, secret);

  if (
    !payload ||
    typeof payload.oid !== "string" ||
    typeof payload.name !== "string" ||
    typeof payload.username !== "string" ||
    !Array.isArray(payload.roles)
  ) {
    return null;
  }

  return {
    oid: payload.oid,
    name: payload.name,
    username: payload.username,
    roles: payload.roles.filter((role): role is string => typeof role === "string"),
  };
}

/** Whether the user has the Admin app role. */
export function isAdmin(user: SessionUser | null): boolean {
  return user?.roles.includes(ADMIN_ROLE) ?? false;
}

/** Cookie attributes shared by the session and auth-flow cookies. */
export function cookieOptions(secure: boolean, maxAgeSeconds: number) {
  return {
    httpOnly: true,
    secure,
    sameSite: "lax" as const,
    path: "/",
    maxAge: maxAgeSeconds,
  };
}

/** Keeps redirects on this site: only same-origin relative paths are allowed. */
export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.startsWith("/\\")) {
    return "/";
  }

  return value;
}

/** Reads one cookie from a raw Cookie header. */
export function cookieFromHeader(header: string | null, name: string): string | undefined {
  if (!header) {
    return undefined;
  }

  for (const part of header.split(";")) {
    const index = part.indexOf("=");
    if (index > 0 && part.slice(0, index).trim() === name) {
      return decodeURIComponent(part.slice(index + 1).trim());
    }
  }

  return undefined;
}
