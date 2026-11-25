using Modulix.Modules.Crm.Domain;

namespace Modulix.Modules.Crm.Quotes;

/// <summary>Müşteri belgesinin satırları, grup alt toplamları ve "ayrıca gösterilmeyen ek ücret" bayrağı.</summary>
public sealed record ComposedQuoteLines(
    IReadOnlyList<QuoteSnapshotLine> Lines, IReadOnlyList<QuoteSnapshotGroupSubtotal> GroupSubtotals, bool IncludesUnlistedCharges);

/// <summary>
/// Teklif satırlarını müşteri belgesi verisine çevirir (ADR-0018 devamı, Dilim 2c belge gösterimi). Saf ve veritabanından bağımsızdır; tek yer burasıdır:
/// PDF ve herkese açık sayfa yalnızca bu sonucu çizer, bu yüzden gizlenen şey belge verisine hiç girmez (API'den de okunamaz).
/// <list type="bullet">
/// <item><c>SeparateLine</c> ek ücret: kendi satırı olarak, grubunun elle satırlarından hemen sonra (grup yoksa listenin sonunda) görünür.</item>
/// <item><c>IncludedInGroup</c>: satır olarak görünmez; grubun alt toplamına eklenir. Grubu belgede yoksa gizlenmez, ayrı satır olarak gösterilir.</item>
/// <item><c>BreakdownOnly</c>: satır olarak görünmez; tutar toplamlarda kalır ve belgede "toplamlar ayrıca gösterilmeyen ek ücretleri içerir" notu çıkar.</item>
/// </list>
/// Ek ücret satırının açıklaması (kural hesabının iç ayrıntısı) müşteriye verilmez; yalnızca kural adı görünür. Vergi grupları satır sayısından bağımsız
/// olarak teklifin tüm dahil satırlarından hesaplanır, bu yüzden gizlenen ek ücretin vergisi de toplamdadır.
/// </summary>
public static class QuoteDocumentComposer
{
    internal static ComposedQuoteLines Compose(IReadOnlyList<QuoteLine> lines)
    {
        var manual = lines.Where(l => l.SourceRuleId is null).ToList();
        var surcharges = lines.Where(l => l.SourceRuleId is not null && l.Included).OrderBy(l => l.Position).ToList();
        var manualGroups = manual.Where(l => !string.IsNullOrWhiteSpace(l.GroupName)).Select(l => l.GroupName!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var separate = new List<QuoteLine>();
        var folded = new List<QuoteLine>();
        var unlisted = false;
        foreach (var charge in surcharges)
        {
            switch (charge.SurchargePresentation)
            {
                case Pricing.SurchargeRules.BreakdownOnly:
                    unlisted = true;
                    break;
                case Pricing.SurchargeRules.IncludedInGroup when charge.GroupName is { Length: > 0 } group && manualGroups.Contains(group.Trim()):
                    folded.Add(charge);
                    break;
                default:
                    separate.Add(charge);
                    break;
            }
        }

        // Sıra: elle satırlar olduğu gibi; ayrı ek ücret satırı kendi grubunun son elle satırından hemen sonra, grubu olmayan en sona.
        var lastOfGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < manual.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(manual[i].GroupName))
            {
                lastOfGroup[manual[i].GroupName!.Trim()] = i;
            }
        }

        var ordered = new List<QuoteLine>();
        var placed = new HashSet<QuoteLine>();
        for (var i = 0; i < manual.Count; i++)
        {
            ordered.Add(manual[i]);
            if (!string.IsNullOrWhiteSpace(manual[i].GroupName) && lastOfGroup[manual[i].GroupName!.Trim()] == i)
            {
                foreach (var charge in separate.Where(c => c.GroupName is { Length: > 0 } g && string.Equals(g.Trim(), manual[i].GroupName!.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    ordered.Add(charge);
                    placed.Add(charge);
                }
            }
        }

        ordered.AddRange(separate.Where(c => !placed.Contains(c)));

        // Alt toplam: grubun görünen dahil satırları + gruba dahil edilen ek ücretler (yalnızca ek ücret katılan grup için).
        var subtotals = folded
            .Select(c => c.GroupName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new QuoteSnapshotGroupSubtotal(
                ordered.First(l => string.Equals(l.GroupName?.Trim(), name, StringComparison.OrdinalIgnoreCase)).GroupName!.Trim(),
                ordered.Where(l => l.Included && string.Equals(l.GroupName?.Trim(), name, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Net)
                + folded.Where(c => string.Equals(c.GroupName!.Trim(), name, StringComparison.OrdinalIgnoreCase)).Sum(c => c.Net)))
            .ToList();

        return new ComposedQuoteLines([.. ordered.Select(ToSnapshotLine)], subtotals, unlisted);
    }

    private static QuoteSnapshotLine ToSnapshotLine(QuoteLine l) => new(
        l.GroupName, l.ProductCode, l.Name,
        l.SourceRuleId is null ? l.Description : null, // ek ücretin hesap ayrıntısı iç bilgidir
        l.Quantity, l.UnitName, l.UnitPrice, l.DiscountKind, l.DiscountValue, l.TaxRatePercent, l.Role, l.IsSelected, l.Included, l.RecurringPeriod,
        l.ListAmount, l.LineDiscount, l.Net, l.Tax, l.Gross);
}
