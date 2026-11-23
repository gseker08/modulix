using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Approvals;
using Modulix.Core.Contracts.Authorization;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Logistics.Domain;
using Modulix.Modules.Logistics.Records;

namespace Modulix.Modules.Logistics.Lifecycle;

/// <summary>
/// Bir eylemin kullanıcı için durumu: yapılabilir mi, yapılamıyorsa nedeni. İzni olmayan eylem listede hiç yer almaz. Uyarı eylemi engellemez,
/// sonucunu anlatır (ör. iptal edilen seferin bekleyen masrafları; ADR-0038 bölüm 26).
/// </summary>
public sealed record LogisticsActionState(string Action, string Label, bool Allowed, string? Message, bool RequiresReason, bool Destructive, string? Warning = null);

/// <summary>
/// Taşıma talebi, sevkiyat, sefer ve sefer masrafı iş akışı (ADR-0038 K7, K10, dilim 4–5, 8). Durum yalnızca bu komutlarla değişir; her geçiş kayıt boru hattından geçer
/// (izin, sürüm denetimi 409, audit, outbox olayı). Talep onayı aynı işlemde sevkiyatı açar ve talebe bağlar. Sevkiyatın "Planlandı" durumu sefer
/// atamasıdır (<see cref="ITripPlanner"/>). Seferin planı uygunluk denetimi geçince onaylanır (uyarılar seferde saklanır), sefere çıkışta denetim
/// yinelenir; sefer bütün duraklara varış girilince tamamlanır; iptal seferdeki yüklenmemiş sevkiyatları planlamaya döndürür. Teslim eylemleri
/// teslim kanıtıyla gelir (dilim 6).
/// </summary>
public interface ILogisticsWorkflow
{
    /// <summary>Kaydın eylemleri (kullanıcının izni olanlar, ön koşul sonucuyla). Kayıt okunamıyorsa boru hattının hatası.</summary>
    Task<RecordResult<IReadOnlyList<LogisticsActionState>>> ActionsAsync(string entityKey, Guid id, CancellationToken cancellationToken);

    /// <summary>Eylemi uygular. Bilinmeyen eylem 422; izin yoksa 403; ön koşul geçmezse 409; gerekçe isteyen eylemde gerekçe zorunlu (422).</summary>
    Task<RecordResult<RecordData>> RunAsync(string entityKey, Guid id, string action, long version, string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// İptal edilen seferin beklemedeki masrafını başka sefere aktarır (ADR-0038 bölüm 28). Masraf görünmüyorsa 404; masraf yazma izni yoksa 403;
    /// gerekçe zorunlu (422); masraf beklemede değilse ya da seferi iptal edilmemişse 409; hedef sefer yoksa ya da iptal edilmişse 422.
    /// </summary>
    Task<RecordResult<RecordData>> TransferExpenseAsync(Guid id, Guid tripId, long version, string? reason, CancellationToken cancellationToken);
}

/// <summary>Eylem anahtarları (API ve arayüz bunları kullanır).</summary>
public static class LogisticsActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Resubmit = "resubmit";
    public const string Cancel = "cancel";
    public const string Confirm = "confirm";
    public const string Reopen = "reopen";
    public const string Start = "start";
    public const string Complete = "complete";
    public const string StartLoading = "startLoading";
    public const string Depart = "depart";
}

/// <summary>Geçiş tablosu: eylem, izin, hangi durumlardan, hangi duruma, gerekçe gerekir mi.</summary>
public static class LogisticsTransitions
{
    public sealed record Transition(
        string EntityKey, string Action, string Label, string Permission, IReadOnlyCollection<string> From, string To, bool RequiresReason, ServerMessage DeniedMessage,
        bool Destructive = false);

