using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Invitations;

public sealed record ProvisionInvitation(InvitationId InvitationId);

public sealed record SendInvitationEmail(InvitationId InvitationId, string RawToken, bool RequiresPasswordSetup, int Attempt);

public sealed record ExpireInvitation(InvitationId InvitationId, DateTimeOffset ExpiresAt);

public sealed record ExpireGrant(OrgNodeId TenantId, OrgNodeId OrgNodeId, SubjectId SubjectId, DateTimeOffset ExpiresAt);

public sealed record RetrySendInvitationEmail(InvitationId InvitationId, string RawToken, bool RequiresPasswordSetup, int Attempt);

public sealed record FinalizeInvitationFailure(InvitationId InvitationId);

public sealed record CleanupRedundantGrants(OrgNodeId TenantId, OrgNodeId OrgNodeId, SubjectId SubjectId, OrgRole NewRole);

public sealed record CleanupMovedSubtreeGrants(OrgNodeId TenantId, OrgNodeId MovedNodeId);
