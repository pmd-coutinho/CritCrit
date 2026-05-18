using CritCrit.Api.Org.Features.Assets;

public sealed class AssetKeyTests
{
    [Theory]
    [InlineData("kiosk.background-video", true)]
    [InlineData("kiosk.hero.background-video", true)]
    [InlineData("background-video", true)]
    [InlineData("Kiosk.Background", false)]
    [InlineData("kiosk background", false)]
    [InlineData("kiosk/background", false)]
    [InlineData("kiosk.", false)]
    [InlineData(".kiosk", false)]
    [InlineData("kiosk..background", false)]
    public void validates_dot_separated_kebab_keys(string input, bool expected)
    {
        Assert.Equal(expected, AssetKey.IsValid(input));
    }
}
