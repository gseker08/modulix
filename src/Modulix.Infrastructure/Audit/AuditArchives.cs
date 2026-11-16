using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulix.Core.Audit;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Files;
using Modulix.Core.Modules;
using Modulix.Core.Records;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Audit;

/// <summary>
/// <see cref="IAuditArchives"/> uygulaması (ADR-0052 dilim 3). Satırlar EF süzgeci ve RLS ile aktif tenant'a sınırlıdır; dosya tenant'ın depo kökünden
/// açılır. İndirme <c>Exported</c> eylemiyle denetim kaydına yazılır (dönem, satır sayısı, özet). Uygulama rolü arşiv satırını yazamaz; buradaki tek
/// yazma denetim kaydıdır.
/// </summary>
internal sealed partial class AuditArchives(
    ModulixDbContext db,
    IBlobStorage blobs,
    ITenantContextAccessor tenantAccessor,
    IAuditWriter audit,
    ILogger<AuditArchives> logger) : IAuditArchives
{
    public async Task<IReadOnlyList<AuditArchiveItem>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.AuditArchives.AsNoTracking().OrderByDescending(a => a.PeriodEnd).ToListAsync(cancellationToken);
        return rows.Select(ToItem).ToList();
    }

    public async Task<AuditArchiveDownload?> OpenAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenant = tenantAccessor.Current ?? throw new InvalidOperationException("Aktif tenant olmadan denetim kaydı arşivi açılamaz.");
        if (await db.AuditArchives.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) is not { } entry)
        {
            return null;
        }

        var content = await blobs.OpenReadAsync(tenant.TenantId, entry.StorageKey, cancellationToken);
        if (content is null)
        {
            LogMissingFile(logger, entry.Id, tenant.TenantId);
            return null;
        }

        var item = ToItem(entry);
        audit.Add(new AuditEntry(
            tenant.TenantId, tenant.UserId, CoreModule.AuditedEntityKeys.AuditArchive, entry.Id, AuditAction.Exported,
            new JsonObject
            {
                ["period"] = item.PeriodText,
                ["rows"] = entry.RowCount,
                ["bytes"] = entry.SizeBytes,
                ["sha256"] = entry.Sha256,
            },
            ExecutionSource.Api, null));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }

        return new AuditArchiveDownload(item, content);
    }

    private static AuditArchiveItem ToItem(AuditArchiveEntry entry) => new(
        entry.Id,
        entry.PeriodStart == DateTimeOffset.MinValue ? null : entry.PeriodStart,
        entry.PeriodEnd,
        entry.RowCount,
        entry.SizeBytes,
        entry.Sha256,
        entry.ArchivedAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Denetim kaydı arşivi {ArchiveId} için dosya depoda yok (tenant {TenantId}).")]
    private static partial void LogMissingFile(ILogger logger, Guid archiveId, Guid tenantId);
}
