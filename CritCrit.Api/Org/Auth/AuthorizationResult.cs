namespace CritCrit.Api.Org.Auth;

public sealed record AuthorizationResult(bool Succeeded, string? Error = null)
{
    public static AuthorizationResult Success() => new(true);
    public static AuthorizationResult Fail(string error) => new(false, error);
}
