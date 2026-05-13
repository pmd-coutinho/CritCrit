namespace CritCrit.Api.Org.Domain;

public sealed class OrgNodeReadModel
{
    public Guid Id { get; set; }
    public string PublicId { get; set; } = "";
    public Guid TenantId { get; set; }
    public string TenantPublicId { get; set; } = "";
    public Guid? ParentId { get; set; }
    public string? ParentPublicId { get; set; }
    public OrgNodeType Type { get; set; }
    public string Code { get; set; } = "";
    public string CodeNormalized { get; set; } = "";
    public string Name { get; set; } = "";
    public List<Guid> AncestorIds { get; set; } = [];
    public List<string> AncestorPublicIds { get; set; } = [];
    public string Path { get; set; } = "";
    public bool Archived { get; set; }
    public bool EffectiveArchived { get; set; }
    public bool HardDeleted { get; set; }
}

public sealed class OrgNodeCodeIndex
{
    public string Id { get; set; } = "";
    public Guid TenantId { get; set; }
    public string CodeNormalized { get; set; } = "";
    public Guid OrgNodeId { get; set; }
    public bool HardDeleted { get; set; }

    public static string BuildId(OrgNodeId tenantId, string codeNormalized) => $"{tenantId.Value:N}:{codeNormalized}";
}

public sealed class StoreProfileReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public bool HardDeleted { get; set; }
}

public sealed class DeviceProfileReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string SerialNumber { get; set; } = "";
    public string SerialNumberNormalized { get; set; } = "";
    public DeviceType DeviceType { get; set; }
    public bool HardDeleted { get; set; }
}

public sealed class SubjectReadModel
{
    public Guid Id { get; set; }
    public string PublicId { get; set; } = "";
    public SubjectKind Kind { get; set; }
    public string Email { get; set; } = "";
    public string EmailNormalized { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool Active { get; set; }
}

public sealed class ExternalIdentityReadModel
{
    public string Id { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderTenant { get; set; } = "";
    public string ExternalId { get; set; } = "";

    public static string BuildId(string provider, string providerTenant, string externalId) =>
        $"{provider.Trim().ToLowerInvariant()}:{providerTenant.Trim().ToLowerInvariant()}:{externalId}";
}

public sealed class OrgAccessGrantReadModel
{
    public string Id { get; set; } = "";
    public Guid StreamId { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrgNodeId { get; set; }
    public Guid SubjectId { get; set; }
    public OrgRole Role { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public OrgAccessGrantStatus Status { get; set; }
    public OrgAccessGrantSource Source { get; set; }
    public Guid? InvitationId { get; set; }

    public bool IsActive(DateTimeOffset now) =>
        Status == OrgAccessGrantStatus.Active && (ExpiresAt is null || ExpiresAt > now);

    public static string BuildId(OrgNodeId tenantId, OrgNodeId orgNodeId, SubjectId subjectId) =>
        $"{tenantId.Value:N}:{orgNodeId.Value:N}:{subjectId.Value:N}";
}

public sealed class BrandTombstone
{
    public Guid Id { get; set; }
    public string PublicId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset DeletedAt { get; set; }
}

public sealed class ImmutableAuditEvent
{
    public Guid Id { get; set; }
    public string Action { get; set; } = "";
    public Guid? TenantId { get; set; }
    public Guid? TargetOrgNodeId { get; set; }
    public string? Reason { get; set; }
    public string ActorExternalId { get; set; } = "";
    public Guid? ActorSubjectId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public object? Details { get; set; }
}
