using System.Security.Claims;
using System.Text.Json;
using Azure.Storage.Blobs;
using CritCrit.Api.Org.Features.Assets;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Identity;
using CritCrit.Api.Org.Infrastructure;
using CritCrit.Api.Org.Invitations;
using CritCrit.Api.Org.Features.Config;
using CritCrit.Api.Org.Projections;
using CritCrit.Api.Observability.Audit;
using CritCrit.Api.Observability.Support;
using CritCrit.Api.Observability.Telemetry;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Services;
using Marten.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

namespace CritCrit.Api.Configuration;

public static class CritCritApiConfiguration
{
    public static WebApplicationBuilder AddCritCritApiServices(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        builder.AddServiceDefaults();
        builder.Services.AddCritCritObservability();
        builder.Services.AddCritCritOpenApi(builder.Configuration);
        builder.Services.AddCritCritPersistence(builder.Configuration);
        builder.Services.AddCritCritMessaging();
        builder.Services.AddWolverineHttp();
        builder.Services.AddOrgModule(builder.Configuration, builder.Environment);
        builder.Services.AddCritCritAuth(builder.Configuration, builder.Environment);
        builder.Services.AddCritCritCors(builder.Configuration);

        return builder;
    }

    public static WebApplication UseCritCritApiPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            app.MapCritCritOpenApi();

        app.UseHttpsRedirection();
        app.UseCors();
        app.MapDefaultEndpoints();
        app.UseMiddleware<SupportIdMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<DomainExceptionMiddleware>();
        app.UseMiddleware<RequestActorMiddleware>();
        app.UseMiddleware<BrandTenantMiddleware>();
        app.UseMiddleware<WolverineHttpTraceMiddleware>();
        app.MapCritCritWolverineEndpoints();

