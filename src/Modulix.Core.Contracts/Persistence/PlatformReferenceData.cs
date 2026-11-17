using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Modulix.Core.Contracts.Persistence;

/// <summary>
/// Modülün tenant'sız başvuru tablosu (ADR-0046 bölüm 6; ör. GTİP kataloğu, rejim profili): bütün tenant'lar için aynı, salt okunur ürün standardı verisidir.
/// <c>TenantId</c> taşımaz, Row-Level Security dışındadır ve tablosu <c>platform</c> şemasındadır. Tenant verisi dışa aktarma ve silme bu tablolara dokunmaz.
/// Modül tenant işlemlerinde tabloyu yalnızca <see cref="IModuleReferenceData"/> ile okur; yazma yalnızca platform uçlarının (<c>/api/platform</c>)
/// çağırdığı yönetim servislerinde <see cref="IPlatformReferenceStore"/> ile yapılır.
/// </summary>
public interface IPlatformReferenceEntity
{
    /// <summary>Başvuru tablolarının şeması; modül şeması değil, platform tablolarıyla aynı şemadır.</summary>
    public const string Schema = "platform";
}

/// <summary>Modülün başvuru tablolarını okuma (ADR-0046 bölüm 6): sorgular izlenmez, yazma ve kaydetme yoktur.</summary>
public interface IModuleReferenceData
{
    IQueryable<TEntity> Query<TEntity>()
        where TEntity : class, IPlatformReferenceEntity;
}

/// <summary>
/// Başvuru tablolarına yazma (ADR-0046 bölüm 6). Yalnızca platform yönetim uçlarının (<c>/api/platform</c>, <c>PlatformAccessRules</c>) çağırdığı
/// servisler kullanır; tenant işleminden çağrılmaz. Her değişiklik <see cref="Audit"/> ile platform denetim kaydına, değişiklikle aynı
/// <see cref="SaveChangesAsync"/> içinde yazılır.
/// </summary>
public interface IPlatformReferenceStore
{
    DbSet<TEntity> Set<TEntity>()
        where TEntity : class, IPlatformReferenceEntity;

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Çok satırı parça parça ekler ve her parçadan sonra izleyiciyi boşaltır (onbinlerce satır bellekte tutulmaz). Açık işlem varsa ona katılır.</summary>
    Task AddInChunksAsync<TEntity>(IEnumerable<TEntity> entities, int chunkSize, CancellationToken cancellationToken)
        where TEntity : class, IPlatformReferenceEntity;

    /// <summary>Platform denetim kaydı ekler (tenant'sız, ekleme-yalnız); bir sonraki <see cref="SaveChangesAsync"/> ile yazılır.</summary>
    void Audit(Guid actingUserId, string action, string detailsJson);
}
