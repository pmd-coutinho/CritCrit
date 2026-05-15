using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CritCrit.Api.Org.Identity;

public sealed class KeycloakProvisioningOptions
{
    public const string SectionName = "Invitation:IdentityProvider:Keycloak";

    public string BaseUrl { get; set; } = "http://keycloak:8080";
    public string Realm { get; set; } = "api";
    public string AdminRealm { get; set; } = "master";
    public string AdminClientId { get; set; } = "admin-cli";
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "";
    public string ProviderName { get; set; } = "keycloak";
}

public sealed class KeycloakIdentityProviderProvisioning(
    HttpClient client,
    IOptions<KeycloakProvisioningOptions> options)
    : IIdentityProviderProvisioning
{
    private readonly KeycloakProvisioningOptions _options = options.Value;

    public async Task<IdentityProviderUser> EnsureUserAsync(string email, CancellationToken ct)
    {
        var token = await GetAdminAccessTokenAsync(ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var existing = await FindByEmailAsync(email, ct);
        if (existing is null)
        {
            var create = await client.PostAsJsonAsync(
                $"{_options.BaseUrl.TrimEnd('/')}/admin/realms/{_options.Realm}/users",
                new KeycloakCreateUserRequest
                {
                    Username = email.Trim(),
                    Email = email.Trim(),
                    Enabled = true,
                    EmailVerified = true,
                    RequiredActions = ["UPDATE_PASSWORD"]
                },
                ct);

            create.EnsureSuccessStatusCode();

            existing = await FindByEmailAsync(email, ct)
                ?? throw new InvalidOperationException("Keycloak user creation succeeded but the created user could not be reloaded.");

            return new IdentityProviderUser(_options.ProviderName, _options.Realm, existing.Id, existing.Email ?? email.Trim(), true);
        }

        if (!existing.Enabled)
        {
            var update = await client.PutAsJsonAsync(
                $"{_options.BaseUrl.TrimEnd('/')}/admin/realms/{_options.Realm}/users/{existing.Id}",
                new KeycloakUpdateUserRequest
                {
                    Id = existing.Id,
                    Username = existing.Username ?? email.Trim(),
                    Email = existing.Email ?? email.Trim(),
                    Enabled = true,
                    EmailVerified = existing.EmailVerified,
                    RequiredActions = existing.RequiredActions ?? ["UPDATE_PASSWORD"]
                },
                ct);

            update.EnsureSuccessStatusCode();
            existing = existing with { Enabled = true };
        }

        return new IdentityProviderUser(_options.ProviderName, _options.Realm, existing.Id, existing.Email ?? email.Trim(), false);
    }

    public async Task SendPasswordSetupAsync(PasswordSetupRequest request, CancellationToken ct)
    {
        var token = await GetAdminAccessTokenAsync(ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // No client_id / redirect_uri — Keycloak just shows its info page after
        // password setup. The CritCrit invitation email carries the accept link.
        var url =
            $"{_options.BaseUrl.TrimEnd('/')}/admin/realms/{_options.Realm}/users/{request.ExternalId}/execute-actions-email" +
            $"?lifespan={request.LifespanSeconds}";

        using var content = JsonContent.Create(new[] { "UPDATE_PASSWORD" });
        var response = await client.PutAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetAdminAccessTokenAsync(CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _options.AdminClientId,
            ["username"] = _options.AdminUsername,
            ["password"] = _options.AdminPassword
        });

        var response = await client.PostAsync(
            $"{_options.BaseUrl.TrimEnd('/')}/realms/{_options.AdminRealm}/protocol/openid-connect/token",
            form,
            ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Keycloak token response was empty.");

        return payload.AccessToken;
    }

    private async Task<KeycloakUserResponse?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var users = await client.GetFromJsonAsync<List<KeycloakUserResponse>>(
            $"{_options.BaseUrl.TrimEnd('/')}/admin/realms/{_options.Realm}/users?email={Uri.EscapeDataString(email.Trim())}&exact=true",
            ct);

        return users?.FirstOrDefault(x => string.Equals(x.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private sealed record KeycloakTokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record KeycloakUserResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("emailVerified")] bool EmailVerified,
        [property: JsonPropertyName("requiredActions")] string[]? RequiredActions);

    private class KeycloakCreateUserRequest
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("emailVerified")]
        public bool EmailVerified { get; set; }

        [JsonPropertyName("requiredActions")]
        public string[] RequiredActions { get; set; } = [];
    }

    private sealed class KeycloakUpdateUserRequest : KeycloakCreateUserRequest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
    }
}
