using CritCrit.Api.Org.Domain;
using Marten.Events.Aggregation;

namespace CritCrit.Api.Org.Projections;

public sealed class SubjectProjection : SingleStreamProjection<SubjectReadModel, Guid>
{
    public SubjectProjection()
    {
    }

    public SubjectReadModel Create(SubjectCreated e)
    {
        return new SubjectReadModel
        {
            Id = e.Id.Value,
            PublicId = OrgPublicId.FormatSubject(e.Id),
            Kind = e.Kind,
            Email = e.Email,
            EmailNormalized = e.Email.Trim().ToLowerInvariant(),
            DisplayName = e.DisplayName,
            Active = true
        };
    }
}
