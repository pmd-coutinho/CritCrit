using Alba;
using CritCrit.Api.Org.Identity;
using CritCrit.Api.Org.Invitations;
using CritCrit.Test.Outbox;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace CritCrit.Test.Fixtures;

/// <summary>
/// Collection-shared base for integration tests. Resets the Marten store before
/// each test method and exposes a Wolverine-aware <see cref="TrackedHttpCall"/>
/// helper that waits for cascaded messages (provisioning, email dispatch,
/// scheduled expirations) to settle before returning the HTTP result.
/// </summary>
[Collection("integration")]
public abstract class IntegrationContext : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    protected IntegrationContext(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    public IAlbaHost Host => _fixture.AlbaHost;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await Host.ResetAllMartenDataAsync();

        // In-memory test doubles outlive Marten resets; clear them so each test
        // sees a fresh world.
        var emailStore = Host.Services.GetRequiredService<TestInvitationEmailStore>();
        emailStore.Sent.Clear();
        emailStore.FailInvitationIds.Clear();
        emailStore.FailAll = false;

        var idpStore = Host.Services.GetRequiredService<TestIdentityProviderStore>();
        idpStore.Users.Clear();

        var provisioning = (InMemoryIdentityProviderProvisioning)Host.Services
            .GetRequiredService<IIdentityProviderProvisioning>();
        provisioning.PasswordSetupCalls.Clear();

        Host.Services.GetRequiredService<OutboxProbeStore>().Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public Task<IScenarioResult> Scenario(Action<Scenario> configure) => Host.Scenario(configure);

    protected async Task<(ITrackedSession Tracked, IScenarioResult Result)> TrackedHttpCall(
        Action<Scenario> configure,
        int timeoutInMilliseconds = 10_000)
    {
        IScenarioResult result = null!;
        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configure);
        }, timeoutInMilliseconds);
        return (tracked, result);
    }
}
