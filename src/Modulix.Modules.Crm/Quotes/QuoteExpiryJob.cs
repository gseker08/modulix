using Modulix.Core.Contracts.Modules;

namespace Modulix.Modules.Crm.Quotes;

/// <summary>
/// Teklif süre dolumu zamanlanmış işi (ADR-0046): host'taki tek zamanlayıcı, CRM modülü yazılabilir olan her tenant için <see cref="IQuoteExpiry"/> turunu
/// çalıştırır. İş "crm.quote-expiry" sistem kimliğiyle ve kendi tenant kapsamında çalışır; kapsamı, lisans kontrolünü, tenant başına kilidi ve kaydı
/// (kuyruğa yazılan sorumlu e-postaları dahil) zamanlayıcı yapar. Tekrar çalıştırmak güvenlidir (bkz. <see cref="IQuoteExpiry"/>).
/// </summary>
internal sealed class QuoteExpiryJob(IQuoteExpiry expiry, TimeProvider time) : IModuleJob
{
    /// <summary>İşin anahtarı: yapılandırma (<c>Jobs:crm.quote-expiry</c>), ölçüm adı ve kilit (<c>modulix.crm.quote-expiry:&lt;tenant&gt;</c>) bu ada bağlıdır.</summary>
    public const string JobKey = "crm.quote-expiry";

    /// <summary>İşin sistem kimliği (<see cref="QuoteExpiryAnchor"/> aboneliği).</summary>
    public const string Identity = "crm.quote-expiry";

    public async Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken)
    {
        var run = await expiry.RunAsync(time.GetUtcNow(), cancellationToken);
        var parts = new List<string>();
        if (run.Expired + run.RemindersCreated + run.ApprovalRemindersSent > 0)
        {
            parts.Add($"{run.Expired} teklifin süresi doldu, {run.RemindersCreated} hatırlatma açıldı, {run.ApprovalRemindersSent} onay hatırlatması gönderildi.");
        }

        if (run.Warning is { Length: > 0 } warning)
        {
            parts.Add(warning);
        }

        return new ModuleJobResult(parts.Count > 0 ? string.Join(" ", parts) : null, run);
    }
}
