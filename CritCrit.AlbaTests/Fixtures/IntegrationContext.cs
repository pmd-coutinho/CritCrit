using Alba;
using Marten;
using Wolverine.Tracking;

namespace CritCrit.AlbaTests.Fixtures;

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
        // Reset database state before each test.
        // How you do this depends on your persistence provider:

        // For Marten:
        await Host.ResetAllMartenDataAsync();

        // For EF Core, resolve your DbContext and clean up:
        // using var scope = Host.Services.CreateScope();
        // var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        // await db.Database.EnsureDeletedAsync();
        // await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Simple Alba-only HTTP call (no message tracking)
    public Task<IScenarioResult> Scenario(Action<Scenario> configure)
    {
        return Host.Scenario(configure);
    }

    // The key method: combines Alba HTTP calls with Wolverine message tracking
    protected async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(
        Action<Scenario> configuration,
        int timeoutInMilliseconds = 5000)
    {
        IScenarioResult result = null!;

        // The outer part ties into Wolverine's test support
        // to "wait" for all detected message activity to complete
        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            // The inner part makes an HTTP request
            // to the system under test with Alba
            result = await Host.Scenario(configuration);
        }, timeoutInMilliseconds);

        return (tracked, result);
    }
}