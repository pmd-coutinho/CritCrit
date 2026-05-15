/**
 * Placeholder. Replaced by `pnpm generate:api` once the API is running.
 * Hand-written shape covers the v1 endpoints; do not edit other paths here.
 */

export type OrgNodeType = "Brand" | "Country" | "Franchise" | "Store" | "Device";
export type OrgRole = "Viewer" | "Member" | "Admin" | "Owner";
export type InvitationStatus =
  | "Requested"
  | "Provisioning"
  | "Pending"
  | "Accepted"
  | "Cancelled"
  | "Expired"
  | "Failed"
  | "Superseded"
  | "Obsoleted";
export type DeviceType = "Pos" | "Kiosk" | "Display" | "Other";
export type BrandAccessSource = "Grant" | "Platform";
export type OrgAccessGrantSource = "Direct" | "Invitation" | "Owner";

export interface BrandListItem {
  id: string;
  code: string;
  name: string;
  archived: boolean;
  highestRole: OrgRole | null;
  source: BrandAccessSource;
}

export interface GrantListItem {
  id: string;
  orgNodeId: string;
  orgNodeName: string;
  orgNodeType: OrgNodeType;
  subjectId: string;
  subjectEmail: string;
  subjectDisplayName: string | null;
  role: OrgRole;
  expiresAt: string | null;
  source: OrgAccessGrantSource;
}

export interface OrgNodeResponse {
  id: string;
  tenantId: string;
  parentId: string | null;
  type: OrgNodeType;
  code: string;
  name: string;
  path: string;
  archived: boolean;
  effectiveArchived: boolean;
  hardDeleted: boolean;
}

export interface OrgTreeNodeResponse {
  id: string;
  parentId: string | null;
  type: OrgNodeType;
  code: string;
  name: string;
  path: string;
  archived: boolean;
  effectiveArchived: boolean;
  children: OrgTreeNodeResponse[];
}

export interface InvitationResponse {
  id: string;
  brandId: string;
  orgNodeId: string;
  email: string;
  subjectId: string | null;
  role: OrgRole;
  status: InvitationStatus;
  createdAt: string;
  expiresAt: string | null;
  acceptedAt: string | null;
  lastSentAt: string | null;
  failure: string | null;
}

export interface AcceptInvitationResponse {
  invitationId: string;
  status: InvitationStatus;
  grantCreated: boolean;
  subjectOnboarded: boolean;
  autoAppliedInvitations: number;
}

export interface AuditEventResponse {
  id: string;
  action: string;
  occurredAt: string;
  reason: string | null;
  actorExternalId: string;
  actorSubjectId: string | null;
  tenantId: string | null;
  targetOrgNodeId: string | null;
  details: unknown;
}

export interface CreateInvitationRequest {
  orgNodeId: string;
  email: string;
  role: OrgRole;
}

export interface CreateBrandRequest {
  code: string;
  name: string;
}

export interface CreatePlainOrgNodeRequest {
  parentId: string;
  code: string;
  name: string;
}

export interface CreateStoreRequest {
  parentId: string;
  code: string;
  name: string;
  timeZone?: string | null;
}

export interface CreateDeviceRequest {
  parentStoreId: string;
  serialNumber: string;
  name: string;
  deviceType: DeviceType;
}

export interface ArchiveOrgNodeRequest {
  force: boolean;
  reason: string | null;
}

export interface HardDeleteOrgNodeRequest {
  reason: string;
}

export interface MoveOrgNodeRequest {
  newParentId: string;
  reason: string;
}

export interface GrantRoleRequest {
  orgNodeId: string;
  subjectId: string;
  role: OrgRole;
  expiresAt: string | null;
}

export interface SetGrantExpirationRequest {
  orgNodeId: string;
  subjectId: string;
  expiresAt: string | null;
}

export interface GrantResponse {
  id: string;
  orgNodeId: string;
  subjectId: string;
  role: OrgRole;
  expiresAt: string | null;
}

export interface GrantOwnerRequest {
  subjectId: string;
}

export interface DowngradeOwnerRequest {
  newRole: OrgRole;
  reason: string;
}

export interface RevokeOwnerRequest {
  reason: string;
}

export interface RevokeGrantRequest {
  orgNodeId: string;
  subjectId: string;
  reason: string | null;
}

export interface DeactivateSubjectRequest {
  reason: string | null;
}

export interface ReactivateSubjectRequest {
  reason: string | null;
}

export interface RelinkSubjectIdentityRequest {
  provider: string;
  providerTenant: string;
  oldExternalId: string;
  newExternalId: string;
  reason: string | null;
}

export interface CreateSubjectRequest {
  email: string;
  displayName: string | null;
  provider: string;
  providerTenant: string;
  externalId: string;
}

export interface SubjectResponse {
  id: string;
  email: string;
  displayName: string | null;
}

export type SubjectKind = "User" | "Service" | "System";

export interface SubjectListItem {
  id: string;
  email: string;
  displayName: string | null;
  kind: SubjectKind;
  active: boolean;
  onboardedAt: string | null;
}

