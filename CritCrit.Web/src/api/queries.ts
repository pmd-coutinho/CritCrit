import { useQuery, useMutation, useQueryClient } from "@tanstack/vue-query";
import { computed, type MaybeRefOrGetter, toValue } from "vue";
import { api } from "./client";
import type {
  ArchiveOrgNodeRequest,
  CreateBrandRequest,
  CreateInvitationRequest,
  CreatePlainOrgNodeRequest,
  CreateStoreRequest,
  CreateDeviceRequest,
  CreateSubjectRequest,
  DowngradeOwnerRequest,
  GrantOwnerRequest,
  GrantRoleRequest,
  HardDeleteOrgNodeRequest,
  InvitationResponse,
  MoveOrgNodeRequest,
  OrgNodeResponse,
  OrgTreeNodeResponse,
  RevokeOwnerRequest,
  RevokeGrantRequest,
  SetGrantExpirationRequest,
  DeactivateSubjectRequest,
  ReactivateSubjectRequest,
  RelinkSubjectIdentityRequest,
  SubjectResponse,
} from "./generated";

export type AuditFilter = {
  action?: string;
  category?: string;
  severity?: string;
  from?: string;
  to?: string;
  tenantId?: string;
  targetOrgNodeId?: string;
  subjectId?: string;
  actorExternalId?: string;
  supportId?: string;
};

export const keys = {
  myBrands: () => ["brands"] as const,
  grants: (brandId: string) => ["grants", brandId] as const,
  tree: (brandId: string) => ["tree", brandId] as const,
  invitations: (brandId: string) => ["invitations", brandId] as const,
  acceptInvite: (token: string) => ["accept-invite", token] as const,
  audit: (brandId: string, filter: AuditFilter, limit: number) => ["audit", brandId, filter, limit] as const,
  subjects: (filter: string, onboarded: boolean | null, limit: number) =>
    ["subjects", filter, onboarded, limit] as const,
  platformAudit: (filter: AuditFilter, limit: number) => ["platform-audit", filter, limit] as const,
  configSchemas: (includeArchived: boolean) => ["config-schemas", includeArchived] as const,
  configSchema: (code: string) => ["config-schema", code] as const,
  configSchemaVersions: (code: string) => ["config-schema-versions", code] as const,
  configSchemaDrafts: (code: string) => ["config-schema-drafts", code] as const,
  nodeConfig: (brandId: string, nodeId: string) => ["node-config", brandId, nodeId] as const,
  nodeConfigSchema: (brandId: string, nodeId: string, path: string, meta: boolean) =>
    ["node-config-path", brandId, nodeId, path, meta] as const,
  nodeAssignments: (brandId: string, nodeId: string, includeArchived: boolean) =>
    ["node-assignments", brandId, nodeId, includeArchived] as const,
};

function unwrap<T>(value: { data?: T; error?: unknown; response?: Response }): T {
  if (value.error || !value.data) {
    const supportId = value.response?.headers.get("X-CritCrit-Support-Id");
    if (value.error && typeof value.error === "object" && supportId) {
      (value.error as Record<string, unknown>).supportId ??= supportId;
    }
    throw value.error ?? new Error("Request failed");
  }
  return value.data;
}

export function useCreateBrand() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateBrandRequest) => {
      const res = await api.POST("/api/platform/brands", { body });
      return unwrap<OrgNodeResponse>(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.myBrands() }),
  });
}

export function useMyBrands() {
  return useQuery({
    queryKey: keys.myBrands(),
    queryFn: async () => {
      const res = await api.GET("/api/brands");
      return unwrap(res);
    },
  });
}

export function useBrandGrants(brandId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => keys.grants(toValue(brandId))),
    queryFn: async () => {
      const res = await api.GET("/api/brands/{brandId}/access-grants", {
        params: { path: { brandId: toValue(brandId) } },
      });
      return unwrap(res);
    },
  });
}

export function useBrandTree(brandId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => keys.tree(toValue(brandId))),
    queryFn: async () => {
      const res = await api.GET("/api/brands/{brandId}/tree", {
        params: { path: { brandId: toValue(brandId) } },
      });
      return unwrap<OrgTreeNodeResponse>(res);
    },
  });
}

