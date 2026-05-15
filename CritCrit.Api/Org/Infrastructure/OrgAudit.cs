using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Observability.Audit;

namespace CritCrit.Api.Org.Infrastructure;

public static class OrgAudit
{
    public static AuditRecord Record(
        string action,
        string category,
        string severity,
        ActorContext actor,
        Guid? tenantId,
        Guid? targetOrgNodeId,
        string? reason = null,
        object? details = null,
        Guid? subjectId = null,
        IReadOnlyList<AuditFieldChange>? changes = null,
        IReadOnlyList<AuditResourceRef>? related = null,
        string? targetPublicId = null,
        string? targetType = null,
        string? targetLabel = null) =>
        new(
            action,
            category,
            severity,
            Actor: actor,
            TenantId: tenantId,
            TenantPublicId: tenantId is null ? null : OrgPublicId.Format(OrgNodeType.Brand, new OrgNodeId(tenantId.Value)),
            Target: targetOrgNodeId is null
                ? null
                : new AuditResourceRef(targetType ?? "org_node", targetOrgNodeId, targetPublicId, targetLabel),
            SubjectId: subjectId,
            SubjectPublicId: subjectId is null ? null : OrgPublicId.FormatSubject(new SubjectId(subjectId.Value)),
            Reason: reason,
            RelatedResources: related,
            Changes: changes,
            Details: details);

    public static AuditRecord SystemRecord(
        string action,
        string category,
        string severity,
        AuditActor systemActor,
        Guid? tenantId,
        Guid? targetOrgNodeId,
        string? reason = null,
        object? details = null,
        Guid? subjectId = null,
        IReadOnlyList<AuditFieldChange>? changes = null,
        IReadOnlyList<AuditResourceRef>? related = null,
        string? causationId = null) =>
        new(
            action,
            category,
            severity,
            SystemActor: systemActor,
            TenantId: tenantId,
            TenantPublicId: tenantId is null ? null : OrgPublicId.Format(OrgNodeType.Brand, new OrgNodeId(tenantId.Value)),
            Target: targetOrgNodeId is null ? null : new AuditResourceRef("org_node", targetOrgNodeId),
            SubjectId: subjectId,
            SubjectPublicId: subjectId is null ? null : OrgPublicId.FormatSubject(new SubjectId(subjectId.Value)),
            Reason: reason,
            RelatedResources: related,
            Changes: changes,
            Details: details,
            CausationId: causationId);

    public static object InviteDetails(InvitationReadModel invitation, object? extra = null) => new
    {
        invitation.PublicId,
        invitation.TenantPublicId,
        invitation.TargetOrgNodePublicId,
        invitation.SubjectPublicId,
        InviteeEmailMasked = AuditIdentity.MaskEmail(invitation.Email),
        Role = invitation.Role.ToString(),
        Extra = extra
    };

    public static object SubjectDetails(SubjectReadModel subject, object? extra = null) => new
    {
        subject.PublicId,
        EmailMasked = AuditIdentity.MaskEmail(subject.Email),
        subject.Kind,
        Extra = extra
    };
}