export interface paths {
  "/api/brands": {
    get: {
      responses: { 200: { content: { "application/json": BrandListItem[] } } };
    };
  };
  "/api/platform/brands": {
    post: {
      requestBody: { content: { "application/json": CreateBrandRequest } };
      responses: { 201: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/access-grants": {
    get: {
      parameters: { path: { brandId: string } };
      responses: { 200: { content: { "application/json": GrantListItem[] } } };
    };
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": GrantRoleRequest } };
      responses: { 201: { content: { "application/json": GrantResponse } } };
    };
  };
  "/api/brands/{brandId}/access-grants/expiration": {
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": SetGrantExpirationRequest } };
      responses: { 200: { content: { "application/json": GrantResponse } } };
    };
  };
  "/api/brands/{brandId}/access-grants/revoke": {
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": RevokeGrantRequest } };
      responses: { 204: { content: never } };
    };
  };
  "/api/platform/subjects/{subjectId}/deactivate": {
    post: {
      parameters: { path: { subjectId: string } };
      requestBody: { content: { "application/json": DeactivateSubjectRequest } };
      responses: { 204: { content: never } };
    };
  };
  "/api/platform/subjects/{subjectId}/reactivate": {
    post: {
      parameters: { path: { subjectId: string } };
      requestBody: { content: { "application/json": ReactivateSubjectRequest } };
      responses: { 204: { content: never } };
    };
  };
  "/api/platform/subjects/{subjectId}/relink": {
    post: {
      parameters: { path: { subjectId: string } };
      requestBody: { content: { "application/json": RelinkSubjectIdentityRequest } };
      responses: { 204: { content: never } };
    };
  };
  "/api/brands/{brandId}/owners": {
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": GrantOwnerRequest } };
      responses: { 201: { content: { "application/json": GrantResponse } } };
    };
  };
  "/api/brands/{brandId}/owners/{subjectId}/downgrade": {
    post: {
      parameters: { path: { brandId: string; subjectId: string } };
      requestBody: { content: { "application/json": DowngradeOwnerRequest } };
      responses: { 200: { content: { "application/json": GrantResponse } } };
    };
  };
  "/api/brands/{brandId}/owners/{subjectId}/revoke": {
    post: {
      parameters: { path: { brandId: string; subjectId: string } };
      requestBody: { content: { "application/json": RevokeOwnerRequest } };
      responses: { 204: { content: never } };
    };
  };
  "/api/platform/subjects": {
    get: {
      parameters: {
        query?: {
          emailContains?: string;
          onboarded?: boolean;
          limit?: number;
          offset?: number;
        };
      };
      responses: { 200: { content: { "application/json": SubjectListItem[] } } };
    };
    post: {
      requestBody: { content: { "application/json": CreateSubjectRequest } };
      responses: { 201: { content: { "application/json": SubjectResponse } } };
    };
  };
  "/api/platform/audit": {
    get: {
      parameters: {
        query?: {
          action?: string;
          from?: string;
          to?: string;
          targetOrgNodeId?: string;
          actorExternalId?: string;
          tenantId?: string;
          limit?: number;
          offset?: number;
        };
      };
      responses: { 200: { content: { "application/json": AuditEventResponse[] } } };
    };
  };
  "/api/brands/{brandId}/org-nodes/{nodeId}/archive": {
    post: {
      parameters: { path: { brandId: string; nodeId: string } };
      requestBody: { content: { "application/json": ArchiveOrgNodeRequest } };
      responses: { 200: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/org-nodes/{nodeId}/restore": {
    post: {
      parameters: { path: { brandId: string; nodeId: string } };
      responses: { 200: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/org-nodes/{nodeId}/move": {
    post: {
      parameters: { path: { brandId: string; nodeId: string } };
      requestBody: { content: { "application/json": MoveOrgNodeRequest } };
      responses: { 200: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/org-nodes/{nodeId}/hard-delete": {
    post: {
      parameters: { path: { brandId: string; nodeId: string } };
      requestBody: { content: { "application/json": HardDeleteOrgNodeRequest } };
      responses: { 204: { content: never } };
    };
  };
  "/api/brands/{brandId}/tree": {
    get: {
      parameters: {
        path: { brandId: string };
        query?: { includeArchived?: boolean };
      };
      responses: { 200: { content: { "application/json": OrgTreeNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/org-nodes/{nodeId}": {
    get: {
      parameters: { path: { brandId: string; nodeId: string } };
      responses: { 200: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/countries": {
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": CreatePlainOrgNodeRequest } };
      responses: { 201: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/franchises": {
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": CreatePlainOrgNodeRequest } };
      responses: { 201: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/stores": {
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": CreateStoreRequest } };
      responses: { 201: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/devices": {
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": CreateDeviceRequest } };
      responses: { 201: { content: { "application/json": OrgNodeResponse } } };
    };
  };
  "/api/brands/{brandId}/invitations": {
    get: {
      parameters: {
        path: { brandId: string };
        query?: { status?: string; orgNodeId?: string };
      };
      responses: { 200: { content: { "application/json": InvitationResponse[] } } };
    };
    post: {
      parameters: { path: { brandId: string } };
      requestBody: { content: { "application/json": CreateInvitationRequest } };
      responses: { 201: { content: { "application/json": InvitationResponse } } };
    };
  };
  "/api/brands/{brandId}/invitations/{invitationId}/cancel": {
    post: {
      parameters: { path: { brandId: string; invitationId: string } };
      requestBody: { content: { "application/json": { reason: string | null } } };
      responses: { 200: { content: { "application/json": InvitationResponse } } };
    };
  };
  "/api/brands/{brandId}/invitations/{invitationId}/resend": {
    post: {
      parameters: { path: { brandId: string; invitationId: string } };
      responses: { 200: { content: { "application/json": InvitationResponse } } };
    };
  };
  "/api/invitations/accept": {
    get: {
      parameters: { query: { token: string } };
      responses: { 200: { content: { "application/json": AcceptInvitationResponse } } };
    };
  };
  "/api/brands/{brandId}/audit": {
    get: {
      parameters: {
        path: { brandId: string };
        query?: {
          action?: string;
          from?: string;
          to?: string;
          targetOrgNodeId?: string;
          actorExternalId?: string;
          limit?: number;
          offset?: number;
        };
      };
      responses: { 200: { content: { "application/json": AuditEventResponse[] } } };
    };
  };
}
