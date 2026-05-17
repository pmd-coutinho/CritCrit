using Microsoft.AspNetCore.DataProtection;

namespace CritCrit.Api.Org.Features.Config;

public interface IConfigEncryptionService
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

/// <summary>
/// Wraps ASP.NET Data Protection with a stable purpose string so existing
/// ciphertext stays decryptable across deployments — provided Data Protection
/// keys are persisted (not ephemeral). Operational requirement called out in
/// the Config plan §8.
/// </summary>
public sealed class ConfigEncryptionService : IConfigEncryptionService
{
    public const string Purpose = "CritCrit.Org.Config.EncryptedValue.v1";

    private readonly IDataProtector _protector;

    public ConfigEncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
