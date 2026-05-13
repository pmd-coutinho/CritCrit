using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

public sealed class OrgAuthorizationService
{
    public async Task<OrgRole?> GetEffectiveRoleAsync(
        IQuerySession session,
        OrgNodeReadModel target,
        SubjectId subjectId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var scope = target.AncestorIds.Append(target.Id).ToArray();
        var grants = await session.Query<OrgAccessGrantReadModel>()
            .Where(x => x.TenantId == target.TenantId && x.SubjectId == subjectId.Value && scope.Contains(x.OrgNodeId))
            .ToListAsync(ct);

        return grants
            .Where(x => x.IsActive(now))
            .Select(x => (OrgRole?)x.Role)
            .OrderByDescending(x => x)
            .FirstOrDefault();
    }

    public async Task<AuthorizationResult> RequireRoleAsync(
        IQuerySession session,
        ActorContext actor,
        OrgNodeReadModel target,
        OrgRole required,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (actor.IsSuperAdmin)
            return AuthorizationResult.Success();

        if (actor.SubjectId is null)
            return AuthorizationResult.Fail("The authenticated user is not provisioned in CritCrit.");

        var effective = await GetEffectiveRoleAsync(session, target, actor.SubjectId.Value, now, ct);
        return effective is not null && effective.Value.IsAtLeast(required)
            ? AuthorizationResult.Success()
            : AuthorizationResult.Fail($"Requires {required} on {target.PublicId}.");
    }

    public async Task<bool> WouldBeRedundantAsync(
        IQuerySession session,
        OrgNodeReadModel target,
        SubjectId subjectId,
        OrgRole role,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ancestorIds = target.AncestorIds.ToArray();
        if (ancestorIds.Length == 0)
            return false;

        var grants = await session.Query<OrgAccessGrantReadModel>()
            .Where(x => x.TenantId == target.TenantId && x.SubjectId == subjectId.Value && ancestorIds.Contains(x.OrgNodeId))
            .ToListAsync(ct);

        var inherited = grants
            .Where(x => x.IsActive(now))
            .Select(x => (OrgRole?)x.Role)
            .OrderByDescending(x => x)
            .FirstOrDefault();

        return inherited is not null && inherited.Value >= role;
    }
}
