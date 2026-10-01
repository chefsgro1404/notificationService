import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { AudioLogItem, NotificationLogItem, PagedResult } from "@/lib/logs";
import AudioLog from "./AudioLog";
import NotificationLog from "./NotificationLog";
import SiteNav from "./SiteNav";

const pathname = vi.hoisted(() => ({ value: "/" }));
vi.mock("next/navigation", () => ({ usePathname: () => pathname.value }));

function paged<T>(items: T[], extra: Partial<PagedResult<T>> = {}): PagedResult<T> {
  return { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length ? 1 : 0, truncated: false, ...extra };
}

function notification(id: string, status: string, extra: Partial<NotificationLogItem> = {}): NotificationLogItem {
  return {
    id,
    channel: "Email",
    recipient: `${id}@contoso.com`,
    status,
    createdAtUtc: "2026-09-30T10:00:00Z",
    updatedAtUtc: "2026-09-30T10:01:00Z",
    providerMessageId: `pm-${id}`,
    errorCode: null,
    errorMessage: null,
    retryCount: 0,
    hasAttachment: false,
    ...extra,
  };
}

function audio(id: number, status: AudioLogItem["status"], hasFile = true, errorMessage: string | null = null): AudioLogItem {
  return {
    id,
    notificationId: `00000000-0000-0000-0000-00000000000${id}`,
    text: `Audio text ${id}`,
    blobName: hasFile ? `T2A/00000000-0000-0000-0000-00000000000${id}.wav` : "",
    contentType: "audio/wav",
    sizeBytes: hasFile ? 2048 : 0,
    characterCount: 12,
    status,
    errorMessage,
    createdAtUtc: "2026-09-30T10:00:00Z",
    updatedAtUtc: "2026-09-30T10:00:00Z",
    audioUrl: status === "Active" ? `http://127.0.0.1:10000/devstoreaccount1/notifications/T2A/${id}.wav?sig=x` : null,
    audioUrlExpiresAtUtc: status === "Active" ? "2026-09-30T11:00:00Z" : null,
  };
}

const fetchMock = vi.fn<typeof fetch>();
const urls = () => fetchMock.mock.calls.map(([url]) => String(url));

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
});

