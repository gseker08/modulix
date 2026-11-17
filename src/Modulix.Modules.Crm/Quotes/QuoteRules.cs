using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Crm.Quotes;

/// <summary>Teklifin ticari durumları. İç onay ve gönderim durumundan bağımsızdır.</summary>
public static class QuoteStatus
{
    public const string Draft = "Draft";
    public const string Presented = "Presented";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";

    /// <summary>Aynı ailede yeni revizyon açılınca ya da yeni sürüm sunulunca geçersizleşen sürüm.</summary>
    public const string Superseded = "Superseded";

    public static readonly IReadOnlyList<string> All = [Draft, Presented, Accepted, Rejected, Expired, Cancelled, Superseded];

    /// <summary>Artık hiçbir geçiş yapılamayan durumlar.</summary>
    public static bool IsFinal(string status) => status is Accepted or Rejected or Expired or Cancelled or Superseded;
}

/// <summary>İç onay durumu (ticari durumdan ayrı).</summary>
public static class QuoteApprovalStatus
{
    public const string NotRequired = "NotRequired";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string ChangesRequested = "ChangesRequested";

    public static readonly IReadOnlyList<string> All = [NotRequired, Pending, Approved, Rejected, ChangesRequested];
}

/// <summary>
/// Gönderim süreci. Sağlayıcının kabul etmesi (<see cref="Succeeded"/>) müşteriye teslim edildiği anlamına gelmez; teslim bilgisi
/// ayrı bir kanıt olmadan "teslim edildi" diye gösterilmez.
/// </summary>
public static class QuoteDeliveryStatus
{
    public const string NotSent = "NotSent";
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";

    public static readonly IReadOnlyList<string> All = [NotSent, Pending, Processing, Succeeded, Failed];
}

/// <summary>Kalem türü: ürün, hizmet, paket ana kalemi ya da yetkili kullanıcının serbest açıklamalı kalemi.</summary>
public static class QuoteLineKind
{
    public const string Product = "Product";
    public const string Service = "Service";
    public const string Package = "Package";
    public const string Free = "Free";

    public static readonly IReadOnlyList<string> All = [Product, Service, Package, Free];
}

public static class QuoteAction
{
    public const string SubmitForApproval = "submit-for-approval";
    public const string WithdrawApproval = "withdraw-approval";
    public const string Approve = "approve";
    public const string RejectApproval = "reject-approval";
    public const string RequestChanges = "request-changes";
    public const string Present = "present";
    public const string Accept = "accept";
    public const string Reject = "reject";
    public const string Cancel = "cancel";

    /// <summary>Sunulmuş tekliften müşteri belgesi/bağlantısı üretmek (iç eylem; ticari durumu değiştirmez).</summary>
    public const string Share = "share";
}

/// <summary>Bir eylemin neden yapılamadığını açıklayan sonuç (kullanıcıya gösterilir).</summary>
public readonly record struct QuoteRuleResult(bool Allowed, string? Code = null, ServerMessage? Message = null)
{
    public static QuoteRuleResult Ok { get; } = new(true);

    public static QuoteRuleResult Deny(string code, ServerMessage message) => new(false, code, message);
}

