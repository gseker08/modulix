using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Teams;

/// <summary>
/// <see cref="ITeamDirectory"/> uygulaması: ekipleri ve kullanıcının birincil ekibini salt okunur verir. Sorgular tenant süzgeci ve Row-Level Security
/// altındadır; aktif tenant yoksa sonuç boştur. Birincil ekip, kullanıcının en önce katıldığı ekiptir (üyelik kimliği zaman sıralıdır, Guid v7).
/// </summary>
internal sealed class TeamDirectory(ModulixDbContext db, ITenantContextAccessor tenant) : ITeamDirectory
{
    public async Task<IReadOnlyList<TeamInfo>> ListAsync(CancellationToken cancellationToken)
    {
        if (tenant.Current is null)
        {
            return [];
        }

        var teams = await db.Teams.AsNoTracking().Select(t => new TeamInfo(t.Id, t.Name)).ToListAsync(cancellationToken);
        return [.. teams.OrderBy(t => t.Name, StringComparer.CurrentCulture)];
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> PrimaryTeamsOfUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (tenant.Current is not { } current || userIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        Guid[] wanted = [.. userIds.Distinct()];
        var rows = await (
                from member in db.TeamMembers.AsNoTracking()
                join membership in db.Memberships.AsNoTracking() on member.MembershipId equals membership.Id
                where membership.TenantId == current.TenantId && wanted.Contains(membership.UserId)
                select new { membership.UserId, member.TeamId, member.Id })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).First().TeamId);
    }
}