    public static IReadOnlyList<Transition> All { get; } =
    [
        new(LogisticsModule.Entities.TransportRequest, LogisticsActions.Approve, "Onayla ve sevkiyat aç", LogisticsModule.Permissions.RequestsApprove,
            [TransportRequest.Statuses.New], TransportRequest.Statuses.Approved, false, LogisticsMessages.Workflow.RequestApprove),
        new(LogisticsModule.Entities.TransportRequest, LogisticsActions.Reject, "Reddet", LogisticsModule.Permissions.RequestsApprove,
            [TransportRequest.Statuses.New], TransportRequest.Statuses.Rejected, true, LogisticsMessages.Workflow.RequestReject, Destructive: true),
        new(LogisticsModule.Entities.TransportRequest, LogisticsActions.Cancel, "İptal et", LogisticsModule.Permissions.RequestsWrite,
            [TransportRequest.Statuses.New], TransportRequest.Statuses.Cancelled, true, LogisticsMessages.Workflow.RequestCancel, Destructive: true),

        new(LogisticsModule.Entities.Shipment, LogisticsActions.StartLoading, "Yükleme başladı", LogisticsModule.Permissions.ShipmentsWrite,
            [Shipment.Statuses.Planned], Shipment.Statuses.Loading, false, LogisticsMessages.Workflow.ShipmentStartLoading),
        new(LogisticsModule.Entities.Shipment, LogisticsActions.Depart, "Yola çıktı", LogisticsModule.Permissions.ShipmentsWrite,
            [Shipment.Statuses.Loading], Shipment.Statuses.InTransit, false, LogisticsMessages.Workflow.ShipmentDepart),
        new(LogisticsModule.Entities.Shipment, LogisticsActions.Cancel, "İptal et", LogisticsModule.Permissions.ShipmentsWrite,
            [Shipment.Statuses.Pending, Shipment.Statuses.Planned, Shipment.Statuses.Loading], Shipment.Statuses.Cancelled, true,
            LogisticsMessages.Workflow.ShipmentCancel, Destructive: true),

        new(LogisticsModule.Entities.Trip, LogisticsActions.Confirm, "Planı onayla", LogisticsModule.Permissions.TripsWrite,
            [Trip.Statuses.Planning], Trip.Statuses.Ready, false, LogisticsMessages.Workflow.TripConfirm),
        new(LogisticsModule.Entities.Trip, LogisticsActions.Reopen, "Planlamaya geri al", LogisticsModule.Permissions.TripsWrite,
            [Trip.Statuses.Ready], Trip.Statuses.Planning, false, LogisticsMessages.Workflow.TripReopen),
        new(LogisticsModule.Entities.Trip, LogisticsActions.Start, "Sefere çıkar", LogisticsModule.Permissions.TripsWrite,
            [Trip.Statuses.Ready], Trip.Statuses.InProgress, false, LogisticsMessages.Workflow.TripStart),
        new(LogisticsModule.Entities.Trip, LogisticsActions.Complete, "Seferi tamamla", LogisticsModule.Permissions.TripsWrite,
            [Trip.Statuses.InProgress], Trip.Statuses.Completed, false, LogisticsMessages.Workflow.TripComplete),
        new(LogisticsModule.Entities.Trip, LogisticsActions.Cancel, "İptal et", LogisticsModule.Permissions.TripsWrite,
            [Trip.Statuses.Planning, Trip.Statuses.Ready], Trip.Statuses.Cancelled, true, LogisticsMessages.Workflow.TripCancel, Destructive: true),

        new(LogisticsModule.Entities.TripExpense, LogisticsActions.Approve, "Onayla", LogisticsModule.Permissions.ExpensesApprove,
            [TripExpense.Statuses.Pending], TripExpense.Statuses.Approved, false, LogisticsMessages.Workflow.ExpenseApprove),
        new(LogisticsModule.Entities.TripExpense, LogisticsActions.Reject, "Reddet", LogisticsModule.Permissions.ExpensesApprove,
            [TripExpense.Statuses.Pending], TripExpense.Statuses.Rejected, true, LogisticsMessages.Workflow.ExpenseReject, Destructive: true),
        new(LogisticsModule.Entities.TripExpense, LogisticsActions.Resubmit, "Yeniden onaya gönder", LogisticsModule.Permissions.ExpensesWrite,
            [TripExpense.Statuses.Rejected], TripExpense.Statuses.Pending, false, LogisticsMessages.Workflow.ExpenseResubmit),
    ];

    public static Transition? Find(string entityKey, string action) =>
        All.FirstOrDefault(t => t.EntityKey == entityKey && t.Action == action);
}

