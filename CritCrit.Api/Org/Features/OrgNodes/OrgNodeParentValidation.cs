using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Infrastructure;
using Marten;

namespace CritCrit.Api.Org.Features.OrgNodes;

// Shared helpers extracted from the (now-removed) OrgNodeHandlers god-class so
// the per-endpoint classes can stay focused on their own decide logic.
internal static class OrgNodeParentValidation
{
    public static async Task<OrgNodeReadModel> ValidateParentAsync(
        IDocumentSession session,
        ActorContext actor,
        OrgAuthorizationService authorization,
        Guid tenantId,
        OrgNodeId parentId,
        OrgNodeType childType,
        CancellationToken ct)
    {
        var parent = await OrgValidation.LoadActiveNodeAsync(session, parentId, ct);

        if (parent.TenantId != tenantId)
            throw new DomainException("Org node does not belong to the requested brand tenant.");

        if (!OrgRules.CanContain(parent.Type, childType))
            throw new DomainException($"{parent.Type} cannot contain {childType}.");

        await authorization.EnforceRoleAsync(session, actor, parent, OrgRole.Admin, ct);
        return parent;
    }

    public static object DescribeNode(OrgNodeReadModel node) => new
    {
        node.PublicId,
        node.Type,
        node.Code,
        node.Name,
        node.ParentPublicId
    };
}
