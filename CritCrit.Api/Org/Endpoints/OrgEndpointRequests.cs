using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Endpoints;

public sealed record CreateBrandRequest(string Code, string Name);

public sealed record CreatePlainOrgNodeRequest(string ParentId, string Code, string Name);

public sealed record CreateStoreRequest(string ParentId, string Code, string Name, string? TimeZone);

public sealed record CreateDeviceRequest(string ParentStoreId, string SerialNumber, string Name, DeviceType DeviceType);

public sealed record CreateSubjectRequest(string Email, string? DisplayName, string Provider, string ProviderTenant, string ExternalId);

public sealed record GrantRoleRequest(string OrgNodeId, string SubjectId, OrgRole Role, DateTimeOffset? ExpiresAt);

public sealed record OrgNodeResponse(
    string Id,
    string TenantId,
    string? ParentId,
    OrgNodeType Type,
    string Code,
    string Name,
    string Path,
    bool Archived,
    bool EffectiveArchived,
    bool HardDeleted);

public sealed record SubjectResponse(string Id, string Email, string? DisplayName);

public sealed record GrantResponse(string Id, string OrgNodeId, string SubjectId, OrgRole Role, DateTimeOffset? ExpiresAt);
