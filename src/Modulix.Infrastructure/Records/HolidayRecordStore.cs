using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Calendar;
using Modulix.Core.Contracts.Calendar;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Records;

/// <summary>
/// Firmanın resmî tatil listesi (ADR-0049 dilim 6a): <c>core.holiday</c>. Alanlar tarih, ad ve yarım gün; tarih tenant içinde tektir. Listeyi her üye
/// okur (izin gün sayısı, SLA ve takvim ekranları bakar); yazmak <c>core.holidays.manage</c> ister. Yanlış girilen gün silinebilir. Listedeki
/// değişiklik açılmış izin taleplerinin gün sayısını değiştirmez (talep açılırken hesaplanır).
/// </summary>
internal sealed class HolidayRecordStore(ModulixDbContext db) : IRecordStore
{
    private const string DateField = "date";
    private const string NameField = "name";
    private const string HalfDayField = "halfDay";

    private static readonly string[] Writable = [DateField, NameField, HalfDayField];

    private static readonly IReadOnlyCollection<string> Sorts = ["date", "-date", "name", "-name"];

    public string EntityKey => CalendarEntities.Holiday;

    /// <summary>Çekirdeğin kayıt türü: her tenant için açıktır.</summary>
    public string FeatureKey => EntityKey;

    public string? PermissionFor(RecordOperation operation) =>
        operation == RecordOperation.Read ? null : CoreModule.Permissions.ManageHolidays;

    public IReadOnlyList<RecordFieldError> Validate(RecordOperation operation, JsonObject values)
    {
        var errors = new List<RecordFieldError>();
        foreach (var (name, node) in values)
        {
            if (!Writable.Contains(name, StringComparer.Ordinal))
            {
                errors.Add(new RecordFieldError(name, "Bilinmeyen alan."));
            }
            else if (name == DateField && Day(node) is null)
            {
                errors.Add(new RecordFieldError(name, "Tarih yyyy-AA-gg biçiminde olmalıdır."));
            }
            else if (name == NameField && Text(node) is not { Length: > 0 and <= Holiday.NameMaxLength })
            {
                errors.Add(new RecordFieldError(name, $"Ad zorunludur ve en çok {Holiday.NameMaxLength} karakter olabilir."));
            }
            else if (name == HalfDayField && node is not null && Flag(node) is null)
            {
                errors.Add(new RecordFieldError(name, "Evet ya da hayır olmalıdır."));
            }
        }

        if (operation == RecordOperation.Create)
        {
            errors.AddRange(new[] { DateField, NameField }
                .Where(f => !values.ContainsKey(f) || values[f] is null)
                .Select(f => new RecordFieldError(f, ServerMessages.Validation.Required)));
        }

        return errors;
    }

