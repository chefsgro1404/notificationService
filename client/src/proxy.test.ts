// @vitest-environment node
import { NextRequest } from "next/server";
import { beforeEach, describe, expect, it } from "vitest";
import { GUEST_USER, sessionCookie, stubAuthEnv } from "@/test/auth";
import { config, proxy } from "./proxy";

async function request(path: string, cookie?: string) {
  return new NextRequest(`http://localhost:3000${path}`, { headers: cookie ? { cookie } : {} });
}

beforeEach(() => {
  stubAuthEnv();
});

describe("proxy", () => {
  it("sends signed-out visitors to /login, remembering the page", async () => {
    const home = await proxy(await request("/"));
    const logs = await proxy(await request("/audio-logs?status=All"));

    expect(home.headers.get("location")).toBe("http://localhost:3000/login");
    expect(logs.headers.get("location")).toBe(
      "http://localhost:3000/login?returnTo=%2Faudio-logs%3Fstatus%3DAll",
    );
  });

  it("returns 401 for signed-out API calls", async () => {
    const response = await proxy(await request("/api/tts"));

    expect(response.status).toBe(401);
    expect(await response.json()).toEqual({ error: "Sign in to continue." });
  });

  it("treats every request as signed out when the session secret is missing", async () => {
    const cookie = await sessionCookie();
    stubAuthEnv();
    process.env.AUTH_SESSION_SECRET = "";

    const response = await proxy(await request("/", cookie));

    expect(response.headers.get("location")).toBe("http://localhost:3000/login");
  });

  it("sends guests to /unauthorized and blocks their API calls", async () => {
    const cookie = await sessionCookie(GUEST_USER);

    const page = await proxy(await request("/notification-logs", cookie));
    const api = await proxy(await request("/api/audio-logs", cookie));
    const allowed = await proxy(await request("/unauthorized", cookie));

    expect(page.headers.get("location")).toBe("http://localhost:3000/unauthorized");
    expect(api.status).toBe(403);
    expect(allowed.headers.get("x-middleware-next")).toBe("1");
  });

  it("lets admins through", async () => {
    const response = await proxy(await request("/audio-logs", await sessionCookie()));

    expect(response.headers.get("x-middleware-next")).toBe("1");
  });

  it("does not run on the sign-in flow, login page, health checks or static files", () => {
    const matcher = new RegExp(`^${config.matcher[0]}$`);

    for (const path of ["/api/auth/login", "/api/auth/callback", "/api/health", "/api/health/live", "/login", "/_next/static/x.js", "/favicon.ico", "/chefsrhere-logo.png"]) {
      expect(matcher.test(path), path).toBe(false);
    }
    for (const path of ["/", "/audio-logs", "/api/tts", "/unauthorized", "/login-help", "/api/healthx", "/api/health/other"]) {
      expect(matcher.test(path), path).toBe(true);
    }
  });
});
