using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Modules;
using Modulix.Core.Records;
using Modulix.Core.Teams;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Teams;

/// <summary>
/// Ekipler (Y8). Ekip üyeliği değişikliği ekibin kaydı olarak denetime yazılır (<c>members</c>: üye adları), böylece
/// "kim, ne zaman, hangi ekibe eklendi" ekibin geçmişinde görünür.
/// </summary>
internal sealed class TeamService(
    ModulixDbContext db, ITenantContextAccessor tenantAccessor, IAuditWriter audit) : ITeamService
{
    public async Task<IReadOnlyList<TeamSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var counts = await db.TeamMembers.AsNoTracking()
            .GroupBy(tm => tm.TeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TeamId, g => g.Count, cancellationToken);
        return teams.Select(t => new TeamSummary(t.Id, t.Name, t.Description, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<TeamDetail?> GetAsync(Guid teamId, CancellationToken cancellationToken)
    {
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        return team is null ? null : new TeamDetail(team.Id, team.Name, team.Description, await MembersAsync(team.Id, cancellationToken));
    }

    public async Task<TeamDetail> CreateAsync(string name, string? description, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var team = new Team(name, description);
        await EnsureUniqueNameAsync(team.Name, exceptTeamId: null, cancellationToken);
        db.Teams.Add(team);
        Audit(tenant, team.Id, AuditAction.Created, before: null, after: Snapshot(team, []));
        await db.SaveChangesAsync(cancellationToken);
        return new TeamDetail(team.Id, team.Name, team.Description, []);
    }

    public async Task<TeamDetail?> UpdateAsync(Guid teamId, string name, string? description, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team is null)
        {
            return null;
        }

        var members = await MembersAsync(team.Id, cancellationToken);
        var before = Snapshot(team, members);
        team.Rename(name);
        team.Describe(description);
        await EnsureUniqueNameAsync(team.Name, team.Id, cancellationToken);
        var after = Snapshot(team, members);
        if (!JsonNode.DeepEquals(before, after))
        {
            Audit(tenant, team.Id, AuditAction.Updated, before, after);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new TeamDetail(team.Id, team.Name, team.Description, members);
    }

    public async Task<bool> DeleteAsync(Guid teamId, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team is null)
        {
            return false;
        }

        var members = await MembersAsync(team.Id, cancellationToken);
        db.TeamMembers.RemoveRange(await db.TeamMembers.Where(tm => tm.TeamId == team.Id).ToListAsync(cancellationToken));
        db.Teams.Remove(team);
        Audit(tenant, team.Id, AuditAction.Deleted, Snapshot(team, members), after: null);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TeamDetail?> SetMembersAsync(Guid teamId, IReadOnlyCollection<Guid> membershipIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membershipIds);
        var tenant = RequireTenant();
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team is null)
        {
            return null;
        }

        var wanted = membershipIds.ToHashSet();
        await EnsureMembershipsAsync(tenant, wanted, cancellationToken);

        var before = await MembersAsync(team.Id, cancellationToken);
        var current = await db.TeamMembers.Where(tm => tm.TeamId == team.Id).ToListAsync(cancellationToken);
        db.TeamMembers.RemoveRange(current.Where(tm => !wanted.Contains(tm.MembershipId)));
        foreach (var membershipId in wanted.Except(current.Select(tm => tm.MembershipId)))
        {
            db.TeamMembers.Add(new TeamMember(team.Id, membershipId));
        }

        var after = await MembersOfAsync(wanted, cancellationToken);
        if (!before.Select(m => m.MembershipId).ToHashSet().SetEquals(wanted))
        {
            Audit(tenant, team.Id, AuditAction.Updated, Snapshot(team, before), Snapshot(team, after));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new TeamDetail(team.Id, team.Name, team.Description, after);
    }

    public async Task<IReadOnlyList<TeamSummary>?> GetMembershipTeamsAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        if (!await db.Memberships.AnyAsync(m => m.Id == membershipId && m.TenantId == tenant.TenantId, cancellationToken))
        {
            return null;
        }

        var rows = await db.TeamMembers.AsNoTracking()
            .Where(tm => tm.MembershipId == membershipId)
            .Select(tm => new { tm.Id, tm.TeamId, tm.IsPrimary })
            .ToListAsync(cancellationToken);

        // Seçilmiş birincil ekip yoksa en önce katıldığı ekip birincildir (TeamDirectory ile aynı kural).
        var primaryId = (rows.FirstOrDefault(r => r.IsPrimary) ?? rows.OrderBy(r => r.Id).FirstOrDefault())?.TeamId;
        return (await ListAsync(cancellationToken)).Where(t => rows.Any(r => r.TeamId == t.Id)).Select(t => t with { IsPrimary = t.Id == primaryId }).ToList();
    }

    public async Task<bool> SetMembershipTeamsAsync(Guid membershipId, IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(teamIds);
        var tenant = RequireTenant();
        if (!await db.Memberships.AnyAsync(m => m.Id == membershipId && m.TenantId == tenant.TenantId, cancellationToken))
        {
            return false;
        }

        var wanted = teamIds.ToHashSet();
        var teams = await db.Teams.AsNoTracking().Where(t => wanted.Contains(t.Id)).ToListAsync(cancellationToken);
        var missing = wanted.Except(teams.Select(t => t.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException("Bu firmada bulunmayan ekip seçildi.", nameof(teamIds));
        }

        var current = await db.TeamMembers.Where(tm => tm.MembershipId == membershipId).ToListAsync(cancellationToken);
        var changedTeams = current.Select(tm => tm.TeamId).Where(id => !wanted.Contains(id))
            .Concat(wanted.Except(current.Select(tm => tm.TeamId)))
            .ToHashSet();
        if (changedTeams.Count == 0)
        {
            return true;
        }

        // Her ekibin üye listesi değişti; denetim kaydı ekip başına yazılır.
        var allTeams = await db.Teams.AsNoTracking().Where(t => changedTeams.Contains(t.Id)).ToListAsync(cancellationToken);
        var befores = new Dictionary<Guid, IReadOnlyList<TeamMemberSummary>>();
        foreach (var team in allTeams)
        {
            befores[team.Id] = await MembersAsync(team.Id, cancellationToken);
        }

        db.TeamMembers.RemoveRange(current.Where(tm => !wanted.Contains(tm.TeamId)));
        foreach (var teamId in wanted.Except(current.Select(tm => tm.TeamId)))
        {
            db.TeamMembers.Add(new TeamMember(teamId, membershipId));
        }

        foreach (var team in allTeams)
        {
            var ids = befores[team.Id].Select(m => m.MembershipId).ToHashSet();
            if (wanted.Contains(team.Id))
            {
                ids.Add(membershipId);
            }
            else
            {
                ids.Remove(membershipId);
            }

            Audit(tenant, team.Id, AuditAction.Updated, Snapshot(team, befores[team.Id]), Snapshot(team, await MembersOfAsync(ids, cancellationToken)));
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetPrimaryTeamAsync(Guid membershipId, Guid? teamId, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        if (!await db.Memberships.AnyAsync(m => m.Id == membershipId && m.TenantId == tenant.TenantId, cancellationToken))
        {
            return false;
        }

        var rows = await db.TeamMembers.Where(tm => tm.MembershipId == membershipId).ToListAsync(cancellationToken);
        if (teamId is { } wanted && rows.All(r => r.TeamId != wanted))
        {
            throw new ArgumentException("Birincil ekip, kişinin üyesi olduğu ekiplerden biri olmalı.", nameof(teamId));
        }

        var previous = rows.FirstOrDefault(r => r.IsPrimary)?.TeamId;
        if (previous == teamId)
        {
            return true;
        }

        // Tekil dizin üyelik başına tek işaretli satıra izin verir: önce eski işaret kaldırılıp kaydedilir.
        foreach (var row in rows.Where(r => r.IsPrimary))
        {
            row.SetPrimary(false);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (teamId is { } chosen)
        {
            rows.Single(r => r.TeamId == chosen).SetPrimary(true);
        }

        var name = await (from membership in db.Memberships.AsNoTracking()
                          join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
                          where membership.Id == membershipId
                          select user.DisplayName).SingleAsync(cancellationToken);
        foreach (var (id, before, after) in new[] { (previous, name, (string?)null), (teamId, (string?)null, name) })
        {
            if (id is { } changedTeam)
            {
                Audit(tenant, changedTeam, AuditAction.Updated, new JsonObject { ["primaryFor"] = before }, new JsonObject { ["primaryFor"] = after });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<TeamMemberSummary>> MembersAsync(Guid teamId, CancellationToken cancellationToken) =>
        await MembersOfAsync(
            (await db.TeamMembers.AsNoTracking().Where(tm => tm.TeamId == teamId).Select(tm => tm.MembershipId).ToListAsync(cancellationToken)).ToHashSet(),
            cancellationToken);

    private async Task<IReadOnlyList<TeamMemberSummary>> MembersOfAsync(IReadOnlySet<Guid> membershipIds, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant().TenantId;
        return await (
            from membership in db.Memberships.AsNoTracking()
            join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.TenantId == tenantId && membershipIds.Contains(membership.Id)
            orderby user.DisplayName
            select new TeamMemberSummary(membership.Id, user.Id, user.DisplayName, user.Email!))
            .ToListAsync(cancellationToken);
    }

    // Üyelikler platform tablosundadır (RLS dışı); tenant'a ait oldukları burada açıkça denetlenir.
    private async Task EnsureMembershipsAsync(TenantContext tenant, IReadOnlySet<Guid> membershipIds, CancellationToken cancellationToken)
    {
        var found = await db.Memberships
            .Where(m => m.TenantId == tenant.TenantId && membershipIds.Contains(m.Id))
            .CountAsync(cancellationToken);
        if (found != membershipIds.Count)
        {
            throw new ArgumentException("Bu firmada bulunmayan kullanıcı seçildi.", "membershipIds");
        }
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? exceptTeamId, CancellationToken cancellationToken)
    {
        if (await db.Teams.AnyAsync(t => t.Name == name && t.Id != exceptTeamId, cancellationToken))
        {
            throw new ArgumentException($"Bu adda bir ekip zaten var: '{name}'.", nameof(name));
        }
    }

    private TenantContext RequireTenant() =>
        tenantAccessor.Current ?? throw new InvalidOperationException("Aktif tenant olmadan ekipler yönetilemez.");

    private void Audit(TenantContext tenant, Guid teamId, AuditAction action, JsonObject? before, JsonObject? after) =>
        audit.Add(new AuditEntry(
            tenant.TenantId,
            tenant.UserId,
            CoreModule.AuditedEntityKeys.Team,
            teamId,
            action,
            RecordDiff.Compute(before, after),
            ExecutionSource.Api,
            Activity.Current?.TraceId.ToString()));

    private static JsonObject Snapshot(Team team, IEnumerable<TeamMemberSummary> members) => new()
    {
        ["name"] = team.Name,
        ["description"] = team.Description,
        ["members"] = new JsonArray([.. members.Select(m => m.DisplayName).Order(StringComparer.Ordinal).Select(n => JsonValue.Create(n))]),
    };
}
