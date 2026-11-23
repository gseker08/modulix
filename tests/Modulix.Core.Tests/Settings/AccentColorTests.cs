using Modulix.Core.Modules;
using Modulix.Core.Settings;

namespace Modulix.Core.Tests.Settings;

/// <summary>E8: firma vurgu rengi biçimi ve açık/koyu tema kontrastı.</summary>
public class AccentColorTests
{
    [Theory]
    [InlineData("#087DB8", false, true)]   // Modulix açık teması
    [InlineData("#0B5C87", false, true)]
    [InlineData("#7DD3FC", false, false)]  // beyaz üstünde okunmaz
    [InlineData("#FFFFFF", false, false)]
    [InlineData("#3BAEE6", true, true)]    // Modulix koyu teması
    [InlineData("#1E3A5F", true, false)]   // koyu zeminde okunmaz
    [InlineData("#000000", true, false)]
    [InlineData("", false, true)]          // boş = firma rengi yok
    [InlineData("", true, true)]
    public void Contrast_is_checked_against_the_theme_surfaces(string color, bool dark, bool valid)
    {
        Assert.Equal(valid, AccentColor.Validate(color, dark) is null);
    }

    [Theory]
    [InlineData("087DB8")]
    [InlineData("#087DB")]
    [InlineData("#087DB8FF")]
    [InlineData("red")]
    [InlineData("#GGGGGG")]
    [InlineData("#087DB8; } body { display:none")]
    public void Only_hash_rrggbb_is_accepted(string color)
    {
        Assert.NotNull(AccentColor.Validate(color, dark: false));
        var definition = ModuleCatalog.Create().FindSetting(CoreModule.SettingKeys.AccentLight)!.Value.Definition;
        Assert.NotNull(SettingRules.Validate(definition, color));
    }

    [Fact]
    public void Settings_validate_through_the_catalog_definitions_and_defaults_are_empty()
    {
        var catalog = ModuleCatalog.Create();
        var light = catalog.FindSetting(CoreModule.SettingKeys.AccentLight)!.Value.Definition;
        var dark = catalog.FindSetting(CoreModule.SettingKeys.AccentDark)!.Value.Definition;

        Assert.Equal("", light.DefaultValue);
        Assert.Equal("", dark.DefaultValue);
        Assert.Null(SettingRules.Validate(light, "#087DB8"));
        Assert.NotNull(SettingRules.Validate(light, "#7DD3FC"));
        Assert.Null(SettingRules.Validate(dark, "#3BAEE6"));
        Assert.NotNull(SettingRules.Validate(dark, "#087DB8")); // açık temanın rengi koyu zeminde yetmez
    }

    [Fact]
    public void Shipped_brand_colors_pass_their_own_rule()
    {
        // tokens.css varsayılanları kendi kuralımızı geçmelidir.
        Assert.Null(AccentColor.Validate("#087DB8", dark: false));
        Assert.Null(AccentColor.Validate("#3BAEE6", dark: true));
        Assert.True(AccentColor.Contrast("#087DB8", "#FFFFFF") >= AccentColor.MinContrast);
    }

    [Fact]
    public void Check_reports_contrast_for_valid_formats_and_none_for_empty_or_malformed()
    {
        var ok = AccentColor.Check("#087DB8", dark: false);
        Assert.True(ok.Valid);
        Assert.InRange(ok.Contrast!.Value, 4.5, 4.6);
        Assert.Null(ok.Message);

        var low = AccentColor.Check("#7DD3FC", dark: false);
        Assert.False(low.Valid);
        Assert.True(low.Contrast < AccentColor.MinContrast);
        Assert.Contains("açık", low.Message);

        Assert.Equal(new AccentCheck(true, null, null), AccentColor.Check("", dark: true));
        var bad = AccentColor.Check("red", dark: true);
        Assert.False(bad.Valid);
        Assert.Null(bad.Contrast);

        // Kaydetme doğrulaması önizlemeyle aynı sonucu verir.
        Assert.Equal(low.Message, AccentColor.Validate("#7DD3FC", dark: false)?.Text);
    }
}
