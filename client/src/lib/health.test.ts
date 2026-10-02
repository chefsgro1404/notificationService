// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from "vitest";
import { stubAuthEnv } from "@/test/auth";
import { GET as fullHealth } from "@/app/api/health/route";
import { GET as liveHealth } from "@/app/api/health/live/route";
import { checkFunctions, checkWebApp, healthResponse, worstStatus } from "./health";

const FUNCTIONS_HEALTHY = {
  status: "Healthy",
  checks: {
    serviceBus: { status: "Healthy" },
    storage: { status: "Healthy" },
    acs: { status: "Healthy" },
  },
};

describe("health", () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    fetchMock.mockReset();
    stubAuthEnv();
    vi.stubEnv("FUNCTIONS_BASE_URL", "http://functions.test/");
    vi.stubEnv("FUNCTIONS_KEY", "k");
  });

  it("worstStatus picks the most severe status", () => {
    expect(worstStatus([])).toBe("Healthy");
    expect(worstStatus(["Healthy", "Degraded"])).toBe("Degraded");
    expect(worstStatus(["Unhealthy", "Degraded", "Healthy"])).toBe("Unhealthy");
  });

  describe("checkWebApp", () => {
    it("is healthy when sign-in and backend settings are present", () => {
      expect(checkWebApp().status).toBe("Healthy");
    });

    it("names every missing setting without exposing values", () => {
      vi.stubEnv("AZURE_AD_CLIENT_SECRET", "");
      vi.stubEnv("FUNCTIONS_BASE_URL", " ");
      vi.stubEnv("FUNCTIONS_KEY", "");
      vi.stubEnv("NODE_ENV", "production");

      const block = checkWebApp();

      expect(block.status).toBe("Unhealthy");
      expect(block.description).toContain("AZURE_AD_CLIENT_SECRET");
      expect(block.description).toContain("FUNCTIONS_BASE_URL is not configured.");
      expect(block.description).toContain("FUNCTIONS_KEY is not configured.");
    });

    it("does not require FUNCTIONS_KEY outside production", () => {
      vi.stubEnv("FUNCTIONS_KEY", "");
      vi.stubEnv("NODE_ENV", "development");

      expect(checkWebApp().status).toBe("Healthy");
    });

    it("reports a generic message when the auth check throws a non-Error", async () => {
      vi.resetModules();
      vi.doMock("@/lib/auth/config", () => ({
        getAuthConfig: () => {
          throw "boom";
        },
      }));
      const { checkWebApp: isolated } = await import("./health");

      expect(isolated().description).toBe("Authentication is not configured.");
      vi.doUnmock("@/lib/auth/config");
    });
  });

  describe("checkFunctions", () => {
    it("calls the anonymous Functions health endpoint without the function key", async () => {
      fetchMock.mockResolvedValue(Response.json(FUNCTIONS_HEALTHY));

      const block = await checkFunctions();

      expect(block).toEqual({
        status: "Healthy",
        description: "The Functions app and its dependencies are healthy.",
        checks: FUNCTIONS_HEALTHY.checks,
      });
      const [url, init] = fetchMock.mock.calls[0];
      expect(url).toBe("http://functions.test/api/health");
      expect(init?.headers).toBeUndefined();
    });

    it("passes through an unhealthy result (503) and names the failing blocks", async () => {
      fetchMock.mockResolvedValue(
        Response.json(
          { status: "Unhealthy", checks: { serviceBus: { status: "Healthy" }, acs: { status: "Unhealthy" }, x: null } },
          { status: 503 },
        ),
      );

      const block = await checkFunctions();

      expect(block.status).toBe("Unhealthy");
      expect(block.description).toBe("The Functions app is Unhealthy: acs, x.");
    });

    it("treats a missing checks object as empty", async () => {
      fetchMock.mockResolvedValue(Response.json({ status: "Degraded" }));

      expect(await checkFunctions()).toMatchObject({ status: "Degraded", checks: {} });
    });

    it("is unhealthy when the response is not a health report", async () => {
      fetchMock.mockResolvedValue(new Response("<html>", { status: 404 }));
      expect((await checkFunctions()).description).toBe("The Functions app health endpoint returned HTTP 404.");

      fetchMock.mockResolvedValue(Response.json({ status: "Fine" }));
      expect((await checkFunctions()).status).toBe("Unhealthy");
    });

    it("is unhealthy on timeouts and network failures", async () => {
      const timeout = new Error("t");
      timeout.name = "TimeoutError";
      fetchMock.mockRejectedValueOnce(timeout).mockRejectedValueOnce(new TypeError("fetch failed"));

      expect((await checkFunctions()).description).toBe("The Functions app did not answer in time.");
      expect((await checkFunctions()).description).toBe("Could not reach the Functions app.");
    });

    it("is unhealthy without a backend URL and does not call fetch", async () => {
      vi.stubEnv("FUNCTIONS_BASE_URL", "");

      expect((await checkFunctions()).status).toBe("Unhealthy");
      expect(fetchMock).not.toHaveBeenCalled();
    });
  });

  it("healthResponse returns 200 for Degraded and 503 for Unhealthy, uncached", async () => {
    const degraded = healthResponse({ a: { status: "Healthy", description: "" }, b: { status: "Degraded", description: "" } });
    const unhealthy = healthResponse({ a: { status: "Unhealthy", description: "" } });

    expect(degraded.status).toBe(200);
    expect(degraded.headers.get("cache-control")).toBe("no-store");
    expect((await degraded.json()).status).toBe("Degraded");
    expect(unhealthy.status).toBe(503);
  });

  describe("routes", () => {
    it("GET /api/health combines the web app and Functions blocks", async () => {
      fetchMock.mockResolvedValue(Response.json(FUNCTIONS_HEALTHY));

      const response = await fullHealth();
      const body = await response.json();

      expect(response.status).toBe(200);
      expect(Object.keys(body.checks)).toEqual(["webApp", "functions"]);
      expect(body.checks.functions.checks.storage.status).toBe("Healthy");
    });

    it("GET /api/health is 503 when the Functions app is down", async () => {
      fetchMock.mockRejectedValue(new TypeError("fetch failed"));

      expect((await fullHealth()).status).toBe(503);
    });

    it("GET /api/health/live checks only the web app", async () => {
      const response = await liveHealth();

      expect(response.status).toBe(200);
      expect(Object.keys((await response.json()).checks)).toEqual(["webApp"]);
      expect(fetchMock).not.toHaveBeenCalled();
    });
  });
});