export function useInvitations(brandId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => keys.invitations(toValue(brandId))),
    queryFn: async () => {
      const res = await api.GET("/api/brands/{brandId}/invitations", {
        params: { path: { brandId: toValue(brandId) } },
      });
      return unwrap<InvitationResponse[]>(res);
    },
  });
}

export function useCreateInvitation(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateInvitationRequest) => {
      const res = await api.POST("/api/brands/{brandId}/invitations", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      return unwrap<InvitationResponse>(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.invitations(toValue(brandId)) }),
  });
}

export function useCancelInvitation(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ invitationId, reason }: { invitationId: string; reason: string | null }) => {
      const res = await api.POST("/api/brands/{brandId}/invitations/{invitationId}/cancel", {
        params: { path: { brandId: toValue(brandId), invitationId } },
        body: { reason },
      });
      return unwrap<InvitationResponse>(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.invitations(toValue(brandId)) }),
  });
}

export function useResendInvitation(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (invitationId: string) => {
      // Wolverine.Http codegen tries to deserialize the body for POST handlers
      // even when there is no body parameter on the C# side. Send an explicit
      // empty JSON object so deserialization doesn't fail on empty input.
      const res = await api.POST("/api/brands/{brandId}/invitations/{invitationId}/resend", {
        params: { path: { brandId: toValue(brandId), invitationId } },
        body: {} as never,
        headers: { "Content-Type": "application/json" },
      });
      return unwrap<InvitationResponse>(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.invitations(toValue(brandId)) }),
  });
}

export function useCreateCountry(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreatePlainOrgNodeRequest) => {
      const res = await api.POST("/api/brands/{brandId}/countries", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tree(toValue(brandId)) }),
  });
}

export function useCreateFranchise(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreatePlainOrgNodeRequest) => {
      const res = await api.POST("/api/brands/{brandId}/franchises", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tree(toValue(brandId)) }),
  });
}

export function useCreateStore(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateStoreRequest) => {
      const res = await api.POST("/api/brands/{brandId}/stores", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tree(toValue(brandId)) }),
  });
}

export function useCreateDevice(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateDeviceRequest) => {
      const res = await api.POST("/api/brands/{brandId}/devices", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tree(toValue(brandId)) }),
  });
}

export function useBrandAudit(brandId: MaybeRefOrGetter<string>, filter: MaybeRefOrGetter<AuditFilter> = {}, limit = 100) {
  return useQuery({
    queryKey: computed(() => keys.audit(toValue(brandId), toValue(filter), limit)),
    queryFn: async () => {
      const query = compactAuditQuery(toValue(filter), limit);
      const res = await api.GET("/api/brands/{brandId}/audit", {
        params: { path: { brandId: toValue(brandId) }, query },
      });
      return unwrap(res);
    },
  });
}

export function useArchiveNode(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ nodeId, body }: { nodeId: string; body: ArchiveOrgNodeRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/archive", {
        params: { path: { brandId: toValue(brandId), nodeId } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tree(toValue(brandId)) }),
  });
}

export function useRestoreNode(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (nodeId: string) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/restore", {
        params: { path: { brandId: toValue(brandId), nodeId } },
        body: {} as never,
        headers: { "Content-Type": "application/json" },
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tree(toValue(brandId)) }),
  });
}

export function useMoveNode(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ nodeId, body }: { nodeId: string; body: MoveOrgNodeRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/move", {
        params: { path: { brandId: toValue(brandId), nodeId } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tree(toValue(brandId)) }),
  });
}

export function useHardDeleteNode(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ nodeId, body }: { nodeId: string; body: HardDeleteOrgNodeRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/hard-delete", {
        params: { path: { brandId: toValue(brandId), nodeId } },
        body,
      });
      return res.response.ok ? null : (() => { throw res.error ?? new Error("Hard-delete failed"); })();
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tree(toValue(brandId)) }),
  });
}

export function useGrantRole(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: GrantRoleRequest) => {
      const res = await api.POST("/api/brands/{brandId}/access-grants", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.grants(toValue(brandId)) }),
  });
}

