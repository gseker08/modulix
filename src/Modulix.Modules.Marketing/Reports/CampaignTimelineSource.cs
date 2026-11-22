using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Shared.Marketing;

namespace Modulix.Modules.Marketing.Reports;

/// <summary>
/// Müşteri zaman çizelgesinin pazarlama kaynağı (ADR-0053, dilim 2d): müşteriye giden kampanya iletileri ve gelen yanıtlar. Satırlar
/// <see cref="ICampaignReports"/> geçmişinden gelir; rapor ve alıcı görüntüleme izni, kampanya veri kapsamı ve yanıt izni orada uygulanır. Satırın
/// kayıt kimliği kampanyadır (kampanya sayfasına bağlanır); tür kodu yanıt türüdür. Adres ve ileti içeriği satıra girmez.
/// </summary>
internal sealed class CampaignTimelineSource(ICampaignReports reports) : ICustomerTimelineSource
{
    public string Key => "marketing.touches";

    public async Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        if (query.PartyId is not { } partyId)
        {
            // Ortak taraf kaydı olmayan firmaya kampanya gitmez: okunabilir, boş kaynak.
            return new CustomerTimelineSlice([], false);
        }

        var history = await reports.HistoryAsync(partyId, query.Before, query.Take, cancellationToken);
        if (!history.IsSuccess)
        {
            // Çağıran geçmişi okuyamıyorsa kaynak yoktur; başka hata sessizce boş çizelgeye çevrilmez.
            return history.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Zaman çizelgesi okunamadı ({Key}): {history.Error.Message}");
        }

        return new CustomerTimelineSlice(
            [
                .. history.Value!.Items.Select(i => i.Kind == "Delivery"
                    ? new CustomerTimelineEntry(MarketingTimelineKeys.Delivery, i.CampaignId, i.At, i.CampaignName, i.Status)
                    : new CustomerTimelineEntry(MarketingTimelineKeys.Response, i.CampaignId, i.At, i.CampaignName, i.Status, Type: i.Detail)),
            ],
            history.Value.HasMore);
    }
}
