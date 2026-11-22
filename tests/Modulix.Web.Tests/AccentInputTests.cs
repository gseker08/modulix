using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>E8 renk seçici: girişin #RRGGBB'ye yaklaştırılması ve renk seçici değeri. Kontrast kuralı sunucudadır.</summary>
public class AccentInputTests
{
    [Theory]
    [InlineData("087db8", "#087DB8")]
    [InlineData("#087db8", "#087DB8")]
    [InlineData("  #3baee6 ", "#3BAEE6")]
    [InlineData("#08", "#08")]
    [InlineData("red", "red")]
    [InlineData("12345g", "12345g")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_completes_only_six_hex_digits(string? raw, string expected)
    {
        Assert.Equal(expected, AccentInput.Normalize(raw));
    }

    [Theory]
    [InlineData("#087DB8", true)]
    [InlineData("#087db8", true)]
    [InlineData("087DB8", false)]
    [InlineData("#087DB", false)]
    [InlineData("#087DB8F", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("#087DB8; } body", false)]
    [InlineData(null, false)]
    public void IsHex_accepts_only_hash_rrggbb(string? value, bool expected)
    {
        Assert.Equal(expected, AccentInput.IsHex(value));
    }

    [Fact]
    public void Picker_shows_the_color_or_the_theme_default_when_unset_or_invalid()
    {
        Assert.Equal("#0b5c87", AccentInput.PickerValue("#0B5C87", dark: false));
        Assert.Equal("#087db8", AccentInput.PickerValue("", dark: false));
        Assert.Equal("#3baee6", AccentInput.PickerValue("", dark: true));
        Assert.Equal("#3baee6", AccentInput.PickerValue("nonsense", dark: true));
    }
}