internal sealed class LogisticsWorkflow(
    IRecordPipeline pipeline, LogisticsWorkflowGate gate, ICurrentAccess access, ITripPlanner planner, LogisticsStoreContext context,
    IApprovalGate approvalGate, ILogisticsPolicyProvider? policy = null) : ILogisticsWorkflow
{
    /// <summary>Talepten sevkiyata kopyalanan alanlar (yük, güzergâh, taraflar).</summary>
    private static readonly string[] CargoKeys =
    [
        "customerPartyId", "senderPartyId", "receiverPartyId", "transportModeKey", "pickupCity", "pickupCountry", "pickupAddress", "deliveryCity", "deliveryCountry", "deliveryAddress",
        "pickupDate", "deliveryBy", "cargoDescription", "weightKg", "volumeM3", "palletCount", "loadingMeters", "hazardous", "notes",
    ];

    public async Task<RecordResult<IReadOnlyList<LogisticsActionState>>> ActionsAsync(string entityKey, Guid id, CancellationToken cancellationToken)
    {
        var record = await pipeline.GetAsync(new RecordRef(entityKey, id), cancellationToken);
        if (!record.IsSuccess)
        {
            return RecordResult<IReadOnlyList<LogisticsActionState>>.Fail(record.Error!.Code, record.Error.Message, record.Error.FieldErrors);
        }

        var granted = await access.GetPermissionsAsync(cancellationToken);
        var status = StatusOf(record.Value!);
        var actions = new List<LogisticsActionState>();
        foreach (var t in LogisticsTransitions.All.Where(t => t.EntityKey == entityKey && granted.Contains(t.Permission)))
        {
            var denied = !t.From.Contains(status) ? t.DeniedMessage : await PreconditionAsync(entityKey, id, t.Action, cancellationToken);
            var reasonRequired = t.RequiresReason || await ReasonRequiredAsync(entityKey, id, t.Action, cancellationToken);
            var warning = denied is null ? await WarningAsync(entityKey, id, t.Action, granted, cancellationToken) : null;
            actions.Add(new LogisticsActionState(t.Action, t.Label, denied is null, denied?.Text, reasonRequired, t.Destructive, warning));
        }

        return RecordResult<IReadOnlyList<LogisticsActionState>>.Ok([.. actions]);
    }

    public async Task<RecordResult<RecordData>> RunAsync(string entityKey, Guid id, string action, long version, string? reason, CancellationToken cancellationToken)
    {
        if (LogisticsTransitions.Find(entityKey, action) is not { } transition)
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.ValidationFailed, "Bilinmeyen eylem.", [new RecordFieldError("action", "Bilinmeyen eylem.")]);
        }

        // Önce kayıt okunur: göremeyen için kayıt yoktur (404), izin denetimi varlığı sızdırmaz.
        var record = await pipeline.GetAsync(new RecordRef(entityKey, id), cancellationToken);
        if (!record.IsSuccess)
        {
            return record;
        }

        if (!await access.HasPermissionAsync(transition.Permission, cancellationToken))
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.PermissionDenied, LogisticsMessages.Workflow.NotAllowed);
        }

        if (!transition.From.Contains(StatusOf(record.Value!)))
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.Conflict, transition.DeniedMessage);
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is null && (transition.RequiresReason || await ReasonRequiredAsync(entityKey, id, transition.Action, cancellationToken)))
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.ValidationFailed, LogisticsMessages.Workflow.ReasonRequired, [new RecordFieldError("reason", LogisticsMessages.Workflow.ReasonRequired)]);
        }

        if (trimmed is { Length: > CargoEntity.ReasonMaxLength })
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.ValidationFailed, LogisticsMessages.Workflow.ReasonTooLong,
                [new RecordFieldError("reason", LogisticsMessages.Workflow.ReasonMax(CargoEntity.ReasonMaxLength))]);
        }

        if (await PreconditionAsync(entityKey, id, transition.Action, cancellationToken) is { } blocked)
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.Conflict, blocked);
        }

        if (entityKey == LogisticsModule.Entities.Trip)
        {
            return await RunTripAsync(id, transition, version, trimmed, cancellationToken);
        }

        return transition.Action == LogisticsActions.Approve && entityKey == LogisticsModule.Entities.TransportRequest
            ? await pipeline.RunAtomicAsync(ct => ApproveAsync(record.Value!, transition, version, ct), cancellationToken)
            : await TransitionAsync(entityKey, id, transition, version, trimmed, linkedId: null, cancellationToken);
    }

    public async Task<RecordResult<RecordData>> TransferExpenseAsync(Guid id, Guid tripId, long version, string? reason, CancellationToken cancellationToken)
    {
        var record = await pipeline.GetAsync(new RecordRef(LogisticsModule.Entities.TripExpense, id), cancellationToken);
        if (!record.IsSuccess)
        {
            return record;
        }

        if (!await access.HasPermissionAsync(LogisticsModule.Permissions.ExpensesWrite, cancellationToken))
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.PermissionDenied, LogisticsMessages.Workflow.NotAllowed);
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is null || trimmed.Length > CargoEntity.ReasonMaxLength)
        {
            var message = trimmed is null ? LogisticsMessages.Workflow.ReasonRequired : LogisticsMessages.Workflow.ReasonMax(CargoEntity.ReasonMaxLength);
            return RecordResult<RecordData>.Fail(RecordErrorCode.ValidationFailed, message, [new RecordFieldError("reason", message)]);
        }

        // Kapı hedef seferle açılır; kuralları depo uygular (durum, iptal edilen kaynak, hedef sefer). Değer gönderilmez: sefer alanı genel uçtan değişmez.
        using (gate.Open(LogisticsModule.Permissions.ExpensesWrite, status: null, trimmed, tripId))
        {
            return await pipeline.UpdateAsync(new RecordCommand(LogisticsModule.Entities.TripExpense, id, new JsonObject(), version), cancellationToken);
        }
    }

    /// <summary>Talep onayı: sevkiyat açılır (talebin yük ve güzergâhıyla), talep "Onaylandı" olur ve sevkiyata bağlanır; ikisi tek işlemde.</summary>
    private async Task<RecordResult<RecordData>> ApproveAsync(RecordData request, LogisticsTransitions.Transition transition, long version, CancellationToken cancellationToken)
    {
        var values = new JsonObject { ["requestId"] = request.Id.ToString() };
        foreach (var key in CargoKeys)
        {
            if (request.Values[key] is { } node)
            {
                values[key] = node.DeepClone();
            }
        }

        // Özel alanlar türe özgüdür; talebinkiler sevkiyata taşınmaz.
        RecordResult<RecordData> shipment;
        using (gate.Open(transition.Permission, status: null, reason: null))
        {
            shipment = await pipeline.CreateAsync(new RecordCommand(LogisticsModule.Entities.Shipment, null, values), cancellationToken);
        }

        if (!shipment.IsSuccess)
        {
            return shipment;
        }

        return await TransitionAsync(request.EntityKey, request.Id, transition, version, reason: null, shipment.Value!.Id, cancellationToken);
    }

    private async Task<RecordResult<RecordData>> TransitionAsync(
        string entityKey, Guid id, LogisticsTransitions.Transition transition, long version, string? reason, Guid? linkedId, CancellationToken cancellationToken)
    {
        using (gate.Open(transition.Permission, transition.To, reason, linkedId))
        {
            return await pipeline.UpdateAsync(new RecordCommand(entityKey, id, new JsonObject { ["status"] = transition.To }, version), cancellationToken);
        }
    }

    /// <summary>
    /// Geçiş tablosunda gerekçe istemeyen eylemin bu kayıtta gerekçe istediği durum (ADR-0038 bölüm 26): iptal edilen seferin masrafını onaylamak.
    /// Sefer yola çıkmadan gerçek bir harcama yapılmış olabilir (depoda yakıt, peşin geçiş ücreti, harcırah avansı); onay yasak değildir ama
    /// onaylayan nedenini yazar. Gerekçe masrafın durum gerekçesine ve denetim kaydına yazılır.
    /// </summary>
    private async Task<bool> ReasonRequiredAsync(string entityKey, Guid id, string action, CancellationToken cancellationToken) =>
        entityKey == LogisticsModule.Entities.TripExpense && action == LogisticsActions.Approve && await ExpenseTripCancelledAsync(id, cancellationToken);

    /// <summary>
    /// Eylemi engellemeyen uyarı (ADR-0038 bölüm 26). Sefer iptalinde bekleyen masraflar beklemede kalır, onaylayan karar verir; sayı yalnızca masraf
    /// okuma izni olana söylenir. İptal edilen seferin masrafını onaylarken gerekçe istendiği söylenir.
    /// </summary>
    private async Task<string?> WarningAsync(string entityKey, Guid id, string action, IReadOnlySet<string> granted, CancellationToken cancellationToken)
    {
        if (entityKey == LogisticsModule.Entities.Trip && action == LogisticsActions.Cancel && granted.Contains(LogisticsModule.Permissions.ExpensesRead))
        {
            var pending = await context.Data.Set<TripExpense>().CountAsync(e => e.TripId == id && e.Status == TripExpense.Statuses.Pending, cancellationToken);
            return pending == 0 ? null
                : await RejectsPendingExpensesAsync(cancellationToken)
                    ? $"Bu seferin {pending} bekleyen masrafı var. Firma ayarı gereği sefer iptal edilince masraflar \"Sefer iptal edildi\" gerekçesiyle reddedilir."
                    : $"Bu seferin {pending} bekleyen masrafı var. Sefer iptal edilince masraflar beklemede kalır ve düzenlenemez; onaylayan onaylar ya da reddeder.";
        }

        return entityKey == LogisticsModule.Entities.TripExpense && action == LogisticsActions.Approve && await ExpenseTripCancelledAsync(id, cancellationToken)
            ? "Sefer iptal edildi; onay için gerekçe yazın (ör. harcama sefer iptalinden önce yapıldı)."
            : null;
    }

    private Task<bool> ExpenseTripCancelledAsync(Guid expenseId, CancellationToken cancellationToken) =>
        context.Data.Set<TripExpense>().Where(e => e.Id == expenseId)
            .Join(context.Data.Set<Trip>(), e => e.TripId, t => t.Id, (e, t) => t.Status)
            .AnyAsync(status => status == Trip.Statuses.Cancelled, cancellationToken);

    /// <summary>
    /// Durum dışı ön koşul (409 iletisi): sefer bütün duraklara varış girilmeden tamamlanmaz; yüklemesi başlamış sevkiyatı olan sefer iptal edilmez.
    /// Plan onayı ve sefere çıkışın uygunluk denetimi eylemde yapılır (<see cref="RunTripAsync"/>).
    /// </summary>
    private async Task<ServerMessage?> PreconditionAsync(string entityKey, Guid id, string action, CancellationToken cancellationToken)
    {
        if (entityKey == LogisticsModule.Entities.TripExpense)
        {
            // Firma masrafın onaylanmasını bir onay sürecine bağlamışsa kayıt onaylanmış olmalıdır (ADR-0027 Ç6a.3); ret her zaman serbesttir.
            return action == LogisticsActions.Approve
                ? await approvalGate.BlockReasonAsync(new RecordRef(entityKey, id), LogisticsModule.ApprovalCommands.ApproveExpense, cancellationToken) is { } blockReason ? (ServerMessage)blockReason : null
                : null;
        }

        if (entityKey != LogisticsModule.Entities.Trip)
        {
            return null;
        }

        return action switch
        {
            LogisticsActions.Complete when await context.Data.Set<TripStop>().AnyAsync(s => s.TripId == id && s.ArrivedAt == null, cancellationToken) =>
                LogisticsMessages.Workflow.StopsNotArrived,
            LogisticsActions.Cancel when await context.Data.Set<Shipment>().AnyAsync(
                    s => s.TripId == id && (s.Status == Shipment.Statuses.Loading || s.Status == Shipment.Statuses.InTransit), cancellationToken) =>
                LogisticsMessages.Workflow.LoadedShipments,
            _ => null,
        };
    }

    /// <summary>
    /// Sefer eylemleri. Plan onayı ve sefere çıkış uygunluk denetiminden geçer: engel varsa 409 (bulgular iletide); onayda uyarılar seferin notuna
    /// yazılır. İptal tek işlemde seferdeki planlanmış sevkiyatları "Planlama bekliyor"a döndürür ve seferden ayırır (duraklar geçmiş olarak kalır);
    /// firma ayarı "Reddedilsin" ise bekleyen masrafları da reddeder.
    /// </summary>
    private async Task<RecordResult<RecordData>> RunTripAsync(
        Guid id, LogisticsTransitions.Transition transition, long version, string? reason, CancellationToken cancellationToken)
    {
        if (transition.Action is LogisticsActions.Confirm or LogisticsActions.Start)
        {
            var check = await planner.CheckAsync(id, cancellationToken);
            if (!check.IsSuccess)
            {
                return RecordResult<RecordData>.Fail(check.Error!.Code, check.Error.Message, check.Error.FieldErrors);
            }

            if (!check.Value!.CanConfirm)
            {
                return RecordResult<RecordData>.Fail(
                    RecordErrorCode.Conflict, LogisticsMessages.Workflow.TripNotReady(string.Join(" ", check.Value.Issues.Where(i => i.Blocking).Select(i => i.Message))));
            }

            if (transition.Action == LogisticsActions.Confirm && check.Value.Issues.Count > 0)
            {
                var warnings = "Uyarılarla onaylandı: " + string.Join(" ", check.Value.Issues.Select(i => i.Message));
                reason = warnings.Length > Trip.ReasonMaxLength ? warnings[..(Trip.ReasonMaxLength - 1)] + "…" : warnings;
            }
        }

        if (transition.Action != LogisticsActions.Cancel)
        {
            return await TransitionAsync(LogisticsModule.Entities.Trip, id, transition, version, reason, linkedId: null, cancellationToken);
        }

        return await pipeline.RunAtomicAsync(async ct =>
        {
            var planned = await context.Data.Set<Shipment>().AsNoTracking()
                .Where(s => s.TripId == id && s.Status == Shipment.Statuses.Planned).Select(s => s.Id).ToListAsync(ct);
            foreach (var shipmentId in planned)
            {
                var shipment = await pipeline.GetAsync(new RecordRef(LogisticsModule.Entities.Shipment, shipmentId), ct);
                if (!shipment.IsSuccess)
                {
                    return shipment;
                }

                RecordResult<RecordData> released;
                using (gate.Open(LogisticsModule.Permissions.TripsWrite, Shipment.Statuses.Pending, reason: null))
                {
                    released = await pipeline.UpdateAsync(new RecordCommand(
                        LogisticsModule.Entities.Shipment, shipmentId, new JsonObject { ["status"] = Shipment.Statuses.Pending }, shipment.Value!.Version), ct);
                }

                if (!released.IsSuccess)
                {
                    return released;
                }
            }

            if (await RejectsPendingExpensesAsync(ct) && await RejectPendingExpensesAsync(id, reason, ct) is { } failed)
            {
                return failed;
            }

            return await TransitionAsync(LogisticsModule.Entities.Trip, id, transition, version, reason, linkedId: null, ct);
        }, cancellationToken);
    }

    private async Task<bool> RejectsPendingExpensesAsync(CancellationToken cancellationToken) =>
        policy is not null && await policy.RejectPendingExpensesOnCancelAsync(cancellationToken);

    /// <summary>
    /// Firma ayarı "Reddedilsin" ise (ADR-0038 bölüm 30) seferin bekleyen masrafları iptalle aynı işlemde reddedilir. Karar firmanın ayarıdır, iptal
    /// edenin değil: kapı sefer yazma izniyle açılır, iptal eden masraf onay ya da okuma iznine sahip olmasa da masraflar reddedilir. Her ret boru
    /// hattından geçer (sürüm, denetim kaydı, olay); biri başarısız olursa iptal de geri alınır. Reddedilmiş ve onaylanmış masraf değişmez.
    /// </summary>
    private async Task<RecordResult<RecordData>?> RejectPendingExpensesAsync(Guid tripId, string? reason, CancellationToken cancellationToken)
    {
        var expenseReason = reason is null ? "Sefer iptal edildi." : $"Sefer iptal edildi: {reason}";
        if (expenseReason.Length > TripExpense.ReasonMaxLength)
        {
            expenseReason = expenseReason[..(TripExpense.ReasonMaxLength - 1)] + "…";
        }

        var pending = await context.Data.Set<TripExpense>().AsNoTracking()
            .Where(e => e.TripId == tripId && e.Status == TripExpense.Statuses.Pending).Select(e => new { e.Id, e.Version }).ToListAsync(cancellationToken);
        foreach (var expense in pending)
        {
            RecordResult<RecordData> rejected;
            using (gate.Open(LogisticsModule.Permissions.TripsWrite, TripExpense.Statuses.Rejected, expenseReason))
            {
                rejected = await pipeline.UpdateAsync(new RecordCommand(
                    LogisticsModule.Entities.TripExpense, expense.Id, new JsonObject { ["status"] = TripExpense.Statuses.Rejected }, expense.Version), cancellationToken);
            }

            if (!rejected.IsSuccess)
            {
                return rejected;
            }
        }

        return null;
    }

    private static string StatusOf(RecordData record) => record.Values["status"]?.GetValue<string>() ?? string.Empty;
}
