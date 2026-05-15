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
  SubjectResponse,
} from "./generated";

export const keys = {
  myBrands: () => ["brands"] as const,
  grants: (brandId: string) => ["grants", brandId] as const,
  tree: (brandId: string) => ["tree", brandId] as const,
  invitations: (brandId: string) => ["invitations", brandId] as const,
  acceptInvite: (token: string) => ["accept-invite", token] as const,
  audit: (brandId: string, limit: number) => ["audit", brandId, limit] as const,
  subjects: (filter: string, onboarded: boolean | null, limit: number) =>
    ["subjects", filter, onboarded, limit] as const,
  platformAudit: (
    filter: { action?: string; tenantId?: string; actorExternalId?: string },
    limit: number,
  ) => ["platform-audit", filter, limit] as const,
};

function unwrap<T>(value: { data?: T; error?: unknown }): T {
  if (value.error || !value.data) throw value.error ?? new Error("Request failed");
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

export function useBrandAudit(brandId: MaybeRefOrGetter<string>, limit = 100) {
  return useQuery({
    queryKey: computed(() => keys.audit(toValue(brandId), limit)),
    queryFn: async () => {
      const res = await api.GET("/api/brands/{brandId}/audit", {
        params: { path: { brandId: toValue(brandId) }, query: { limit } },
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
  filter: MaybeRefOrGetter<{ action?: string; tenantId?: string; actorExternalId?: string }>,
  limit = 100,
) {
  return useQuery({
    queryKey: computed(() => keys.platformAudit(toValue(filter), limit)),
    queryFn: async () => {
      const f = toValue(filter);
      const query: Record<string, unknown> = { limit };
      if (f.action) query.action = f.action;
      if (f.tenantId) query.tenantId = f.tenantId;
      if (f.actorExternalId) query.actorExternalId = f.actorExternalId;
      const res = await api.GET("/api/platform/audit", { params: { query } });
      return unwrap(res);
    },
  });
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
