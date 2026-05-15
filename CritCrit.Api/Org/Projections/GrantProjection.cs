using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Patching;

namespace CritCrit.Api.Org.Projections;

public sealed class GrantProjection : EventProjection
{
    public void Project(IEvent<OrgAccessGranted> e, IDocumentOperations ops)
    {
        ops.Store(new OrgAccessGrantReadModel
        {
            Id = OrgAccessGrantReadModel.BuildId(e.Data.TenantId, e.Data.OrgNodeId, e.Data.SubjectId),
            StreamId = e.StreamId,
            TenantId = e.Data.TenantId.Value,
            OrgNodeId = e.Data.OrgNodeId.Value,
            SubjectId = e.Data.SubjectId.Value,
            Role = e.Data.Role,
            ExpiresAt = e.Data.ExpiresAt,
            Status = OrgAccessGrantStatus.Active,
            Source = e.Data.Source,
            InvitationId = e.Data.InvitationId?.Value
        });
    }

    public void Project(OrgAccessRoleChanged e, IDocumentOperations ops)
    {
        var id = OrgAccessGrantReadModel.BuildId(e.TenantId, e.OrgNodeId, e.SubjectId);
        ops.Patch<OrgAccessGrantReadModel>(id)
            .Set(x => x.Role, e.NewRole);
    }

    public void Project(OrgAccessRevoked e, IDocumentOperations ops)
    {
        var id = OrgAccessGrantReadModel.BuildId(e.TenantId, e.OrgNodeId, e.SubjectId);
        ops.Patch<OrgAccessGrantReadModel>(id)
            .Set(x => x.Status, OrgAccessGrantStatus.Revoked);
    }

    public void Project(OrgAccessExpired e, IDocumentOperations ops)
    {
        var id = OrgAccessGrantReadModel.BuildId(e.TenantId, e.OrgNodeId, e.SubjectId);
        ops.Patch<OrgAccessGrantReadModel>(id)
            .Set(x => x.Status, OrgAccessGrantStatus.Expired);
    }

    public void Project(OrgAccessExpirationChanged e, IDocumentOperations ops)
    {
        var id = OrgAccessGrantReadModel.BuildId(e.TenantId, e.OrgNodeId, e.SubjectId);
        ops.Patch<OrgAccessGrantReadModel>(id)
            .Set(x => x.ExpiresAt, e.NewExpiresAt);
    }
}
