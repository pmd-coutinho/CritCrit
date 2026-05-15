using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alba;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Identity;
using CritCrit.Api.Org.Invitations;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace CritCrit.Test.Fixtures;

/// <summary>
/// Adds shared helpers (auth headers, JSON, scenario sugar) on top of
/// <see cref="IntegrationContext"/>. Keep it thin — anything domain-specific
/// belongs in the test file itself.
/// </summary>
public abstract class IntegrationTestBase(ApiFixture fixture) : IntegrationContext(fixture)
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    protected IDocumentStore DocumentStore => Host.Services.GetRequiredService<IDocumentStore>();
    protected IMessageBus Bus => Host.Services.GetRequiredService<IMessageBus>();
    protected TestInvitationEmailStore InvitationEmailStore =>
        Host.Services.GetRequiredService<TestInvitationEmailStore>();
    protected TestIdentityProviderStore IdentityProviderStore =>
        Host.Services.GetRequiredService<TestIdentityProviderStore>();
    protected InMemoryIdentityProviderProvisioning Provisioning =>
        (InMemoryIdentityProviderProvisioning)Host.Services.GetRequiredService<IIdentityProviderProvisioning>();

    protected static string Code(string prefix = "brand") => $"{prefix}-{Guid.NewGuid():N}"[..32];

    protected static void AsSuperAdmin(Scenario s)
    {
        s.WithRequestHeader("X-Test-User", "superadmin-idp");
        s.WithRequestHeader("X-Test-Email", "superadmin@example.com");
        s.WithRequestHeader("X-Test-Roles", "critcrit.superadmin");
    }

    protected static void AsUser(Scenario s, string externalId, string email)
    {
        s.WithRequestHeader("X-Test-User", externalId);
        s.WithRequestHeader("X-Test-Email", email);
    }

    protected async Task<TResponse> PostAsSuperAdmin<TRequest, TResponse>(
        string url,
        TRequest request,
        HttpStatusCode expectedStatus = HttpStatusCode.Created)
    {
        var result = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(request!, JsonStyle.MinimalApi).ToUrl(url);
            _.StatusCodeShouldBe(expectedStatus);
        });
        return JsonSerializer.Deserialize<TResponse>(await result.ReadAsTextAsync(), JsonOptions)!;
    }

    protected async Task<TResponse> GetAsSuperAdmin<TResponse>(
        string url,
        HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        var result = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url(url);
            _.StatusCodeShouldBe(expectedStatus);
        });
        return JsonSerializer.Deserialize<TResponse>(await result.ReadAsTextAsync(), JsonOptions)!;
    }

    protected async Task<TResponse> GetAsUser<TResponse>(
        string url,
        string externalId,
        string email,
        HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        var result = await Host.Scenario(_ =>
        {
            AsUser(_, externalId, email);
            _.Get.Url(url);
            _.StatusCodeShouldBe(expectedStatus);
        });
        return JsonSerializer.Deserialize<TResponse>(await result.ReadAsTextAsync(), JsonOptions)!;
    }

    protected async Task<IScenarioResult> GetAsUserRaw(
        string url,
        string externalId,
        string email,
        HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        return await Host.Scenario(_ =>
        {
            AsUser(_, externalId, email);
            _.Get.Url(url);
            _.StatusCodeShouldBe(expectedStatus);
        });
    }

    protected async Task<OrgNodeResponse> CreateBrand(string? code = null)
    {
        return await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(code ?? Code(), "Brand"));
    }
}
