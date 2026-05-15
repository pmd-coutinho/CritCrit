using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Invitations;

public sealed record MessageAuditContext(string? CausationId);

public sealed record ProvisionInvitation(InvitationId InvitationId, MessageAuditContext? Audit = null);

public sealed record SendInvitationEmail(InvitationId InvitationId, bool RequiresPasswordSetup, int Attempt, MessageAuditContext? Audit = null);

public sealed record ExpireInvitation(InvitationId InvitationId, DateTimeOffset ExpiresAt, MessageAuditContext? Audit = null);

public sealed record ExpireGrant(OrgNodeId TenantId, OrgNodeId OrgNodeId, SubjectId SubjectId, DateTimeOffset ExpiresAt, MessageAuditContext? Audit = null);

public sealed record RetrySendInvitationEmail(InvitationId InvitationId, bool RequiresPasswordSetup, int Attempt, MessageAuditContext? Audit = null);

public sealed record FinalizeInvitationFailure(InvitationId InvitationId, MessageAuditContext? Audit = null);

public sealed record CleanupRedundantGrants(OrgNodeId TenantId, OrgNodeId OrgNodeId, SubjectId SubjectId, OrgRole NewRole, MessageAuditContext? Audit = null);

public sealed record CleanupMovedSubtreeGrants(OrgNodeId TenantId, OrgNodeId MovedNodeId, MessageAuditContext? Audit = null);
