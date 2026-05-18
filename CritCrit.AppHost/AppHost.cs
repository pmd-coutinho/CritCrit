using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgWeb()
    .WithDataVolume();

var database = postgres.AddDatabase("marten");

var keycloakUsername = builder.AddParameter("keycloak-admin-username", "admin");
var keycloakPassword = builder.AddParameter("keycloak-admin-password", secret: true);

var keycloak = builder.AddKeycloak("keycloak", 8080, keycloakUsername, keycloakPassword)
    .WithDataVolume()
    .WithOtlpExporter();

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume()
    .WithOtlpExporter();

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume());
// "blobs" is the blob-service connection string (SDK-format URI) the API consumes.
// "assets" is the container resource — used for declarative container creation +
// dashboard discovery but its connection string is not SDK-compatible.
var blobs = storage.AddBlobs("blobs");
var assets = storage.AddBlobContainer("assets", blobContainerName: "assets");

var mailpit = builder.AddMailPit("mailpit");

const string spaUrl = "http://localhost:5173";

var api = builder.AddProject<CritCrit_Api>("api")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithReference(blobs)
    .WithReference(assets)
    .WaitFor(assets)
    .WithReference(mailpit)
    .WaitFor(mailpit)
    .WithEnvironment("Invitation__IdentityProvider__Keycloak__BaseUrl", "http://localhost:8080")
    .WithEnvironment("Invitation__IdentityProvider__Keycloak__Realm", "api")
    .WithEnvironment("Invitation__IdentityProvider__Keycloak__AdminRealm", "master")
    .WithEnvironment("Invitation__IdentityProvider__Keycloak__AdminClientId", "admin-cli")
    .WithEnvironment("Invitation__IdentityProvider__Keycloak__AdminUsername", keycloakUsername)
    .WithEnvironment("Invitation__IdentityProvider__Keycloak__AdminPassword", keycloakPassword)
    .WithEnvironment("Invitation__PublicBaseUrl", spaUrl)
    .WithEnvironment("Cors__AllowedOrigins__0", spaUrl);

builder.AddViteApp("web", "../CritCrit.Web")
    .WithYarn()
    .WithReference(api)
    .WaitFor(api)
    .WithEndpoint("http", e =>
    {
        e.Port = 5173;
        e.TargetPort = 5173;
        e.IsProxied = false;
    })
    .WithExternalHttpEndpoints()
    .WithEnvironment("VITE_API_URL", api.GetEndpoint("https"))
    .WithEnvironment("VITE_KEYCLOAK_URL", "http://localhost:8080")
    .WithEnvironment("VITE_KEYCLOAK_REALM", "api")
    .WithEnvironment("VITE_KEYCLOAK_CLIENT_ID", "critcrit.web");

builder.Build().Run();
