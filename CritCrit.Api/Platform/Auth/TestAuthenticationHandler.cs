using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CritCrit.Api.Platform.Auth;

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ToString()),
            new("sub", user.ToString()),
            new("idp", Request.Headers.TryGetValue("X-Test-Provider", out var provider) ? provider.ToString() : "test"),
            new("idp_tenant", Request.Headers.TryGetValue("X-Test-Provider-Tenant", out var tenant) ? tenant.ToString() : "default")
        };

        if (Request.Headers.TryGetValue("X-Test-Email", out var email))
            claims.Add(new Claim(ClaimTypes.Email, email.ToString()));

        if (Request.Headers.TryGetValue("X-Test-Roles", out var roles))
        {
            foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.NameIdentifier, ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
