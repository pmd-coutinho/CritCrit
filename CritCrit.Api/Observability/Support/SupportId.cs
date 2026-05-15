using System.Diagnostics;

namespace CritCrit.Api.Observability.Support;

public static class SupportId
{
    public const string HeaderName = "X-CritCrit-Support-Id";
    public const string ProblemDetailsExtensionName = "supportId";

    public static string Current =>
        Activity.Current?.TraceId.ToString() is { Length: > 0 } traceId
            ? traceId
            : "untraced";
}
