using Modulix.Core.Contracts.Localization;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Endpoints;
using Modulix.Modules.Crm.Pricing;
using Modulix.Modules.Crm.Quotes;

namespace Modulix.Modules.Crm.Tests;

/// <summary>
/// Müşteri belgesinde ek ücret gösterimi (ADR-0018 devamı, Dilim 2c): ayrı satır, gruba dahil (alt toplam) ve yalnız döküm (ayrıca gösterilmeyen ek ücret).
/// Gizlenen şey belge verisine hiç girmez; açıklama (hesabın iç ayrıntısı) hiçbir ek ücret satırında müşteriye verilmez (veritabanı gerektirmez).
/// </summary>
public class QuoteDocumentComposerTests
{
    private static int position;

    private static QuoteLine Manual(string name, string? group, decimal net, bool included = true, string? description = null) =>
        new() { Name = name, GroupName = group, Net = net, ListAmount = net, Included = included, Description = description, Position = ++position, Quantity = 1m, UnitPrice = net };

    private static QuoteLine Charge(string name, string? group, decimal net, string presentation) =>
        new()
        {
            Name = name, GroupName = group, Net = net, ListAmount = net, Included = true, SourceRuleId = Guid.NewGuid(), SurchargePresentation = presentation,
            Description = "%10 × 150 × 3 birim = 45 EUR (iç ayrıntı)", Position = ++position, Quantity = 1m, UnitPrice = net,
        };

    [Fact]
    public void Without_surcharges_the_lines_are_unchanged_and_nothing_is_flagged()
    {
        var lines = new[] { Manual("A", "Ardiye", 100m, description: "Not"), Manual("B", null, 50m) };

        var result = QuoteDocumentComposer.Compose(lines);

        Assert.Equal(["A", "B"], result.Lines.Select(l => l.Name));
        Assert.Equal("Not", result.Lines[0].Description);
        Assert.Empty(result.GroupSubtotals);
        Assert.False(result.IncludesUnlistedCharges);
    }

    [Fact]
    public void A_separate_charge_follows_the_last_manual_line_of_its_group_and_never_shows_the_calculation_detail()
    {
        var lines = new[]
        {
            Manual("Ardiye 1", "Ardiye Bedeli", 100m), Manual("Gümrük", "Gümrük", 100m), Manual("Ardiye 2", "Ardiye Bedeli", 50m),
            Charge("ADR ek ücreti", "Ardiye Bedeli", 100m, SurchargeRules.SeparateLine),
            Charge("Grupsuz ücret", null, 10m, SurchargeRules.SeparateLine),
        };

        var result = QuoteDocumentComposer.Compose(lines);

        Assert.Equal(["Ardiye 1", "Gümrük", "Ardiye 2", "ADR ek ücreti", "Grupsuz ücret"], result.Lines.Select(l => l.Name));
        Assert.Null(result.Lines.Single(l => l.Name == "ADR ek ücreti").Description);
        Assert.Empty(result.GroupSubtotals);
    }

    [Fact]
    public void An_included_in_group_charge_is_folded_into_the_group_subtotal_and_is_not_a_line()
    {
        var lines = new[]
        {
            Manual("Parsiyel ardiye", "Ardiye Bedeli", 150m), Manual("Pasif kalem", "Ardiye Bedeli", 999m, included: false), Manual("Beyanname", "Gümrük", 100m),
            Charge("ADR ek ücreti", "Ardiye Bedeli", 100m, SurchargeRules.IncludedInGroup),
            Charge("Demuraj", "ardiye bedeli", 45m, SurchargeRules.IncludedInGroup),
        };

        var result = QuoteDocumentComposer.Compose(lines);

        Assert.Equal(["Parsiyel ardiye", "Pasif kalem", "Beyanname"], result.Lines.Select(l => l.Name));
        var subtotal = Assert.Single(result.GroupSubtotals);
        Assert.Equal(("Ardiye Bedeli", 295m), (subtotal.GroupName, subtotal.Net)); // 150 + 100 + 45; dahil olmayan kalem sayılmaz
        Assert.False(result.IncludesUnlistedCharges);
    }

