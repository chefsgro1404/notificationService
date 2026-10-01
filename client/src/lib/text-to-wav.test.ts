import { describe, expect, it } from "vitest";
import {
  countCharacters,
  isGeneratedAudio,
  limitCharacters,
  MAX_CHARACTERS,
  validateText,
} from "./text-to-wav";

describe("countCharacters", () => {
  it("counts code points so an emoji is one character", () => {
    expect(countCharacters("hi 👋")).toBe(4);
  });
});

describe("limitCharacters", () => {
  it("returns short text unchanged", () => {
    expect(limitCharacters("hello")).toBe("hello");
  });

  it("cuts text to the maximum without splitting an emoji", () => {
    const text = "👋".repeat(MAX_CHARACTERS + 5);
    const limited = limitCharacters(text);

    expect(countCharacters(limited)).toBe(MAX_CHARACTERS);
    expect(limited).toBe("👋".repeat(MAX_CHARACTERS));
  });
});

describe("validateText", () => {
  it.each(["", "   ", "\n\t"])("rejects blank text %j", (text) => {
    expect(validateText(text)).toBe("Please enter some text.");
  });

  it("accepts text at the limit", () => {
    expect(validateText("a".repeat(MAX_CHARACTERS))).toBeNull();
  });

  it("rejects text over the limit", () => {
    expect(validateText("a".repeat(MAX_CHARACTERS + 1))).toMatch(/150 characters or fewer/);
  });

  it("ignores surrounding whitespace when counting", () => {
    expect(validateText(`  ${"a".repeat(MAX_CHARACTERS)}  `)).toBeNull();
  });
});

describe("isGeneratedAudio", () => {
  const valid = { audioUrl: "https://acct.blob.core.windows.net/c/a.wav?sig=x", expiresAtUtc: "2026-01-01T00:00:00Z" };

  it("accepts an http(s) audio URL", () => {
    expect(isGeneratedAudio(valid)).toBe(true);
    expect(isGeneratedAudio({ ...valid, audioUrl: "http://127.0.0.1:10000/devstoreaccount1/a.wav" })).toBe(true);
  });

  it.each([
    null,
    "text",
    {},
    { audioUrl: 1, expiresAtUtc: "x" },
    { audioUrl: "https://x", expiresAtUtc: 5 },
    { ...valid, audioUrl: "javascript:alert(1)" },
    { ...valid, audioUrl: "not a url" },
  ])("rejects %j", (value) => {
    expect(isGeneratedAudio(value)).toBe(false);
  });
});
