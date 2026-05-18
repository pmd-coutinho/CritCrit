using CritCrit.Api.Org.Domain;
using CritCrit.Api.Platform.Errors;

namespace CritCrit.Api.Org.Features.Owners;

/// <summary>
/// Pure invariants for Owner-grant transitions. Throws <see cref="DomainException"/> on violation.
/// No DB, no session, no logging. Unit-testable in isolation.
/// </summary>
public static class OwnerRules
{
    public static void RequireBrandRoot(OrgNodeReadModel node)
    {
        if (node.Type != OrgNodeType.Brand)
            throw new DomainException("Owner can only be granted at the brand root.");
    }

    public static void RequireActiveSubject(SubjectReadModel? subject)
    {
        if (subject is null || !subject.Active)
            throw new DomainException("Subject does not exist or is inactive.");
    }

    public static void RequireNotAlreadyOwner(OrgAccessGrantReadModel? grant)
    {
        if (grant is { Status: OrgAccessGrantStatus.Active, Role: OrgRole.Owner })
            throw new DomainException("Equivalent direct owner grant already exists.");
    }

    public static void RequireActiveOwnerGrant(OrgAccessGrantReadModel? grant)
    {
        if (grant is not { Status: OrgAccessGrantStatus.Active, Role: OrgRole.Owner })
            throw new DomainException("Active owner grant not found.");
    }

    public static void RequireDowngradeTarget(OrgRole newRole)
    {
        if (newRole == OrgRole.Owner)
            throw new DomainException("Downgrade cannot target Owner role.");
        if (!OrgRules.CanGrantRoleAt(newRole, OrgNodeType.Brand))
            throw new DomainException($"{newRole} can only be granted at the Brand root.");
    }
}
