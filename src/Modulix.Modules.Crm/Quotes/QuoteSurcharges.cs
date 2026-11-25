using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Pricing;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Pricing;
using Modulix.Modules.Crm.Records;

namespace Modulix.Modules.Crm.Quotes;

/// <summary>Teklife uygulanan ek ücretlerin dökümü: motor sonucu ve teklifin para birimi.</summary>
public sealed record QuoteSurchargePreview(string Currency, SurchargeResult Result);

/// <summary>
/// Teklifin ek ücret önizlemesi (ADR-0018 devamı, Dilim 2c ilk aşama). Salt okunurdur: hiçbir şey yazmaz, teklifin toplamlarını değiştirmez.
/// Görünürlük (izin ve veri kapsamı) kayıt boru hattında doğrulanır; özel alan değerleri alan seviyesi yetki uygulanmış kayıttan okunur, yani
/// kullanıcının göremediği alan bağlamda yoktur ve o alana bağlı kural "eksik değer" ile uygulanmaz.
/// </summary>
public interface IQuoteSurcharges
{
    Task<RecordResult<QuoteSurchargePreview>> PreviewAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class QuoteSurchargeService(IRecordPipeline pipeline, QuoteReader reader) : IQuoteSurcharges
{
    public async Task<RecordResult<QuoteSurchargePreview>> PreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await pipeline.GetAsync(new RecordRef(CrmModule.Entities.Quote, id), cancellationToken);
        if (!record.IsSuccess)
        {
            return RecordResult<QuoteSurchargePreview>.Fail(record.Error!);
        }

        var quote = await reader.QuoteAsync(id, cancellationToken);
        if (quote is null)
        {
            return RecordResult<QuoteSurchargePreview>.Fail(RecordErrorCode.NotFound, CrmMessages.Quote.NotFound);
        }

        var lines = await reader.LinesAsync(id, cancellationToken);
        var rules = await reader.ActiveSurchargeRulesAsync(quote.Currency, cancellationToken);
        var context = QuoteSurchargeContextBuilder.Build(quote.Currency, lines, record.Value!.Values[RecordFields.Custom] as JsonObject);

        try
        {
            // Satır üretimiyle aynı değerlendirme: vergi oranı olmayan kural ve kalemsiz teklif orada da uygulanmaz.
            var result = QuoteSurchargeGenerator.Evaluate(rules, context, lines.Any(l => l.Included && l.SourceRuleId is null));
            return RecordResult<QuoteSurchargePreview>.Ok(new QuoteSurchargePreview(quote.Currency, result));
        }
        catch (PricingException ex)
        {
            // Kayıtta doğrulanan kural bozulamaz; yine de bozuk bir kayıt tüm teklifleri 500'e düşürmesin.
            return RecordResult<QuoteSurchargePreview>.Fail(RecordErrorCode.ValidationFailed, ex.Message);
        }
    }
}
