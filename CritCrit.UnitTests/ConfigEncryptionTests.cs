using CritCrit.Api.Org.Features.Config;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace CritCrit.UnitTests;

public class ConfigEncryptionTests
{
    private static IDataProtectionProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    [Fact]
    public void protect_unprotect_roundtrip()
    {
        var svc = new ConfigEncryptionService(Provider());
        var plaintext = "super-secret-value-123";

        var ciphertext = svc.Protect(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, svc.Unprotect(ciphertext));
    }

    [Fact]
    public void wrong_purpose_cannot_unprotect()
    {
        var provider = Provider();
        var svc = new ConfigEncryptionService(provider);
        var other = provider.CreateProtector("Different.Purpose.v1");

        var ciphertext = svc.Protect("hello");

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => other.Unprotect(ciphertext));
    }

    [Fact]
    public void same_plaintext_protect_returns_different_ciphertext_each_time()
    {
        var svc = new ConfigEncryptionService(Provider());
        var a = svc.Protect("same");
        var b = svc.Protect("same");
        Assert.NotEqual(a, b);
        Assert.Equal("same", svc.Unprotect(a));
        Assert.Equal("same", svc.Unprotect(b));
    }
}
