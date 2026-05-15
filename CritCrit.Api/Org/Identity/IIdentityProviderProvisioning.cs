namespace CritCrit.Api.Org.Identity;

public sealed record IdentityProviderUser(
    string Provider,
    string ProviderTenant,
    string ExternalId,
    string Email,
    bool WasCreated);

public sealed record PasswordSetupRequest(
    string ExternalId,
    int LifespanSeconds);

public interface IIdentityProviderProvisioning
{
    Task<IdentityProviderUser> EnsureUserAsync(string email, CancellationToken ct);

    /// <summary>
    /// Triggers a provider-side password-setup email (Keycloak <c>execute-actions-email</c>
    /// with <c>UPDATE_PASSWORD</c>). No redirect — Keycloak shows its standard
    /// "account updated" page after the user sets their password. The invitation
    /// accept link is sent separately by the CritCrit invitation email.
    /// </summary>
    Task SendPasswordSetupAsync(PasswordSetupRequest request, CancellationToken ct);
}
