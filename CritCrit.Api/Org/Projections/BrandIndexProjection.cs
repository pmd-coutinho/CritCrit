using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Marten.Patching;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Cross-tenant index of brands at the platform level. Updated inline whenever a
/// Brand-type org node is created, archived, restored, or hard-deleted in any
/// tenant schema. Drives SuperAdmin's GET /api/brands path.
/// </summary>
public sealed class BrandIndexProjection : EventProjection
{
    public void Project(IEvent<OrgNodeCreated> e, IDocumentOperations ops)
    {
        if (e.Data.Type != OrgNodeType.Brand)
            return;

        ops.Store(new BrandIndexReadModel
        {
            Id = e.Data.Id.Value,
            PublicId = OrgPublicId.Format(OrgNodeType.Brand, e.Data.Id),
            Code = e.Data.Code,
            Name = e.Data.Name,
            Archived = false,
            CreatedAt = e.Timestamp
        });
    }

    public void Project(IEvent<OrgNodeRenamed> e, IDocumentOperations ops)
    {
        ops.Patch<BrandIndexReadModel>(e.Data.Id.Value)
            .Set(x => x.Name, e.Data.NewName);
    }

    public void Project(IEvent<OrgNodeArchived> e, IDocumentOperations ops)
    {
        // Only brands appear in this index; patching a non-existent id is a no-op.
        ops.Patch<BrandIndexReadModel>(e.Data.Id.Value)
            .Set(x => x.Archived, true);
    }

    public void Project(IEvent<OrgNodeRestored> e, IDocumentOperations ops)
    {
        ops.Patch<BrandIndexReadModel>(e.Data.Id.Value)
            .Set(x => x.Archived, false);
    }

    public void Project(IEvent<OrgNodeHardDeleted> e, IDocumentOperations ops)
    {
        ops.Delete<BrandIndexReadModel>(e.Data.Id.Value);
    }
}
