import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import TextToWav from "./TextToWav";

const audio = {
  id: 1,
  notificationId: "00000000-0000-0000-0000-000000000001",
  blobName: "tts/a.wav",
  audioUrl: "http://127.0.0.1:10000/devstoreaccount1/notifications/tts/a.wav?sp=r&sig=x",
  expiresAtUtc: "2026-09-30T13:00:00Z",
  contentType: "audio/wav",
  sizeBytes: 10,
  characterCount: 5,
};

describe("TextToWav", () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    fetchMock.mockReset();
  });

  it("shows a live character counter and disables the button for empty text", async () => {
    render(<TextToWav />);

    expect(screen.getByText("0 / 150")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Generate Audio" })).toBeDisabled();

    await userEvent.type(screen.getByLabelText("Text to convert"), "Hello");

    expect(screen.getByText("5 / 150")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Generate Audio" })).toBeEnabled();
  });

  it("stops accepting input at 150 characters", async () => {
    render(<TextToWav />);
    const textbox = screen.getByLabelText("Text to convert");

    await userEvent.click(textbox);
    await userEvent.paste("a".repeat(200));

    expect(textbox).toHaveValue("a".repeat(150));
    expect(screen.getByText("150 / 150")).toBeInTheDocument();
  });

  it("does not allow submitting whitespace-only text", async () => {
    render(<TextToWav />);

    await userEvent.type(screen.getByLabelText("Text to convert"), "   ");

    expect(screen.getByText("3 / 150")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Generate Audio" })).toBeDisabled();

    fireEvent.submit(screen.getByLabelText("Text to convert").closest("form")!);

    expect(screen.getByRole("alert")).toHaveTextContent("Please enter some text.");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("generates audio and shows the success message, URL and player", async () => {
    fetchMock.mockResolvedValue(Response.json(audio));
    const { container } = render(<TextToWav />);

    await userEvent.type(screen.getByLabelText("Text to convert"), "Hello");
    await userEvent.click(screen.getByRole("button", { name: "Generate Audio" }));

    expect(await screen.findByText(/Audio generated successfully/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: audio.audioUrl })).toHaveAttribute("href", audio.audioUrl);

    const player = container.querySelector("audio");
    expect(player).toHaveAttribute("src", audio.audioUrl);
    expect(player).toHaveAttribute("controls");

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/tts");
    expect(init?.body).toBe(JSON.stringify({ text: "Hello" }));
  });

  it("shows a loading state while the request is in flight", async () => {
    let resolve!: (value: Response) => void;
    fetchMock.mockReturnValue(new Promise((r) => (resolve = r)));
    render(<TextToWav />);

    await userEvent.type(screen.getByLabelText("Text to convert"), "Hello");
    await userEvent.click(screen.getByRole("button", { name: "Generate Audio" }));

    expect(screen.getByRole("button", { name: "Generating…" })).toBeDisabled();
    expect(screen.getByLabelText("Text to convert")).toBeDisabled();

    resolve(Response.json(audio));
    expect(await screen.findByText(/Audio generated successfully/)).toBeInTheDocument();
  });

  it("shows the server error message", async () => {
    fetchMock.mockResolvedValue(Response.json({ error: "Speech is down." }, { status: 502 }));
    render(<TextToWav />);

    await userEvent.type(screen.getByLabelText("Text to convert"), "Hello");
    await userEvent.click(screen.getByRole("button", { name: "Generate Audio" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Speech is down.");
  });

  it("shows a generic message for an unreadable error response", async () => {
    fetchMock.mockResolvedValue(new Response("<html>", { status: 500 }));
    render(<TextToWav />);

    await userEvent.type(screen.getByLabelText("Text to convert"), "Hello");
    await userEvent.click(screen.getByRole("button", { name: "Generate Audio" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Something went wrong while generating audio.");
  });

  it("shows a network error when the request fails", async () => {
    fetchMock.mockRejectedValue(new TypeError("Failed to fetch"));
    render(<TextToWav />);

    await userEvent.type(screen.getByLabelText("Text to convert"), "Hello");
    await userEvent.click(screen.getByRole("button", { name: "Generate Audio" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Network error.");
  });
});