/// <summary>
/// Teklif geçiş kuralları (saf; veri erişimi yok). Sunucu her eylemde bunu çağırır; arayüz aynı sonucu düğme durumu ve
/// açıklama olarak kullanır. Süre kontrolü kabul anında da yapılır, zamanlanmış işe bırakılmaz.
/// </summary>
public static class QuoteRules
{
    public static QuoteRuleResult Can(string action, string status, string approval, DateTimeOffset? validUntil, DateTimeOffset now, bool hasLines)
    {
        bool expired = validUntil is { } until && until <= now;
        switch (action)
        {
            case QuoteAction.SubmitForApproval:
                if (status != QuoteStatus.Draft)
                {
                    return QuoteRuleResult.Deny("not-draft", CrmMessages.Quote.SubmitDraftOnly);
                }

                if (!hasLines)
                {
                    return QuoteRuleResult.Deny("no-lines", CrmMessages.Quote.SubmitNeedsLines);
                }

                return approval is QuoteApprovalStatus.Pending
                    ? QuoteRuleResult.Deny("already-pending", CrmMessages.Quote.AlreadyPending)
                    : QuoteRuleResult.Ok;

            case QuoteAction.WithdrawApproval:
                return status == QuoteStatus.Draft && approval == QuoteApprovalStatus.Pending
                    ? QuoteRuleResult.Ok
                    : QuoteRuleResult.Deny("not-pending", CrmMessages.Quote.NothingToWithdraw);

            case QuoteAction.Approve or QuoteAction.RejectApproval or QuoteAction.RequestChanges:
                return status == QuoteStatus.Draft && approval == QuoteApprovalStatus.Pending
                    ? QuoteRuleResult.Ok
                    : QuoteRuleResult.Deny("not-pending", CrmMessages.Quote.NotPending);

            case QuoteAction.Present:
                if (status != QuoteStatus.Draft)
                {
                    return QuoteRuleResult.Deny("not-draft", CrmMessages.Quote.PresentDraftOnly);
                }

                if (!hasLines)
                {
                    return QuoteRuleResult.Deny("no-lines", CrmMessages.Quote.PresentNeedsLines);
                }

                if (expired)
                {
                    return QuoteRuleResult.Deny("expired", CrmMessages.Quote.PresentExpired);
                }

                // Onay gerekiyorsa tamamlanmadan sunum sunucuda engellenir.
                return approval is QuoteApprovalStatus.NotRequired or QuoteApprovalStatus.Approved
                    ? QuoteRuleResult.Ok
                    : QuoteRuleResult.Deny("approval-required", CrmMessages.Quote.ApprovalRequired);

            case QuoteAction.Accept or QuoteAction.Reject:
                if (status != QuoteStatus.Presented)
                {
                    return QuoteRuleResult.Deny(StatusCode(status), StatusMessage(status));
                }

                return expired
                    ? QuoteRuleResult.Deny("expired", CrmMessages.Quote.Expired)
                    : QuoteRuleResult.Ok;

            case QuoteAction.Share:
                if (status != QuoteStatus.Presented)
                {
                    return QuoteRuleResult.Deny(StatusCode(status), status == QuoteStatus.Draft
                        ? CrmMessages.Quote.ShareDraft
                        : StatusMessage(status));
                }

                return expired
                    ? QuoteRuleResult.Deny("expired", CrmMessages.Quote.ShareExpired)
                    : QuoteRuleResult.Ok;

            case QuoteAction.Cancel:
                return status is QuoteStatus.Draft or QuoteStatus.Presented
                    ? QuoteRuleResult.Ok
                    : QuoteRuleResult.Deny(StatusCode(status), CrmMessages.Quote.CancelNotAllowed);

            default:
                return QuoteRuleResult.Deny("unknown-action", CrmMessages.Quote.UnknownAction);
        }
    }

    /// <summary>Müşteriye sunulmuş ya da sonuçlanmış sürümün ticari içeriği yerinde değiştirilemez.</summary>
    public static bool IsCommerciallyEditable(string status) => status == QuoteStatus.Draft;

    private static string StatusCode(string status) => status switch
    {
        QuoteStatus.Accepted => "already-accepted",
        QuoteStatus.Rejected => "already-rejected",
        QuoteStatus.Cancelled => "cancelled",
        QuoteStatus.Superseded => "superseded",
        QuoteStatus.Expired => "expired",
        _ => "not-presented",
    };

    private static ServerMessage StatusMessage(string status) => status switch
    {
        QuoteStatus.Accepted => CrmMessages.Quote.AlreadyAccepted,
        QuoteStatus.Rejected => CrmMessages.Quote.AlreadyRejected,
        QuoteStatus.Cancelled => CrmMessages.Quote.Cancelled,
        QuoteStatus.Superseded => CrmMessages.Quote.Superseded,
        QuoteStatus.Expired => CrmMessages.Quote.Expired,
        _ => CrmMessages.Quote.NotPresented,
    };
}

/// <summary>Revizyon ve numara kuralları (saf).</summary>
public static class QuoteRevisionRules
{
    /// <summary>
    /// Yeni revizyon açılabilir mi? Kabul edilmiş sürümün yeniden yazılması ya da iptal/geçersiz sürümden devam edilmesi
    /// ayrı bir ticari süreçtir; revizyon yalnızca sunulmuş, reddedilmiş ya da süresi dolmuş sürümden açılır. Engel varsa nedeni döner.
    /// </summary>
    public static ServerMessage? CanRevise(string status) => status switch
    {
        QuoteStatus.Presented or QuoteStatus.Rejected or QuoteStatus.Expired => null,
        QuoteStatus.Draft => CrmMessages.Quote.ReviseDraft,
        QuoteStatus.Accepted => CrmMessages.Quote.ReviseAccepted,
        _ => CrmMessages.Quote.ReviseNotAllowed,
    };
}
