using System.Net;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public sealed class SubjectListTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task superadmin_lists_subjects()
    {
        await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("alpha@example.com", "Alpha", "test", "default", "alpha-ext"));
        await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("beta@example.com", null, "test", "default", "beta-ext"));

        var rows = await GetAsSuperAdmin<List<SubjectListItem>>("/api/platform/subjects");

        Assert.Contains(rows, x => x.Email == "alpha@example.com");
        Assert.Contains(rows, x => x.Email == "beta@example.com");
        Assert.All(rows, x =>
        {
            Assert.Equal(SubjectKind.User, x.Kind);
            Assert.True(x.Active);
            Assert.Null(x.OnboardedAt);
        });
    }

    [Fact]
    public async Task filter_by_email_contains_narrows_results()
    {
        await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("alpha@example.com", null, "test", "default", "alpha-ext"));
        await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("beta@example.com", null, "test", "default", "beta-ext"));

        var rows = await GetAsSuperAdmin<List<SubjectListItem>>("/api/platform/subjects?emailContains=alpha");

        Assert.Single(rows);
        Assert.Equal("alpha@example.com", rows[0].Email);
    }

    [Fact]
    public async Task non_superadmin_is_forbidden()
    {
        await GetAsUserRaw("/api/platform/subjects", "regular-user", "user@example.com", HttpStatusCode.Forbidden);
    }
}
