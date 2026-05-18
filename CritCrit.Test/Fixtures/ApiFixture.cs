using Alba;
using CritCrit.Test.Outbox;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Wolverine;

namespace CritCrit.Test.Fixtures;

[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<ApiFixture>;

public class ApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgreSqlContainer = null!;
    private IContainer _azuriteContainer = null!;
    public IAlbaHost AlbaHost { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        _postgreSqlContainer = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("marten")
            .Build();
        _azuriteContainer = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithPortBinding(10000, true)
            .WithCommand("azurite-blob", "--blobHost", "0.0.0.0")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(10000))
            .Build();
        await Task.WhenAll(_postgreSqlContainer.StartAsync(), _azuriteContainer.StartAsync());

        JasperFxEnvironment.AutoStartHost = true;
        var azuriteConnectionString = AzuriteConnectionString();
        
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
            builder.UseSetting("ConnectionStrings:assets", azuriteConnectionString);
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

    private string AzuriteConnectionString()
    {
        var host = _azuriteContainer.Hostname;
        var blobPort = _azuriteContainer.GetMappedPublicPort(10000);
        const string accountName = "devstoreaccount1";
        const string accountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
        return $"DefaultEndpointsProtocol=http;AccountName={accountName};AccountKey={accountKey};BlobEndpoint=http://{host}:{blobPort}/{accountName};";
    }
}
