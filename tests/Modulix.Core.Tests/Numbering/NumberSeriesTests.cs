using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Numbering;

/// <summary>ADR-0049: belge serisi tanımı (biçim, dönem, doğrulama) ve modül kataloğundaki beyan kuralları.</summary>
public class NumberSeriesTests
{
    [Fact]
    public void A_yearly_series_carries_the_year_and_pads_the_sequence()
    {
        var series = new NumberSeriesDefinition("logistics.trip", "Sefer", "SF");

        Assert.Equal("SF-2026-00001", series.Format(1, 2026));
        Assert.Equal("SF-2026-123456", series.Format(123_456, 2026)); // basamağı aşan sıra kısaltılmaz
        Assert.Equal(2026, series.PeriodOf(2026));
        Assert.Equal(42, series.ValueOf("SF-2026-00042", 2026));
        Assert.Null(series.ValueOf("SF-2025-00042", 2026));
        Assert.Null(series.ValueOf("SF-2026-00A42", 2026));
        Assert.Null(series.ValueOf(null, 2026));
    }

    [Fact]
    public void A_series_that_never_resets_has_no_year_and_a_single_period()
    {
        var series = new NumberSeriesDefinition("sales.customer", "Müşteri", "MS", Digits: 4, Reset: NumberSeriesReset.Never);

        Assert.Equal("MS-0007", series.Format(7, 2026));
        Assert.Equal(0, series.PeriodOf(2026));
        Assert.Equal(7, series.ValueOf("MS-0007", 2031));
    }

    [Theory]
    [InlineData("", "Sefer", "SF", 5)]
    [InlineData("logistics.trip", " ", "SF", 5)]
    [InlineData("logistics.trip", "Sefer", "", 5)]
    [InlineData("logistics.trip", "Sefer", "sf", 5)]
    [InlineData("logistics.trip", "Sefer", "S-F", 5)]
    [InlineData("logistics.trip", "Sefer", "ABCDEFGHIJK", 5)]
    [InlineData("logistics.trip", "Sefer", "SF", 2)]
    [InlineData("logistics.trip", "Sefer", "SF", 11)]
    public void Invalid_definitions_are_recognised(string key, string name, string prefix, int digits) =>
        Assert.False(new NumberSeriesDefinition(key, name, prefix, digits).IsValid);

    [Fact]
    public void The_catalog_finds_declared_series_and_rejects_foreign_duplicate_or_invalid_ones()
    {
        var catalog = new ModuleCatalog([new CoreModule(), new SeriesModule("sales", new NumberSeriesDefinition("sales.order", "Sipariş", "SP"))]);

        Assert.Equal("SP", catalog.FindNumberSeries("sales.order")!.Prefix);
        Assert.Null(catalog.FindNumberSeries("sales.invoice"));

        // Başka modülün anahtarı, aynı serinin iki kez bildirilmesi ve geçersiz önek açılışta reddedilir.
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
            [new CoreModule(), new SeriesModule("sales", new NumberSeriesDefinition("billing.invoice", "Fatura", "FT"))]));
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
            [new CoreModule(), new SeriesModule("sales", new NumberSeriesDefinition("sales.order", "Sipariş", "SP"), new NumberSeriesDefinition("sales.order", "Sipariş", "SP"))]));
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
            [new CoreModule(), new SeriesModule("sales", new NumberSeriesDefinition("sales.order", "Sipariş", "sp"))]));
    }

    private sealed class SeriesModule(string key, params NumberSeriesDefinition[] series) : IModule
    {
        public string Key => key;

        public string DisplayName => key;

        public IReadOnlyList<PermissionDefinition> Permissions => [];

        public IReadOnlyList<NumberSeriesDefinition> NumberSeries => series;
    }
}
