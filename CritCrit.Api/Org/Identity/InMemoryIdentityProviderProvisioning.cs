using System.Collections.Concurrent;

namespace CritCrit.Api.Org.Identity;

public sealed class TestIdentityProviderStore
{
    public ConcurrentDictionary<string, FakeIdentityProviderUser> Users { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record FakeIdentityProviderUser(
    string ExternalId,
    string Email,
    bool Enabled);

public sealed class InMemoryIdentityProviderProvisioning(
    TestIdentityProviderStore store) : IIdentityProviderProvisioning
{
    public ConcurrentQueue<PasswordSetupRequest> PasswordSetupCalls { get; } = new();

    public Task<IdentityProviderUser> EnsureUserAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var wasCreated = false;
        var user = store.Users.AddOrUpdate(normalized,
            _ =>
            {
                wasCreated = true;
                return new FakeIdentityProviderUser(Guid.CreateVersion7().ToString(), email.Trim(), true);
            },
            (_, existing) =>
            {
                if (existing.Enabled)
                    return existing;

                return existing with { Enabled = true };
            });

        return Task.FromResult(new IdentityProviderUser("test", "default", user.ExternalId, user.Email, wasCreated));
    }

    public Task SendPasswordSetupAsync(PasswordSetupRequest request, CancellationToken ct)
    {
        PasswordSetupCalls.Enqueue(request);
        return Task.CompletedTask;
    }
}
