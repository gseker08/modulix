using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Core.Contracts.Pricing;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Pricing;
using Modulix.Modules.Crm.Quotes;
using StoredRule = Modulix.Modules.Crm.Domain.SurchargeRule;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Ek ücret kurallarının teklif satırına dönüşümü (ADR-0018 devamı, Dilim 2c). Kural sonuçları teklif taslakken her yeniden hesapta
/// <see cref="QuoteLine.SourceRuleId"/> dolu satırlar olarak kurulur: kural artık uygulanmıyorsa satır silinir, değişmişse güncellenir, yeniyse eklenir.
/// Ek ücret satırı ana toplama girer ama teklif geneli indirim almaz (<c>ExcludedFromDocumentDiscount</c>). Kural vergi oranı vermiyorsa uygulanmaz
/// (oran uydurulmaz). Teklifte ana toplama giren kalem yoksa ek ücret üretilmez. Taslak dışındaki teklife dokunulmaz: sunulmuş teklif sabittir.
/// </summary>
internal static class QuoteSurchargeGenerator
{
    /// <summary>Üretilecek satırın saf tanımı (veritabanı yok).</summary>
    internal sealed record DesiredLine(
        Guid RuleId, long RuleVersion, string Name, string Explanation, decimal Amount, decimal TaxRatePercent, string Presentation, Guid StableKey);

    /// <summary>
    /// Kuralları bağlama karşı değerlendirir. Vergi oranı olmayan kural uygulanmaz (nedeniyle listelenir); ana toplama giren kalem yoksa hiçbir kural
    /// uygulanmaz. Önizleme ve satır üretimi aynı fonksiyonu kullanır, böylece döküm ile teklif birbirinden ayrışmaz.
    /// </summary>
    internal static SurchargeResult Evaluate(IReadOnlyList<StoredRule> rules, SurchargeContext context, bool hasIncludedLines)
    {
        if (!hasIncludedLines)
        {
            return new SurchargeResult([], [.. rules.Select(r => new SurchargeSkip(r.Id, r.Name, "Teklifte kalem yok."))], new Dictionary<string, decimal>());
        }

        var usable = rules.Where(r => r.TaxRatePercent is not null).ToList();
        var result = SurchargeEngine.Evaluate(usable.Select(SurchargeRules.ToEngineRule), context);
        var untaxed = rules.Where(r => r.TaxRatePercent is null).Select(r => new SurchargeSkip(r.Id, r.Name, "Vergi oranı tanımlı değil."));
        return result with { Skipped = [.. result.Skipped, .. untaxed] };
    }

    /// <summary>Motor sonucunu satır tanımlarına çevirir (motorun sırasıyla). Kararlı anahtar aileye ve kurala bağlıdır: revizyonlar arası karşılaştırma eşleşir.</summary>
    internal static IReadOnlyList<DesiredLine> Plan(Guid familyId, IReadOnlyList<StoredRule> rules, SurchargeResult result)
    {
        var byId = rules.ToDictionary(r => r.Id);
        return
        [
            .. result.Applied.Select(a =>
            {
                var rule = byId[a.RuleId];
                return new DesiredLine(
                    rule.Id, rule.Version, Truncate(a.Name, QuoteLine.NameMaxLength), Truncate(a.Explanation, QuoteLine.TextMaxLength), a.Amount,
                    rule.TaxRatePercent!.Value, a.Presentation.ToString(), StableKey(familyId, rule.Id));
            }),
        ];
    }

    /// <summary>Aile ve kuraldan türetilen sabit kimlik (aynı aile ve kural her zaman aynı anahtarı verir).</summary>
    internal static Guid StableKey(Guid familyId, Guid ruleId)
    {
        Span<byte> bytes = stackalloc byte[32];
        familyId.TryWriteBytes(bytes[..16]);
        ruleId.TryWriteBytes(bytes[16..]);
        return new Guid(SHA256.HashData(bytes)[..16]);
    }

    /// <summary>
    /// Taslak teklifte satırları kural sonuçlarına göre eşitler ve toplam hesabına girecek satır listesini döner (elle satırlar + üretilmiş satırlar).
    /// Elle satırların tutarı önce (genel indirimsiz) tazelenir, çünkü grup toplamları ve tabanlar o tutarlardan kurulur.
    /// </summary>
    internal static async Task<List<QuoteLine>> SyncAsync(IModuleData data, Quote quote, List<QuoteLine> lines, CancellationToken cancellationToken)
    {
        if (quote.Status != QuoteStatus.Draft)
        {
            return lines;
        }

        var manual = lines.Where(l => l.SourceRuleId is null).ToList();
        var existing = lines.Where(l => l.SourceRuleId is not null).ToList();

        var pass = QuoteCalculator.Calculate(
            [.. manual.Select(QuoteTotals.ToInput)], new QuotePolicy(quote.PriceIncludesTax, quote.CalculationPolicyVersion));
        foreach (var line in manual)
        {
            var result = pass.Lines.Single(r => r.Id == line.StableKey);
            line.Included = result.Included;
            line.ListAmount = result.ListAmount;
            line.LineDiscount = result.LineDiscount;
        }

        var rules = await data.Set<StoredRule>().AsNoTracking().Where(r => r.IsActive && r.Currency == quote.Currency).ToListAsync(cancellationToken);
        var custom = string.IsNullOrWhiteSpace(quote.CustomFields) ? null : JsonNode.Parse(quote.CustomFields) as JsonObject;
        var context = QuoteSurchargeContextBuilder.Build(quote.Currency, manual, custom);
        // Yeni teklifte aile kimliği kayıt oluşturulurken (OnCreated) teklifin kendi kimliği olarak atanır; hesap bundan önce çalışsa da aynı anahtar çıkar.
        var familyId = quote.FamilyId == Guid.Empty ? quote.Id : quote.FamilyId;
        var desired = Plan(familyId, rules, Evaluate(rules, context, manual.Any(l => l.Included)));

        var byRule = existing.ToDictionary(l => l.SourceRuleId!.Value);
        var merged = new List<QuoteLine>(manual);
        int position = manual.Count == 0 ? 0 : manual.Max(l => l.Position);
        foreach (var want in desired)
        {
            position++;
            if (!byRule.Remove(want.RuleId, out var line))
            {
                line = new QuoteLine { QuoteId = quote.Id, StableKey = want.StableKey, Kind = QuoteLineKind.Free, Role = "Standard", DiscountKind = "None", SourceRuleId = want.RuleId };
                data.Set<QuoteLine>().Add(line);
            }

            line.Position = position;
            line.Name = want.Name;
            line.Description = want.Explanation;
            line.Quantity = 1m;
            line.UnitPrice = want.Amount;
            line.TaxRatePercent = want.TaxRatePercent;
            line.SourceRuleVersion = want.RuleVersion;
            line.SurchargePresentation = want.Presentation;
            merged.Add(line);
        }

        // Artık uygulanmayan kuralın satırı kalkar (koşul bozuldu, kural kapatıldı ya da silindi).
        data.Set<QuoteLine>().RemoveRange(byRule.Values);
        return merged;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
