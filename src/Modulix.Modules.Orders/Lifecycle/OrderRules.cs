using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Orders.Lifecycle;

// Sipariş durum sabitleri ve saf kurallar (ADR-0023). Kurallar sunucuda uygulanır; arayüz yalnızca aynı kuralları gösterir.

/// <summary>Ticari sipariş durumu. İç onay ve müşteri kabulünden ayrıdır: müşteri kabulü tekliftedir, burada siparişin kendi onayı vardır.</summary>
public static class OrderStatus
{
    public const string Draft = "Draft";
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Cancelled = "Cancelled";
    public const string Closed = "Closed";

    public static readonly IReadOnlyList<string> All = [Draft, PendingApproval, Approved, Cancelled, Closed];

    /// <summary>Son durumlar: sipariş bir daha değişmez.</summary>
    public static bool IsFinal(string status) => status is Cancelled or Closed;
}

/// <summary>Satış devri durumu (operasyon ekibine devir). Ticari durumdan ayrı izlenir.</summary>
public static class HandoverStatus
{
    public const string NotStarted = "NotStarted";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Sent = "Sent";
    public const string InfoRequested = "InfoRequested";
    public const string Accepted = "Accepted";

    public static readonly IReadOnlyList<string> All = [NotStarted, Preparing, Ready, Sent, InfoRequested, Accepted];
}

/// <summary>Gerçekleşme durumu: kalem teslimatları ve hizmet başlangıçlarından türetilir; elle yazılmaz.</summary>
public static class FulfillmentStatus
{
    public const string NotStarted = "NotStarted";
    public const string Planned = "Planned";
    public const string Partial = "Partial";
    public const string Completed = "Completed";

    public static readonly IReadOnlyList<string> All = [NotStarted, Planned, Partial, Completed];
}

public static class ConversionMode
{
    public const string Full = "Full";
    public const string Partial = "Partial";

    public static readonly IReadOnlyList<string> All = [Full, Partial];
}

public static class AcceptanceMethods
{
    public const string Portal = "Portal";
    public const string Manual = "Manual";
}

public static class LineKinds
{
    public const string Product = "Product";
    public const string Service = "Service";
    public const string Package = "Package";
    public const string Free = "Free";

    /// <summary>Teklifteki masraf kalemi: teslim edilmez, hizmet gibi başlangıçla izlenir.</summary>
    public const string Expense = "Expense";

    public static readonly IReadOnlyList<string> All = [Product, Service, Package, Free, Expense];

    /// <summary>Fiziksel teslimat gerektiren kalem: ürün. Hizmet, paket ve serbest kalem teslim miktarı değil başlangıçla izlenir.</summary>
    public static bool IsDeliverable(string kind) => kind == Product;

    public static bool IsService(string kind) => kind is Service or Package or Free or Expense;
}

/// <summary>Tarihçe türleri. Yeni tür eklemek serbesttir; eski satırlar aynen okunur.</summary>
public static class OrderEventKinds
{
    public const string CreatedFromQuote = "CreatedFromQuote";
    public const string SubmittedForApproval = "SubmittedForApproval";
    public const string ApprovalWithdrawn = "ApprovalWithdrawn";
    public const string Approved = "Approved";
    public const string ReturnedToDraft = "ReturnedToDraft";
    public const string Cancelled = "Cancelled";
    public const string Closed = "Closed";
    public const string ForceClosed = "ForceClosed";
    public const string ChangeRequested = "ChangeRequested";
    public const string ChangeApplied = "ChangeApplied";
    public const string ChangeRejected = "ChangeRejected";
    public const string ChangeWithdrawn = "ChangeWithdrawn";
    public const string HandoverChanged = "HandoverChanged";
}

public readonly record struct OrderRuleResult(bool Allowed, string? Code = null, ServerMessage? Message = null)
{
    public static OrderRuleResult Ok { get; } = new(true);

    public static OrderRuleResult Deny(string code, ServerMessage message) => new(false, code, message);
}

public static class OrderRules
{
    /// <summary>
    /// Ticari alanlar yalnızca taslakta düzenlenir. Onay bekleyen ya da onaylı sipariş yerinde değişmez: gerekçeli değişiklik talebi
    /// (sonraki dilim) ya da taslağa geri alma kullanılır.
    /// </summary>
    public static OrderRuleResult CanEditContent(string status) =>
        status == OrderStatus.Draft
            ? OrderRuleResult.Ok
            : OrderRuleResult.Deny("locked", status switch
            {
                OrderStatus.PendingApproval => OrdersMessages.Transition.EditPending,
                OrderStatus.Approved => OrdersMessages.Transition.EditApproved,
                _ => OrdersMessages.Transition.EditFinal,
            });
}
