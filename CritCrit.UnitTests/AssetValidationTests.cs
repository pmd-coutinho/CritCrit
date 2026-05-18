using System.Text;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Features.Assets;

public sealed class AssetValidationTests
{
    [Theory]
    [InlineData("image/png", "hero.png", AssetKind.Image)]
    [InlineData("video/mp4", "loop.mp4", AssetKind.Video)]
    [InlineData("application/pdf", "menu.pdf", AssetKind.Pdf)]
    [InlineData("text/plain", "readme.md", AssetKind.Markdown)]
    public void classifies_supported_asset_types(string contentType, string fileName, AssetKind expected)
    {
        var (kind, _) = AssetValidation.Classify(contentType, fileName);
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void rejects_svg()
    {
        Assert.Throws<DomainException>(() => AssetValidation.Classify("image/svg+xml", "logo.svg"));
    }

    [Fact]
    public async Task rejects_content_over_limit()
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("abcdef"));
        await Assert.ThrowsAsync<DomainException>(() => AssetValidation.BufferAndHashAsync(content, 5, CancellationToken.None));
    }
}
