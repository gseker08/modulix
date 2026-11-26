using System.Globalization;
using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Currency;
using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Records;

namespace Modulix.Core.Currency;

/// <summary>Bir yazma turunun özeti: açılan, güncellenen, atlanan (elle girilmiş ya da aynı kur) ve bültende bulunmayan para birimleri.</summary>
public sealed record CentralBankSyncResult(int Created, int Updated, int Skipped, int Missing);

/// <summary>
/// Merkez bankası bültenini firmanın kur defterine yazar (ADR-0049 dilim 5d). Kayıt boru hattından <c>core.exchange-rate</c> olarak,
/// <c>CentralBank</c> kaynağıyla yazılır; aynı çift ve gün için <b>elle girilmiş kur ezilmez</b> (elle giriş kazanır), aynı kur tekrar yazılmaz.
/// Hedef para birimi TRY'dir. Tekrar çalıştırmak güvenlidir.
/// </summary>
public sealed class CentralBankRateSync(IRecordPipeline pipeline)
{
    public const string Target = "TRY";

    public async Task<CentralBankSyncResult> ApplyAsync(CentralBankSnapshot snapshot, IEnumerable<string> currencies, CancellationToken cancellationToken)
    {
        var (created, updated, skipped, missing) = (0, 0, 0, 0);
        foreach (var code in currencies.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length == 3 && c != Target).Distinct())
        {
            if (snapshot.Rates.FirstOrDefault(r => r.Code == code) is not { } published)
            {
                missing++;
                continue;
            }

            var rate = decimal.Round(published.Rate, 8, MidpointRounding.AwayFromZero);
            var existing = await FindAsync(code, snapshot.Date, cancellationToken);
            if (existing is null)
            {
                var result = await pipeline.CreateAsync(new RecordCommand(CurrencyEntities.ExchangeRate, null, Values(code, rate, snapshot.Date), Source: ExecutionSource.BackgroundJob), cancellationToken);
                Require(result);
                created++;
            }
            else if (existing.Values["source"]?.GetValue<string>() != ExchangeRateSources.CentralBank
                || existing.Values["rate"]?.GetValue<decimal>() == rate)
            {
                skipped++;
            }
            else
            {
                var result = await pipeline.UpdateAsync(new RecordCommand(CurrencyEntities.ExchangeRate, existing.Id, Values(code, rate, snapshot.Date), existing.Version, ExecutionSource.BackgroundJob), cancellationToken);
                Require(result);
                updated++;
            }
        }

        return new CentralBankSyncResult(created, updated, skipped, missing);
    }

    private async Task<RecordData?> FindAsync(string code, DateOnly day, CancellationToken cancellationToken)
    {
        var page = await pipeline.QueryAsync(new RecordQuery(CurrencyEntities.ExchangeRate, 1, 10, null,
            new Dictionary<string, string> { ["fromCurrency"] = code, ["toCurrency"] = Target, ["sort"] = "-validOn" }), cancellationToken);
        Require(page);
        return page.Value!.Items.FirstOrDefault(r => r.Values["validOn"]?.GetValue<string>() == day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static JsonObject Values(string code, decimal rate, DateOnly day) => new()
    {
        ["fromCurrency"] = code,
        ["toCurrency"] = Target,
        ["rate"] = rate,
        ["validOn"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["source"] = ExchangeRateSources.CentralBank,
        ["note"] = "TCMB döviz satış kuru",
    };

    private static void Require<T>(RecordResult<T> result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Merkez bankası kuru yazılamadı: {result.Error!.Message}");
        }
    }
}

/// <summary>
/// Merkez bankası kurlarını günlük yazan zamanlanmış iş (ADR-0046, ADR-0049 dilim 5d). Kaynak kapalıysa ya da ulaşılamıyorsa hiçbir şey yapmaz.
/// Para birimleri <c>Jobs:core.exchange-rates:Currencies</c> (virgülle; varsayılan USD, EUR, GBP). Yapılandırma <c>ExchangeRates:CentralBank</c>.
/// </summary>
internal sealed class ExchangeRateJob(ICentralBankFeed feed, CentralBankRateSync sync) : IModuleJob
{
    /// <summary>İşin anahtarı: yapılandırma (<c>Jobs:core.exchange-rates</c>), ölçüm adı ve kilit bu ada bağlıdır.</summary>
    public const string JobKey = "core.exchange-rates";

    /// <summary>İşin sistem kimliği (<see cref="ExchangeRateJobAnchor"/> aboneliği).</summary>
    public const string Identity = "core.exchange-rates";

    public const string DefaultCurrencies = "USD,EUR,GBP";

    public async Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken)
    {
        var snapshot = await feed.FetchAsync(cancellationToken);
        if (snapshot is null)
        {
            return ModuleJobResult.Nothing;
        }

        var currencies = (context.Settings.GetValueOrDefault("Currencies") ?? DefaultCurrencies).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = await sync.ApplyAsync(snapshot, currencies, cancellationToken);
        return new ModuleJobResult(
            result.Created + result.Updated > 0 ? $"{result.Created} kur eklendi, {result.Updated} kur güncellendi ({snapshot.Date:yyyy-MM-dd})." : null, result);
    }
}

/// <summary>"core.exchange-rates" kimliğinin izin çapası: olay işlemez, yalnızca zamanlanmış işin izinlerini tanımlar.</summary>
internal sealed class ExchangeRateJobAnchor : IEventSubscriber
{
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}
