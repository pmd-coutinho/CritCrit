import { describe, it, expect } from "vitest";
import { errorMessage } from "./errors";

describe("errorMessage", () => {
  it("returns the `error` field from RFC-style domain responses", () => {
    expect(errorMessage({ error: "Brand not found." })).toBe("Brand not found.");
  });

  it("falls back to `detail` for ProblemDetails", () => {
    expect(errorMessage({ detail: "Conflict" })).toBe("Conflict");
  });

  it("falls back to `title` when neither error nor detail are present", () => {
    expect(errorMessage({ title: "Bad Request" })).toBe("Bad Request");
  });

  it("falls back to `message` for plain Error-like shapes", () => {
    expect(errorMessage({ message: "Network" })).toBe("Network");
  });

  it("unwraps native Error instances", () => {
    expect(errorMessage(new Error("Boom"))).toBe("Boom");
  });

  it("stringifies anything else", () => {
    expect(errorMessage(42)).toBe("42");
    expect(errorMessage(null)).toBe("null");
  });
});
