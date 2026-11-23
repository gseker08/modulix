using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Customization;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Logistics.Domain;

namespace Modulix.Modules.Logistics.Records;

/// <summary>
/// Sefer masrafı deposu (ADR-0038 K10, dilim 8). Masraf bir sefere bağlıdır (yalnızca oluştururken); iptal edilmiş sefere masraf açılmaz. Tür
/// firmanın listesindendir ve kural türün kategorisine bakar: yakıtta litre zorunludur (kilometre isteğe bağlı), yakıt dışında litre ve kilometre
/// girilmez; sürücüye rücu bayrağı yalnızca ceza kategorisinde işaretlenir. Tutar sıfırdan büyük, para birimi üç harflidir; KDV payı tutarı aşamaz;
/// masraf tarihi ileri bir gün olamaz. Beklemedeki ve reddedilmiş masraf düzenlenir (reddedilen düzeltilip yeniden gönderilir), seferi iptal
/// edilmişse düzenlenmez (ADR-0038 bölüm 25; karar komutları açık kalır); onaylanmış masraf
/// değişmez: onay tutara bağlıdır. Durum yalnızca iş akışı komutuyla (onay, ret, yeniden gönderme) değişir. Masraf silinmez (K12): yanlış masraf reddedilir. Makbuz ve fiş kayda Core dosya deposuyla
/// eklenir (okuma = masraf okuma izni, ekleme ve silme = masraf yazma izni ve kaydın kuralı, ADR-0038 bölüm 24: onaylanmış masrafta ve iptal edilmiş
/// seferde belge eklenmez, silinmez).
/// </summary>
internal sealed class TripExpenseRecordStore(LogisticsStoreContext context) : LogisticsRecordStore<TripExpense>(context)
{
    /// <summary>Seferi iptal edilmiş masrafların seferleri (<see cref="PrepareAsync"/> doldurur; <c>tripCancelled</c> alanı okur).</summary>
    private readonly HashSet<Guid> cancelledTrips = [];

    private IReadOnlyList<RecordField<TripExpense>>? fields;

    public override string EntityKey => LogisticsModule.Entities.TripExpense;

    protected override string ReadPermission => LogisticsModule.Permissions.ExpensesRead;

    protected override string WritePermission => LogisticsModule.Permissions.ExpensesWrite;

    protected override IReadOnlyList<RecordField<TripExpense>> Fields => fields ??=
    [
        Field.Reference<TripExpense, Trip>("tripId", LogisticsMessages.NotFound.Trip, e => e.TripId, (e, v) => e.TripId = v!.Value, required: true, createOnly: true),
        Field.ListChoice<TripExpense>("kindKey", LogisticsModule.Lists.ExpenseKind, e => e.KindKey, (e, v) => e.KindKey = v ?? string.Empty),
        Field.Text<TripExpense>("description", TripExpense.DescriptionMaxLength, e => e.Description, (e, v) => e.Description = v ?? string.Empty, required: true),
        Field.Date<TripExpense>("expenseDate", e => e.ExpenseDate, (e, v) => e.ExpenseDate = v!.Value, required: true),
        Field.Decimal<TripExpense>("amount", e => e.Amount == 0 ? null : e.Amount, (e, v) => e.Amount = v ?? 0, 2, 0, LogisticsEntity.MaxAmount, required: true),
        MoneyRules.Currency<TripExpense>("currency", e => e.Currency.Length == 0 ? null : e.Currency, (e, v) => e.Currency = v ?? string.Empty),
        MoneyRules.Amount<TripExpense>("vatAmount", e => e.VatAmount, (e, v) => e.VatAmount = v),
        Field.PartyRef<TripExpense>("supplierPartyId", LogisticsMessages.NotFound.Supplier, e => e.SupplierPartyId, (e, v) => e.SupplierPartyId = v),
        Field.Text<TripExpense>("documentNo", TripExpense.DocumentNoMaxLength, e => e.DocumentNo, (e, v) => e.DocumentNo = v),
        Field.Decimal<TripExpense>("liters", e => e.Liters, (e, v) => e.Liters = v, 3, 0, TripExpense.MaxLiters),
        Field.Integer<TripExpense>("odometerKm", e => e.OdometerKm, (e, v) => e.OdometerKm = v, 0, TripExpense.MaxOdometerKm),
        Field.Flag<TripExpense>("chargeToDriver", e => e.ChargeToDriver, (e, v) => e.ChargeToDriver = v),
        // Litre fiyatı (KDV dahil) tutardan ve litreden türetilir; araç yakıt raporunun birim maliyeti buradan okunur.
        Field.Computed<TripExpense>("unitPrice", e => e.Liters is > 0 ? JsonValue.Create(decimal.Round(e.Amount / e.Liters.Value, 4)) : null),
        Field.Computed<TripExpense>("status", e => e.Status),
        Field.Computed<TripExpense>("statusReason", e => e.StatusReason),
        // Masrafın kendi kuralını belirler (düzenleme, belge, gerekçeli onay; ADR-0038 bölüm 25-26); seferin başka bilgisi açılmaz.
        Field.Computed<TripExpense>("tripCancelled", e => cancelledTrips.Contains(e.TripId)),
    ];

