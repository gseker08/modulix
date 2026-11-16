using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulix.Core.Contracts.Files;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Files;
using Modulix.Core.Modules;
using Modulix.Core.Records;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Files;

/// <summary>
/// Dosya yaşam döngüsü temizliği (ADR-0040 dilim 3). İki parça:
/// <list type="bullet">
/// <item>Silinen kaydın dosyaları (<see cref="IRecordFileCleanup"/>): kayıt silindi olayının abonesi çağırır; üst veri silinir ve denetim kaydına
/// yazılır, içerik yetim kalır.</item>
/// <item>Yetim içerik (<see cref="IOrphanBlobSweeper"/>): üst verisi olmayan eski içerik depodan silinir. Hem silinen kaydın içeriğini hem de üst
/// verisi kaydedilemeyen ya da içeriği silinemeyen yüklemeleri temizler.</item>
/// </list>
/// Her ikisi de aktif tenant'ın bağlamında çalışır (tenant süzgeci ve RLS); başka tenant'ın dosyasına dokunamaz.
/// </summary>
internal sealed class FileCleanup(
    ModulixDbContext db,
    IBlobStorage blobs,
    ITenantContextAccessor tenantAccessor,
    IAuditWriter audit,
    TimeProvider time,
    ILogger<FileCleanup> logger) : IRecordFileCleanup, IOrphanBlobSweeper
{
    public async Task<int> RemoveForRecordAsync(RecordRef record, CancellationToken cancellationToken)
    {
        var tenant = tenantAccessor.Current ?? throw new InvalidOperationException("Aktif tenant yok.");
        var files = await db.StoredFiles.Where(f => f.EntityKey == record.EntityKey && f.RecordId == record.Id).ToListAsync(cancellationToken);
        if (files.Count == 0)
        {
            return 0;
        }

        foreach (var file in files)
        {
            db.StoredFiles.Remove(file);
            var changes = RecordDiff.Compute(new JsonObject
            {
                ["fileName"] = file.FileName,
                ["contentType"] = file.ContentType,
                ["sizeBytes"] = file.SizeBytes,
                ["sha256"] = file.Sha256,
                ["entityKey"] = record.EntityKey,
                ["recordId"] = record.Id.ToString(),
            }, null);
            audit.Add(new AuditEntry(tenant.TenantId, tenant.UserId, CoreModule.AuditedEntityKeys.File, file.Id, AuditAction.Deleted, changes,
                ExecutionSource.BackgroundJob, CorrelationId: null));
        }

        await db.SaveChangesAsync(cancellationToken);
        return files.Count;
    }

    public async Task<int> SweepAsync(TimeSpan grace, CancellationToken cancellationToken)
    {
        var tenant = tenantAccessor.Current ?? throw new InvalidOperationException("Aktif tenant yok.");
        var cutoff = time.GetUtcNow() - grace;

        // Önce depo listelenir, sonra üst veri okunur: listeden sonra yüklenen dosyanın içeriği listede yoktur, üst verisi okunmadan önce yazılan
        // dosyanınki ise kümede vardır; ikisi de silinmez.
        List<StoredBlob> candidates = [];
        await foreach (var blob in blobs.ListAsync(tenant.TenantId, cancellationToken))
        {
            if (blob.LastModified < cutoff)
            {
                candidates.Add(blob);
            }
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        var known = (await db.StoredFiles.AsNoTracking().Select(f => f.StorageKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        // Denetim kaydı arşivinin dosyaları da bilinen içeriktir (ADR-0052); üst verisi core.AuditArchives'dedir.
        known.UnionWith(await db.AuditArchives.AsNoTracking().Select(a => a.StorageKey).ToListAsync(cancellationToken));
        var removed = 0;
        foreach (var orphan in candidates.Where(b => !known.Contains(b.Key)))
        {
            try
            {
                await blobs.DeleteAsync(tenant.TenantId, orphan.Key, cancellationToken);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Silinemeyen içerik sonraki turda yeniden denenir.
                logger.LogWarning(exception, "Tenant {TenantId} yetim içeriği silinemedi.", tenant.TenantId);
            }
        }

        return removed;
    }
}
