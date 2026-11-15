using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulix.Core.Authorization;
using Modulix.Core.Modules;
using Modulix.Core.Organization;
using Modulix.Core.Platform;
using Modulix.Core.Settings;

namespace Modulix.Infrastructure.Authorization;

/// <summary>
/// Yetki önbelleğini (<see cref="AccessCache"/>, ADR-0048) tek noktadan geçersiz kılar: kaydedilen değişiklikler arasında yetkiyi
/// etkileyen bir tür varsa o tenant'ın girdileri boşaltılır. Uç noktaları tek tek bilmez; yeni bir yazma ucu kendiliğinden kapsanır.
/// Açık işlem içindeki kayıt, işlem sonlanınca bir kez daha geçersiz kılar (işlem sürerken başka istek eski değeri okuyup saklamış
/// olabilir). Bu tablolara <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> ya da ham SQL ile yazan kod bu yolu atlar; değişiklik en geç
/// <see cref="AccessCache.Ttl"/> içinde görünür. Bağlam başına bir örnektir (bekleyen tenant'ları tutar).
/// </summary>
internal sealed class AccessCacheInvalidator(AccessCache cache) : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    /// <summary>Önbellekteki verinin okunduğu tablolar; buraya eklenmeyen türün değişmesi önbelleği boşaltmaz.</summary>
    internal static readonly IReadOnlySet<Type> WatchedTypes = new HashSet<Type>
    {
        typeof(Tenant),
        typeof(Membership),
        typeof(SupportAccessGrant),
        typeof(ModuleEntitlement),
        typeof(TenantSetting),
        typeof(TenantScopeLimit),
        typeof(Role),
        typeof(MembershipRole),
        typeof(RoleScopeOverride),
        typeof(Company),
        typeof(MembershipCompany),
        typeof(BusinessUnit),
        typeof(BusinessUnitMember),
        typeof(BusinessUnitRole),
    };

    private readonly HashSet<Guid> pendingTenants = [];
    private bool pendingAll;

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AfterSave(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        AfterSave(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => AfterSave(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        AfterSave(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Flush(clear: true);

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Flush(clear: true);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Flush(clear: true);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Flush(clear: true);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is (EntityState.Added or EntityState.Modified or EntityState.Deleted) && WatchedTypes.Contains(entry.Metadata.ClrType))
            {
                if (TenantOf(entry) is { } tenantId)
                {
                    pendingTenants.Add(tenantId);
                }
                else
                {
                    pendingAll = true;
                }
            }
        }
    }

    /// <summary>Kayıt bitti: hemen geçersiz kıl; açık işlem varsa işlem sonlanınca bir kez daha (bekleyenler orada temizlenir).</summary>
    private void AfterSave(DbContext? context) => Flush(clear: context?.Database.CurrentTransaction is null);

    private void Flush(bool clear)
    {
        if (pendingAll)
        {
            cache.InvalidateAll();
        }

        foreach (var tenantId in pendingTenants)
        {
            cache.Invalidate(tenantId);
        }

        if (clear)
        {
            pendingTenants.Clear();
            pendingAll = false;
        }
    }

    private static Guid? TenantOf(EntityEntry entry)
    {
        if (entry.Entity is Tenant tenant)
        {
            return tenant.Id;
        }

        return entry.Metadata.FindProperty("TenantId") is { } property && entry.Property(property.Name).OriginalValue is Guid tenantId && tenantId != Guid.Empty
            ? tenantId
            : null;
    }
}