    protected override IReadOnlyDictionary<string, RecordFilter<TripExpense>> Filters { get; } = new Dictionary<string, RecordFilter<TripExpense>>
    {
        ["tripId"] = (q, v) => FilterValue.Id(v) is { } id ? q.Where(e => e.TripId == id) : null,
        ["status"] = (q, v) => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } keys && keys.All(TripExpense.Statuses.All.Contains)
            ? q.Where(e => keys.Contains(e.Status))
            : null,
        ["kindKey"] = (q, v) => ChoiceValueKeys.IsValid(v) ? q.Where(e => e.KindKey == v) : null,
        ["supplierPartyId"] = (q, v) => FilterValue.Id(v) is { } id ? q.Where(e => e.SupplierPartyId == id) : null,
        ["from"] = (q, v) => FilterValue.Day(v) is { } day ? q.Where(e => e.ExpenseDate >= day) : null,
        ["to"] = (q, v) => FilterValue.Day(v) is { } day ? q.Where(e => e.ExpenseDate <= day) : null,
    };

    // Varsayılan: en yeni masraf önce.
    protected override IOrderedQueryable<TripExpense> Order(IQueryable<TripExpense> source) =>
        source.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.CreatedAt);

    protected override IReadOnlyCollection<string>? SortKeys { get; } = WithDescending("expenseDate", "amount", "status", "createdAt");

    protected override IOrderedQueryable<TripExpense> Order(IQueryable<TripExpense> source, string? sort)
    {
        var desc = sort?.StartsWith('-') == true;
        return sort?.TrimStart('-') switch
        {
            "expenseDate" => By(source, e => e.ExpenseDate, desc).ThenByDescending(e => e.CreatedAt),
            "amount" => By(source, e => e.Amount, desc).ThenByDescending(e => e.ExpenseDate),
            "status" => By(source, e => e.Status, desc).ThenByDescending(e => e.ExpenseDate),
            "createdAt" => By(source, e => e.CreatedAt, desc),
            _ => Order(source),
        };
    }

    protected override IQueryable<TripExpense> Search(IQueryable<TripExpense> source, string term) =>
        source.Where(e => e.Description.Contains(term));

    protected override async Task AppliedAsync(TripExpense entity, JsonObject values, bool isNew, CancellationToken cancellationToken)
    {
        if (Context.Gate.IsOpen)
        {
            if (Context.Gate.Status is { } status)
            {
                entity.Status = status;
                entity.StatusReason = Context.Gate.Reason;
            }
            else if (!isNew && Context.Gate.LinkedId is { } targetTrip)
            {
                await TransferAsync(entity, targetTrip, cancellationToken);
            }
        }
        else if (!isNew && entity.Status is not (TripExpense.Statuses.Pending or TripExpense.Statuses.Rejected))
        {
            throw new RecordConflictException(LogisticsMessages.Expense.ApprovedLocked);
        }

        // İptal edilen sefere masraf açılmaz; beklemedeki ya da reddedilmiş masrafı da artık düzenlenmez (ADR-0038 bölüm 25). Karar komutları
        // (onay, ret, yeniden gönderme) kapıdan geçer ve açık kalır: iptal edilen seferin bekleyen masrafı reddedilerek kapatılır.
        if (isNew || !Context.Gate.IsOpen)
        {
            var tripStatus = await Context.Data.Set<Trip>().AsNoTracking().Where(t => t.Id == entity.TripId).Select(t => t.Status).SingleAsync(cancellationToken);
            if (tripStatus == Trip.Statuses.Cancelled)
            {
                throw new RecordConflictException(isNew
                    ? LogisticsMessages.Expense.CancelledNoAdd
                    : LogisticsMessages.Expense.CancelledNoEdit);
            }
        }

        entity.Description = entity.Description.Trim();
        entity.Currency = entity.Currency.Trim();
        entity.DocumentNo = string.IsNullOrWhiteSpace(entity.DocumentNo) ? null : entity.DocumentNo.Trim();
        var errors = new List<RecordFieldError>();

        if (entity.Description.Length == 0)
        {
            errors.Add(new RecordFieldError("description", ServerMessages.Validation.Required));
        }

        if (entity.Amount <= 0)
        {
            errors.Add(new RecordFieldError("amount", LogisticsMessages.Expense.AmountPositive));
        }

        if (entity.Currency.Length == 0)
        {
            errors.Add(new RecordFieldError("currency", LogisticsMessages.Expense.CurrencyRequired));
        }
        else
        {
            MoneyRules.Validate(entity.Amount, entity.Currency, "amount", "currency", errors);
        }

        if (entity.VatAmount is { } vat && vat > entity.Amount)
        {
            errors.Add(new RecordFieldError("vatAmount", LogisticsMessages.Expense.VatExceeds));
        }

        if (entity.ExpenseDate > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
        {
            errors.Add(new RecordFieldError("expenseDate", LogisticsMessages.Expense.DateFuture));
        }

        var category = (await Context.ChoiceLists.GetAsync(LogisticsModule.Lists.ExpenseKind, cancellationToken)).Find(entity.KindKey)?.Category;
        if (category == LogisticsModule.ExpenseKindCategories.Fuel)
        {
            if (entity.Liters is not > 0)
            {
                errors.Add(new RecordFieldError("liters", LogisticsMessages.Expense.LitersRequired));
            }
        }
        else
        {
            if (entity.Liters is not null)
            {
                errors.Add(new RecordFieldError("liters", LogisticsMessages.Expense.LitersFuelOnly));
            }

            if (entity.OdometerKm is not null)
            {
                errors.Add(new RecordFieldError("odometerKm", LogisticsMessages.Expense.OdometerFuelOnly));
            }
        }

        if (entity.ChargeToDriver && category != LogisticsModule.ExpenseKindCategories.Fine)
        {
            errors.Add(new RecordFieldError("chargeToDriver", LogisticsMessages.Expense.ChargeFineOnly));
        }

        if (errors.Count > 0)
        {
            throw new RecordValidationException(errors);
        }
    }

    /// <summary>
    /// Başka sefere aktarma (ADR-0038 bölüm 28; komut <see cref="Lifecycle.ILogisticsWorkflow.TransferExpenseAsync"/> kapıyı hedef seferle açar):
    /// yalnızca iptal edilen seferin beklemedeki masrafı, iptal edilmemiş başka bir sefere taşınır. Durum değişmez (onaylayan yeni seferde karar
    /// verir); gerekçe durum gerekçesine yazılır, eski ve yeni sefer denetim kaydında görünür. Belgeler masrafa bağlıdır, onunla gider.
    /// </summary>
    private async Task TransferAsync(TripExpense entity, Guid targetTrip, CancellationToken cancellationToken)
    {
        if (entity.Status != TripExpense.Statuses.Pending)
        {
            throw new RecordConflictException(LogisticsMessages.Expense.TransferPendingOnly);
        }

        var trips = Context.Data.Set<Trip>().AsNoTracking();
        if (await trips.Where(t => t.Id == entity.TripId).Select(t => t.Status).SingleAsync(cancellationToken) != Trip.Statuses.Cancelled)
        {
            throw new RecordConflictException(LogisticsMessages.Expense.TransferCancelledOnly);
        }

        var targetStatus = await trips.Where(t => t.Id == targetTrip).Select(t => t.Status).SingleOrDefaultAsync(cancellationToken);
        ServerMessage? error = targetStatus is null ? LogisticsMessages.NotFound.Trip
            : targetStatus == Trip.Statuses.Cancelled ? LogisticsMessages.Expense.TransferTargetCancelled
            : null;
        if (error is not null)
        {
            throw new RecordValidationException([new RecordFieldError("tripId", error)]);
        }

        entity.TripId = targetTrip;
        entity.StatusReason = Context.Gate.Reason;
    }

    protected override async Task PrepareAsync(IReadOnlyCollection<TripExpense> entities, CancellationToken cancellationToken)
    {
        cancelledTrips.Clear();
        var tripIds = entities.Select(e => e.TripId).Distinct().ToList();
        if (tripIds.Count > 0)
        {
            cancelledTrips.UnionWith(await Context.Data.Set<Trip>().AsNoTracking()
                .Where(t => tripIds.Contains(t.Id) && t.Status == Trip.Statuses.Cancelled).Select(t => t.Id).ToListAsync(cancellationToken));
        }
    }

    protected override Task BeforeDeleteAsync(TripExpense entity, CancellationToken cancellationToken) =>
        throw new RecordConflictException(LogisticsMessages.Expense.NoDelete);

    /// <summary>
    /// Makbuz ve fiş kuralı kaydın güncelleme kuralıyla aynıdır (ADR-0038 bölüm 24 ve 25): beklemedeki ve reddedilmiş masrafın belgesi değişir
    /// (reddedilen masraf belgesi düzeltilip yeniden gönderilir); onaylanmış masrafın belgesi değişmez (onay belgeye de bağlıdır). İptal edilmiş
    /// seferin masrafına belge eklenmez ve silinmez (masrafın kendisi de düzenlenmez).
    /// </summary>
    protected override async Task<string?> FileWriteDeniedAsync(TripExpense entity, CancellationToken cancellationToken)
    {
        if (entity.Status is not (TripExpense.Statuses.Pending or TripExpense.Statuses.Rejected))
        {
            return "Onaylanmış masrafın makbuzu ve fişi değiştirilemez; hatalıysa yeni masraf girin.";
        }

        var tripStatus = await Context.Data.Set<Trip>().AsNoTracking().Where(t => t.Id == entity.TripId).Select(t => t.Status).SingleOrDefaultAsync(cancellationToken);
        return tripStatus == Trip.Statuses.Cancelled ? "İptal edilen seferin masrafına makbuz ya da fiş eklenmez, silinmez." : null;
    }
}
