using Alba;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Testcontainers.PostgreSql;
using Wolverine;

namespace CritCrit.Test.Fixtures;

public class ApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgreSqlContainer = null!;
    public IAlbaHost AlbaHost { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        _postgreSqlContainer = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("marten")
            .Build();
        await _postgreSqlContainer.StartAsync();

        JasperFxEnvironment.AutoStartHost = true;
        
        AlbaHost = await Alba.AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                // Run Wolverine in "solo" mode for faster test startup —
                // skips leader election, durability agents, etc.
                services.RunWolverineInSoloMode();

                // Disable external messaging transports (Rabbit MQ, SQS, etc.)
                // so tests run without infrastructure dependencies
                services.DisableAllExternalWolverineTransports();
            });
            builder.UseSetting("ConnectionStrings:marten", _postgreSqlContainer.GetConnectionString());
        });

    }

    public async Task DisposeAsync()
    {
        await AlbaHost.StopAsync();
        await AlbaHost.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
    }
}
