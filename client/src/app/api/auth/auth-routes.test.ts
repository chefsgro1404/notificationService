// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AUTH_FLOW_COOKIE, readSession, seal, SESSION_COOKIE, unseal, type AuthFlowState } from "@/lib/auth/session";
import { stubAuthEnv, TEST_AUTH } from "@/test/auth";
import { GET as callback } from "./callback/route";
import { GET as login } from "./login/route";
import { POST as logout } from "./logout/route";

const msal = vi.hoisted(() => ({
  getAuthCodeUrl: vi.fn(),
  acquireTokenByCode: vi.fn(),
  removeAccount: vi.fn(),
}));

vi.mock("@/lib/auth/msal", () => ({
  getMsalClient: () => ({
    getAuthCodeUrl: msal.getAuthCodeUrl,
    acquireTokenByCode: msal.acquireTokenByCode,
    getTokenCache: () => ({ removeAccount: msal.removeAccount }),
  }),
}));

function setCookie(response: Response, name: string): string | undefined {
  const header = response.headers.getSetCookie().find((c) => c.startsWith(`${name}=`));
  return header?.split(";")[0].slice(name.length + 1);
}

beforeEach(() => {
  stubAuthEnv();
  vi.spyOn(console, "error").mockImplementation(() => {});
  vi.spyOn(console, "warn").mockImplementation(() => {});
  msal.getAuthCodeUrl.mockReset().mockResolvedValue("https://login.microsoftonline.com/authorize?x=1");
  msal.acquireTokenByCode.mockReset();
  msal.removeAccount.mockReset();
});

describe("GET /api/auth/login", () => {
  it("redirects to Microsoft with PKCE, state and nonce, and stores the flow in a cookie", async () => {
    const response = await login(new Request("http://localhost:3000/api/auth/login?returnTo=%2Faudio-logs"));

    expect(response.status).toBe(307);
    expect(response.headers.get("location")).toBe("https://login.microsoftonline.com/authorize?x=1");

    const request = msal.getAuthCodeUrl.mock.calls[0][0];
    expect(request).toMatchObject({
      scopes: ["openid", "profile", "email"],
      redirectUri: "http://localhost:3000/api/auth/callback",
      codeChallengeMethod: "S256",
      prompt: "select_account",
    });

    const flow = await unseal<AuthFlowState>(setCookie(response, AUTH_FLOW_COOKIE), TEST_AUTH.sessionSecret);
    expect(flow).toMatchObject({ state: request.state, nonce: request.nonce, returnTo: "/audio-logs" });
    expect(flow?.codeVerifier).toBeTruthy();
    expect(response.headers.getSetCookie()[0]).toMatch(/HttpOnly/i);
  });

  it("ignores off-site return paths", async () => {
    const response = await login(new Request("http://localhost:3000/api/auth/login?returnTo=https%3A%2F%2Fevil.example"));

    const flow = await unseal<AuthFlowState>(setCookie(response, AUTH_FLOW_COOKIE), TEST_AUTH.sessionSecret);
    expect(flow?.returnTo).toBe("/");
  });

  it("sends the user back to /login when auth is not configured or MSAL fails", async () => {
    msal.getAuthCodeUrl.mockRejectedValueOnce(new Error("bad authority"));
    const failed = await login(new Request("http://localhost:3000/api/auth/login"));
    expect(failed.headers.get("location")).toBe("http://localhost:3000/login?error=signin_failed");

    vi.stubEnv("AZURE_AD_CLIENT_SECRET", "");
    const unconfigured = await login(new Request("http://localhost:3000/api/auth/login"));
    expect(unconfigured.headers.get("location")).toBe("http://localhost:3000/login?error=config");
  });
});

