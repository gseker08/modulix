using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Customization;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Customization;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Customization;

/// <summary>Aktif tenant'ın profilinin önerdiği katalog kalemleri; profil anahtarı platform tablosundan, kalemler koddaki profil tanımından gelir.</summary>
internal sealed class SectorCatalogSuggestions(ModulixDbContext db, ITenantContextAccessor tenantAccessor, SectorProfileCatalog profiles) : ISectorCatalogSuggestions
{
    public async Task<IReadOnlyList<CatalogSuggestion>> ListAsync(CancellationToken cancellationToken)
    {
        var tenantId = (tenantAccessor.Current ?? throw new InvalidOperationException("Aktif firma yok.")).TenantId;
        var key = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.ProfileKey).SingleOrDefaultAsync(cancellationToken);
        return profiles.Find(key)?.CatalogItems ?? [];
    }
}
