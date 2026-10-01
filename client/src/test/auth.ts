import { vi } from "vitest";
import { seal, SESSION_COOKIE, type SessionUser } from "@/lib/auth/session";

/** Test auth settings (not real credentials). */
export const TEST_AUTH = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  clientId: "22222222-2222-2222-2222-222222222222",
  clientSecret: "test-secret",
  sessionSecret: "test-session-secret-that-is-long-enough-123",
  baseUrl: "http://localhost:3000",
};

export function stubAuthEnv() {
  vi.stubEnv("AZURE_AD_TENANT_ID", TEST_AUTH.tenantId);
  vi.stubEnv("AZURE_AD_CLIENT_ID", TEST_AUTH.clientId);
  vi.stubEnv("AZURE_AD_CLIENT_SECRET", TEST_AUTH.clientSecret);
  vi.stubEnv("AUTH_SESSION_SECRET", TEST_AUTH.sessionSecret);
  vi.stubEnv("APP_BASE_URL", TEST_AUTH.baseUrl);
}

export const ADMIN_USER: SessionUser = { oid: "oid-admin", name: "Ada Admin", username: "ada@contoso.com", roles: ["Admin"] };
export const GUEST_USER: SessionUser = { oid: "oid-guest", name: "Gus Guest", username: "gus@contoso.com", roles: ["Guest"] };

/** A Cookie header value holding a valid session for the user. */
export async function sessionCookie(user: SessionUser = ADMIN_USER): Promise<string> {
  return `${SESSION_COOKIE}=${await seal(user, TEST_AUTH.sessionSecret, 3600)}`;
}
