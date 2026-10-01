// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ADMIN_USER, GUEST_USER, sessionCookie, stubAuthEnv, TEST_AUTH } from "@/test/auth";
import { getAuthConfig } from "./config";
import { withAdmin } from "./guard";
import {
  cookieFromHeader,
  cookieOptions,
  isAdmin,
  readSession,
  safeReturnTo,
  seal,
  unseal,
} from "./session";

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/navigation", () => ({
  redirect: vi.fn((url: string) => {
    throw new Error(`REDIRECT:${url}`);
  }),
}));

beforeEach(() => {
  stubAuthEnv();
});

describe("getAuthConfig", () => {
  it("derives authority, redirect URIs and cookie security from the settings", () => {
    vi.stubEnv("APP_BASE_URL", "https://app.contoso.com/");

    const config = getAuthConfig();

    expect(config.authority).toBe(`https://login.microsoftonline.com/${TEST_AUTH.tenantId}`);
    expect(config.redirectUri).toBe("https://app.contoso.com/api/auth/callback");
    expect(config.postLogoutRedirectUri).toBe("https://app.contoso.com/login");
    expect(config.secureCookies).toBe(true);
  });

  it("lists every missing setting", () => {
    vi.stubEnv("AZURE_AD_CLIENT_SECRET", "");
    vi.stubEnv("APP_BASE_URL", "");
    vi.stubEnv("AUTH_SESSION_SECRET", "short");

    expect(() => getAuthConfig()).toThrow(
      "Authentication is not configured. Missing: AZURE_AD_CLIENT_SECRET, APP_BASE_URL, AUTH_SESSION_SECRET (at least 32 characters).",
    );
  });

  it("treats unset variables as missing", () => {
    vi.stubEnv("AZURE_AD_TENANT_ID", undefined as unknown as string);
    vi.stubEnv("AZURE_AD_CLIENT_ID", undefined as unknown as string);
    vi.stubEnv("AZURE_AD_CLIENT_SECRET", undefined as unknown as string);
    vi.stubEnv("AUTH_SESSION_SECRET", undefined as unknown as string);
    vi.stubEnv("APP_BASE_URL", undefined as unknown as string);

    expect(() => getAuthConfig()).toThrow(/AZURE_AD_TENANT_ID, AZURE_AD_CLIENT_ID/);
  });
});

describe("session sealing", () => {
  it("round-trips a session and rejects a different secret", async () => {
    const token = await seal(ADMIN_USER, TEST_AUTH.sessionSecret, 60);

    expect(await readSession(token, TEST_AUTH.sessionSecret)).toEqual(ADMIN_USER);
    expect(await readSession(token, "another-secret-that-is-also-long-enough")).toBeNull();
  });

  it("rejects missing, tampered and expired tokens", async () => {
    const token = await seal(ADMIN_USER, TEST_AUTH.sessionSecret, 60);
    const expired = await seal(ADMIN_USER, TEST_AUTH.sessionSecret, -10);

    expect(await readSession(undefined, TEST_AUTH.sessionSecret)).toBeNull();
    expect(await readSession(`${token.slice(0, -4)}AAAA`, TEST_AUTH.sessionSecret)).toBeNull();
    expect(await readSession(expired, TEST_AUTH.sessionSecret)).toBeNull();
  });

  it("rejects payloads that are not sessions and drops non-string roles", async () => {
    const notASession = await seal({ state: "x" }, TEST_AUTH.sessionSecret, 60);
    const oddRoles = await seal({ ...ADMIN_USER, roles: ["Admin", 7] }, TEST_AUTH.sessionSecret, 60);

    expect(await readSession(notASession, TEST_AUTH.sessionSecret)).toBeNull();
    expect((await readSession(oddRoles, TEST_AUTH.sessionSecret))?.roles).toEqual(["Admin"]);
    expect(await unseal<{ state: string }>(notASession, TEST_AUTH.sessionSecret)).toMatchObject({ state: "x" });
  });
});

describe("session helpers", () => {
  it("recognizes the Admin role only", () => {
    expect(isAdmin(ADMIN_USER)).toBe(true);
    expect(isAdmin(GUEST_USER)).toBe(false);
    expect(isAdmin(null)).toBe(false);
  });

  it.each([
    [null, "/"],
    ["", "/"],
    ["https://evil.example", "/"],
    ["//evil.example", "/"],
    ["/\\evil.example", "/"],
    ["/audio-logs?status=All", "/audio-logs?status=All"],
  ])("safeReturnTo(%j) is %j", (value, expected) => {
    expect(safeReturnTo(value)).toBe(expected);
  });

  it("reads cookies from a header", () => {
    expect(cookieFromHeader("a=1; t2w_session=abc%3D; b=2", "t2w_session")).toBe("abc=");
    expect(cookieFromHeader("a=1", "t2w_session")).toBeUndefined();
    expect(cookieFromHeader(null, "t2w_session")).toBeUndefined();
  });

  it("builds http-only lax cookies", () => {
    expect(cookieOptions(true, 60)).toEqual({ httpOnly: true, secure: true, sameSite: "lax", path: "/", maxAge: 60 });
  });
});

describe("withAdmin", () => {
  const handler = vi.fn(async () => Response.json({ ok: true }));
  const guarded = withAdmin(handler);

  it("returns 401 without a session", async () => {
    const response = await guarded(new Request("http://localhost/api/x"), undefined);

    expect(response.status).toBe(401);
    expect(handler).not.toHaveBeenCalled();
  });

  it("returns 403 for a signed-in guest", async () => {
    const request = new Request("http://localhost/api/x", { headers: { cookie: await sessionCookie(GUEST_USER) } });

    expect((await guarded(request, undefined)).status).toBe(403);
    expect(handler).not.toHaveBeenCalled();
  });

  it("runs the handler for an admin", async () => {
    const request = new Request("http://localhost/api/x", { headers: { cookie: await sessionCookie() } });

    const response = await guarded(request, { params: 1 });

    expect(response.status).toBe(200);
    expect(handler).toHaveBeenCalledWith(request, { params: 1 });
  });
});

describe("page guards", () => {
  async function withCookie(value: string | undefined) {
    const { cookies } = await import("next/headers");
    vi.mocked(cookies).mockResolvedValue({ get: () => (value ? { value } : undefined) } as never);
  }

  it("getSessionUser reads the session cookie", async () => {
    const { getSessionUser } = await import("./guard");
    await withCookie((await sessionCookie()).split("=")[1]);

    expect(await getSessionUser()).toEqual(ADMIN_USER);
  });

  it("requireAdminPage redirects signed-out users to login with the return path", async () => {
    const { requireAdminPage } = await import("./guard");
    await withCookie(undefined);

    await expect(requireAdminPage("/audio-logs")).rejects.toThrow("REDIRECT:/login?returnTo=%2Faudio-logs");
  });

  it("requireAdminPage redirects guests to /unauthorized and returns admins", async () => {
    const { requireAdminPage } = await import("./guard");

    await withCookie((await sessionCookie(GUEST_USER)).split("=")[1]);
    await expect(requireAdminPage()).rejects.toThrow("REDIRECT:/unauthorized");

    await withCookie((await sessionCookie()).split("=")[1]);
    expect(await requireAdminPage()).toEqual(ADMIN_USER);
  });
});
