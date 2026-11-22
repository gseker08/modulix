using System.Globalization;
using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Orders.Lifecycle;

/// <summary>Kalem değişikliği: yeni etkin miktar (yalnızca azaltma) ve/veya planlanan tarih.</summary>
public sealed record OrderLineChange(Guid LineId, decimal? Quantity = null, DateOnly? PlannedDate = null);

/// <summary>
/// Önerilen değişiklikler. <see cref="Header"/>: başlık alanı → yeni değer (boş değer alanı temizler); izin verilen alanlar
/// <see cref="OrderChangeRules.HeaderFields"/>. Ticari kopya alanları (fiyat, müşteri, kaynak teklif) değişiklik talebiyle de değişmez.
/// </summary>
public sealed record OrderChangeSet(IReadOnlyDictionary<string, string?> Header, IReadOnlyList<OrderLineChange> Lines)
{
    public bool IsEmpty => Header.Count == 0 && Lines.Count == 0;
}

/// <summary>Değişikliğin operasyon, teslimat ve sözleşme üzerindeki etkisi; tutar farkı yalnızca tutar görme izni olana gösterilir.</summary>
public sealed record OrderChangeImpact(
    IReadOnlyList<string> Operations, IReadOnlyList<string> Deliveries, IReadOnlyList<string> Contracts, decimal NetDelta, decimal TaxDelta);

/// <summary>Talep anındaki sipariş başlığı (etki ve doğrulama için).</summary>
public sealed record OrderHeaderState(
    string Status, string HandoverStatus, string FulfillmentStatus, string? ContractNumber, IReadOnlyDictionary<string, string?> Values);

public readonly record struct ChangeError(string Field, ServerMessage Message);

/// <summary>Değişiklik talebi kuralları (saf): doğrulama, etkisiz değişikliğin ayıklanması ve etki özeti.</summary>
public static class OrderChangeRules
{
    public static readonly IReadOnlyList<string> HeaderFields =
    [
        "deliveryAddress", "location", "plannedStartDate", "plannedDeliveryDate", "paymentTerms", "deliveryTerms", "commercialTerms", "commitments", "operationsNotes",
    ];

    private static readonly string[] DateFields = ["plannedStartDate", "plannedDeliveryDate"];
    private static readonly string[] ContractRelevant = ["plannedStartDate", "plannedDeliveryDate", "paymentTerms", "deliveryTerms", "commercialTerms", "commitments"];

    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Mevcut değerle aynı olan başlık değişikliklerini ve etkisiz kalem değişikliklerini ayıklar.</summary>
    public static OrderChangeSet Effective(OrderChangeSet set, OrderHeaderState state, IReadOnlyList<LineAmounts> lines, IReadOnlyDictionary<Guid, DateOnly?> linePlanned)
    {
        var header = new Dictionary<string, string?>();
        foreach (var (field, value) in set.Header)
        {
            var next = Normalize(value);
            state.Values.TryGetValue(field, out var current);
            if (next != Normalize(current))
            {
                header[field] = next;
            }
        }

        var changed = new List<OrderLineChange>();
        foreach (var change in set.Lines)
        {
            var line = lines.FirstOrDefault(l => l.LineId == change.LineId);
            var quantity = change.Quantity is { } q && line is not null && q != line.Effective ? q : (decimal?)null;
            linePlanned.TryGetValue(change.LineId, out var currentPlanned);
            var planned = change.PlannedDate is { } p && p != currentPlanned ? p : (DateOnly?)null;
            if (line is null || quantity is not null || planned is not null)
            {
                changed.Add(new OrderLineChange(change.LineId, quantity, planned));
            }
        }

        return new OrderChangeSet(header, changed);
    }

