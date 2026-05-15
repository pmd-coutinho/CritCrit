using CritCrit.Api2.Infrastructure.Email;
using CritCrit.Api2.Todo;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Resources;
using Marten;
using Marten.Storage;
using Scalar.AspNetCore;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;
using Wolverine.Http.FluentValidation;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.AddNpgsqlDataSource("marten");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.AddServiceDefaults();

builder.Services.AddMarten(m =>
    {
        m.DisableNpgsqlLogging = true;
        m.Connection(builder.Configuration.GetConnectionString("marten")!);
        
        // Make all document types use "conjoined" multi-tenancy -- unless explicitly marked with
        // [SingleTenanted] or explicitly configured via the fluent interfce
        // to be single-tenanted
        m.Policies.AllDocumentsAreMultiTenanted();
        m.Events.TenancyStyle = TenancyStyle.Conjoined;

        m.Projections.Add<Projection>(ProjectionLifecycle.Inline);
        
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
    })
    .UseLightweightSessions()

    .IntegrateWithWolverine(x =>
    {
        x.UseWolverineManagedEventSubscriptionDistribution = true;
    });

builder.Services.AddResourceSetupOnStartup();

builder.Services.AddWolverine(opts =>
{
    opts.UseRabbitMqUsingNamedConnection("rabbitmq")
        .AutoProvision();
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

    opts.UseSystemTextJsonForSerialization();
    opts.UseFluentValidation();
    opts.UseFluentValidationProblemDetail();
});

builder.Services.AddWolverineHttp();

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<TestEmailStore>();
    builder.Services.AddSingleton<IEmailSender,  InMemoryEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapSwagger("/openapi/{documentName}.json");
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapDefaultEndpoints();

app.MapWolverineEndpoints(opts =>
{
    opts.TenantId.IsRequestHeaderValue("X-Tenant-ID");
    // Opting into the Fluent Validation middleware from
    // Wolverine.Http.FluentValidation
    opts.UseFluentValidationProblemDetailMiddleware();

    // Or instead, you could use Data Annotations that are built
    // into the Wolverine.HTTP library
    opts.UseDataAnnotationsValidationProblemDetailMiddleware();
});

await app.RunJasperFxCommands(args);