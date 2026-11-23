using System.Security.Cryptography;
using Modulix.Modules.Orders.Domain;
using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Orders.Lifecycle;

public static class DeliveryStatus
{
    public const string Planned = "Planned";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = [Planned, Delivered, Failed, Cancelled];
}

public static class ServiceStatus
{
    public const string Planned = "Planned";
    public const string Started = "Started";
    public const string Completed = "Completed";

    public static readonly IReadOnlyList<string> All = [Planned, Started, Completed];
}

public static class DeliveryAction
{
    public const string Deliver = "deliver";
    public const string Fail = "fail";
    public const string Cancel = "cancel";
    public const string Reschedule = "reschedule";

    public static readonly IReadOnlyList<string> All = [Deliver, Fail, Cancel, Reschedule];
}

public static class ServiceAction
{
    public const string Start = "start";
    public const string Confirm = "confirm";
    public const string Complete = "complete";

    public static readonly IReadOnlyList<string> All = [Start, Confirm, Complete];
}

/// <summary>Gerçekleşme tarihçe türleri (sipariş tarihçesine yazılır).</summary>
public static class FulfillmentEventKinds
{
    public const string DeliveryPlanned = "DeliveryPlanned";
    public const string Delivered = "Delivered";
    public const string DeliveryFailed = "DeliveryFailed";
    public const string DeliveryRescheduled = "DeliveryRescheduled";
    public const string DeliveryCancelled = "DeliveryCancelled";
    public const string EvidenceAdded = "EvidenceAdded";
    public const string ServicePlanned = "ServicePlanned";
    public const string ServiceStarted = "ServiceStarted";
    public const string ServiceConfirmed = "ServiceConfirmed";
    public const string ServiceCompleted = "ServiceCompleted";
    public const string FulfillmentChanged = "FulfillmentChanged";
}

/// <summary>Gerçekleşme hesabı için etkin sipariş kalemi durumu.</summary>
/// <param name="ServiceStatus">Hizmet kaleminin başlangıç kaydı durumu; kayıt yoksa null. Ürün kaleminde kullanılmaz.</param>
public sealed record FulfillmentLine(Guid LineId, string Kind, decimal Effective, decimal Delivered, string? ServiceStatus);

/// <summary>
/// Siparişin gerçekleşme durumu (türetilir; elle yazılmaz). Ürün kalemleri teslim edilen miktardan, hizmet kalemleri başlangıç kaydından izlenir; karma siparişte
/// ikisi ayrı izlenir ve sipariş özeti bunlardan türetilir. Hizmetin <b>başlaması</b> tamamlanması sayılmaz: tamamlandı olmak için hizmet kalemi için ayrıca tamamlama
/// kaydı gerekir; süreli hizmetin devam eden yükümlülüğü bu yüzden görünür kalır.
/// </summary>
public static class FulfillmentCalculator
{
    public static string Compute(IReadOnlyList<FulfillmentLine> lines, bool anyPlannedDelivery)
    {
        var items = lines.Where(l => l.Effective > 0m).ToList();
        if (items.Count == 0)
        {
            return FulfillmentStatus.NotStarted;
        }

        static bool IsProduct(FulfillmentLine l) => LineKinds.IsDeliverable(l.Kind);
        var allDone = items.All(l => IsProduct(l) ? l.Delivered >= l.Effective : l.ServiceStatus == ServiceStatus.Completed);
        if (allDone)
        {
            return FulfillmentStatus.Completed;
        }

        var progress = items.Any(l => IsProduct(l) ? l.Delivered > 0m : l.ServiceStatus is Lifecycle.ServiceStatus.Started or Lifecycle.ServiceStatus.Completed);
        if (progress)
        {
            return FulfillmentStatus.Partial;
        }

        return anyPlannedDelivery || items.Any(l => !IsProduct(l) && l.ServiceStatus == ServiceStatus.Planned) ? FulfillmentStatus.Planned : FulfillmentStatus.NotStarted;
    }
}

public sealed record QuantityRequest(Guid LineId, decimal Quantity);

public readonly record struct DeliveryError(string Field, ServerMessage Message);

