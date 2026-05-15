using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Features.Invitations;
using CritCrit.Api.Observability.Audit;
using CritCrit.Api.Observability.Telemetry;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace CritCrit.UnitTests;

public sealed class ObservabilityPolicyTests
{
    [Theory]
    [InlineData("person@example.com", "p***@example.com")]
    [InlineData("x@y.io", "x***@y.io")]
    [InlineData("", null)]
    public void masks_email_for_audit_details(string value, string? expected)
    {
        Assert.Equal(expected, AuditIdentity.MaskEmail(value));
    }

    [Fact]
    public void actor_external_id_does_not_fall_back_to_email_like_values()
    {
        var subjectId = SubjectId.New();
        var actor = new ActorContext(true, false, subjectId, "person@example.com", "person@example.com");

        var auditActor = AuditIdentity.FromActor(actor);

        Assert.Equal(AuditActorKinds.User, auditActor.Kind);
        Assert.Equal(OrgPublicId.FormatSubject(subjectId), auditActor.ExternalId);
        Assert.DoesNotContain("@", auditActor.ExternalId);
    }

    [Theory]
    [InlineData(typeof(SendInvitationEmail))]
    [InlineData(typeof(RetrySendInvitationEmail))]
    public void invitation_email_messages_do_not_carry_raw_tokens(Type messageType)
    {
        var properties = messageType.GetProperties().Select(x => x.Name).ToArray();

        Assert.DoesNotContain("RawToken", properties);
        Assert.DoesNotContain(properties, x => x.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task wolverine_http_trace_middleware_wraps_api_requests_in_a_span()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CritCritTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        Activity? captured = null;
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/brands";

        var middleware = new WolverineHttpTraceMiddleware(_ =>
        {
            captured = Activity.Current;
            _.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.NotNull(captured);
        Assert.Equal("wolverine.http GET /api/brands", captured.DisplayName);
        Assert.Equal(200, captured.GetTagItem("http.response.status_code"));
        Assert.True((bool?)captured.GetTagItem("wolverine.http.endpoint"));
    }
}
