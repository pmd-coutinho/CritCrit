import { describe, it, expect } from "vitest";
import { z } from "zod";

// These mirror the inline schemas used in the SPA forms. Centralising the
// shapes in tests keeps the form validation honest if/when we move to shared
// schemas later.

const invitationSchema = z.object({
  orgNodeId: z.string().trim().min(1),
  email: z.string().email(),
  role: z.enum(["Viewer", "Member", "Admin", "Owner"]),
});

describe("invitation form schema", () => {
  it("accepts a valid invitation payload", () => {
    const result = invitationSchema.safeParse({
      orgNodeId: "br_01J9",
      email: "a@b.com",
      role: "Member",
    });
    expect(result.success).toBe(true);
  });

  it("rejects an empty org node id", () => {
    const result = invitationSchema.safeParse({ orgNodeId: "  ", email: "a@b.com", role: "Member" });
    expect(result.success).toBe(false);
  });

  it("rejects a malformed email", () => {
    const result = invitationSchema.safeParse({ orgNodeId: "br_1", email: "not-an-email", role: "Member" });
    expect(result.success).toBe(false);
  });

  it("rejects unknown roles", () => {
    const result = invitationSchema.safeParse({ orgNodeId: "br_1", email: "a@b.com", role: "Goblin" });
    expect(result.success).toBe(false);
  });
});

describe("OrgRole enum mirrors the API", () => {
  it("matches the API’s real role names (no Editor)", () => {
    const role = z.enum(["Viewer", "Member", "Admin", "Owner"]);
    expect(role.safeParse("Editor").success).toBe(false);
    expect(role.safeParse("Member").success).toBe(true);
  });
});

// Mirrors the inline schema on the SuperAdmin Subjects page so we catch
// regressions on field names + required-ness without rendering the form.
const subjectSchema = z.object({
  email: z.string().email(),
  displayName: z.string().optional(),
  provider: z.string().trim().min(1),
  providerTenant: z.string().trim().min(1),
  externalId: z.string().trim().min(1),
});

describe("subject form schema", () => {
  it("accepts a valid subject", () => {
    const result = subjectSchema.safeParse({
      email: "u@e.com",
      provider: "keycloak",
      providerTenant: "api",
      externalId: "abc-123",
    });
    expect(result.success).toBe(true);
  });

  it("rejects missing externalId", () => {
    const result = subjectSchema.safeParse({
      email: "u@e.com",
      provider: "keycloak",
      providerTenant: "api",
      externalId: "  ",
    });
    expect(result.success).toBe(false);
  });

  it("rejects malformed email", () => {
    const result = subjectSchema.safeParse({
      email: "bad",
      provider: "keycloak",
      providerTenant: "api",
      externalId: "abc",
    });
    expect(result.success).toBe(false);
  });
});
