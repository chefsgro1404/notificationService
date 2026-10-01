import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ADMIN_USER, GUEST_USER } from "@/test/auth";
import LoginPage from "./login/page";
import UnauthorizedPage from "./unauthorized/page";

const auth = vi.hoisted(() => ({ getSessionUser: vi.fn() }));

vi.mock("@/lib/auth/guard", () => ({ getSessionUser: auth.getSessionUser }));
vi.mock("next/navigation", () => ({
  redirect: vi.fn((url: string) => {
    throw new Error(`REDIRECT:${url}`);
  }),
}));

async function renderLogin(params: { error?: string; returnTo?: string } = {}) {
  render(await LoginPage({ searchParams: Promise.resolve(params) }));
}

beforeEach(() => {
  auth.getSessionUser.mockReset().mockResolvedValue(null);
});

describe("Login page", () => {
  it("shows the logo and a Microsoft sign-in link", async () => {
    await renderLogin();

    expect(screen.getByRole("heading", { name: "Welcome back" })).toBeInTheDocument();
    expect(screen.getByAltText("Chefs R Here — Cravings delivered")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Sign in with Microsoft" })).toHaveAttribute("href", "/api/auth/login");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("passes a safe return path to the sign-in route", async () => {
    await renderLogin({ returnTo: "/notification-logs" });

    expect(screen.getByRole("link", { name: "Sign in with Microsoft" })).toHaveAttribute(
      "href",
      "/api/auth/login?returnTo=%2Fnotification-logs",
    );
  });

  it.each([
    ["cancelled", "Sign-in was cancelled"],
    ["expired", "took too long"],
    ["config", "isn't configured"],
    ["something-else", "couldn't sign you in"],
  ])("explains the %s error", async (error, text) => {
    await renderLogin({ error });

    expect(screen.getByRole("alert")).toHaveTextContent(text);
  });

  it("sends signed-in admins on to their page and guests to /unauthorized", async () => {
    auth.getSessionUser.mockResolvedValue(ADMIN_USER);
    await expect(renderLogin({ returnTo: "/audio-logs" })).rejects.toThrow("REDIRECT:/audio-logs");

    auth.getSessionUser.mockResolvedValue(GUEST_USER);
    await expect(renderLogin()).rejects.toThrow("REDIRECT:/unauthorized");
  });

  it("still renders when auth is not configured", async () => {
    auth.getSessionUser.mockRejectedValue(new Error("Authentication is not configured."));

    await renderLogin();

    expect(screen.getByRole("link", { name: "Sign in with Microsoft" })).toBeInTheDocument();
  });
});

describe("Unauthorized page", () => {
  it("explains that the Admin role is required and offers sign out", async () => {
    auth.getSessionUser.mockResolvedValue(GUEST_USER);

    const { container } = render(await UnauthorizedPage());

    expect(screen.getByRole("heading", { name: /don.t have access yet/ })).toBeInTheDocument();
    expect(screen.getByText("Gus Guest")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Your role: Guest.");
    expect(container.querySelector("form")).toHaveAttribute("action", "/api/auth/logout");
  });

  it("handles users with no role or no display name", async () => {
    auth.getSessionUser.mockResolvedValue({ ...GUEST_USER, name: "", roles: [] });

    render(await UnauthorizedPage());

    expect(screen.getByText("gus@contoso.com")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("none assigned");
  });

  it("redirects signed-out users to login and admins home", async () => {
    await expect(UnauthorizedPage()).rejects.toThrow("REDIRECT:/login");

    auth.getSessionUser.mockResolvedValue(ADMIN_USER);
    await expect(UnauthorizedPage()).rejects.toThrow("REDIRECT:/");
  });
});
