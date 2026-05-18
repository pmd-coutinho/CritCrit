using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Brands;

public static class BrandHandlers
{
    [WolverineGet("/api/brands")]
    public static async Task<IReadOnlyList<BrandListItem>> ListMyBrands(
        IDocumentStore store,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = store.QuerySession();

        if (actor.IsSuperAdmin)
        {
            var all = await session.Query<BrandIndexReadModel>()
                .OrderBy(x => x.Name)
                .ToListAsync(ct);

            return all
                .Select(b => new BrandListItem(b.PublicId, b.Code, b.Name, b.Archived, null, BrandAccessSource.Platform))
                .ToArray();
        }

        if (actor.SubjectId is null)
            return [];

        var subjectGuid = actor.SubjectId.Value.Value;
        var access = await session.Query<SubjectBrandAccessReadModel>()
            .Where(x => x.SubjectId == subjectGuid)
            .ToListAsync(ct);

        if (access.Count == 0)
            return [];

        var tenantIds = access.Select(a => a.TenantId).ToArray();
        var brands = await session.Query<BrandIndexReadModel>()
            .Where(b => tenantIds.Contains(b.Id))
            .ToListAsync(ct);
        var brandById = brands.ToDictionary(b => b.Id);

        return access
            .Select(a => brandById.TryGetValue(a.TenantId, out var b)
                ? new BrandListItem(b.PublicId, b.Code, b.Name, b.Archived, a.HighestRole, BrandAccessSource.Grant)
                : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.Name)
            .ToArray();
    }

    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}")]
    public static async Task<IResult> GetNode(
        OrgNodeId nodeId,
        IDocumentStore store,
        BrandTenantContext tenant,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        var node = await session.LoadAsync<OrgNodeReadModel>(nodeId.Value, ct);
        if (node is null || node.TenantId != tenant.TenantId.Value || node.HardDeleted)
            return Results.NotFound();
        return Results.Ok(ToResponse(node));
    }

    [WolverineGet("/api/brands/{brandId}/tree")]
    public static async Task<IResult> GetBrandTree(
        bool? includeArchived,
        IDocumentStore store,
        BrandTenantContext tenant,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);

        var nodes = await session.Query<OrgNodeReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value && !x.HardDeleted)
            .ToListAsync(ct);

        var filtered = includeArchived == true
            ? nodes
            : nodes.Where(x => !x.EffectiveArchived).ToList();

        var root = filtered.FirstOrDefault(x => x.Id == tenant.TenantId.Value);
        if (root is null)
            return Results.NotFound();

        var byParent = filtered
            .Where(x => x.ParentId is not null)
            .GroupBy(x => x.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Type).ThenBy(n => n.CodeNormalized).ToList());

        return Results.Ok(BuildTree(root, byParent));
    }

    private static OrgTreeNodeResponse BuildTree(OrgNodeReadModel node, IReadOnlyDictionary<Guid, List<OrgNodeReadModel>> byParent)
    {
        var children = byParent.TryGetValue(node.Id, out var kids)
            ? kids.Select(c => BuildTree(c, byParent)).ToArray()
            : [];

        return new OrgTreeNodeResponse(
            node.PublicId,
            node.ParentPublicId,
            node.Type,
            node.Code,
            node.Name,
            node.Path,
            node.Archived,
            node.EffectiveArchived,
            children);
    }

    internal static OrgNodeResponse ToResponse(OrgNodeReadModel node) => new(
        node.PublicId,
        node.TenantPublicId,
        node.ParentPublicId,
        node.Type,
        node.Code,
        node.Name,
        node.Path,
        node.Archived,
        node.EffectiveArchived,
        node.HardDeleted);
}
