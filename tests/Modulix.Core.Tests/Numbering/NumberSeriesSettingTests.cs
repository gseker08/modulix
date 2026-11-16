using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Numbering;

namespace Modulix.Core.Tests.Numbering;

/// <summary>ADR-0049 dilim 2a: firmanın seri yapılandırması varsayılanın üzerine uygulanır, varsayılana eşit değer saklanmaz.</summary>
public class NumberSeriesSettingTests
{
    private static readonly NumberSeriesDefinition Declared = new("logistics.trip", "Sefer", "SF");
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_setting_keeps_the_declared_series()
    {
        var setting = NumberSeriesSetting.Create(Declared.Key, Guid.NewGuid(), Now);

        Assert.Equal(Declared, setting.Apply(Declared));
        Assert.False(setting.IsCustomized);
        Assert.Equal(0, setting.Version);
    }

    [Fact]
    public void Configuring_applies_prefix_digits_and_reset_and_bumps_the_version()
    {
        var setting = NumberSeriesSetting.Create(Declared.Key, Guid.NewGuid(), Now);

        setting.Configure(Declared, " sev ", 4, NumberSeriesReset.Never, Guid.NewGuid(), Now);

        var effective = setting.Apply(Declared);
        Assert.Equal(("SEV", 4, NumberSeriesReset.Never), (effective.Prefix, effective.Digits, effective.Reset));
        Assert.Equal("SEV-0042", effective.Format(42, 2026));
        Assert.True(setting.IsCustomized);
        Assert.Equal(1, setting.Version);
    }

    [Fact]
    public void Values_equal_to_the_default_are_not_stored()
    {
        var setting = NumberSeriesSetting.Create(Declared.Key, Guid.NewGuid(), Now);
        setting.Configure(Declared, "SEV", 5, NumberSeriesReset.Yearly, Guid.NewGuid(), Now);

        setting.Configure(Declared, "SF", 5, NumberSeriesReset.Yearly, Guid.NewGuid(), Now);

        Assert.Null(setting.Prefix);
        Assert.False(setting.IsCustomized);
        Assert.Equal(Declared, setting.Apply(Declared));
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("S-F", 5)]
    [InlineData("ÇOKUZUNÖNEK", 5)]
    [InlineData("SF", 2)]
    [InlineData("SF", 11)]
    public void Invalid_values_are_rejected_and_nothing_changes(string prefix, int digits)
    {
        var setting = NumberSeriesSetting.Create(Declared.Key, Guid.NewGuid(), Now);

        Assert.Throws<ArgumentException>(() => setting.Configure(Declared, prefix, digits, NumberSeriesReset.Yearly, Guid.NewGuid(), Now));
        Assert.False(setting.IsCustomized);
        Assert.Equal(0, setting.Version);
    }
}