export function useGrantOwner(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: GrantOwnerRequest) => {
      const res = await api.POST("/api/brands/{brandId}/owners", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.grants(toValue(brandId)) }),
  });
}

export function useDowngradeOwner(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ subjectId, body }: { subjectId: string; body: DowngradeOwnerRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/owners/{subjectId}/downgrade", {
        params: { path: { brandId: toValue(brandId), subjectId } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.grants(toValue(brandId)) }),
  });
}

export function useRevokeOwner(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ subjectId, body }: { subjectId: string; body: RevokeOwnerRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/owners/{subjectId}/revoke", {
        params: { path: { brandId: toValue(brandId), subjectId } },
        body,
      });
      return res.response.ok ? null : (() => { throw res.error ?? new Error("Revoke failed"); })();
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.grants(toValue(brandId)) }),
  });
}

export function useRevokeGrant(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: RevokeGrantRequest) => {
      const res = await api.POST("/api/brands/{brandId}/access-grants/revoke", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      if (!res.response.ok) throw (res as { error?: unknown }).error ?? new Error("Request failed");
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.grants(toValue(brandId)) }),
  });
}

export function useSetGrantExpiration(brandId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: SetGrantExpirationRequest) => {
      const res = await api.POST("/api/brands/{brandId}/access-grants/expiration", {
        params: { path: { brandId: toValue(brandId) } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.grants(toValue(brandId)) }),
  });
}

export function useDeactivateSubject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ subjectId, body }: { subjectId: string; body: DeactivateSubjectRequest }) => {
      const res = await api.POST("/api/platform/subjects/{subjectId}/deactivate", {
        params: { path: { subjectId } },
        body,
      });
      if (!res.response.ok) throw (res as { error?: unknown }).error ?? new Error("Request failed");
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["subjects"] }),
  });
}

export function useReactivateSubject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ subjectId, body }: { subjectId: string; body: ReactivateSubjectRequest }) => {
      const res = await api.POST("/api/platform/subjects/{subjectId}/reactivate", {
        params: { path: { subjectId } },
        body,
      });
      if (!res.response.ok) throw (res as { error?: unknown }).error ?? new Error("Request failed");
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["subjects"] }),
  });
}

export function useRelinkSubject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ subjectId, body }: { subjectId: string; body: RelinkSubjectIdentityRequest }) => {
      const res = await api.POST("/api/platform/subjects/{subjectId}/relink", {
        params: { path: { subjectId } },
        body,
      });
      if (!res.response.ok) throw (res as { error?: unknown }).error ?? new Error("Request failed");
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["subjects"] }),
  });
}

export function useSubjects(
  filter: MaybeRefOrGetter<string>,
  onboarded: MaybeRefOrGetter<boolean | null> = () => null,
  limit = 100,
) {
  return useQuery({
    queryKey: computed(() => keys.subjects(toValue(filter), toValue(onboarded), limit)),
    queryFn: async () => {
      const query: Record<string, unknown> = { limit };
      const f = toValue(filter).trim();
      if (f) query.emailContains = f;
      const ob = toValue(onboarded);
      if (ob !== null) query.onboarded = ob;
      const res = await api.GET("/api/platform/subjects", { params: { query } });
      return unwrap(res);
    },
  });
}

export function useCreateSubject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateSubjectRequest) => {
      const res = await api.POST("/api/platform/subjects", { body });
      return unwrap<SubjectResponse>(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["subjects"] }),
  });
}

export function usePlatformAudit(
  filter: MaybeRefOrGetter<AuditFilter>,
  limit = 100,
) {
  return useQuery({
    queryKey: computed(() => keys.platformAudit(toValue(filter), limit)),
    queryFn: async () => {
      const query = compactAuditQuery(toValue(filter), limit);
      const res = await api.GET("/api/platform/audit", { params: { query } });
      return unwrap(res);
    },
  });
}

function compactAuditQuery(filter: AuditFilter, limit: number) {
  const query: Record<string, string | number> = { limit };
  for (const [key, value] of Object.entries(filter)) {
    const trimmed = typeof value === "string" ? value.trim() : "";
    if (trimmed) query[key] = trimmed;
  }
  return query;
}

