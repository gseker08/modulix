using System.Globalization;
using Modulix.Core.Contracts.Customization;
using Modulix.Core.Contracts.Reporting;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// Satış genel görünümü kartının ayrıntı satırlarını rapor merkezinin dışa aktarma tablosuna (<see cref="ReportTable"/>) çevirir (ADR-0035).
/// Satırlar ekrandaki kart ayrıntısıyla <b>aynı</b> hesaptan gelir (<c>ISalesOverview.DetailAsync</c>): kart toplamı = dosyadaki tutar sütunu toplamı.
/// Tutar izni yoksa tutar sütunları hiç yazılmaz (ekrandaki gibi). Burada yeniden hesap, izin ya da kapsam kararı yoktur.
/// </summary>
internal static class SalesCardExport
{
    /// <summary>Kart satırları fırsatlardır; dosyanın erişim kuralı (okuma yetkisi, kapsam, indirmede yeniden denetim) fırsat veri kümesinden gelir.</summary>
    public const string DatasetKey = "crm.opportunities";

    public static ReportTable Build(
        string name, SalesCardDetailDto detail, IReadOnlyDictionary<Guid, string> owners, ChoiceList? stages, string cardLabel, DateTimeOffset now)
    {
        var columns = new List<ReportTableColumn>
        {
            Column("number", "Fırsat no", DatasetColumnKind.Text),
            Column("title", "Başlık", DatasetColumnKind.Text),
            Column("owner", "Sorumlu", DatasetColumnKind.Text),
            Column("status", "Durum", DatasetColumnKind.Text),
            Column("stage", "Aşama", DatasetColumnKind.Text),
            Column("expectedCloseDate", "Beklenen kapanış", DatasetColumnKind.Text),
            Column("actualCloseDate", "Gerçek kapanış", DatasetColumnKind.Text),
            Column("probability", "Olasılık (%)", DatasetColumnKind.Number),
        };
        if (detail.AmountsVisible)
        {
            columns.Add(Column("sourceAmount", "Kaynak tutar", DatasetColumnKind.Number));
            columns.Add(Column("sourceCurrency", "Kaynak para birimi", DatasetColumnKind.Text));
            columns.Add(Column("amount", $"Tutar ({detail.Currency})", DatasetColumnKind.Number));
            columns.Add(Column("rateUsed", "Kullanılan kur", DatasetColumnKind.Number));
            columns.Add(Column("issue", "Sorun", DatasetColumnKind.Text));
        }

        var rows = new List<IReadOnlyList<object?>>(detail.Rows.Count);
        foreach (var row in detail.Rows)
        {
            var cells = new List<object?>
            {
                row.OpportunityNumber,
                row.Title,
                row.OwnerId is { } owner && owners.TryGetValue(owner, out var ownerName) ? ownerName : string.Empty,
                OpportunityStatus.Label(row.Status),
                stages?.Find(row.Stage)?.Label ?? row.Stage,
                Date(row.ExpectedCloseDate),
                Date(row.ActualCloseDate),
                row.Probability,
            };
            if (detail.AmountsVisible)
            {
                cells.Add(row.SourceAmount);
                cells.Add(row.SourceCurrency);
                cells.Add(row.Amount);
                cells.Add(row.RateUsed);
                cells.Add(row.Issue switch { "MissingAmount" => "Tutar yok", "MissingRate" => "Kur yok", { } other => other, _ => null });
            }

            rows.Add(cells);
        }

        return new ReportTable(name, DatasetKey, cardLabel, columns, rows, detail.Truncated, now);
    }

    /// <summary>Dosya adında ve başlıkta görünen kart adı (ekrandaki kart başlıklarıyla aynı).</summary>
    public static string Label(Modulix.Modules.Crm.Reporting.SalesCard card) => card switch
    {
        Modulix.Modules.Crm.Reporting.SalesCard.Realized => "Gerçekleşen",
        Modulix.Modules.Crm.Reporting.SalesCard.OpenPipeline => "Açık pipeline",
        Modulix.Modules.Crm.Reporting.SalesCard.ExpectedOpen => "Bu dönem beklenen",
        Modulix.Modules.Crm.Reporting.SalesCard.WeightedOpen => "Ağırlıklı tahmin",
        Modulix.Modules.Crm.Reporting.SalesCard.Lost => "Kaybedilen",
        _ => card.ToString(),
    };

    private static ReportTableColumn Column(string key, string label, DatasetColumnKind kind) => new(key, label, kind);

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
