import { describe, it, expect } from "vitest";
import { z } from "zod";

describe("smoke", () => {
  it("zod validates an email schema", () => {
    const schema = z.object({ email: z.string().email() });
    expect(schema.safeParse({ email: "x@y.com" }).success).toBe(true);
    expect(schema.safeParse({ email: "not-an-email" }).success).toBe(false);
  });

  it("zod refines an OrgRole", () => {
    const role = z.enum(["Viewer", "Editor", "Admin", "Owner"]);
    expect(role.safeParse("Admin").success).toBe(true);
    expect(role.safeParse("Goblin").success).toBe(false);
  });
});