export function useAcceptInvite(token: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => keys.acceptInvite(toValue(token))),
    queryFn: async () => {
      const res = await api.GET("/api/invitations/accept", {
        params: { query: { token: toValue(token) } },
      });
      return unwrap(res);
    },
    retry: 0,
  });
}

// ─── Config service ───

export function useConfigSchemas(includeArchived: MaybeRefOrGetter<boolean> = () => false) {
  return useQuery({
    queryKey: computed(() => keys.configSchemas(toValue(includeArchived))),
    queryFn: async () => {
      const res = await api.GET("/api/platform/config-schemas", {
        params: { query: { includeArchived: toValue(includeArchived) } },
      });
      return unwrap(res);
    },
  });
}

export function useConfigSchemaVersions(code: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => keys.configSchemaVersions(toValue(code))),
    queryFn: async () => {
      const res = await api.GET("/api/platform/config-schemas/{schemaCode}/versions", {
        params: { path: { schemaCode: toValue(code) } },
      });
      return unwrap(res);
    },
    enabled: computed(() => !!toValue(code)),
  });
}

export function useConfigSchemaDrafts(code: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => keys.configSchemaDrafts(toValue(code))),
    queryFn: async () => {
      const res = await api.GET("/api/platform/config-schemas/{schemaCode}/drafts", {
        params: { path: { schemaCode: toValue(code) } },
      });
      return unwrap(res);
    },
    enabled: computed(() => !!toValue(code)),
  });
}

export function useCreateConfigSchema() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: import("./generated").CreateConfigSchemaRequest) => {
      const res = await api.POST("/api/platform/config-schemas", { body });
      return unwrap(res);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["config-schemas"] }),
  });
}

export function usePublishConfigDraft(schemaCode: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ draftId, body }: { draftId: string; body: import("./generated").PublishConfigDraftRequest }) => {
      const res = await api.POST("/api/platform/config-schemas/{schemaCode}/drafts/{draftId}/publish", {
        params: { path: { schemaCode: toValue(schemaCode), draftId } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["config-schemas"] });
      qc.invalidateQueries({ queryKey: keys.configSchemaDrafts(toValue(schemaCode)) });
      qc.invalidateQueries({ queryKey: keys.configSchemaVersions(toValue(schemaCode)) });
    },
  });
}

export function useNodeConfigSchemas(brandId: MaybeRefOrGetter<string>, nodeId: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => keys.nodeConfig(toValue(brandId), toValue(nodeId))),
    queryFn: async () => {
      const res = await api.GET("/api/brands/{brandId}/org-nodes/{nodeId}/config", {
        params: { path: { brandId: toValue(brandId), nodeId: toValue(nodeId) } },
      });
      return unwrap(res);
    },
    enabled: computed(() => !!toValue(brandId) && !!toValue(nodeId)),
  });
}

export function useNodeConfigMetadata(
  brandId: MaybeRefOrGetter<string>,
  nodeId: MaybeRefOrGetter<string>,
  path: MaybeRefOrGetter<string>,
) {
  return useQuery({
    queryKey: computed(() => keys.nodeConfigSchema(toValue(brandId), toValue(nodeId), toValue(path), true)),
    queryFn: async () => {
      const res = await api.GET("/api/brands/{brandId}/org-nodes/{nodeId}/config/{path}", {
        params: {
          path: { brandId: toValue(brandId), nodeId: toValue(nodeId), path: toValue(path) },
          query: { includeMetadata: true },
        },
      });
      return unwrap(res);
    },
    enabled: computed(() => !!toValue(brandId) && !!toValue(nodeId) && !!toValue(path)),
  });
}

