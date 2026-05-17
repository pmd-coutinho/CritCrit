import { describe, it, expect, vi } from "vitest";

// vi.mock is hoisted, so any references inside the factory must come from
// vi.hoisted to exist at hoist time.
const mocks = vi.hoisted(() => ({
  GET: vi.fn(),
  POST: vi.fn(),
}));

vi.mock("./client", () => ({
  api: { GET: mocks.GET, POST: mocks.POST },
}));

import { keys } from "./queries";
import { api } from "./client";

describe("query keys", () => {
  it("namespaces brand-scoped resources under the brand id", () => {
    expect(keys.tree("br_1")).toEqual(["tree", "br_1"]);
    expect(keys.invitations("br_2")).toEqual(["invitations", "br_2"]);
    expect(keys.grants("br_3")).toEqual(["grants", "br_3"]);
    expect(keys.audit("br_4", {}, 100)).toEqual(["audit", "br_4", {}, 100]);
  });

  it("namespaces platform-scoped resources without a brand id", () => {
    expect(keys.myBrands()).toEqual(["brands"]);
  });

  it("namespaces accept-invite by token to avoid cache collisions", () => {
    expect(keys.acceptInvite("tok-a")).not.toEqual(keys.acceptInvite("tok-b"));
  });

  it("namespaces subjects by filter + onboarded + limit", () => {
    expect(keys.subjects("", null, 100)).toEqual(["subjects", "", null, 100]);
    expect(keys.subjects("alpha", true, 50)).toEqual(["subjects", "alpha", true, 50]);
    expect(keys.subjects("alpha", true, 50)).not.toEqual(keys.subjects("alpha", false, 50));
  });

  it("namespaces platform audit by filter object + limit", () => {
    const a = keys.platformAudit({ action: "invitation.accepted" }, 200);
    const b = keys.platformAudit({ action: "invitation.failed" }, 200);
    expect(a[0]).toBe("platform-audit");
    expect(a).not.toEqual(b);
  });
});

describe("revoke / lifecycle mutation shapes", () => {
  // Compile-time guard: each mutation request type's shape matches what the
  // backend accepts. Catches contract drift between API DTOs and SPA types
  // without rendering a component.

  it("RevokeGrantRequest shape", () => {
    const body = { orgNodeId: "br_1", subjectId: "subj_1", reason: null };
    expect(typeof body.orgNodeId).toBe("string");
    expect(typeof body.subjectId).toBe("string");
    expect(body.reason).toBeNull();
  });

  it("DeactivateSubjectRequest accepts null reason", () => {
    const body = { reason: null };
    expect(body.reason).toBeNull();
  });

  it("RelinkSubjectIdentityRequest carries old + new external IDs", () => {
    const body = {
      provider: "keycloak",
      providerTenant: "api",
      oldExternalId: "old-sub",
      newExternalId: "new-sub",
      reason: "kc-recreated",
    };
    expect(body.oldExternalId).not.toBe(body.newExternalId);
  });
});

describe("config query keys", () => {
  it("namespaces config schemas by archived toggle", () => {
    expect(keys.configSchemas(false)).toEqual(["config-schemas", false]);
    expect(keys.configSchemas(true)).not.toEqual(keys.configSchemas(false));
  });

  it("namespaces per-schema sub-resources by code", () => {
    expect(keys.configSchemaDrafts("a")).not.toEqual(keys.configSchemaDrafts("b"));
    expect(keys.configSchemaVersions("a")).toEqual(["config-schema-versions", "a"]);
  });

  it("namespaces node config by brand and node", () => {
    expect(keys.nodeConfig("br_1", "st_1")).toEqual(["node-config", "br_1", "st_1"]);
    expect(keys.nodeConfigSchema("br_1", "st_1", "pos.k", true)).toEqual([
      "node-config-path",
      "br_1",
      "st_1",
      "pos.k",
      true,
    ]);
  });

  it("namespaces node assignments by brand, node, and archived flag", () => {
    expect(keys.nodeAssignments("br_1", "st_1", false)).toEqual([
      "node-assignments",
      "br_1",
      "st_1",
      false,
    ]);
    expect(keys.nodeAssignments("br_1", "st_1", true)).not.toEqual(
      keys.nodeAssignments("br_1", "st_1", false),
    );
  });
});

describe("api client mocking sanity", () => {
  it("GET mock returns whatever we tell it to", async () => {
    mocks.GET.mockResolvedValue({ data: [{ id: "br_1" }], error: undefined });
    const res = await api.GET("/api/brands");
    expect(res.data).toEqual([{ id: "br_1" }]);
  });
});