/// <summary>Teslimat miktar kuralları (saf): fazla teslimat ve planlama aşımı sunucuda engellenir.</summary>
public static class DeliveryRules
{
    /// <summary>
    /// Planlama: her kalem teslim edilebilir (ürün) olmalı, miktar sıfırdan büyük olmalı ve <paramref name="remaining"/> eksi diğer açık teslimatlarda planlanan miktarı
    /// (<paramref name="plannedElsewhere"/>) aşmamalıdır.
    /// </summary>
    public static IReadOnlyList<DeliveryError> ValidatePlan(
        IReadOnlyList<QuantityRequest> requested, IReadOnlyDictionary<Guid, (string Name, string Kind, decimal Remaining)> lines, IReadOnlyDictionary<Guid, decimal> plannedElsewhere)
    {
        var errors = new List<DeliveryError>();
        if (requested.Count == 0)
        {
            errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.PlanNeedLine));
            return errors;
        }

        if (requested.Select(r => r.LineId).Distinct().Count() != requested.Count)
        {
            errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.PlanDuplicate));
        }

        foreach (var request in requested)
        {
            if (!lines.TryGetValue(request.LineId, out var line))
            {
                errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.PlanLineMissing));
                continue;
            }

            if (!LineKinds.IsDeliverable(line.Kind))
            {
                errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.PlanNotProduct(line.Name)));
            }
            else if (request.Quantity <= 0m)
            {
                errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.PlanQuantityPositive(line.Name)));
            }
            else
            {
                var capacity = line.Remaining - plannedElsewhere.GetValueOrDefault(request.LineId);
                if (request.Quantity > capacity)
                {
                    errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.PlanCapacity(line.Name, OrdersMessages.Q(Math.Max(0m, capacity)), OrdersMessages.Q(line.Remaining), OrdersMessages.Q(plannedElsewhere.GetValueOrDefault(request.LineId)))));
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Teslim kaydı: gerçekleşen miktar negatif olamaz, toplam sıfırdan büyük olmalı ve kalemin kalan miktarını (<paramref name="remaining"/>) aşmamalıdır. Fazla teslimat
    /// reddedilir; eksik teslimat kalan miktar olarak açık kalır.
    /// </summary>
    public static IReadOnlyList<DeliveryError> ValidateDeliver(IReadOnlyList<QuantityRequest> actual, IReadOnlyDictionary<Guid, (string Name, decimal Remaining)> remaining)
    {
        var errors = new List<DeliveryError>();
        if (actual.All(a => a.Quantity <= 0m))
        {
            errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.DeliverPositive));
            return errors;
        }

        foreach (var item in actual)
        {
            if (!remaining.TryGetValue(item.LineId, out var line))
            {
                errors.Add(new DeliveryError("lines", OrdersMessages.Delivery.LineMissing));
            }
            else if (item.Quantity < 0m)
            {
                errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.DeliverNegative(line.Name)));
            }
            else if (item.Quantity > line.Remaining)
            {
                errors.Add(new DeliveryError("lines", OrdersMessages.FulfillmentRule.DeliverExcess(line.Name, OrdersMessages.Q(line.Remaining), OrdersMessages.Q(item.Quantity))));
            }
        }

        return errors;
    }
}

/// <summary>Teslimat ve hizmet başlangıcı geçiş ve ön koşul kuralları (saf).</summary>
public static class FulfillmentTransitions
{
    public static OrderRuleResult CanDeliveryAct(string action, string status, string orderStatus, bool alreadyRescheduled)
    {
        if (orderStatus != OrderStatus.Approved && action != DeliveryAction.Cancel)
        {
            return OrderRuleResult.Deny("order-not-approved", OrdersMessages.FulfillmentRule.OrderNotApproved);
        }

        return action switch
        {
            DeliveryAction.Deliver or DeliveryAction.Fail or DeliveryAction.Cancel =>
                status == DeliveryStatus.Planned ? OrderRuleResult.Ok : OrderRuleResult.Deny("not-planned", OrdersMessages.FulfillmentRule.NotPlanned),
            DeliveryAction.Reschedule => status != DeliveryStatus.Failed
                ? OrderRuleResult.Deny("not-failed", OrdersMessages.FulfillmentRule.NotFailed)
                : alreadyRescheduled ? OrderRuleResult.Deny("already-rescheduled", OrdersMessages.FulfillmentRule.AlreadyRescheduled) : OrderRuleResult.Ok,
            _ => OrderRuleResult.Deny("unknown", OrdersMessages.Workflow.UnknownAction),
        };
    }

