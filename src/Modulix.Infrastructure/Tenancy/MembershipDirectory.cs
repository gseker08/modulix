using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Organization;
using Modulix.Core.Platform;
using Modulix.Infrastructure.Authorization;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Tenancy;

/// <summary>
/// ADR-0020: platform verisi (üyelik, tenant) her istekte okunur; tenant verisi (erişilebilir şirketler) yalnızca aktif
/// tenant varken. Tenant DbContext'i bu yüzden tembel çözülür: aktif tenant olmayan akışlarda (ör. giriş) kurulmaz.
/// Her istekte sorulan etkin üyelik ve erişilebilir şirketler kısa süre önbellekte tutulur (ADR-0048); destek üyeliği, süresi
/// dolunca hemen düşmesi için önbelleğe girmez.
/// </summary>
internal sealed class MembershipDirectory(ModulixDbContext db, TimeProvider time, AccessCache cache, ITenantContextAccessor tenantAccessor)
    : IMembershipDirectory
{
    public async Task<IReadOnlyList<MembershipSummary>> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return await (
            from membership in db.Memberships.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
            where membership.UserId == userId
                  && (membership.Status == MembershipStatus.Active || (membership.Status == MembershipStatus.Support && db.SupportAccessGrants.Any(g => g.MembershipId == membership.Id && g.EndedAt == null && g.ExpiresAt > now)))
                  && TenantStatusRules.Operational.Contains(tenant.Status)
            orderby tenant.Name
            select new MembershipSummary(membership.Id, tenant.Id, tenant.Name, tenant.Slug))
            .ToListAsync(cancellationToken);
    }

    public async Task<MembershipSummary?> FindActiveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        // Yalnızca etkin (destek olmayan) üyelik saklanır; üyeliği olmayan ve destek üyeliği her istekte veritabanına sorulur.
        return await cache.GetOrLoadAsync(
            "membership", tenantId, userId, db, () => ReadActiveAsync(userId, tenantId, cancellationToken), store: found => found is { IsSupport: false });
    }

    private Task<MembershipSummary?> ReadActiveAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return (
            from membership in db.Memberships.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
            where membership.UserId == userId
                  && membership.TenantId == tenantId
                  && (membership.Status == MembershipStatus.Active || (membership.Status == MembershipStatus.Support && db.SupportAccessGrants.Any(g => g.MembershipId == membership.Id && g.EndedAt == null && g.ExpiresAt > now)))
                  && TenantStatusRules.Operational.Contains(tenant.Status)
            select new MembershipSummary(membership.Id, tenant.Id, tenant.Name, tenant.Slug, membership.Status == MembershipStatus.Support))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task RecordSignInAsync(Guid membershipId, CancellationToken cancellationToken) =>
        db.Memberships
            .Where(m => m.Id == membershipId)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.LastSignInAt, time.GetUtcNow()), cancellationToken);

    public Task<CompanyAccess> GetAccessibleCompaniesAsync(Guid membershipId, CancellationToken cancellationToken) =>
        tenantAccessor.Current is { } tenant
            ? cache.GetOrLoadAsync("companies", tenant.TenantId, membershipId, db, () => ReadAccessibleCompaniesAsync(membershipId, cancellationToken))
            : ReadAccessibleCompaniesAsync(membershipId, cancellationToken);

    private async Task<CompanyAccess> ReadAccessibleCompaniesAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        // Tenant verisi: yalnızca aktif tenant varken çağrılır (CompanyContextMiddleware). Tenant DbContext'i burada çözülür.

        // Yönetici (sistem rolü, ADR-0008 kararı) tenant'ın bütün şirketlerine erişir. Roller tenant verisidir: RLS yalnızca
        // aktif tenant'ınkileri döndürür.
        var isSupport = await db.Memberships.AsNoTracking().AnyAsync(m => m.Id == membershipId && m.Status == MembershipStatus.Support, cancellationToken);
        var isAdministrator = await (
            from assignment in db.MembershipRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
            where assignment.MembershipId == membershipId && role.IsSystem
            select assignment.Id)
            .AnyAsync(cancellationToken);

        // Şirketler RLS altındadır: sorgu yalnızca aktif tenant'ın şirketlerini döndürür. Yönetici hepsini; diğerleri yalnızca
        // görevlendirildikleri şirketleri görür. Geçici vekâlet (ADR-0008, adım 5): yalnızca o an geçerli görevler sayılır,
        // böylece süresi dolan ya da henüz başlamamış vekâlet erişilebilir şirketlerden kendiliğinden düşer.
        var now = time.GetUtcNow();
        IQueryable<Company> companies = isAdministrator || isSupport
            ? db.Companies.AsNoTracking()
            : from company in db.Companies.AsNoTracking()
              join membershipCompany in db.MembershipCompanies.AsNoTracking() on company.Id equals membershipCompany.CompanyId
              where membershipCompany.MembershipId == membershipId
                    && (membershipCompany.ValidFrom == null || membershipCompany.ValidFrom <= now)
                    && (membershipCompany.ValidUntil == null || membershipCompany.ValidUntil > now)
              select company;

        var summaries = await companies
            .OrderBy(c => c.Name)
            .Select(c => new CompanySummary(c.Id, c.Name, c.IsDefault))
            .ToListAsync(cancellationToken);

        return new CompanyAccess(summaries, isAdministrator);
    }
}
