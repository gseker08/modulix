using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Platform;

/// <summary><see cref="ICurrentTenantInfo"/> uygulaması: aktif tenant'ın kısa adı ve adı platform tablosundan okunur.</summary>
internal sealed class CurrentTenantInfo(ModulixDbContext platform, ITenantContextAccessor tenant) : ICurrentTenantInfo
{
    public async Task<TenantInfo?> GetAsync(CancellationToken cancellationToken)
    {
        if (tenant.Current?.TenantId is not { } id)
        {
            return null;
        }

        return await platform.Tenants.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TenantInfo(t.Slug, t.Name))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
