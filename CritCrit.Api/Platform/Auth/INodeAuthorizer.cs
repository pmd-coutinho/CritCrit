using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Platform.Auth;

/// <summary>
/// Platform-level role-at-org-node authorization API. Implemented by Org slice
/// (which owns the role + grant model). Other slices (Config, future ones)
/// depend only on this interface so they don't need to reach into Org/Infrastructure.
/// </summary>
public interface INodeAuthorizer
{
    /// <summary>Throws 403 DomainException if actor is not platform SuperAdmin.</summary>
    void EnforceSuperAdmin(ActorContext actor);

    /// <summary>Throws 403 DomainException if actor lacks <paramref name="required"/> at <paramref name="target"/>.</summary>
    Task EnforceRoleAsync(
        IQuerySession session,
        ActorContext actor,
        OrgNodeReadModel target,
        OrgRole required,
        CancellationToken ct);

    /// <summary>Throws 403 DomainException unless actor is SuperAdmin or Owner at the brand root for the target.</summary>
    Task EnforceRootOwnerOrSuperAdminAsync(
        IQuerySession session,
        ActorContext actor,
        OrgNodeReadModel target,
        CancellationToken ct);

    /// <summary>Non-throwing variant of <see cref="EnforceRoleAsync"/> for caller-side conditional logic.</summary>
    Task<AuthorizationResult> RequireRoleAsync(
        IQuerySession session,
        ActorContext actor,
        OrgNodeReadModel target,
        OrgRole required,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>Returns the highest active role <paramref name="subjectId"/> holds at <paramref name="target"/> or any ancestor.</summary>
    Task<OrgRole?> GetEffectiveRoleAsync(
        IQuerySession session,
        OrgNodeReadModel target,
        SubjectId subjectId,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>Returns true if a new grant of <paramref name="role"/> at <paramref name="target"/> would be shadowed by an ancestor grant.</summary>
    Task<bool> WouldBeRedundantAsync(
        IQuerySession session,
        OrgNodeReadModel target,
        SubjectId subjectId,
        OrgRole role,
        DateTimeOffset now,
        CancellationToken ct);
}
