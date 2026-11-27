using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Currency;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Currency;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Records;

/// <summary>
/// Firmanın döviz kurları (ADR-0049 dilim 5a): <c>core.exchange-rate</c>. Alanlar kaynak ve hedef para birimi, kur, geçerlilik günü, kaynak ve not;
/// aynı çift ve gün için tek kayıt vardır. Kurları her üye okur (fiyat hesabı, raporlar ve masraf ekranları bakar); yazmak
/// <c>core.exchange-rates.manage</c> ister. Kurdaki değişiklik belgelere uygulanmış kuru değiştirmez (belge kuru belgede saklanır).
/// </summary>
internal sealed class ExchangeRateRecordStore(ModulixDbContext db) : IRecordStore
{
    private const string FromField = "fromCurrency";
    private const string ToField = "toCurrency";
    private const string RateField = "rate";
    private const string ValidOnField = "validOn";
    private const string SourceField = "source";
    private const string NoteField = "note";

    private static readonly string[] Writable = [FromField, ToField, RateField, ValidOnField, SourceField, NoteField];

    private static readonly IReadOnlyCollection<string> Sorts = ["pair", "-pair", "validOn", "-validOn", "rate", "-rate"];

    public string EntityKey => CurrencyEntities.ExchangeRate;

    /// <summary>Çekirdeğin kayıt türü: her tenant için açıktır.</summary>
    public string FeatureKey => EntityKey;

    public string? PermissionFor(RecordOperation operation) =>
        operation == RecordOperation.Read ? null : CoreModule.Permissions.ManageExchangeRates;

    public IReadOnlyList<RecordFieldError> Validate(RecordOperation operation, JsonObject values)
    {
        var errors = new List<RecordFieldError>();
        foreach (var (name, node) in values)
        {
            if (!Writable.Contains(name, StringComparer.Ordinal))
            {
                errors.Add(new RecordFieldError(name, ServerMessages.Validation.UnknownField));
            }
            else if (name is FromField or ToField && Currency(node) is null)
            {
                errors.Add(new RecordFieldError(name, ServerMessages.Validation.Currency));
            }
            else if (name == RateField && Amount(node) is not > 0)
            {
                errors.Add(new RecordFieldError(name, ServerMessages.ExchangeRate.RatePositive));
            }
            else if (name == ValidOnField && Day(node) is null)
            {
                errors.Add(new RecordFieldError(name, ServerMessages.Validation.DateFormat));
            }
            else if (name == SourceField && !ExchangeRateSources.All.Contains(Text(node) ?? string.Empty, StringComparer.Ordinal))
            {
                errors.Add(new RecordFieldError(name, ServerMessages.ExchangeRate.SourceOneOf(string.Join(", ", ExchangeRateSources.All))));
            }
            else if (name == NoteField && node is not null && Text(node) is not { Length: <= ExchangeRate.NoteMaxLength })
            {
                errors.Add(new RecordFieldError(name, ServerMessages.ExchangeRate.NoteTooLong(ExchangeRate.NoteMaxLength)));
            }
        }

        if (operation == RecordOperation.Create)
        {
            errors.AddRange(new[] { FromField, ToField, RateField, ValidOnField }
                .Where(f => !values.ContainsKey(f) || values[f] is null)
                .Select(f => new RecordFieldError(f, ServerMessages.Validation.Required)));
        }

        return errors;
    }

