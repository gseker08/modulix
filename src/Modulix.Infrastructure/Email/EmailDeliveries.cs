using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Notifications;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Email;

/// <summary><see cref="IEmailDeliveries"/> uygulaması: kuyruk satırlarını aktif tenant ve gönderim anahtarıyla süzer (tenant süzgeci açıkça yazılır).</summary>
internal sealed class EmailDeliveries(ModulixDbContext platform, ITenantContextAccessor tenant) : IEmailDeliveries
{
    public async Task<IReadOnlyDictionary<string, EmailDeliveryState>> GetAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken)
    {
        if (tenant.Current?.TenantId is not { } tenantId || references.Count == 0)
        {
            return new Dictionary<string, EmailDeliveryState>();
        }

        var keys = references.Distinct().ToList();
        var rows = await platform.EmailMessages.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Reference != null && keys.Contains(e.Reference))
            .Select(e => new { e.Reference, e.Status, e.AttemptCount, e.LastError, e.SentAt })
            .ToListAsync(cancellationToken);

        // Aynı anahtarla birden çok e-posta varsa (beklenmez) sonuncusu kazanır; sıra veritabanına bırakılmaz.
        var result = new Dictionary<string, EmailDeliveryState>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            result[row.Reference!] = new EmailDeliveryState(Map(row.Status), row.AttemptCount, row.LastError, row.SentAt);
        }

        return result;
    }

    private static EmailDeliveryStatus Map(EmailStatus status) => status switch
    {
        EmailStatus.Sent => EmailDeliveryStatus.Sent,
        EmailStatus.Failed => EmailDeliveryStatus.Failed,
        _ => EmailDeliveryStatus.Pending,
    };
}
