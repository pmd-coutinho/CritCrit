using Alba;
using CritCrit.Test.Outbox;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;
using Wolverine;

namespace CritCrit.Test.Fixtures;

[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<ApiFixture>;

public class ApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgreSqlContainer = null!;
    private AzuriteContainer _azuriteContainer = null!;
    public IAlbaHost AlbaHost { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        _postgreSqlContainer = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("marten")
            .Build();
        _azuriteContainer = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .Build();
        await Task.WhenAll(_postgreSqlContainer.StartAsync(), _azuriteContainer.StartAsync());

        JasperFxEnvironment.AutoStartHost = true;

        AlbaHost = await Alba.AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<OutboxProbeStore>();
                services.ConfigureWolverine(options =>
                {
                    options.Discovery.IncludeAssembly(typeof(OutboxProbeHandlers).Assembly);
                });

                // Run Wolverine in "solo" mode for faster test startup —
                // skips leader election, durability agents, etc.
                services.RunWolverineInSoloMode();

                // Disable external messaging transports (Rabbit MQ, SQS, etc.)
                // so tests run without infrastructure dependencies
                services.DisableAllExternalWolverineTransports();
            });
            builder.UseSetting("ConnectionStrings:marten", _postgreSqlContainer.GetConnectionString());
            builder.UseSetting("ConnectionStrings:assets", _azuriteContainer.GetConnectionString());
            builder.UseSetting("Assets:ContainerName", "assets-tests");
        });

    }

    public async Task DisposeAsync()
    {
        await AlbaHost.StopAsync();
        await AlbaHost.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
        await _azuriteContainer.DisposeAsync();
    }
}