    public static IReadOnlyList<ChangeError> Validate(OrderChangeSet effective, OrderHeaderState state, IReadOnlyList<LineAmounts> lines)
    {
        var errors = new List<ChangeError>();
        if (state.Status != OrderStatus.Approved)
        {
            errors.Add(new ChangeError("status", OrdersMessages.ChangeRule.OnlyApproved));
            return errors;
        }

        if (effective.IsEmpty)
        {
            errors.Add(new ChangeError("changes", OrdersMessages.ChangeRule.NoDifference));
            return errors;
        }

        foreach (var (field, value) in effective.Header)
        {
            if (!HeaderFields.Contains(field))
            {
                errors.Add(new ChangeError(field, OrdersMessages.ChangeRule.FieldLocked));
            }
            else if (DateFields.Contains(field) && value is not null && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add(new ChangeError(field, ServerMessages.Validation.DateFormat));
            }
            else if (value is { Length: > 4000 } || (field is "deliveryAddress" or "location" && value is { Length: > 1000 }))
            {
                errors.Add(new ChangeError(field, OrdersMessages.ChangeRule.TextTooLong));
            }
        }

        string? Resulting(string field) => effective.Header.TryGetValue(field, out var next) ? next : state.Values.GetValueOrDefault(field);
        if (DateOnly.TryParseExact(Resulting("plannedStartDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            && DateOnly.TryParseExact(Resulting("plannedDeliveryDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var delivery) && delivery < start)
        {
            errors.Add(new ChangeError("plannedDeliveryDate", OrdersMessages.ChangeRule.PlannedBeforeStart));
        }

        var seen = new HashSet<Guid>();
        foreach (var change in effective.Lines)
        {
            var line = lines.FirstOrDefault(l => l.LineId == change.LineId);
            if (line is null)
            {
                errors.Add(new ChangeError("lines", OrdersMessages.ChangeRule.LineMissing));
                continue;
            }

            if (!seen.Add(change.LineId))
            {
                errors.Add(new ChangeError("lines", OrdersMessages.ChangeRule.LineDuplicate(line.Name)));
            }

            if (change.Quantity is { } quantity)
            {
                if (quantity < 0m)
                {
                    errors.Add(new ChangeError("lines", OrdersMessages.ChangeRule.LineNegative(line.Name)));
                }
                else if (quantity > line.Effective)
                {
                    errors.Add(new ChangeError("lines", OrdersMessages.ChangeRule.LineIncrease(line.Name, OrdersMessages.Q(line.Effective))));
                }
                else if (quantity < line.Delivered)
                {
                    errors.Add(new ChangeError("lines", OrdersMessages.ChangeRule.LineDelivered(line.Name, OrdersMessages.Q(line.Delivered))));
                }
            }
        }

        if (errors.Count == 0 && lines.Where(l => l.Effective > 0m).All(l => effective.Lines.Any(c => c.LineId == l.LineId && c.Quantity == 0m)))
        {
            errors.Add(new ChangeError("lines", OrdersMessages.ChangeRule.AllZero));
        }

        return errors;
    }

    public static OrderChangeImpact Impact(OrderChangeSet effective, OrderHeaderState state, IReadOnlyList<LineAmounts> lines)
    {
        var operations = new List<string>();
        var deliveries = new List<string>();
        var contracts = new List<string>();
        var netDelta = 0m;
        var taxDelta = 0m;

        foreach (var change in effective.Lines)
        {
            var line = lines.First(l => l.LineId == change.LineId);
            if (change.Quantity is { } quantity)
            {
                var reduction = OrderAmounts.Reduce(line, quantity);
                netDelta -= reduction.ReleasedNet;
                taxDelta -= reduction.ReleasedTax;
                deliveries.Add(line.Delivered > 0m
                    ? $"{line.Name}: miktar {line.Effective:0.####} → {quantity:0.####}; teslim edilen {line.Delivered:0.####} korunur, kalan açık miktar {quantity - line.Delivered:0.####}."
                    : $"{line.Name}: miktar {line.Effective:0.####} → {quantity:0.####}.");
            }

            if (change.PlannedDate is { } planned)
            {
                deliveries.Add($"{line.Name}: planlanan tarih {planned:dd.MM.yyyy} olarak değişir.");
            }
        }

        foreach (var (field, value) in effective.Header)
        {
            state.Values.TryGetValue(field, out var current);
            if (field is "plannedDeliveryDate" or "plannedStartDate")
            {
                var label = field == "plannedDeliveryDate" ? "Planlanan teslim" : "Planlanan başlangıç";
                deliveries.Add($"{label}: {current ?? "—"} → {value ?? "—"}.");
            }
            else if (field is "deliveryAddress" or "location")
            {
                deliveries.Add($"{(field == "location" ? "Lokasyon" : "Teslimat adresi")} değişir; operasyon ve teslimat ekibi güncel adresi görmelidir.");
            }
        }

        if (state.HandoverStatus is HandoverStatus.Sent or HandoverStatus.Accepted or HandoverStatus.InfoRequested)
        {
            operations.Add("Satış devri operasyona gönderilmiş; operasyon ekibine değişiklik ayrıca bildirilmeli ve devir kaydı güncel kapsama göre gözden geçirilmeli.");
        }
        else if (state.HandoverStatus is HandoverStatus.Preparing or HandoverStatus.Ready)
        {
            operations.Add("Devir kaydı hazırlanıyor; gönderilmeden önce güncel kapsama göre gözden geçirilmeli.");
        }

        if (state.FulfillmentStatus is FulfillmentStatus.Planned or FulfillmentStatus.Partial)
        {
            operations.Add("Gerçekleşme başladı ya da planlandı; mevcut teslimat ve başlangıç kayıtları silinmez, yalnızca kalan açık kısım etkilenir.");
        }

        if (state.ContractNumber is not null && (effective.Lines.Any(l => l.Quantity is not null) || effective.Header.Keys.Any(ContractRelevant.Contains)))
        {
            contracts.Add($"Bağlı sözleşme {state.ContractNumber}: kapsam ya da başlangıç koşulları değişiyor; sözleşme bu talepten otomatik değişmez, ayrıca gözden geçirilmeli.");
        }

        return new OrderChangeImpact(operations, deliveries, contracts, netDelta, taxDelta);
    }
}
