import { describe, expect, it } from "vitest";
import {
  endOfLocalDayUtc,
  formatBytes,
  formatDateTime,
  NOTIFICATION_STATUSES,
  startOfLocalDayUtc,
  statusKey,
  toQueryString,
} from "./logs";

describe("statusKey", () => {
  it("lower-cases known statuses and maps others to unknown", () => {
    expect(statusKey("Sent", NOTIFICATION_STATUSES)).toBe("sent");
    expect(statusKey("Exploded", NOTIFICATION_STATUSES)).toBe("unknown");
  });
});

describe("local day boundaries", () => {
  it("covers the whole selected local day", () => {
    const start = new Date(startOfLocalDayUtc("2026-09-30"));
    const end = new Date(endOfLocalDayUtc("2026-09-30"));

    expect(start.getFullYear()).toBe(2026);
    expect(start.getMonth()).toBe(8);
    expect(start.getDate()).toBe(30);
    expect(start.getHours()).toBe(0);
    expect(end.getDate()).toBe(1);
    expect(end.getMonth()).toBe(9);
    expect(end.getHours()).toBe(0);
  });
});

describe("formatters", () => {
  it("formats bytes", () => {
    expect(formatBytes(512)).toBe("512 B");
    expect(formatBytes(2048)).toBe("2.0 KB");
    expect(formatBytes(3 * 1024 * 1024)).toBe("3.0 MB");
  });

  it("returns invalid dates unchanged", () => {
    expect(formatDateTime("not a date")).toBe("not a date");
    expect(formatDateTime("2026-09-30T10:00:00Z")).not.toBe("2026-09-30T10:00:00Z");
  });
});

describe("toQueryString", () => {
  it("drops empty values and trims the rest", () => {
    expect(toQueryString({ a: " x ", b: "", c: undefined, d: 2, e: "  " })).toBe("a=x&d=2");
  });
});
