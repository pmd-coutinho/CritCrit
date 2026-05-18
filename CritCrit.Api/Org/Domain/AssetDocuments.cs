namespace CritCrit.Api.Org.Domain;

/// <summary>
/// Per-(tenant, node) bag of static asset entries. Multi-tenanted.
/// Doc id = "{tenant}:{node}"; StreamId is the Marten stream for the slot.
/// </summary>
public sealed class AssetNodeValueReadModel
{
    public string Id { get; set; } = "";
    public Guid StreamId { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrgNodeId { get; set; }
    public Dictionary<string, AssetEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static string BuildId(Guid tenantId, Guid orgNodeId) => $"{tenantId:N}:{orgNodeId:N}";
}
