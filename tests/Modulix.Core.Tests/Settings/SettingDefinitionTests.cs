using Modulix.Core.Contracts.Modules;
using Modulix.Core.Localization;
using Modulix.Core.Modules;
using Modulix.Core.Settings;

namespace Modulix.Core.Tests.Settings;

/// <summary>Y6: tipli ayar tanımları ve değer kuralları.</summary>
public class SettingDefinitionTests
{
    [Fact]
    public void Core_settings_are_in_the_catalog_with_valid_defaults()
    {
        var catalog = ModuleCatalog.Create();

        var timeZone = catalog.FindSetting(CoreModule.SettingKeys.TimeZone);
        Assert.NotNull(timeZone);
        Assert.Equal(CoreModule.ModuleKey, timeZone.Value.ModuleKey);
        Assert.Equal("Europe/Istanbul", timeZone.Value.Definition.DefaultValue);
        Assert.Null(catalog.FindSetting("core.yok"));
        Assert.All(RegionalOptions.TimeZones, zone => Assert.True(TimeZoneInfo.TryFindSystemTimeZoneById(zone.Value, out _), zone.Value));

        // Denetim kaydında ayarın adı gösterilir.
        var audited = catalog.FindAuditedEntity(CoreModule.AuditedEntityKeys.TenantSetting)!;
        Assert.Equal("Para birimi", audited.Options["key"][CoreModule.SettingKeys.Currency]);
    }

    [Theory]
    [InlineData("crm.currency")]
    [InlineData("sales.Currency")]
    [InlineData("sales")]
    public void Setting_key_must_start_with_the_module_key(string key)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ModuleCatalog.Create(new SettingsModule(new SettingDefinition(key, "Ayar", "Grup", SettingType.Text))));
    }

    [Fact]
    public void Invalid_default_and_choice_without_options_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => ModuleCatalog.Create(new SettingsModule(
            new SettingDefinition("sales.mode", "Mod", "Grup", SettingType.Choice, "a") { Options = [new("b", "B")] })));
        Assert.Throws<InvalidOperationException>(() => ModuleCatalog.Create(new SettingsModule(
            new SettingDefinition("sales.mode", "Mod", "Grup", SettingType.Choice))));
        Assert.Throws<InvalidOperationException>(() => ModuleCatalog.Create(new SettingsModule(
            new SettingDefinition("sales.a", "A", "Grup", SettingType.Text), new SettingDefinition("sales.a", "A", "Grup", SettingType.Text))));
    }

    [Theory]
    [InlineData(CoreModule.SettingKeys.Currency, "USD", true)]
    [InlineData(CoreModule.SettingKeys.Currency, "XYZ", false)]
    [InlineData(CoreModule.SettingKeys.Email, "muhasebe@anka.com.tr", true)]
    [InlineData(CoreModule.SettingKeys.Email, "muhasebe", false)]
    [InlineData(CoreModule.SettingKeys.Phone, "+90 (212) 555-01-01", true)]
    [InlineData(CoreModule.SettingKeys.Phone, "12ab", false)]
    [InlineData(CoreModule.SettingKeys.TaxNumber, "1234567890", true)]
    [InlineData(CoreModule.SettingKeys.TaxNumber, "12345", false)]
    [InlineData(CoreModule.SettingKeys.DisplayName, "", true)]
    [InlineData(CoreModule.SettingKeys.DisplayName, " Anka", false)]
    [InlineData(CoreModule.SettingKeys.DisplayName, "Anka\nGıda", false)]
    [InlineData(CoreModule.SettingKeys.Address, "Merkez Mah.\nİstanbul", true)]
    public void Values_follow_the_definition(string key, string value, bool valid)
    {
        var definition = ModuleCatalog.Create().FindSetting(key)!.Value.Definition;

        Assert.Equal(valid, SettingRules.Validate(definition, value) is null);
    }

    [Fact]
    public void Pattern_message_is_a_translatable_message_with_the_turkish_source_text()
    {
        // Ayarın kendi biçim iletisi (PatternMessage) de sözlükten çevrilir: anahtar meta.setting.<anahtar>.pattern-message.
        var definition = ModuleCatalog.Create().FindSetting(CoreModule.SettingKeys.TaxNumber)!.Value.Definition;

        var error = SettingRules.Validate(definition, "12345")!;

        Assert.Equal("meta.setting.core.profile.tax-number.pattern-message", error.Key);
        Assert.Equal("Vergi numarası 10, T.C. kimlik numarası 11 rakamdır.", error.Text);
        var catalog = new TextCatalog();
        Assert.Equal("A tax number has 10 digits, a Turkish ID number has 11.", catalog.Translate("en", error));
        Assert.Equal("Vergi nömrəsi 10, T.C. şəxsiyyət nömrəsi 11 rəqəmdir.", catalog.Translate("az", error));
        Assert.Equal(error.Text, catalog.Translate("tr", error));

        // PatternMessage'ı olmayan ayarda ortak ileti kullanılır.
        var plain = ModuleCatalog.Create().FindSetting(CoreModule.SettingKeys.Email)!.Value.Definition;
        Assert.Equal("server.validation.email", SettingRules.Validate(plain, "bozuk")!.Key);
    }

    [Fact]
    public void Per_company_flag_defaults_to_false_and_is_preserved_in_the_catalog()
    {
        // Çerçeve (ADR-0008, adım 5): bir ayar şirket başına yapılabilir işaretlenebilir; varsayılan tenant genelidir.
        Assert.False(new SettingDefinition("sales.mode", "Mod", "Grup", SettingType.Text).PerCompany);

        var catalog = ModuleCatalog.Create(new SettingsModule(
            new SettingDefinition("sales.series", "Seri", "Grup", SettingType.Text) { PerCompany = true }));

        Assert.True(catalog.FindSetting("sales.series")!.Value.Definition.PerCompany);
    }

    private sealed class SettingsModule(params SettingDefinition[] settings) : IModule
    {
        public string Key => "sales";

        public string DisplayName => "Satış";

        public IReadOnlyList<PermissionDefinition> Permissions => [];

        public IReadOnlyList<SettingDefinition> Settings => settings;
    }
}

public class RegionalTimeTests
{
    [Fact]
    public void Day_starts_at_midnight_in_the_tenant_time_zone()
    {
        var istanbul = RegionalTime.Find("Europe/Istanbul");
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.FromHours(3)), RegionalTime.StartOfDay(new DateOnly(2026, 10, 24), istanbul));

        var newYork = RegionalTime.Find("America/New_York");
        Assert.Equal(TimeSpan.FromHours(-4), RegionalTime.StartOfDay(new DateOnly(2026, 7, 1), newYork).Offset);
        Assert.Equal(TimeSpan.FromHours(-5), RegionalTime.StartOfDay(new DateOnly(2026, 12, 1), newYork).Offset);

        // Bilinmeyen saat dilimi İstanbul sayılır.
        Assert.Equal(istanbul.Id, RegionalTime.Find("Mars/Olympus").Id);
    }

    [Fact]
    public void Logo_type_is_detected_from_the_content()
    {
        Assert.Equal(TenantLogo.Png, TenantLogo.DetectContentType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal(TenantLogo.Jpeg, TenantLogo.DetectContentType([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Null(TenantLogo.DetectContentType("<svg"u8));
        Assert.Null(TenantLogo.DetectContentType([]));
        Assert.Null(TenantLogo.DetectContentType(new byte[TenantLogo.MaxBytes + 1]));
    }
}
