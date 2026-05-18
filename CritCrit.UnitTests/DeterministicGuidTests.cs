using CritCrit.Api.Platform;

public sealed class DeterministicGuidTests
{
    [Fact]
    public void identical_inputs_yield_identical_guid()
    {
        var tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subject = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var a = DeterministicGuid.From(tenant, tenant, subject);
        var b = DeterministicGuid.From(tenant, tenant, subject);

        Assert.Equal(a, b);
    }

    [Fact]
    public void distinct_inputs_yield_distinct_guids()
    {
        var tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subjectA = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var subjectB = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var a = DeterministicGuid.From(tenant, tenant, subjectA);
        var b = DeterministicGuid.From(tenant, tenant, subjectB);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void order_of_parts_matters()
    {
        var x = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var y = Guid.Parse("22222222-2222-2222-2222-222222222222");

        Assert.NotEqual(DeterministicGuid.From(x, y), DeterministicGuid.From(y, x));
    }

    [Fact]
    public void produces_uuid_v5_with_rfc_4122_variant()
    {
        var g = DeterministicGuid.From("anything");
        var bytes = g.ToByteArray();
        // .NET Guid bytes are mixed-endian; byte index 7 in RFC 4122 ordering
        // is the time_hi_and_version byte. In the .NET layout that is index 7
        // (the last byte of the reversed first three-field group).
        Array.Reverse(bytes, 6, 2);
        var version = bytes[6] >> 4;
        Array.Reverse(bytes, 6, 2);

        // Variant byte (index 8 in both RFC and .NET ordering since the swap
        // covers indices 0–7 only).
        var variantTopTwo = bytes[8] >> 6;

        Assert.Equal(5, version);
        Assert.Equal(0b10, variantTopTwo);
    }

    [Fact]
    public void canonical_test_vector_matches_uuid_v5_dns_namespace()
    {
        // Standard UUID v5 test vector from RFC 4122 Appendix B:
        // namespace = DNS namespace (6ba7b810-9dad-11d1-80b4-00c04fd430c8)
        // name      = "www.example.com"
        // expected  = 2ed6657d-e927-568b-95e1-2665a8aea6a2
        var dnsNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        var actual = DeterministicGuid.Create(dnsNamespace, "www.example.com");
        Assert.Equal(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"), actual);
    }
}
