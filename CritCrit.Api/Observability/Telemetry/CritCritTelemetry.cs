using System.Diagnostics;

namespace CritCrit.Api.Observability.Telemetry;

public static class CritCritTelemetry
{
    public const string ActivitySourceName = "CritCrit.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static Activity? StartActivity(string name) => ActivitySource.StartActivity(name);
}