describe("SiteNav", () => {
  const user = { name: "Ada Lovelace", username: "ada@contoso.com", roles: ["Admin"] };

  it("marks the current page", () => {
    pathname.value = "/audio-logs";
    render(<SiteNav user={user} />);

    expect(screen.getByRole("link", { name: "Audio Log" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Text to WAV" })).not.toHaveAttribute("aria-current");
  });

  it("marks home only on the root path", () => {
    pathname.value = "/";
    render(<SiteNav user={user} />);

    expect(screen.getByRole("link", { name: "Text to WAV" })).toHaveAttribute("aria-current", "page");
  });

  it("shows the signed-in user, their role and a sign-out form", () => {
    pathname.value = "/";
    const { container } = render(<SiteNav user={user} />);

    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.getByText("AL")).toBeInTheDocument();
    expect(screen.getByAltText("Chefs R Here")).toBeInTheDocument();

    const form = container.querySelector("form")!;
    expect(form).toHaveAttribute("action", "/api/auth/logout");
    expect(form).toHaveAttribute("method", "post");
    expect(within(form).getByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });

  it("falls back to the username and 'No role'", () => {
    render(<SiteNav user={{ name: "", username: "bob@contoso.com", roles: [] }} />);

    expect(screen.getByText("bob@contoso.com")).toBeInTheDocument();
    expect(screen.getByText("No role")).toBeInTheDocument();
    expect(screen.getByText("BC")).toBeInTheDocument();
  });

  it("shows ? when there is no name at all", () => {
    render(<SiteNav user={{ name: " ", username: "", roles: [] }} />);

    expect(screen.getByText("?")).toBeInTheDocument();
  });
});

describe("NotificationLog", () => {
  it("renders rows colored by status with error details", async () => {
    fetchMock.mockResolvedValue(
      Response.json(
        paged([
          notification("a", "Sent", { hasAttachment: true }),
          notification("b", "Failed", { errorCode: "550", errorMessage: "Mailbox unavailable", providerMessageId: null }),
          notification("c", "Mystery", { providerMessageId: null }),
        ]),
      ),
    );

    render(<NotificationLog />);

    const sent = (await screen.findByText("a@contoso.com")).closest("tr")!;
    const failed = screen.getByText("b@contoso.com").closest("tr")!;
    const unknown = screen.getByText("c@contoso.com").closest("tr")!;

    expect(sent.className).toMatch(/status-sent/);
    expect(failed.className).toMatch(/status-failed/);
    expect(unknown.className).toMatch(/status-unknown/);
    expect(within(failed).getByText("550: Mailbox unavailable")).toBeInTheDocument();
    expect(within(sent).getByText("Attachment")).toBeInTheDocument();
    expect(within(unknown).getByText("—")).toBeInTheDocument();
    expect(screen.getByText("Showing 1–3 of 3")).toBeInTheDocument();
    expect(urls()[0]).toBe("/api/notification-logs?page=1&pageSize=20");
  });

  it("applies filters, including an inclusive date range, and resets to page 1", async () => {
    fetchMock.mockImplementation(async () => Response.json(paged([], { totalCount: 0 })));
    render(<NotificationLog />);
    await screen.findByText("No notifications match these filters.");

    await userEvent.selectOptions(screen.getByLabelText("Channel"), "Email");
    await userEvent.selectOptions(screen.getByLabelText("Status"), "Failed");
    await userEvent.type(screen.getByLabelText("Recipient"), "contoso");
    await userEvent.type(screen.getByLabelText("From"), "2026-09-01");
    await userEvent.type(screen.getByLabelText("To"), "2026-09-30");
    await userEvent.click(screen.getByRole("button", { name: "Apply" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const params = new URL(urls()[1], "http://x").searchParams;
    expect(params.get("channel")).toBe("Email");
    expect(params.get("status")).toBe("Failed");
    expect(params.get("recipient")).toBe("contoso");
    expect(params.get("from")).toBe(new Date("2026-09-01T00:00:00").toISOString());
    expect(params.get("to")).toBe(new Date("2026-10-01T00:00:00").toISOString());
    expect(params.get("page")).toBe("1");

    await userEvent.click(screen.getByRole("button", { name: "Reset" }));
    await waitFor(() => expect(urls().at(-1)).toBe("/api/notification-logs?page=1&pageSize=20"));
  });

  it("rejects a From date after the To date", async () => {
    fetchMock.mockImplementation(async () => Response.json(paged([])));
    render(<NotificationLog />);
    await screen.findByText("No notifications match these filters.");

    await userEvent.type(screen.getByLabelText("From"), "2026-09-30");
    await userEvent.type(screen.getByLabelText("To"), "2026-09-01");
    await userEvent.click(screen.getByRole("button", { name: "Apply" }));

    expect(screen.getByRole("alert")).toHaveTextContent("The From date must be on or before the To date.");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("pages through results and changes page size", async () => {
    fetchMock.mockImplementation(async (input) => {
      const page = Number(new URL(String(input), "http://x").searchParams.get("page"));
      return Response.json(paged([notification(`p${page}`, "Sent")], { page, totalCount: 45, totalPages: 3 }));
    });

    render(<NotificationLog />);
    await screen.findByText("p1@contoso.com");

    await userEvent.click(screen.getByRole("button", { name: "Next" }));
    await screen.findByText("p2@contoso.com");
    expect(screen.getByText("Page 2 of 3")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Last page" }));
    await screen.findByText("p3@contoso.com");
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: "Previous" }));
    await screen.findByText("p2@contoso.com");

    await userEvent.click(screen.getByRole("button", { name: "First page" }));
    await screen.findByText("p1@contoso.com");

    await userEvent.selectOptions(screen.getByLabelText("Rows"), "50");
    await waitFor(() => expect(urls().at(-1)).toBe("/api/notification-logs?page=1&pageSize=50"));
  });

  it("shows server errors, truncation warnings and supports refresh", async () => {
    fetchMock.mockResolvedValueOnce(Response.json({ error: "Unable to read the notification log." }, { status: 502 }));
    render(<NotificationLog />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Unable to read the notification log.");

    fetchMock.mockResolvedValueOnce(Response.json(paged([notification("t", "Sent")], { truncated: true, totalCount: 5000 })));
    await userEvent.click(screen.getByRole("button", { name: "Refresh" }));

    expect(await screen.findByText(/Only the first 5000 matching records were read/)).toBeInTheDocument();
  });

  it("shows generic and network errors", async () => {
    fetchMock.mockResolvedValueOnce(new Response("<html>", { status: 500 }));
    render(<NotificationLog />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Request failed (500).");

    fetchMock.mockRejectedValueOnce(new TypeError("offline"));
    await userEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Network error."));
  });
});

describe("AudioLog", () => {
  let categoryList: unknown = [];

  function routeFetch(list: () => PagedResult<AudioLogItem>, patchResponse?: () => Response) {
    fetchMock.mockImplementation(async (input, init) => {
      if (init?.method === "PATCH") {
        return patchResponse ? patchResponse() : Response.json({});
      }
      if (String(input) === "/api/audio-categories") {
        return Response.json(categoryList);
      }
      return Response.json(list());
    });
  }

  beforeEach(() => {
    categoryList = [];
  });

  it("lists audio with players only for active items and status-based actions", async () => {
    routeFetch(() => paged([audio(3, "Active"), audio(2, "Inactive"), audio(1, "Deleted")]));
    const { container } = render(<AudioLog />);

    const active = (await screen.findByText("Audio text 3")).closest("tr")!;
    const inactive = screen.getByText("Audio text 2").closest("tr")!;
    const deleted = screen.getByText("Audio text 1").closest("tr")!;

    expect(active.className).toMatch(/status-active/);
    expect(inactive.className).toMatch(/status-inactive/);
    expect(deleted.className).toMatch(/status-deleted/);
    expect(container.querySelectorAll("audio")).toHaveLength(1);
    expect(within(active).getByRole("button", { name: "Deactivate audio #3" })).toBeInTheDocument();
    expect(within(inactive).getByRole("button", { name: "Activate audio #2" })).toBeInTheDocument();
    expect(within(deleted).getByRole("button", { name: "Restore audio #1" })).toBeInTheDocument();
    expect(within(deleted).getByText("Not playable")).toBeInTheDocument();
    expect(within(active).getByText("2.0 KB")).toBeInTheDocument();
    expect(urls()[0]).toBe("/api/audio-logs?page=1&pageSize=20");
  });

  it("shows not-generated audio with its error, and only offers Delete", async () => {
    routeFetch(() =>
      paged([
        audio(9, "AudioNotGenerated", false, "Azure AI Speech returned 401 (Unauthorized)."),
        audio(8, "AudioNotGenerated", false),
        audio(7, "Deleted", false),
      ]),
    );
    render(<AudioLog />);

    const failed = (await screen.findByText("Audio text 9")).closest("tr")!;
    const pending = screen.getByText("Audio text 8").closest("tr")!;
    const deletedWithoutFile = screen.getByText("Audio text 7").closest("tr")!;

    expect(failed.className).toMatch(/status-audionotgenerated/);
    expect(within(failed).getByText("Audio not generated")).toBeInTheDocument();
    expect(within(failed).getByText("Azure AI Speech returned 401 (Unauthorized).")).toBeInTheDocument();
    expect(within(failed).getByText("00000000-0000-0000-0000-000000000009")).toBeInTheDocument();
    expect(within(failed).getByText("—")).toBeInTheDocument();
    expect(within(failed).getAllByRole("button").map((b) => b.textContent)).toEqual(["Delete"]);
    expect(within(pending).getByText("No audio file")).toBeInTheDocument();
    expect(within(deletedWithoutFile).queryAllByRole("button")).toHaveLength(0);
  });

  it("filters by status and text", async () => {
    routeFetch(() => paged([]));
    render(<AudioLog />);
    await screen.findByText("No audio matches these filters.");

    await userEvent.selectOptions(screen.getByLabelText("Status"), "AudioNotGenerated");
    await userEvent.selectOptions(screen.getByLabelText("Status"), "All");
    await userEvent.type(screen.getByLabelText("Text"), "hello");
    await userEvent.click(screen.getByRole("button", { name: "Apply" }));

    await waitFor(() => expect(urls().at(-1)).toBe("/api/audio-logs?status=All&search=hello&page=1&pageSize=20"));

    await userEvent.click(screen.getByRole("button", { name: "Reset" }));
    await waitFor(() => expect(urls().at(-1)).toBe("/api/audio-logs?page=1&pageSize=20"));
  });

  it("deactivates audio and reloads the list", async () => {
    routeFetch(() => paged([audio(3, "Active")]));
    render(<AudioLog />);

    await userEvent.click(await screen.findByRole("button", { name: "Deactivate audio #3" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Audio #3 is now inactive.");
    const patchCall = fetchMock.mock.calls.find(([, init]) => init?.method === "PATCH")!;
    expect(patchCall[0]).toBe("/api/audio-logs/3/status");
    expect(patchCall[1]?.body).toBe(JSON.stringify({ status: "Inactive" }));
    await waitFor(() => expect(urls().filter((u) => u.startsWith("/api/audio-logs?"))).toHaveLength(2));
  });

  it("asks for confirmation before deleting", async () => {
    routeFetch(() => paged([audio(4, "Inactive")]));
    const confirm = vi.spyOn(window, "confirm").mockReturnValueOnce(false).mockReturnValueOnce(true);
    render(<AudioLog />);

    const button = await screen.findByRole("button", { name: "Delete audio #4" });

    await userEvent.click(button);
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "PATCH")).toBe(false);

    await userEvent.click(button);
    expect(await screen.findByRole("status")).toHaveTextContent("Audio #4 is now deleted.");
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  it("shows status change errors", async () => {
    routeFetch(
      () => paged([audio(5, "Deleted")]),
      () => Response.json({ error: "Audio 5 was not found." }, { status: 404 }),
    );
    render(<AudioLog />);

    await userEvent.click(await screen.findByRole("button", { name: "Restore audio #5" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Audio 5 was not found.");
  });

  it("shows a generic message for unreadable errors and network failures", async () => {
    routeFetch(
      () => paged([audio(6, "Active")], { truncated: true, totalCount: 5000 }),
      () => new Response("oops", { status: 502 }),
    );
    render(<AudioLog />);

    expect(await screen.findByText("Only the first 5000 matching records were read.")).toBeInTheDocument();

    await userEvent.click(await screen.findByRole("button", { name: "Deactivate audio #6" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not update the audio status.");

    fetchMock.mockImplementation(async (input, init) => {
      if (init?.method === "PATCH") throw new TypeError("offline");
      return Response.json(paged([audio(6, "Active")]));
    });
    await userEvent.click(screen.getByRole("button", { name: "Deactivate audio #6" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Network error."));
  });

  it("shows the categories linked to each audio and offers Link only for active audio", async () => {
    categoryList = [
      { id: 1, name: "Support", audioId: 3, createdAtUtc: "2026-09-30T10:00:00Z", updatedAtUtc: "2026-09-30T10:00:00Z" },
      { id: 2, name: "Billing", audioId: 3, createdAtUtc: "2026-09-30T10:00:00Z", updatedAtUtc: "2026-09-30T10:00:00Z" },
      { id: 3, name: "Sales", audioId: null, createdAtUtc: "2026-09-30T10:00:00Z", updatedAtUtc: "2026-09-30T10:00:00Z" },
    ];
    routeFetch(() => paged([audio(3, "Active"), audio(2, "Inactive")]));
    render(<AudioLog />);

    const active = (await screen.findByText("Audio text 3")).closest("tr")!;
    const inactive = screen.getByText("Audio text 2").closest("tr")!;

    await waitFor(() => expect(within(active).getByText("Support")).toBeInTheDocument());
    expect(within(active).getByText("Billing")).toBeInTheDocument();
    expect(within(active).getByRole("button", { name: "Link audio #3 to a category" })).toBeInTheDocument();
    expect(within(inactive).queryByRole("button", { name: /Link audio/ })).not.toBeInTheDocument();
  });

  it("links audio to a category from the pop-up", async () => {
    categoryList = [
      { id: 1, name: "Support", audioId: 9, createdAtUtc: "2026-09-30T10:00:00Z", updatedAtUtc: "2026-09-30T10:00:00Z" },
      { id: 2, name: "Sales", audioId: null, createdAtUtc: "2026-09-30T10:00:00Z", updatedAtUtc: "2026-09-30T10:00:00Z" },
      { id: 4, name: "Alerts", audioId: 3, createdAtUtc: "2026-09-30T10:00:00Z", updatedAtUtc: "2026-09-30T10:00:00Z" },
    ];
    routeFetch(() => paged([audio(3, "Active")]));
    const originalImpl = fetchMock.getMockImplementation()!;
    fetchMock.mockImplementation(async (input, init) =>
      init?.method === "PUT"
        ? Response.json({ id: 1, name: "Support", audioId: 3, createdAtUtc: "x", updatedAtUtc: "x" })
        : originalImpl(input, init),
    );
    render(<AudioLog />);

    await userEvent.click(await screen.findByRole("button", { name: "Link audio #3 to a category" }));

    const dialog = screen.getByRole("dialog", { name: "Link audio #3 to a category" });
    expect(within(dialog).getByText("Currently audio #9")).toBeInTheDocument();
    expect(within(dialog).getByText("No audio linked")).toBeInTheDocument();
    expect(within(dialog).getByText("Linked to this audio")).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Save" })).toBeDisabled();

    await userEvent.click(within(dialog).getByLabelText(/Support/));
    await userEvent.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Audio #3 is now linked to “Support”.");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    const put = fetchMock.mock.calls.find(([, init]) => init?.method === "PUT")!;
    expect(put[0]).toBe("/api/audio-categories/1/audio");
    expect(put[1]?.body).toBe(JSON.stringify({ audioId: 3 }));
    await waitFor(() => expect(urls().filter((u) => u === "/api/audio-categories")).toHaveLength(2));
  });

  it("shows link errors in the pop-up and closes it with Cancel, Escape or the backdrop", async () => {
    categoryList = [{ id: 1, name: "Support", audioId: null, createdAtUtc: "x", updatedAtUtc: "x" }];
    routeFetch(() => paged([audio(3, "Active")]));
    const originalImpl = fetchMock.getMockImplementation()!;
    let putCalls = 0;
    fetchMock.mockImplementation(async (input, init) => {
      if (init?.method !== "PUT") return originalImpl(input, init);
      putCalls += 1;
      if (putCalls === 1) return Response.json({ error: "Only active audio can be linked; audio 3 is Inactive." }, { status: 409 });
      if (putCalls === 2) return new Response("oops", { status: 502 });
      throw new TypeError("offline");
    });
    render(<AudioLog />);

    const open = async () => {
      await userEvent.click(await screen.findByRole("button", { name: "Link audio #3 to a category" }));
      return screen.getByRole("dialog");
    };

    let dialog = await open();
    await userEvent.click(within(dialog).getByLabelText(/Support/));
    await userEvent.click(within(dialog).getByRole("button", { name: "Save" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Only active audio can be linked");

    await userEvent.click(within(dialog).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(within(dialog).getByRole("alert")).toHaveTextContent("Could not link the category."));

    await userEvent.click(within(dialog).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(within(dialog).getByRole("alert")).toHaveTextContent("Network error."));

    await userEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    dialog = await open();
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    dialog = await open();
    await userEvent.click(dialog.parentElement!);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("tells the user to add a category first when there are none", async () => {
    routeFetch(() => paged([audio(3, "Active")]));
    render(<AudioLog />);

    await userEvent.click(await screen.findByRole("button", { name: "Link audio #3 to a category" }));

    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("link", { name: "Add a category" })).toHaveAttribute("href", "/audio-categories");
  });

  it("shows a load error in the pop-up when categories cannot be read", async () => {
    categoryList = null;
    fetchMock.mockImplementation(async (input) =>
      String(input) === "/api/audio-categories"
        ? Response.json({ error: "Unable to list audio categories." }, { status: 502 })
        : Response.json(paged([audio(3, "Active")])),
    );
    render(<AudioLog />);

    await userEvent.click(await screen.findByRole("button", { name: "Link audio #3 to a category" }));

    await waitFor(() =>
      expect(within(screen.getByRole("dialog")).getByRole("alert")).toHaveTextContent("Unable to list audio categories."),
    );
  });

  it("pages and refreshes", async () => {
    routeFetch(() => paged([audio(1, "Active")], { totalCount: 30, totalPages: 2 }));
    render(<AudioLog />);
    await screen.findByText("Audio text 1");

    await userEvent.click(screen.getByRole("button", { name: "Next" }));
    await waitFor(() => expect(urls().at(-1)).toBe("/api/audio-logs?page=2&pageSize=20"));

    await userEvent.selectOptions(screen.getByLabelText("Rows"), "10");
    await waitFor(() => expect(urls().at(-1)).toBe("/api/audio-logs?page=1&pageSize=10"));

    const before = fetchMock.mock.calls.length;
    await userEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(fetchMock.mock.calls.length).toBe(before + 1));
  });
});
