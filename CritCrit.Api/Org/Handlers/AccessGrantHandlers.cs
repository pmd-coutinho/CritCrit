using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class AccessGrantHandlers
{
    [WolverinePost("/api/brands/{brandId}/access-grants")]
    public static async Task<IResult> GrantRole(
        GrantRoleRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(request.OrgNodeId, out var nodeId, out _))
            throw new DomainException("Invalid org node ID.");
        if (!OrgPublicId.TryParseSubject(request.SubjectId, out var subjectId))
            throw new DomainException("Invalid subject ID.");

        var target = await OrgValidation.LoadNodeAsync(session, nodeId, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted || target.EffectiveArchived)
            throw new DomainException("Cannot grant access to inactive org nodes.");
        if (!OrgRules.CanGrantRoleAt(request.Role, target.Type))
            throw new DomainException($"{request.Role} can only be granted at the Brand root.");

        if (request.Role == OrgRole.Owner)
            authorization.EnforceSuperAdmin(actor);
        else
            await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);

        var subject = await session.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
        if (subject is null || !subject.Active)
            throw new DomainException("Subject does not exist or is inactive.");

        if (await authorization.WouldBeRedundantAsync(session, target, subjectId, request.Role, TimeProvider.System.GetUtcNow(), ct))
            throw new DomainException("Grant would be redundant with inherited access.");

        var id = OrgAccessGrantReadModel.BuildId(tenant.TenantId, nodeId, subjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
        if (grant is { Status: OrgAccessGrantStatus.Active })
        {
            if (grant.Role == request.Role)
                throw new DomainException("Equivalent direct grant already exists.");

            session.Events.Append(grant.StreamId, new OrgAccessRoleChanged(tenant.TenantId, nodeId, subjectId, grant.Role, request.Role));
            await session.SaveChangesAsync(ct);
            var updated = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
            return Results.Created($"/api/brands/{brandId}/access-grants/{id}",
                new GrantResponse(updated!.Id, request.OrgNodeId, request.SubjectId, updated.Role, updated.ExpiresAt));
        }

        var streamId = Guid.CreateVersion7();
        session.Events.StartStream<OrgAccessGrantReadModel>(streamId, new OrgAccessGranted(tenant.TenantId, nodeId, subjectId, request.Role, request.ExpiresAt, OrgAccessGrantSource.DirectGrant, null));
        await session.SaveChangesAsync(ct);
        var created = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgAccessGrantReadModel.");
        return Results.Created($"/api/brands/{brandId}/access-grants/{id}",
            new GrantResponse(created.Id, request.OrgNodeId, request.SubjectId, created.Role, created.ExpiresAt));
    }
}