        return app;
    }

    private static IServiceCollection AddCritCritOpenApi(this IServiceCollection services, IConfiguration configuration)
    {
        var keycloakBaseUrl = KeycloakBaseUrl(configuration);
        var keycloakRealm = KeycloakRealm(configuration);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("keycloak", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{keycloakBaseUrl}/realms/{keycloakRealm}/protocol/openid-connect/auth"),
                        TokenUrl = new Uri($"{keycloakBaseUrl}/realms/{keycloakRealm}/protocol/openid-connect/token"),
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

        return services;
    }

    private static IServiceCollection AddCritCritPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMarten(m =>
            {
                m.Connection(configuration.GetConnectionString("marten")!);
                m.DisableNpgsqlLogging = true;
                m.OpenTelemetry.TrackConnections = TrackLevel.Normal;
                m.OpenTelemetry.TrackEventCounters();

                ConfigureDocumentStorage(m);
                ConfigureEventStore(m);
                ConfigureProjections(m);
            })
            .ApplyAllDatabaseChangesOnStartup()
            .UseLightweightSessions()
            .IntegrateWithWolverine(x =>
            {
                x.UseWolverineManagedEventSubscriptionDistribution = true;
            });

        services.AddMartenTenancyDetection(
            t => t.DetectWith(new BrandTenantDetection()),
            (httpContext, session) =>
            {
                if (httpContext.Items[RequestActorMiddleware.ItemKey] is ActorContext actor && actor.IsAuthenticated)
                    SessionMetadata.StampActor(session, actor);
            });

        return services;
    }

    private static void ConfigureDocumentStorage(StoreOptions m)
    {
        m.Schema.For<OrgNodeReadModel>()
            .MultiTenanted()
            .Index(x => x.TenantId)
            .Index(x => x.ParentId)
            .Index(x => x.HardDeleted);
        m.Schema.For<OrgNodeCodeIndex>()
            .MultiTenanted()
            .Index(x => x.TenantId)
            .Index(x => x.CodeNormalized);
        m.Schema.For<StoreProfileReadModel>()
            .MultiTenanted()
            .Index(x => x.TenantId);
        m.Schema.For<DeviceProfileReadModel>()
            .MultiTenanted()
            .Index(x => x.TenantId)
            .Index(x => x.SerialNumberNormalized);
        m.Schema.For<OrgAccessGrantReadModel>()
            .MultiTenanted()
            .Index(x => x.TenantId)
            .Index(x => x.OrgNodeId)
            .Index(x => x.SubjectId)
            .Index(x => x.Status);

        m.Schema.For<SubjectReadModel>()
            .MultiTenanted()
            .Index(x => x.EmailNormalized)
            .Index(x => x.Active);
        m.Schema.For<ExternalIdentityReadModel>().SingleTenanted();
        m.Schema.For<BrandTombstone>().SingleTenanted();
        m.Schema.For<ImmutableAuditEvent>()
            .SingleTenanted()
            .Index(x => x.Action)
            .Index(x => x.Category)
            .Index(x => x.Severity)
            .Index(x => x.TenantId)
            .Index(x => x.TargetOrgNodeId)
            .Index(x => x.SubjectId)
            .Index(x => x.ActorExternalId)
            .Index(x => x.ActorSubjectId)
            .Index(x => x.SupportId)
            .Index(x => x.OccurredAt);
        m.Schema.For<InvitationReadModel>()
            .MultiTenanted()
            .Index(x => x.TenantId)
            .Index(x => x.TargetOrgNodeId)
            .Index(x => x.EmailNormalized)
            .Index(x => x.Status)
            .Index(x => x.TokenHash);
        m.Schema.For<BrandIndexReadModel>()
            .SingleTenanted()
            .Index(x => x.Name);
        m.Schema.For<SubjectBrandAccessReadModel>()
            .SingleTenanted()
            .Index(x => x.SubjectId)
            .Index(x => x.TenantId);

        // ─── Config service ───
        m.Schema.For<ConfigSchemaReadModel>()
            .MultiTenanted()
            .Index(x => x.CodeNormalized)
            .Index(x => x.Archived);
        m.Schema.For<ConfigSchemaVersionReadModel>()
            .SingleTenanted()
            .Index(x => x.SchemaCode)
            .Index(x => x.Version);
        m.Schema.For<ConfigSchemaDraftReadModel>()
            .MultiTenanted()
            .Index(x => x.SchemaCode)
            .Index(x => x.Archived)
            .Index(x => x.Published);
        m.Schema.For<ConfigAssignmentReadModel>()
            .MultiTenanted()
            .Index(x => x.TenantId)
            .Index(x => x.RootOrgNodeId)
            .Index(x => x.SchemaCode)
            .Index(x => x.SchemaVersion)
            .Index(x => x.Archived);
        m.Schema.For<ConfigNodeValueReadModel>()
            .MultiTenanted()
            .Index(x => x.TenantId)
            .Index(x => x.OrgNodeId)
            .Index(x => x.SchemaCode);

        // ─── Assets service ───
        m.Schema.For<AssetNodeValueReadModel>()
            .MultiTenanted()
            .Index(x => x.TenantId)
            .Index(x => x.OrgNodeId);
    }

    private static void ConfigureEventStore(StoreOptions m)
    {
        m.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        m.Events.MetadataConfig.HeadersEnabled = true;
        m.Events.MetadataConfig.CausationIdEnabled = true;
        m.Events.MetadataConfig.CorrelationIdEnabled = true;
        m.Events.MetadataConfig.UserNameEnabled = true;
        m.Events.UseArchivedStreamPartitioning = true;
        m.Events.EnableAdvancedAsyncTracking = true;
        m.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;
        m.Events.UseIdentityMapForAggregates = true;
        m.Events.UseMandatoryStreamTypeDeclaration = true;
        // Conjoined-tenancy unlocks SingleStreamProjection for MultiTenanted
        // aggregates (Org / AccessGrant / ConfigAssignment / ConfigNodeValue /
        // AssetNodeValue). Platform-scoped streams use the PlatformTenant
        // sentinel. See .scratch/event-store-tenancy/PRD.md.
        m.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
    }

    private static IServiceCollection AddCritCritObservability(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuditWriter, AuditWriter>();
        return services;
    }

    private static void ConfigureProjections(StoreOptions m)
    {
        m.Projections.Add<OrgNodeProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<OrgNodeCodeIndexProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<StoreProfileProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<DeviceProfileProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<SubjectProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<ExternalIdentityProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<GrantProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<MoveOrgNodeProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<InvitationProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<BrandIndexProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<SubjectBrandAccessProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<ConfigSchemaProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<ConfigSchemaDraftProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<ConfigSchemaVersionProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<ConfigSchemaDraftPublishedTracker>(ProjectionLifecycle.Inline);
        m.Projections.Add<ConfigAssignmentProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<ConfigNodeValueProjection>(ProjectionLifecycle.Inline);
        m.Projections.Add<AssetNodeValueProjection>(ProjectionLifecycle.Inline);
    }

    private static IServiceCollection AddCritCritMessaging(this IServiceCollection services)
    {
        services.AddWolverine(opts =>
        {
            opts.UseRabbitMqUsingNamedConnection("rabbitmq")
                .AutoProvision()
                .EnableWolverineControlQueues()
                .UseConventionalRouting();

            opts.Policies.AutoApplyTransactions();
            opts.Durability.EnableInboxPartitioning = true;
            opts.Durability.InboxStaleTime = 10.Minutes();
            opts.Durability.OutboxStaleTime = 10.Minutes();
            opts.EnableAutomaticFailureAcks = false;
            opts.UnknownMessageBehavior = UnknownMessageBehavior.DeadLetterQueue;
            opts.Metrics.Mode = WolverineMetricsMode.SystemDiagnosticsMeter;
            opts.Tracking.HandlerExecutionDiagnosticsEnabled = true;
            opts.Tracking.OutboxDiagnosticsEnabled = true;
        });

        return services;
    }

    private static IServiceCollection AddOrgModule(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<OrgAuthorizationService>();
        services.AddScoped<INodeAuthorizer>(sp => sp.GetRequiredService<OrgAuthorizationService>());
        services.AddScoped<ActorContext>(sp =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new InvalidOperationException("ActorContext requires an HTTP request.");
            var actor = http.Items[RequestActorMiddleware.ItemKey] as ActorContext
                ?? throw new InvalidOperationException("ActorContext was not resolved. Ensure RequestActorMiddleware runs after authentication.");
            if (!actor.IsAuthenticated)
                throw new DomainException("Authentication required.", 401);
            return actor;
        });
        services.AddScoped<BrandTenantContext>(sp =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new InvalidOperationException("BrandTenantContext requires an HTTP request.");
            return http.Items[BrandTenantContext.ItemKey] as BrandTenantContext
                ?? throw new DomainException("Brand tenant not resolved.");
        });
        services.AddSingleton<InvitationTokenService>();

        // ─── Config service ───
        services.AddDataProtection();
        services.AddSingleton<IConfigEncryptionService, ConfigEncryptionService>();
        services.AddSingleton<ConfigValidationService>();
        services.AddScoped<ConfigResolutionService>();
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            // Prefer the blob service connection string (SDK-format Endpoint URI
            // emitted by Aspire's AddBlobs). Aspire's AddBlobContainer emits a
            // "Endpoint=...;ContainerName=..." form BlobContainerClient cannot
            // parse, so we pair the blob service URI with Assets:ContainerName.
            // "assets" remains the fallback for tests where Testcontainers.Azurite
            // hands back a full DefaultEndpointsProtocol-style connection string.
            var connectionString = configuration.GetConnectionString("blobs")
                ?? configuration.GetConnectionString("assets")
                ?? configuration.GetConnectionString("storage")
                ?? throw new InvalidOperationException("Asset blob storage connection string is not configured.");
            var containerName = configuration.GetValue("Assets:ContainerName", "assets");
            return new BlobContainerClient(connectionString, containerName);
        });
        services.AddSingleton<IAssetStorage, BlobAssetStorage>();
        services.AddScoped<AssetResolutionService>();
        services.Configure<KeycloakProvisioningOptions>(configuration.GetSection(KeycloakProvisioningOptions.SectionName));

        if (environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<TestIdentityProviderStore>();
            services.AddSingleton<IIdentityProviderProvisioning, InMemoryIdentityProviderProvisioning>();
            services.AddSingleton<TestInvitationEmailStore>();
            services.AddSingleton<IInvitationEmailSender, InMemoryInvitationEmailSender>();
        }
        else
        {
            services.AddHttpClient<IIdentityProviderProvisioning, KeycloakIdentityProviderProvisioning>();
            services.AddSingleton<IInvitationEmailSender, SmtpInvitationEmailSender>();
        }

        return services;
    }

    private static IServiceCollection AddCritCritAuth(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing"))
        {
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        }
        else
        {
            var keycloakBaseUrl = KeycloakBaseUrl(configuration);
            var keycloakRealm = KeycloakRealm(configuration);

            services.AddAuthentication()
                .AddKeycloakJwtBearer(
                    serviceName: "keycloak",
                    realm: keycloakRealm,
                    options =>
                    {
                        options.Audience = "store.api";
                        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
                        options.TokenValidationParameters.NameClaimType = "preferred_username";

                        if (environment.IsDevelopment())
                        {
                            options.RequireHttpsMetadata = false;
                            options.TokenValidationParameters.ValidIssuers =
                            [
                                $"{keycloakBaseUrl}/realms/{keycloakRealm}"
                            ];
                        }

                        options.Events = new JwtBearerEvents
                        {
                            OnTokenValidated = ctx =>
                            {
                                if (ctx.Principal?.Identity is not ClaimsIdentity identity)
                                    return Task.CompletedTask;

                                if (!identity.HasClaim(c => c.Type == "idp"))
                                    identity.AddClaim(new Claim("idp", "keycloak"));
                                if (!identity.HasClaim(c => c.Type == "idp_tenant"))
                                    identity.AddClaim(new Claim("idp_tenant", keycloakRealm));

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

        services.AddAuthorization();
        return services;
    }

    private static IServiceCollection AddCritCritCors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedCorsOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["http://localhost:5173", "http://127.0.0.1:5173"];

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(allowedCorsOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }

    private static void MapCritCritOpenApi(this WebApplication app)
    {
        var swaggerClientId = app.Configuration.GetValue("Keycloak:SwaggerClientId", "store.api.swagger");

        app.MapSwagger("/openapi/{documentName}.json");
        app.MapScalarApiReference(opts =>
        {
            opts.AddPreferredSecuritySchemes("keycloak")
                .AddAuthorizationCodeFlow("keycloak", flow =>
                {
                    flow.ClientId = swaggerClientId;
                    flow.Pkce = Pkce.Sha256;
                    flow.SelectedScopes = ["openid", "profile"];
                });
        });
    }

    private static void MapCritCritWolverineEndpoints(this WebApplication app)
    {
        app.MapWolverineEndpoints(c =>
        {
            c.ServiceProviderSource = ServiceProviderSource.FromHttpContextRequestServices;

            c.SourceServiceFromHttpContext<ActorContext>();
            c.SourceServiceFromHttpContext<BrandTenantContext>();
        });
    }

    private static string KeycloakBaseUrl(IConfiguration configuration) =>
        configuration.GetValue("Invitation:IdentityProvider:Keycloak:BaseUrl", "http://localhost:8080").TrimEnd('/');

    private static string KeycloakRealm(IConfiguration configuration) =>
        configuration.GetValue("Invitation:IdentityProvider:Keycloak:Realm", "api");
}
