using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using System.Security.Claims;
using System.Text.Json;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Infrastructure;
using CritCrit.Api.Org.Projections;
using Marten.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

const string KeycloakDevBaseUrl = "http://localhost:8080";
const string KeycloakRealm = "api";

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("keycloak", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"{KeycloakDevBaseUrl}/realms/{KeycloakRealm}/protocol/openid-connect/auth"),
                TokenUrl = new Uri($"{KeycloakDevBaseUrl}/realms/{KeycloakRealm}/protocol/openid-connect/token"),
                Scopes = new Dictionary<string, string>
                {
                    ["openid"] = "OpenID",
                    ["profile"] = "Profile"
                }
            }
        }
    });

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("keycloak")] = new List<string>()
    });
});

builder.AddServiceDefaults();

builder.Services.AddMarten(m =>
{
    // Much more coming...
    m.Connection(builder.Configuration.GetConnectionString("marten")!);
    m.Schema.For<OrgNodeReadModel>().MultiTenanted();
    m.Schema.For<OrgNodeCodeIndex>().MultiTenanted();
    m.Schema.For<StoreProfileReadModel>().MultiTenanted();
    m.Schema.For<DeviceProfileReadModel>().MultiTenanted();
    m.Schema.For<OrgAccessGrantReadModel>().MultiTenanted();
    m.Schema.For<SubjectReadModel>().SingleTenanted();
    m.Schema.For<ExternalIdentityReadModel>().SingleTenanted();
    m.Schema.For<BrandTombstone>().SingleTenanted();
    m.Schema.For<ImmutableAuditEvent>().SingleTenanted();

    // 50% improvement in throughput, less "event skipping"
    m.Events.AppendMode = EventAppendMode.Quick;
    // or if you care about the timestamps -->
    m.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;

    // 100% do this, but be aggressive about taking advantage of it
    m.Events.UseArchivedStreamPartitioning = true;

    // These cause some database changes, so can't be defaults,
    // but these might help "heal" systems that have problems
    // later
    m.Events.EnableAdvancedAsyncTracking = true;

    // Enables you to mark events as just plain bad so they are skipped
    // in projections from here on out.
    m.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;

    // If you do this, just now you pretty well have to use FetchForWriting
    // in your commands
    // But also, you should use FetchForWriting() for command handlers 
    // any way
    // This will optimize the usage of Inline projections, but will force
    // you to treat your aggregate projection "write models" as being 
    // immutable in your command handler code
    // You'll want to use the "Decider Pattern" / "Aggregate Handler Workflow"
    // style for your commands rather than a self-mutating "AggregateRoot"
    m.Events.UseIdentityMapForAggregates = true;

    // Future proofing a bit. Will help with some future optimizations
    // for rebuild optimizations
    m.Events.UseMandatoryStreamTypeDeclaration = true;

    // This is just annoying anyway
    m.DisableNpgsqlLogging = true;
    
    m.Projections.Add<OrgNodeProjection>(ProjectionLifecycle.Inline);
    m.Projections.Add<OrgNodeCodeIndexProjection>(ProjectionLifecycle.Inline);
    m.Projections.Add<StoreProfileProjection>(ProjectionLifecycle.Inline);
    m.Projections.Add<DeviceProfileProjection>(ProjectionLifecycle.Inline);
    m.Projections.Add<SubjectProjection>(ProjectionLifecycle.Inline);
    m.Projections.Add<ExternalIdentityProjection>(ProjectionLifecycle.Inline);
    m.Projections.Add<GrantProjection>(ProjectionLifecycle.Inline);
})
 .ApplyAllDatabaseChangesOnStartup()
 // This will remove some runtime overhead from Marten
.UseLightweightSessions()

.IntegrateWithWolverine(x =>
{
    // Let Wolverine do the load distribution better than
    // what Marten by itself can do
    x.UseWolverineManagedEventSubscriptionDistribution = true;
});

builder.Services.AddWolverine(opts =>
{
    opts.UseRabbitMqUsingNamedConnection("rabbitmq")
        .AutoProvision()
        .EnableWolverineControlQueues();
    
    // This *should* have some performance improvements, but would
    // require downtime to enable in existing systems
    opts.Durability.EnableInboxPartitioning = true;

    // Extra resiliency for unexpected problems, but can't be
    // defaults because this causes database changes
    opts.Durability.InboxStaleTime = 10.Minutes();
    opts.Durability.OutboxStaleTime = 10.Minutes();

    // Just annoying
    opts.EnableAutomaticFailureAcks = false;

    // Relatively new behavior that will store "unknown" messages
    // in the dead letter queue for possible recovery later
    opts.UnknownMessageBehavior = UnknownMessageBehavior.DeadLetterQueue;
    
    opts.UseFluentValidation();
    opts.UseFluentValidationProblemDetail();
});

builder.Services.AddWolverineHttp();
builder.Services.AddScoped<OrgAuthorizationService>();
builder.Services.AddScoped<OrgCommandService>();

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthenticationHandler>(
            TestAuthenticationHandler.SchemeName,
            _ => { });
}
else
{
    builder.Services.AddAuthentication()
        .AddKeycloakJwtBearer(
        serviceName: "keycloak",
        realm: KeycloakRealm,
        options =>
        {
            options.Audience = "store.api";
            options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
            options.TokenValidationParameters.NameClaimType = "preferred_username";

            if (builder.Environment.IsDevelopment())
            {
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters.ValidIssuers =
                [
                    $"{KeycloakDevBaseUrl}/realms/{KeycloakRealm}"
                ];
            }

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = ctx =>
                {
                    if (ctx.Principal?.Identity is not ClaimsIdentity identity)
                        return Task.CompletedTask;

                    var realmAccess = identity.FindFirst("realm_access")?.Value;
                    if (!string.IsNullOrEmpty(realmAccess))
                    {
                        using var doc = JsonDocument.Parse(realmAccess);
                        if (doc.RootElement.TryGetProperty("roles", out var roles)
                            && roles.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var role in roles.EnumerateArray())
                            {
                                var value = role.GetString();
                                if (!string.IsNullOrEmpty(value))
                                    identity.AddClaim(new Claim(ClaimTypes.Role, value));
                            }
                        }
                    }

                    return Task.CompletedTask;
                }
            };
        });
}

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapSwagger("/openapi/{documentName}.json");
    app.MapScalarApiReference(opts =>
    {
        opts.AddPreferredSecuritySchemes("keycloak")
            .AddAuthorizationCodeFlow("keycloak", flow =>
            {
                flow.ClientId = "store.api.swagger";
                flow.Pkce = Pkce.Sha256;
                flow.SelectedScopes = ["openid", "profile"];
            });
    });
}

app.UseHttpsRedirection();

app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<DomainExceptionMiddleware>();
app.UseMiddleware<BrandTenantMiddleware>();

app.MapWolverineEndpoints(c =>
{
    c.UseFluentValidationProblemDetailMiddleware();
    c.ServiceProviderSource = ServiceProviderSource.FromHttpContextRequestServices;
    c.SourceServiceFromHttpContext<IDocumentStore>();
    c.SourceServiceFromHttpContext<OrgCommandService>();
    c.AddPolicy<OrgHttpPolicy>();
});

return await app.RunJasperFxCommands(args);