    public async Task<RecordData?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var rate = await db.ExchangeRates.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return rate is null ? null : ToData(rate);
    }

    public async Task<RecordPage> QueryAsync(RecordQuery query, CancellationToken cancellationToken)
    {
        var source = db.ExchangeRates.AsNoTracking();
        string? sort = null;
        var errors = new List<RecordFieldError>();
        foreach (var (name, value) in query.Filters ?? new Dictionary<string, string>())
        {
            switch (name)
            {
                case "sort" when Sorts.Contains(value, StringComparer.Ordinal):
                    sort = value;
                    break;
                case FromField when Currency(value) is { } from:
                    source = source.Where(r => r.FromCurrency == from);
                    break;
                case ToField when Currency(value) is { } to:
                    source = source.Where(r => r.ToCurrency == to);
                    break;
                case "sort" or FromField or ToField:
                    errors.Add(new RecordFieldError(name, name == "sort" ? "Geçersiz sıralama." : "Geçersiz filtre değeri."));
                    break;
                default:
                    errors.Add(new RecordFieldError(name, ServerMessages.Validation.UnknownFilter));
                    break;
            }
        }

        if (errors.Count > 0)
        {
            throw new RecordValidationException(errors);
        }

        if (query.Search is { } term)
        {
            source = source.Where(r => r.FromCurrency.Contains(term) || r.ToCurrency.Contains(term));
        }

        var ordered = sort switch
        {
            "-pair" => source.OrderByDescending(r => r.FromCurrency).ThenByDescending(r => r.ToCurrency).ThenByDescending(r => r.ValidOn),
            "validOn" => source.OrderBy(r => r.ValidOn),
            "-validOn" => source.OrderByDescending(r => r.ValidOn),
            "rate" => source.OrderBy(r => r.Rate),
            "-rate" => source.OrderByDescending(r => r.Rate),
            _ => source.OrderBy(r => r.FromCurrency).ThenBy(r => r.ToCurrency).ThenByDescending(r => r.ValidOn),
        };
        var total = await source.CountAsync(cancellationToken);
        var items = await ordered.ThenBy(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new RecordPage([.. items.Select(ToData)], query.Page, query.PageSize, total);
    }

    public async Task<RecordData> CreateAsync(JsonObject values, CancellationToken cancellationToken)
    {
        var rate = new ExchangeRate(
            Currency(values[FromField])!, Currency(values[ToField])!, Amount(values[RateField])!.Value, Day(values[ValidOnField])!.Value,
            values.ContainsKey(SourceField) ? Text(values[SourceField])! : ExchangeRateSources.Manual, Text(values[NoteField]));
        await EnsureValidAsync(rate.FromCurrency, rate.ToCurrency, rate.ValidOn, rate.Id, cancellationToken);
        db.ExchangeRates.Add(rate);
        return ToData(rate);
    }

    public async Task<RecordData?> UpdateAsync(Guid id, JsonObject values, long? expectedVersion, CancellationToken cancellationToken)
    {
        var rate = await db.ExchangeRates.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rate is null)
        {
            return null;
        }

        EnsureVersion(rate, expectedVersion);
        var from = values.ContainsKey(FromField) ? Currency(values[FromField])! : rate.FromCurrency;
        var to = values.ContainsKey(ToField) ? Currency(values[ToField])! : rate.ToCurrency;
        var amount = values.ContainsKey(RateField) ? Amount(values[RateField])!.Value : rate.Rate;
        var day = values.ContainsKey(ValidOnField) ? Day(values[ValidOnField])!.Value : rate.ValidOn;
        var sourceName = values.ContainsKey(SourceField) ? Text(values[SourceField])! : rate.Source;
        var note = values.ContainsKey(NoteField) ? Text(values[NoteField]) : rate.Note;
        await EnsureValidAsync(from, to, day, rate.Id, cancellationToken);
        rate.Change(from, to, amount, day, sourceName, note);
        return ToData(rate);
    }

    public async Task<bool> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        var rate = await db.ExchangeRates.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rate is null)
        {
            return false;
        }

        EnsureVersion(rate, expectedVersion);
        db.ExchangeRates.Remove(rate);
        return true;
    }

    private async Task EnsureValidAsync(string from, string to, DateOnly day, Guid id, CancellationToken cancellationToken)
    {
        if (from == to)
        {
            throw new RecordValidationException([new RecordFieldError(ToField, ServerMessages.ExchangeRate.CurrenciesSame)]);
        }

        if (await db.ExchangeRates.AsNoTracking().AnyAsync(r => r.Id != id && r.FromCurrency == from && r.ToCurrency == to && r.ValidOn == day, cancellationToken))
        {
            throw new RecordValidationException([new RecordFieldError(ValidOnField, ServerMessages.ExchangeRate.Exists)]);
        }
    }

    private static void EnsureVersion(ExchangeRate rate, long? expectedVersion)
    {
        if (expectedVersion is { } expected && expected != rate.Version)
        {
            throw new RecordConflictException(
                $"Kayıt siz düzenlerken değişti (beklenen sürüm {expected}, güncel sürüm {rate.Version}). Güncel halini alıp tekrar deneyin.");
        }
    }

    private static RecordData ToData(ExchangeRate rate) =>
        new(CurrencyEntities.ExchangeRate, rate.Id, rate.Version, new JsonObject
        {
            [FromField] = rate.FromCurrency,
            [ToField] = rate.ToCurrency,
            [RateField] = rate.Rate,
            [ValidOnField] = rate.ValidOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [SourceField] = rate.Source,
            [NoteField] = rate.Note,
            ["createdAt"] = rate.CreatedAt,
            ["updatedAt"] = rate.UpdatedAt,
        });

    private static string? Currency(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? Currency(text) : null;

    private static string? Currency(string text)
    {
        var code = text.Trim();
        return code.Length == ExchangeRate.CurrencyLength && code.All(char.IsAsciiLetter) ? code.ToUpperInvariant() : null;
    }

    private static decimal? Amount(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<decimal>(out var amount) ? amount : null;

    private static DateOnly? Day(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
        && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}

/// <summary>Firmanın kurlarını okur (ADR-0049 dilim 5a); izin gerektirmez, aktif tenant'ın satırlarını görür (sorgu süzgeci ve RLS).</summary>
internal sealed class ExchangeRateReader(ModulixDbContext db) : IExchangeRateReader
{
    public async Task<ExchangeRateQuote?> FindAsync(string from, string to, DateOnly day, CancellationToken cancellationToken)
    {
        var direct = await db.ExchangeRates.AsNoTracking().Where(r => r.FromCurrency == from && r.ToCurrency == to && r.ValidOn <= day)
            .OrderByDescending(r => r.ValidOn).FirstOrDefaultAsync(cancellationToken);
        if (direct is not null)
        {
            return new ExchangeRateQuote(direct.Rate, direct.ValidOn, Inverted: false);
        }

        var inverse = await db.ExchangeRates.AsNoTracking().Where(r => r.FromCurrency == to && r.ToCurrency == from && r.ValidOn <= day)
            .OrderByDescending(r => r.ValidOn).FirstOrDefaultAsync(cancellationToken);
        return inverse is null ? null : new ExchangeRateQuote(1m / inverse.Rate, inverse.ValidOn, Inverted: true);
    }

    public async Task<ExchangeRateQuote?> FindForInvoiceAsync(string from, string to, DateOnly invoiceDate, CancellationToken cancellationToken)
    {
        // Ayar tenant geneli değerdir (şirket başına değil); okuyucu kayıt boru hattına bağlanmasın diye aynı bağlamdan doğrudan okunur (sorgu süzgeci ve RLS tenant'ı sınırlar).
        var rule = await db.TenantSettings.AsNoTracking()
            .Where(s => s.Key == Modulix.Core.Modules.CoreModule.SettingKeys.InvoiceRateRule && s.CompanyId == null)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken) ?? InvoiceRateRules.Default;
        return await FindAsync(from, to, InvoiceRateRules.LookupDay(invoiceDate, rule), cancellationToken);
    }

    public async Task<IReadOnlyList<ExchangeRatePoint>> ListAsync(CancellationToken cancellationToken) =>
        await db.ExchangeRates.AsNoTracking().Select(r => new ExchangeRatePoint(r.FromCurrency, r.ToCurrency, r.Rate, r.ValidOn)).ToListAsync(cancellationToken);
}
