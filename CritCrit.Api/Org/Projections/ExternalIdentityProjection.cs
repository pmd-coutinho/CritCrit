using CritCrit.Api.Org.Domain;
using Marten;
using Marten.Events;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

public sealed class ExternalIdentityProjection : EventProjection
{
    public void Project(ExternalIdentityLinked e, IDocumentOperations ops)
    {
        ops.Store(new ExternalIdentityReadModel
        {
            Id = ExternalIdentityReadModel.BuildId(e.Provider, e.ProviderTenant, e.ExternalId),
            SubjectId = e.SubjectId.Value,
            Provider = e.Provider,
            ProviderTenant = e.ProviderTenant,
            ExternalId = e.ExternalId
        });
    }

    public void Project(ExternalIdentityRelinked e, IDocumentOperations ops)
    {
        // Drop the old link so authentication via the previous Keycloak user
        // no longer resolves to this subject, then write the fresh one.
        ops.Delete<ExternalIdentityReadModel>(
            ExternalIdentityReadModel.BuildId(e.Provider, e.ProviderTenant, e.OldExternalId));

        ops.Store(new ExternalIdentityReadModel
        {
            Id = ExternalIdentityReadModel.BuildId(e.Provider, e.ProviderTenant, e.NewExternalId),
            SubjectId = e.SubjectId.Value,
            Provider = e.Provider,
            ProviderTenant = e.ProviderTenant,
            ExternalId = e.NewExternalId
        });
    }
}