export function usePatchNodeConfig(brandId: MaybeRefOrGetter<string>, nodeId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ schemaCode, body }: { schemaCode: string; body: import("./generated").PatchConfigValuesRequest }) => {
      const res = await api.PATCH("/api/brands/{brandId}/org-nodes/{nodeId}/config/{schemaCode}", {
        params: { path: { brandId: toValue(brandId), nodeId: toValue(nodeId), schemaCode } },
        body,
      });
      if (!res.response.ok) throw (res as { error?: unknown }).error ?? new Error("Patch failed");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.nodeConfig(toValue(brandId), toValue(nodeId)) });
      qc.invalidateQueries({ queryKey: ["node-config-path", toValue(brandId), toValue(nodeId)] });
    },
  });
}

// ─── Config assignments (per-node) ───

export function useNodeAssignments(
  brandId: MaybeRefOrGetter<string>,
  nodeId: MaybeRefOrGetter<string>,
  includeArchived: MaybeRefOrGetter<boolean> = () => false,
) {
  return useQuery({
    queryKey: computed(() =>
      keys.nodeAssignments(toValue(brandId), toValue(nodeId), toValue(includeArchived)),
    ),
    queryFn: async () => {
      const res = await api.GET("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments", {
        params: {
          path: { brandId: toValue(brandId), nodeId: toValue(nodeId) },
          query: { includeArchived: toValue(includeArchived) },
        },
      });
      return unwrap(res);
    },
    enabled: computed(() => !!toValue(brandId) && !!toValue(nodeId)),
  });
}

export function useCreateAssignment(brandId: MaybeRefOrGetter<string>, nodeId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: import("./generated").AssignConfigSchemaRequest) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments", {
        params: { path: { brandId: toValue(brandId), nodeId: toValue(nodeId) } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["node-assignments", toValue(brandId), toValue(nodeId)] });
      qc.invalidateQueries({ queryKey: keys.nodeConfig(toValue(brandId), toValue(nodeId)) });
    },
  });
}

export function useArchiveAssignment(brandId: MaybeRefOrGetter<string>, nodeId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ assignmentId, body }: { assignmentId: string; body: import("./generated").ArchiveConfigAssignmentRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/archive", {
        params: { path: { brandId: toValue(brandId), nodeId: toValue(nodeId), assignmentId } },
        body,
      });
      if (!res.response.ok) throw (res as { error?: unknown }).error ?? new Error("Archive failed");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["node-assignments", toValue(brandId), toValue(nodeId)] });
      qc.invalidateQueries({ queryKey: keys.nodeConfig(toValue(brandId), toValue(nodeId)) });
    },
  });
}

export function useRestoreAssignment(brandId: MaybeRefOrGetter<string>, nodeId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ assignmentId, body }: { assignmentId: string; body: import("./generated").RestoreConfigAssignmentRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/restore", {
        params: { path: { brandId: toValue(brandId), nodeId: toValue(nodeId), assignmentId } },
        body,
      });
      if (!res.response.ok) throw (res as { error?: unknown }).error ?? new Error("Restore failed");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["node-assignments", toValue(brandId), toValue(nodeId)] });
      qc.invalidateQueries({ queryKey: keys.nodeConfig(toValue(brandId), toValue(nodeId)) });
    },
  });
}

export function useUpgradeAssignmentPreview(brandId: MaybeRefOrGetter<string>, nodeId: MaybeRefOrGetter<string>) {
  return useMutation({
    mutationFn: async ({ assignmentId, body }: { assignmentId: string; body: import("./generated").UpgradeConfigAssignmentRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/upgrade-preview", {
        params: { path: { brandId: toValue(brandId), nodeId: toValue(nodeId), assignmentId } },
        body,
      });
      return unwrap(res);
    },
  });
}

export function useUpgradeAssignment(brandId: MaybeRefOrGetter<string>, nodeId: MaybeRefOrGetter<string>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ assignmentId, body }: { assignmentId: string; body: import("./generated").UpgradeConfigAssignmentRequest }) => {
      const res = await api.POST("/api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/upgrade", {
        params: { path: { brandId: toValue(brandId), nodeId: toValue(nodeId), assignmentId } },
        body,
      });
      return unwrap(res);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["node-assignments", toValue(brandId), toValue(nodeId)] });
      qc.invalidateQueries({ queryKey: keys.nodeConfig(toValue(brandId), toValue(nodeId)) });
    },
  });
}
