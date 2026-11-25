using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>Ek ücret kuralı ekranı (ADR-0018 devamı, dilim 2d): etiketler sunucudaki kural sözlüğüyle aynı anahtarları taşır, özet ve anahtar listesi dönüşümleri kararlıdır.</summary>
public class SurchargeLabelsTests
{
    [Fact]
    public void Labels_cover_every_server_key()
    {
        Assert.Equal(["Fixed", "Percent", "Minimum", "Duration"], SurchargeLabels.Kinds.Select(k => k.Key));
        Assert.Equal(["SeparateLine", "IncludedInGroup", "BreakdownOnly"], SurchargeLabels.Presentations.Select(k => k.Key));
        Assert.Equal(6, SurchargeLabels.Operators.Count);
        Assert.Equal("Sabit tutar", SurchargeLabels.Kind("Fixed"));
        Assert.Equal("bilinmeyen", SurchargeLabels.Kind("bilinmeyen"));
    }

    [Fact]
    public void Summary_describes_each_kind_without_inventing_missing_inputs()
    {
        Assert.Equal("150 TRY sabit", SurchargeLabels.Summary("Fixed", "TRY", 150m, null, null, null, null, 0m));
        Assert.Equal("%10 × quote.subtotal", SurchargeLabels.Summary("Percent", "TRY", null, 10m, null, "quote.subtotal", null, 0m));
        Assert.Equal("Ardiye grubu en az 500 TRY", SurchargeLabels.Summary("Minimum", "TRY", 500m, null, "Ardiye", null, null, 0m));
        Assert.Contains("ilk 3 birim ücretsiz", SurchargeLabels.Summary("Duration", "EUR", 20m, null, null, null, "quantity.gun", 3m));
        Assert.Contains("? ", SurchargeLabels.Summary("Percent", "TRY", null, 5m, null, null, null, 0m) + " ");
    }

    [Fact]
    public void Keys_round_trip_and_drop_blanks_and_duplicates()
    {
        var keys = SurchargeLabels.ParseKeys(" quantity.a, quantity.b ;quantity.a\n,, ");
        Assert.Equal(["quantity.a", "quantity.b"], keys.Select(k => k!.GetValue<string>()));
        Assert.Equal("quantity.a, quantity.b", SurchargeLabels.JoinKeys(keys));
        Assert.Empty(SurchargeLabels.ParseKeys(null));
        Assert.Equal(string.Empty, SurchargeLabels.JoinKeys(null));
    }
}
