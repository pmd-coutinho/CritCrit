namespace CritCrit.Api.Org.Domain;

/// <summary>Top-level schema index. One doc per schema code, single-tenanted.</summary>
public sealed class ConfigSchemaReadModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string CodeNormalized { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? LatestPublishedVersion { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

/// <summary>Immutable snapshot of a published schema version. Doc id = "{code}:{version}".</summary>
public sealed class ConfigSchemaVersionReadModel
{
    public string Id { get; set; } = "";
    public Guid SchemaId { get; set; }
    public string SchemaCode { get; set; } = "";
    public int Version { get; set; }
    public ConfigSchemaDefinition Definition { get; set; } = default!;
    public DateTimeOffset PublishedAt { get; set; }
    public string PublishedByExternalId { get; set; } = "";

    public static string BuildId(string schemaCode, int version) => $"{schemaCode}:{version}";
}

/// <summary>Editable draft. Multiple drafts per schema allowed.</summary>
public sealed class ConfigSchemaDraftReadModel
{
    public Guid Id { get; set; }
    public Guid SchemaId { get; set; }
    public string SchemaCode { get; set; } = "";
    public string Name { get; set; } = "";
    public int? BaseVersion { get; set; }
    public ConfigSchemaDefinition Definition { get; set; } = default!;
    public bool Archived { get; set; }
    public bool Published { get; set; }
    public int? PublishedAsVersion { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Per-tenant assignment of a schema version to an org-node subtree.
/// Multi-tenanted; tenant scopes the document to a brand schema.
/// </summary>
public sealed class ConfigAssignmentReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RootOrgNodeId { get; set; }
    public string RootOrgNodePublicId { get; set; } = "";
    public string SchemaCode { get; set; } = "";
    public int SchemaVersion { get; set; }
    public bool Archived { get; set; }
    /// <summary>
    /// Doc revision counter — bumped by every projected event. Distinct from
    /// SchemaVersion (assigned schema version) and from Marten's internal
    /// mt_version metadata. Used for optimistic concurrency at the API layer.
    /// </summary>
    public long DocVersion { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Per-(tenant, node, schemaCode) bag of value entries. Multi-tenanted.
/// Doc id = "{tenant}:{node}:{schemaCode}"; StreamId is the Marten stream the
/// events for this slot are appended to.
/// </summary>
public sealed class ConfigNodeValueReadModel
{
    public string Id { get; set; } = "";
    public Guid StreamId { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrgNodeId { get; set; }
    public string OrgNodePublicId { get; set; } = "";
    public string SchemaCode { get; set; } = "";
    public Dictionary<string, ConfigValueEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static string BuildId(Guid tenantId, Guid orgNodeId, string schemaCode) =>
        $"{tenantId:N}:{orgNodeId:N}:{schemaCode}";
}
