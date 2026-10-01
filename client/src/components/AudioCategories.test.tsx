import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { categoriesForAudio, validateCategoryName, type AudioCategory } from "@/lib/categories";
import AudioCategories from "./AudioCategories";

const fetchMock = vi.fn<typeof fetch>();

function category(id: number, name: string, audioId: number | null = null): AudioCategory {
  return { id, name, audioId, createdAtUtc: "2026-09-30T10:00:00Z", updatedAtUtc: "2026-09-30T10:00:00Z" };
}

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
});

describe("category helpers", () => {
  it("validates names", () => {
    expect(validateCategoryName("  ")).toBe("Enter a category name.");
    expect(validateCategoryName("a".repeat(51))).toMatch(/50 characters or fewer/);
    expect(validateCategoryName(" Support ")).toBeNull();
  });

  it("finds the categories linked to an audio", () => {
    const list = [category(1, "Support", 3), category(2, "Billing", 3), category(3, "Sales", 4)];

    expect(categoriesForAudio(list, 3)).toEqual(["Billing", "Support"]);
    expect(categoriesForAudio(null, 3)).toEqual([]);
  });
});

describe("AudioCategories", () => {
  it("lists categories with their linked audio", async () => {
    fetchMock.mockResolvedValue(Response.json([category(1, "Support", 7), category(2, "Sales")]));
    render(<AudioCategories />);

    const support = (await screen.findByText("Support")).closest("tr")!;
    const sales = screen.getByText("Sales").closest("tr")!;

    expect(within(support).getByRole("link", { name: "Audio #7" })).toHaveAttribute("href", "/audio-logs");
    expect(within(sales).getByText("Not linked")).toBeInTheDocument();
  });

  it("shows an empty state", async () => {
    fetchMock.mockResolvedValue(Response.json([]));
    render(<AudioCategories />);

    expect(await screen.findByText(/No categories yet/)).toBeInTheDocument();
  });

  it("adds a category and reloads the list", async () => {
    fetchMock.mockImplementation(async (_input, init) =>
      init?.method === "POST" ? Response.json(category(3, "Support"), { status: 201 }) : Response.json([]),
    );
    render(<AudioCategories />);
    await screen.findByText(/No categories yet/);

    expect(screen.getByRole("button", { name: "Add category" })).toBeDisabled();
    await userEvent.type(screen.getByLabelText("New category"), "  Support ");
    await userEvent.click(screen.getByRole("button", { name: "Add category" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Category “Support” was added.");
    expect(screen.getByLabelText("New category")).toHaveValue("");
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === "POST")!;
    expect(post[1]?.body).toBe(JSON.stringify({ name: "Support" }));
    await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => init?.method !== "POST")).toHaveLength(2));
  });

  it("shows duplicate, generic and network errors", async () => {
    let posts = 0;
    fetchMock.mockImplementation(async (_input, init) => {
      if (init?.method !== "POST") return Response.json([]);
      posts += 1;
      if (posts === 1) return Response.json({ error: "A category named \"Support\" already exists." }, { status: 409 });
      if (posts === 2) return new Response("oops", { status: 502 });
      throw new TypeError("offline");
    });
    render(<AudioCategories />);
    await screen.findByText(/No categories yet/);
    await userEvent.type(screen.getByLabelText("New category"), "Support");

    await userEvent.click(screen.getByRole("button", { name: "Add category" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("already exists");

    await userEvent.click(screen.getByRole("button", { name: "Add category" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Could not add the category."));

    await userEvent.click(screen.getByRole("button", { name: "Add category" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Network error."));
  });

  it("validates the name before sending", async () => {
    fetchMock.mockResolvedValue(Response.json([]));
    const { container } = render(<AudioCategories />);
    await screen.findByText(/No categories yet/);

    await userEvent.type(screen.getByLabelText("New category"), "   ");
    container.querySelector("form")!.requestSubmit();

    expect(await screen.findByRole("alert")).toHaveTextContent("Enter a category name.");
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
  });

  it("shows a load error", async () => {
    fetchMock.mockResolvedValue(Response.json({ error: "Unable to list audio categories." }, { status: 502 }));
    render(<AudioCategories />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Unable to list audio categories.");
  });
});
