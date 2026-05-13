using CritCrit.Api.Org.Domain;
using Marten;
using Marten.Events;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

public sealed class OrgNodeProjection : EventProjection
{
    public async Task Project(OrgNodeCreated e, IDocumentOperations ops, CancellationToken ct)
    {
        var parent = e.ParentId is not null
            ? await ops.LoadAsync<OrgNodeReadModel>(e.ParentId.Value.Value, ct)
            : null;

        var ancestors = parent is null
            ? new List<Guid>()
            : parent.AncestorIds.Append(parent.Id).ToList();

        var ancestorPublicIds = parent is null
            ? new List<string>()
            : parent.AncestorPublicIds.Append(parent.PublicId).ToList();

        var segment = $"{e.Type.ToString().ToLowerInvariant()}/{e.Code}";
        var path = parent is null ? segment : $"{parent.Path}/{segment}";

        ops.Store(new OrgNodeReadModel
        {
            Id = e.Id.Value,
            PublicId = OrgPublicId.Format(e.Type, e.Id),
            TenantId = e.TenantId.Value,
            TenantPublicId = OrgPublicId.Format(OrgNodeType.Brand, e.TenantId),
            ParentId = e.ParentId?.Value,
            ParentPublicId = parent?.PublicId,
            Type = e.Type,
            Code = e.Code,
            CodeNormalized = e.CodeNormalized,
            Name = e.Name,
            AncestorIds = ancestors,
            AncestorPublicIds = ancestorPublicIds,
            Path = path
        });
    }

    public async Task Project(OrgNodeRenamed e, IDocumentOperations ops, CancellationToken ct)
    {
        var node = await ops.LoadAsync<OrgNodeReadModel>(e.Id.Value, ct);
        if (node is not null)
        {
            node.Name = e.NewName;
            ops.Store(node);
        }
    }

    public async Task Project(OrgNodeArchived e, IDocumentOperations ops, CancellationToken ct)
    {
        var node = await ops.LoadAsync<OrgNodeReadModel>(e.Id.Value, ct);
        if (node is not null)
        {
            node.Archived = true;
            node.EffectiveArchived = true;
            ops.Store(node);
        }
    }

    public async Task Project(OrgNodeRestored e, IDocumentOperations ops, CancellationToken ct)
    {
        var node = await ops.LoadAsync<OrgNodeReadModel>(e.Id.Value, ct);
        if (node is not null)
        {
            node.Archived = false;
            node.EffectiveArchived = false;
            ops.Store(node);
        }
    }

    public void Project(OrgNodeHardDeleted e, IDocumentOperations ops)
    {
        ops.Delete<OrgNodeReadModel>(e.Id.Value);
    }
}
