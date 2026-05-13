using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgWeb()
    .WithDataVolume();

var database = postgres.AddDatabase("marten");

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithOtlpExporter();

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume()
    .WithOtlpExporter();

builder.AddProject<CritCrit_Api>("api")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.Build().Run();