    public static bool DeliveryRequiresReason(string action) => action is DeliveryAction.Fail or DeliveryAction.Cancel;

    public static OrderRuleResult CanServiceAct(string action, string status) => action switch
    {
        ServiceAction.Start => status == ServiceStatus.Planned ? OrderRuleResult.Ok : OrderRuleResult.Deny("not-planned", status == ServiceStatus.Started ? OrdersMessages.FulfillmentRule.ServiceAlreadyStarted : OrdersMessages.FulfillmentRule.ServiceAlreadyCompleted),
        ServiceAction.Confirm => status == ServiceStatus.Started ? OrderRuleResult.Ok : OrderRuleResult.Deny("not-started", OrdersMessages.FulfillmentRule.ConfirmNeedsStarted),
        ServiceAction.Complete => status == ServiceStatus.Started ? OrderRuleResult.Ok : OrderRuleResult.Deny("not-started", status == ServiceStatus.Planned ? OrdersMessages.FulfillmentRule.CompleteBeforeStart : OrdersMessages.FulfillmentRule.ServiceAlreadyCompleted),
        _ => OrderRuleResult.Deny("unknown", OrdersMessages.Workflow.UnknownAction),
    };

    /// <summary>
    /// Hizmet başlangıcını engelleyen eksikler. Başlangıç kaydı bunlardan biri varken yapılamaz: sipariş onaylı değil, satış devri kabul edilmemiş, bağımlı iş
    /// tamamlanmamış, hizmet ön koşul görevi tamamlanmamış.
    /// </summary>
    public static IReadOnlyList<string> ServiceStartBlockers(
        string orderStatus, string handoverStatus, IReadOnlyList<string> openDependencies, IReadOnlyList<string> undonePrerequisites)
    {
        var blockers = new List<string>();
        if (orderStatus != OrderStatus.Approved)
        {
            blockers.Add("Sipariş onaylı olmadığı için hizmet başlatılamaz.");
        }

        if (handoverStatus != HandoverStatus.Accepted)
        {
            blockers.Add("Satış devri operasyon tarafından kabul edilmedi.");
        }

        blockers.AddRange(openDependencies.Select(d => $"Bağımlı olduğu iş tamamlanmadı: {d}"));
        blockers.AddRange(undonePrerequisites.Select(t => $"Başlangıç ön koşulu tamamlanmadı: {t}"));
        return blockers;
    }
}

/// <summary>Teslim kanıtı dosya kuralları: sınırlı boyut ve tür; içerik başlığı türle uyuşmalıdır.</summary>
public static class EvidenceRules
{
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
    };

    public static ServerMessage? Validate(string? fileName, string? contentType, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 120 || fileName.IndexOfAny(['/', '\\', '\0']) >= 0)
        {
            return OrdersMessages.Evidence.FileName;
        }

        if (content.Length == 0)
        {
            return OrdersMessages.Evidence.Empty;
        }

        if (content.Length > OrderDeliveryEvidence.MaxBytes)
        {
            return OrdersMessages.Evidence.TooBig(OrderDeliveryEvidence.MaxBytes / (1024 * 1024));
        }

        if (contentType is null || !AllowedTypes.ContainsKey(contentType.Split(';')[0].Trim()))
        {
            return OrdersMessages.Evidence.Type;
        }

        return Matches(contentType.Split(';')[0].Trim().ToLowerInvariant(), content) ? null : OrdersMessages.Evidence.Mismatch;
    }

    private static bool Matches(string type, byte[] content) => type switch
    {
        "application/pdf" => content.Length >= 5 && content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F' && content[4] == '-',
        "image/png" => content.Length >= 8 && content[0] == 0x89 && content[1] == 'P' && content[2] == 'N' && content[3] == 'G',
        "image/jpeg" => content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF,
        _ => false,
    };

    public static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
