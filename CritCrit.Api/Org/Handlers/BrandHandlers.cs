using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class BrandHandlers
{
    [WolverinePost("/api/platform/brands")]
    public static async Task<OrgNodeResponse> CreateBrand(
        CreateBrandRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);
        authorization.EnforceSuperAdmin(actor);

        var id = OrgNodeId.New();
        await using var session = store.LightweightSession(id.Value.ToString());
        var normalized = OrgValidation.ValidateAndNormalizeCode(OrgNodeType.Brand, request.Code);
        await OrgValidation.EnsureCodeAvailableAsync(session, id, normalized, ct);

        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(
            id, id, null, OrgNodeType.Brand,
            request.Code.Trim(), normalized, request.Name.Trim()));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        return ToResponse(node);
    }

    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}")]
    public static async Task<IResult> GetNode(
        string nodeId,
        IDocumentStore store,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            return Results.NotFound();

        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct);
        if (node is null || node.TenantId != tenant.TenantId.Value)
            return Results.NotFound();

        return Results.Ok(ToResponse(node));
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
