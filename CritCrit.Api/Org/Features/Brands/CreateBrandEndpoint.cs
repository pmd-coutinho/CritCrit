using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Brands;

// Refactor wave 1 (per .scratch/aggregate-handler-workflow/issues/01-owner-pilot.md):
// brand creation extracted into its own endpoint class so Wolverine.Http can
// dispatch the convention `Validate` method without colliding with other handlers.
// No `LoadAsync` because brand creation generates its own tenant id; no
// `Rules` module yet because OrgValidation already covers the only domain
// invariant (code uniqueness within the new tenant).
public static class CreateBrandEndpoint
{
    public static ProblemDetails Validate(CreateBrandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return new ProblemDetails { Title = "code", Detail = "Code is required.", Status = 400 };
        if (request.Code.Length > 128)
            return new ProblemDetails { Title = "code", Detail = "Code must be 128 characters or fewer.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.Name))
            return new ProblemDetails { Title = "name", Detail = "Name is required.", Status = 400 };
        if (request.Name.Length > 200)
            return new ProblemDetails { Title = "name", Detail = "Name must be 200 characters or fewer.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/platform/brands")]
    public static async Task<IResult> Handle(
        CreateBrandRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        var id = OrgNodeId.New();
        await using var session = store.LightweightSession(id.Value.ToString());
        SessionMetadata.StampActor(session, actor);
        var normalized = OrgValidation.ValidateAndNormalizeCode(OrgNodeType.Brand, request.Code);
        await OrgValidation.EnsureCodeAvailableAsync(session, id, normalized, ct);

        session.Events.StartStream<OrgNodeReadModel>(id.Value, new OrgNodeCreated(
            id, id, null, OrgNodeType.Brand,
            request.Code.Trim(), normalized, request.Name.Trim()));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        var publicId = OrgPublicId.Format(OrgNodeType.Brand, id);
        return Results.Created($"/api/brands/{publicId}/org-nodes/{publicId}", BrandHandlers.ToResponse(node));
    }
}
