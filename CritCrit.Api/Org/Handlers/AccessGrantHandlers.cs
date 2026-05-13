using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class AccessGrantHandlers
{
    [WolverinePost("/api/brands/{brandId}/access-grants")]
    public static async Task<GrantResponse> GrantRole(
        GrantRoleRequest request,
        IDocumentStore store,
        OrgCommandService commands,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = httpContext.GetActor();

        if (!OrgPublicId.TryParseOrgNode(request.OrgNodeId, out var nodeId, out _))
            throw new DomainException("Invalid org node ID.");
        if (!OrgPublicId.TryParseSubject(request.SubjectId, out var subjectId))
            throw new DomainException("Invalid subject ID.");

        if (request.Role == OrgRole.Owner)
        {
            authorization.EnforceSuperAdmin(actor);
        }
        else
        {
            var target = await session.LoadAsync<OrgNodeReadModel>(nodeId.Value, ct)
                ?? throw new DomainException("Org node not found.");
            await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);
        }

        var grant = await commands.GrantRoleAsync(
            session, actor, tenant.TenantId, nodeId, subjectId,
            request.Role, request.ExpiresAt,
            OrgAccessGrantSource.DirectGrant, null, ct);

        return new GrantResponse(grant.Id, request.OrgNodeId, request.SubjectId, grant.Role, grant.ExpiresAt);
    }
}