describe("GET /api/auth/callback", () => {
  const flow: AuthFlowState = { state: "state-1", nonce: "nonce-1", codeVerifier: "verifier-1", returnTo: "/audio-logs" };

  async function callbackRequest(query: string, flowState: AuthFlowState | null = flow) {
    const headers: Record<string, string> = {};
    if (flowState) {
      headers.cookie = `${AUTH_FLOW_COOKIE}=${await seal(flowState, TEST_AUTH.sessionSecret, 600)}`;
    }
    return new Request(`http://localhost:3000/api/auth/callback?${query}`, { headers });
  }

  function claims(extra: Record<string, unknown> = {}) {
    return {
      idTokenClaims: {
        nonce: "nonce-1",
        aud: TEST_AUTH.clientId,
        tid: TEST_AUTH.tenantId,
        oid: "oid-1",
        name: "Ada Admin",
        preferred_username: "ada@contoso.com",
        roles: ["Admin"],
        ...extra,
      },
      account: { homeAccountId: "h" },
    };
  }

  it("redeems the code, creates the session and returns the admin to the requested page", async () => {
    msal.acquireTokenByCode.mockResolvedValue(claims());

    const response = await callback(await callbackRequest("code=abc&state=state-1"));

    expect(response.headers.get("location")).toBe("http://localhost:3000/audio-logs");
    expect(msal.acquireTokenByCode).toHaveBeenCalledWith({
      code: "abc",
      scopes: ["openid", "profile", "email"],
      redirectUri: "http://localhost:3000/api/auth/callback",
      codeVerifier: "verifier-1",
      nonce: "nonce-1",
      state: "state-1",
    });
    expect(msal.removeAccount).toHaveBeenCalledWith({ homeAccountId: "h" });
    expect(await readSession(setCookie(response, SESSION_COOKIE), TEST_AUTH.sessionSecret)).toEqual({
      oid: "oid-1",
      name: "Ada Admin",
      username: "ada@contoso.com",
      roles: ["Admin"],
    });
    expect(setCookie(response, AUTH_FLOW_COOKIE)).toBe("");
  });

  it("sends users without the Admin role to /unauthorized", async () => {
    msal.acquireTokenByCode.mockResolvedValue({ ...claims({ roles: ["Guest"], name: 5, preferred_username: 6 }), account: null });

    const response = await callback(await callbackRequest("code=abc&state=state-1"));

    expect(response.headers.get("location")).toBe("http://localhost:3000/unauthorized");
    const user = await readSession(setCookie(response, SESSION_COOKIE), TEST_AUTH.sessionSecret);
    expect(user).toEqual({ oid: "oid-1", name: "", username: "", roles: ["Guest"] });
    expect(msal.removeAccount).not.toHaveBeenCalled();
  });

  it("handles a token without roles", async () => {
    msal.acquireTokenByCode.mockResolvedValue(claims({ roles: undefined }));

    const response = await callback(await callbackRequest("code=abc&state=state-1"));

    expect(response.headers.get("location")).toBe("http://localhost:3000/unauthorized");
  });

  it.each([
    ["error=access_denied&error_description=cancelled", "cancelled"],
    ["error=server_error", "signin_failed"],
  ])("maps Microsoft errors (%s)", async (query, code) => {
    const response = await callback(await callbackRequest(query));

    expect(response.headers.get("location")).toBe(`http://localhost:3000/login?error=${code}`);
    expect(msal.acquireTokenByCode).not.toHaveBeenCalled();
  });

  it.each([
    ["code=abc&state=other", flow],
    ["state=state-1", flow],
    ["code=abc", flow],
    ["code=abc&state=state-1", null],
  ])("rejects a missing or mismatched state (%s)", async (query, flowState) => {
    const response = await callback(await callbackRequest(query, flowState));

    expect(response.headers.get("location")).toBe("http://localhost:3000/login?error=expired");
  });

  it("fails when the code cannot be redeemed", async () => {
    msal.acquireTokenByCode.mockRejectedValue(new Error("invalid_grant"));

    const response = await callback(await callbackRequest("code=abc&state=state-1"));

    expect(response.headers.get("location")).toBe("http://localhost:3000/login?error=signin_failed");
  });

  it.each([{ nonce: "other" }, { aud: "other-app" }, { tid: "other-tenant" }, { oid: undefined }])(
    "rejects an ID token with bad claims %j",
    async (bad) => {
      msal.acquireTokenByCode.mockResolvedValue(claims(bad));

      const response = await callback(await callbackRequest("code=abc&state=state-1"));

      expect(response.headers.get("location")).toBe("http://localhost:3000/login?error=signin_failed");
      expect(setCookie(response, SESSION_COOKIE)).toBeUndefined();
    },
  );

  it("fails safely when the result has no claims", async () => {
    msal.acquireTokenByCode.mockResolvedValue({ account: null });

    const response = await callback(await callbackRequest("code=abc&state=state-1"));

    expect(response.headers.get("location")).toBe("http://localhost:3000/login?error=signin_failed");
  });

  it("reports missing configuration", async () => {
    vi.stubEnv("AUTH_SESSION_SECRET", "");

    const response = await callback(new Request("http://localhost:3000/api/auth/callback?code=a&state=b"));

    expect(response.headers.get("location")).toBe("http://localhost:3000/login?error=config");
  });
});

describe("POST /api/auth/logout", () => {
  it("clears the session and signs out of Microsoft, returning to /login", async () => {
    const response = await logout(new Request("http://localhost:3000/api/auth/logout", { method: "POST" }));

    expect(response.status).toBe(303);
    const location = new URL(response.headers.get("location")!);
    expect(location.origin + location.pathname).toBe(
      `https://login.microsoftonline.com/${TEST_AUTH.tenantId}/oauth2/v2.0/logout`,
    );
    expect(location.searchParams.get("post_logout_redirect_uri")).toBe("http://localhost:3000/login");
    expect(setCookie(response, SESSION_COOKIE)).toBe("");
  });

  it("still clears the session when auth is not configured", async () => {
    vi.stubEnv("AZURE_AD_TENANT_ID", "");

    const response = await logout(new Request("http://localhost:3000/api/auth/logout", { method: "POST" }));

    expect(response.headers.get("location")).toBe("http://localhost:3000/login");
    expect(setCookie(response, SESSION_COOKIE)).toBe("");
  });
});
