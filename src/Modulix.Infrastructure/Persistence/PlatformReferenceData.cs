using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Modulix.Core.Contracts.Persistence;
using Modulix.Core.Platform;

namespace Modulix.Infrastructure.Persistence;

/// <summary>Modül başvuru tablolarının okunması (ADR-0046 bölüm 6); isteğin <see cref="ModulixDbContext"/>'ini paylaşır.</summary>
internal sealed class ModuleReferenceData(ModulixDbContext db) : IModuleReferenceData
{
    public IQueryable<TEntity> Query<TEntity>()
        where TEntity : class, IPlatformReferenceEntity => db.Set<TEntity>().AsNoTracking();
}

/// <summary>Modül başvuru tablolarına platform yönetiminden yazma (ADR-0046 bölüm 6); denetim kaydı <c>PlatformAuditLog</c>'a aynı kaydetmede girer.</summary>
internal sealed class PlatformReferenceStore(ModulixDbContext db) : IPlatformReferenceStore
{
    public DbSet<TEntity> Set<TEntity>()
        where TEntity : class, IPlatformReferenceEntity => db.Set<TEntity>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => db.Database.BeginTransactionAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task AddInChunksAsync<TEntity>(IEnumerable<TEntity> entities, int chunkSize, CancellationToken cancellationToken)
        where TEntity : class, IPlatformReferenceEntity
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            foreach (var chunk in entities.Chunk(chunkSize))
            {
                db.Set<TEntity>().AddRange(chunk);
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    public void Audit(Guid actingUserId, string action, string detailsJson) =>
        db.PlatformAuditLog.Add(new PlatformAuditEntry(actingUserId, action, null, null, detailsJson, Activity.Current?.TraceId.ToString()));
}
