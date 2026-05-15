using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Cross-tenant per-(subject, brand) index of active grants. Drives the
/// non-SuperAdmin GET /api/brands path: "which brands can this subject see?".
/// Stores the list of (orgNodeId, role) grant entries and the highest role
/// among them. Removes the document when the last active grant for the pair
/// disappears.
/// </summary>
public sealed class SubjectBrandAccessProjection : EventProjection
{
    public async Task Project(IEvent<OrgAccessGranted> e, IDocumentOperations ops, CancellationToken ct)
    {
        var id = SubjectBrandAccessReadModel.BuildId(e.Data.SubjectId, e.Data.TenantId);
        var doc = await ops.LoadAsync<SubjectBrandAccessReadModel>(id, ct) ?? new SubjectBrandAccessReadModel
        {
            Id = id,
            SubjectId = e.Data.SubjectId.Value,
            TenantId = e.Data.TenantId.Value,
            Grants = []
        };

        UpsertGrant(doc, e.Data.OrgNodeId.Value, e.Data.Role);
        doc.UpdatedAt = e.Timestamp;
        ops.Store(doc);
    }

    public async Task Project(IEvent<OrgAccessRoleChanged> e, IDocumentOperations ops, CancellationToken ct)
    {
        var id = SubjectBrandAccessReadModel.BuildId(e.Data.SubjectId, e.Data.TenantId);
        var doc = await ops.LoadAsync<SubjectBrandAccessReadModel>(id, ct);
        if (doc is null) return;

        UpsertGrant(doc, e.Data.OrgNodeId.Value, e.Data.NewRole);
        doc.UpdatedAt = e.Timestamp;
        ops.Store(doc);
    }

    public async Task Project(IEvent<OrgAccessRevoked> e, IDocumentOperations ops, CancellationToken ct)
    {
        await RemoveGrantAsync(e.Data.SubjectId, e.Data.TenantId, e.Data.OrgNodeId.Value, e.Timestamp, ops, ct);
    }

    public async Task Project(IEvent<OrgAccessExpired> e, IDocumentOperations ops, CancellationToken ct)
    {
        await RemoveGrantAsync(e.Data.SubjectId, e.Data.TenantId, e.Data.OrgNodeId.Value, e.Timestamp, ops, ct);
    }

    private static async Task RemoveGrantAsync(
        SubjectId subjectId,
        OrgNodeId tenantId,
        Guid orgNodeId,
        DateTimeOffset at,
        IDocumentOperations ops,
        CancellationToken ct)
    {
        var id = SubjectBrandAccessReadModel.BuildId(subjectId, tenantId);
        var doc = await ops.LoadAsync<SubjectBrandAccessReadModel>(id, ct);
        if (doc is null) return;

        doc.Grants = doc.Grants.Where(g => g.OrgNodeId != orgNodeId).ToList();
        if (doc.Grants.Count == 0)
        {
            ops.Delete<SubjectBrandAccessReadModel>(id);
            return;
        }

        doc.HighestRole = doc.Grants.Max(g => (OrgRole?)g.Role);
        doc.UpdatedAt = at;
        ops.Store(doc);
    }

    private static void UpsertGrant(SubjectBrandAccessReadModel doc, Guid orgNodeId, OrgRole role)
    {
        var existing = doc.Grants.FirstOrDefault(g => g.OrgNodeId == orgNodeId);
        if (existing is null)
        {
            doc.Grants.Add(new SubjectBrandGrantEntry { OrgNodeId = orgNodeId, Role = role });
        }
        else
        {
            existing.Role = role;
        }

        doc.HighestRole = doc.Grants.Max(g => (OrgRole?)g.Role);
    }
}
