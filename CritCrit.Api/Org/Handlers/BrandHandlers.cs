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
        OrgCommandService commands,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var actor = httpContext.GetActor();
        authorization.EnforceSuperAdmin(actor);
        var node = await commands.CreateBrandAsync(store, actor, request.Code, request.Name, ct);
        return ToResponse(node);
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