    [Fact]
    public void An_included_in_group_charge_without_a_matching_group_is_shown_as_a_line_instead_of_being_hidden()
    {
        var lines = new[] { Manual("Kalem", "Ardiye", 100m), Charge("Başka grup ücreti", "Olmayan Grup", 20m, SurchargeRules.IncludedInGroup), Charge("Grupsuz", null, 5m, SurchargeRules.IncludedInGroup) };

        var result = QuoteDocumentComposer.Compose(lines);

        Assert.Equal(["Kalem", "Başka grup ücreti", "Grupsuz"], result.Lines.Select(l => l.Name));
        Assert.Empty(result.GroupSubtotals);
    }

    [Fact]
    public void A_breakdown_only_charge_is_not_in_the_document_data_but_flags_the_unlisted_charges_note()
    {
        var lines = new[] { Manual("Kalem", "Ardiye", 100m), Charge("İç ek ücret", "Ardiye", 45m, SurchargeRules.BreakdownOnly) };

        var result = QuoteDocumentComposer.Compose(lines);

        Assert.Equal(["Kalem"], result.Lines.Select(l => l.Name));
        Assert.DoesNotContain(result.Lines, l => l.Name == "İç ek ücret");
        Assert.True(result.IncludesUnlistedCharges);
        Assert.Empty(result.GroupSubtotals);
    }

    [Fact]
    public void All_three_modes_can_coexist_in_one_quote()
    {
        var lines = new[]
        {
            Manual("Parsiyel ardiye", "Ardiye Bedeli", 150m),
            Charge("Ayrı ücret", "Ardiye Bedeli", 10m, SurchargeRules.SeparateLine),
            Charge("Dahil ücret", "Ardiye Bedeli", 20m, SurchargeRules.IncludedInGroup),
            Charge("Dökümde ücret", "Ardiye Bedeli", 30m, SurchargeRules.BreakdownOnly),
        };

        var result = QuoteDocumentComposer.Compose(lines);

        Assert.Equal(["Parsiyel ardiye", "Ayrı ücret"], result.Lines.Select(l => l.Name));
        Assert.Equal(180m, Assert.Single(result.GroupSubtotals).Net); // 150 + 10 (görünen) + 20 (dahil); dökümde 30 alt toplamda da yok
        Assert.True(result.IncludesUnlistedCharges);
    }

    private sealed class KeyCatalog : ITextCatalog
    {
        public string Get(string language, string key, params object?[] args) => args.Length == 0 ? key : $"{key} {string.Join(' ', args)}";

        public IReadOnlyDictionary<string, string> Bundle(string language) => new Dictionary<string, string>();
    }

    [Fact]
    public void The_pdf_renders_with_subtotal_rows_and_the_unlisted_note_without_error()
    {
        var snapshot = new QuoteDocumentSnapshot(
            1, "TK-2026-1", 1, "Ardiye teklifi", null, new DateOnly(2026, 11, 25), null, DateTimeOffset.UtcNow, "EUR", false, "Anka Ltd.", null, null, null, null,
            new QuoteSellerInfo("Satıcı", null, null),
            [
                new QuoteSnapshotLine("Ardiye Bedeli", "ARD", "Parsiyel ardiye", null, 6m, null, 25m, "None", 0m, 20m, "Standard", false, true, null, 150m, 0m, 150m, 30m, 180m),
                new QuoteSnapshotLine("Ardiye Bedeli", null, "Ayrı ücret", null, 1m, null, 10m, "None", 0m, 20m, "Standard", false, true, null, 10m, 0m, 10m, 2m, 12m),
            ],
            [new QuoteSnapshotTax(20m, 180m, 36m)], 160m, 0m, 180m, 36m, 216m, 0m, null, null, null,
            [new QuoteSnapshotGroupSubtotal("Ardiye Bedeli", 180m)], IncludesUnlistedCharges: true);

        var pdf = QuotePdf.Render(snapshot, new LocalizedTexts("tr", new KeyCatalog()));

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Equal(2, QuotePdf.TemplateVersion);
    }
}