    public async Task<RecordData?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var holiday = await db.Holidays.AsNoTracking().SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        return holiday is null ? null : ToData(holiday);
    }

    public async Task<RecordPage> QueryAsync(RecordQuery query, CancellationToken cancellationToken)
    {
        var source = db.Holidays.AsNoTracking();
        string? sort = null;
        var errors = new List<RecordFieldError>();
        foreach (var (name, value) in query.Filters ?? new Dictionary<string, string>())
        {
            switch (name)
            {
                case "sort" when Sorts.Contains(value, StringComparer.Ordinal):
                    sort = value;
                    break;
                case "year" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is >= 2000 and <= 2100:
                    var first = new DateOnly(year, 1, 1);
                    var last = new DateOnly(year, 12, 31);
                    source = source.Where(h => h.Date >= first && h.Date <= last);
                    break;
                case "from" when ParseDay(value) is { } from:
                    source = source.Where(h => h.Date >= from);
                    break;
                case "to" when ParseDay(value) is { } to:
                    source = source.Where(h => h.Date <= to);
                    break;
                case "sort" or "year" or "from" or "to":
                    errors.Add(new RecordFieldError(name, name == "sort" ? "Geçersiz sıralama." : "Geçersiz filtre değeri."));
                    break;
                default:
                    errors.Add(new RecordFieldError(name, "Bilinmeyen filtre."));
                    break;
            }
        }

        if (errors.Count > 0)
        {
            throw new RecordValidationException(errors);
        }

        if (query.Search is { } term)
        {
            source = source.Where(h => h.Name.Contains(term));
        }

        var ordered = sort switch
        {
            "-date" => source.OrderByDescending(h => h.Date),
            "name" => source.OrderBy(h => h.Name).ThenBy(h => h.Date),
            "-name" => source.OrderByDescending(h => h.Name).ThenBy(h => h.Date),
            _ => source.OrderBy(h => h.Date),
        };
        var total = await source.CountAsync(cancellationToken);
        var items = await ordered.ThenBy(h => h.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new RecordPage([.. items.Select(ToData)], query.Page, query.PageSize, total);
    }

    public async Task<RecordData> CreateAsync(JsonObject values, CancellationToken cancellationToken)
    {
        var holiday = new Holiday(Day(values[DateField])!.Value, Text(values[NameField])!, Flag(values[HalfDayField]) ?? false);
        await EnsureUniqueAsync(holiday.Date, holiday.Id, cancellationToken);
        db.Holidays.Add(holiday);
        return ToData(holiday);
    }

    public async Task<RecordData?> UpdateAsync(Guid id, JsonObject values, long? expectedVersion, CancellationToken cancellationToken)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (holiday is null)
        {
            return null;
        }

        EnsureVersion(holiday, expectedVersion);
        var date = values.ContainsKey(DateField) ? Day(values[DateField])!.Value : holiday.Date;
        var name = values.ContainsKey(NameField) ? Text(values[NameField])! : holiday.Name;
        var halfDay = values.ContainsKey(HalfDayField) ? Flag(values[HalfDayField]) ?? false : holiday.HalfDay;
        await EnsureUniqueAsync(date, holiday.Id, cancellationToken);
        holiday.Change(date, name, halfDay);
        return ToData(holiday);
    }

    public async Task<bool> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (holiday is null)
        {
            return false;
        }

        EnsureVersion(holiday, expectedVersion);
        db.Holidays.Remove(holiday);
        return true;
    }

    private async Task EnsureUniqueAsync(DateOnly date, Guid id, CancellationToken cancellationToken)
    {
        if (await db.Holidays.AsNoTracking().AnyAsync(h => h.Date == date && h.Id != id, cancellationToken))
        {
            throw new RecordValidationException([new RecordFieldError(DateField, "Bu tarih tatil listesinde zaten var.")]);
        }
    }

    private static void EnsureVersion(Holiday holiday, long? expectedVersion)
    {
        if (expectedVersion is { } expected && expected != holiday.Version)
        {
            throw new RecordConflictException(
                $"Kayıt siz düzenlerken değişti (beklenen sürüm {expected}, güncel sürüm {holiday.Version}). Güncel halini alıp tekrar deneyin.");
        }
    }

    private static RecordData ToData(Holiday holiday) =>
        new(CalendarEntities.Holiday, holiday.Id, holiday.Version, new JsonObject
        {
            [DateField] = holiday.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [NameField] = holiday.Name,
            [HalfDayField] = holiday.HalfDay,
            ["createdAt"] = holiday.CreatedAt,
            ["updatedAt"] = holiday.UpdatedAt,
        });

    private static DateOnly? Day(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? ParseDay(text) : null;

    private static DateOnly? ParseDay(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static bool? Flag(JsonNode? node) =>
        node is null ? null : node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
}

/// <summary>Firmanın tatil listesini okur (ADR-0049 dilim 6a); izin gerektirmez, aktif tenant'ın satırlarını görür (sorgu süzgeci ve RLS).</summary>
internal sealed class HolidayCalendar(ModulixDbContext db) : IHolidayCalendar
{
    public async Task<IReadOnlyDictionary<DateOnly, HolidayDay>> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return new Dictionary<DateOnly, HolidayDay>();
        }

        return await db.Holidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to)
            .ToDictionaryAsync(h => h.Date, h => new HolidayDay(h.Date, h.Name, h.HalfDay), cancellationToken);
    }
}
