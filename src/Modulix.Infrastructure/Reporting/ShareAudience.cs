using Microsoft.EntityFrameworkCore;
using Modulix.Core.Reporting;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Reporting;

/// <summary>
/// Rapor ve pano paylaşımının grup hedeflerini (rol, ekip, iş birimi) çözen ortak yardımcı (ADR-0054 dilim 4a). Kural tek yerdedir: rapor
/// ve pano görünürlüğü aynı sorguları kullanır. Hiçbiri önbelleğe alınmaz; üyelik değişince erişim bir sonraki istekte değişir.
/// </summary>
internal static class ShareAudience
{
    /// <summary>Bir paylaşımdaki en fazla grup hedefi sayısı.</summary>
    public const int MaxGroups = 50;

    /// <summary>Üyeliğin rolleri.</summary>
    public static IQueryable<Guid> RoleIds(ModulixDbContext db, Guid membershipId) =>
        db.MembershipRoles.Where(x => x.MembershipId == membershipId).Select(x => x.RoleId);

    /// <summary>Üyeliğin ekipleri.</summary>
    public static IQueryable<Guid> TeamIds(ModulixDbContext db, Guid membershipId) =>
        db.TeamMembers.Where(x => x.MembershipId == membershipId).Select(x => x.TeamId);

    /// <summary>
    /// Üyeliğin iş birimleri ve bunların üstündeki birimler: üst birime yapılan paylaşım alt birimlerin üyelerine de açılır
    /// (birim yolu hedef birimin yoluyla başlar). Kapalı birim paylaşım vermez.
    /// </summary>
    public static IQueryable<Guid> UnitIds(ModulixDbContext db, Guid membershipId) =>
        db.BusinessUnits
            .Where(target => target.IsActive && db.BusinessUnitMembers.Any(m =>
                m.MembershipId == membershipId
                && db.BusinessUnits.Any(mine => mine.Id == m.BusinessUnitId && mine.Path.StartsWith(target.Path))))
            .Select(target => target.Id);

    /// <summary>
    /// Yazılacak grup hedeflerini doğrular: tür geçerli ve kullanıcı dışı, aynı hedef bir kez, en çok <see cref="MaxGroups"/>, hedef firmada
    /// var (kapalı iş birimi sayılmaz). Başka firmanın kaydı sorguda görünmez; bu yüzden "yok" sayılır.
    /// </summary>
    /// <param name="fail">Doğrulama hatasını modülün kendi istisnasına çevirir.</param>
    public static async Task<List<ShareGroup>> NormalizeAsync(
        ModulixDbContext db, IReadOnlyList<ShareGroup> groups, Func<string, Exception> fail, CancellationToken cancellationToken)
    {
        var wanted = groups.Distinct().ToList();
        if (wanted.Any(g => !Enum.IsDefined(g.Kind) || g.Kind == ShareTargetKind.User))
        {
            throw fail("Paylaşım hedefi rol, ekip ya da iş birimi olmalı.");
        }

        if (wanted.Count > MaxGroups)
        {
            throw fail($"Bir paylaşımda en fazla {MaxGroups} grup seçilebilir.");
        }

        var roleIds = wanted.Where(g => g.Kind == ShareTargetKind.Role).Select(g => g.Id).ToList();
        var teamIds = wanted.Where(g => g.Kind == ShareTargetKind.Team).Select(g => g.Id).ToList();
        var unitIds = wanted.Where(g => g.Kind == ShareTargetKind.BusinessUnit).Select(g => g.Id).ToList();
        var existing = new HashSet<ShareGroup>();
        if (roleIds.Count > 0)
        {
            foreach (var id in await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken))
            {
                existing.Add(new ShareGroup(ShareTargetKind.Role, id));
            }
        }

        if (teamIds.Count > 0)
        {
            foreach (var id in await db.Teams.Where(t => teamIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken))
            {
                existing.Add(new ShareGroup(ShareTargetKind.Team, id));
            }
        }

        if (unitIds.Count > 0)
        {
            foreach (var id in await db.BusinessUnits.Where(u => u.IsActive && unitIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken))
            {
                existing.Add(new ShareGroup(ShareTargetKind.BusinessUnit, id));
            }
        }

        if (wanted.Any(g => !existing.Contains(g)))
        {
            throw fail("Seçilen gruplardan biri firmada bulunamadı.");
        }

        return wanted;
    }

    /// <summary>Grup hedeflerinden birine üye olan etkin kullanıcıların kimlikleri (varsayılan pano temizliği için).</summary>
    public static async Task<HashSet<Guid>> UserIdsAsync(ModulixDbContext db, IReadOnlyList<ShareGroup> groups, CancellationToken cancellationToken)
    {
        var membershipIds = new HashSet<Guid>();
        var roleIds = groups.Where(g => g.Kind == ShareTargetKind.Role).Select(g => g.Id).ToList();
        var teamIds = groups.Where(g => g.Kind == ShareTargetKind.Team).Select(g => g.Id).ToList();
        var unitIds = groups.Where(g => g.Kind == ShareTargetKind.BusinessUnit).Select(g => g.Id).ToList();
        if (roleIds.Count > 0)
        {
            membershipIds.UnionWith(await db.MembershipRoles.Where(x => roleIds.Contains(x.RoleId)).Select(x => x.MembershipId).ToListAsync(cancellationToken));
        }

        if (teamIds.Count > 0)
        {
            membershipIds.UnionWith(await db.TeamMembers.Where(x => teamIds.Contains(x.TeamId)).Select(x => x.MembershipId).ToListAsync(cancellationToken));
        }

        if (unitIds.Count > 0)
        {
            var paths = await db.BusinessUnits.Where(u => u.IsActive && unitIds.Contains(u.Id)).Select(u => u.Path).ToListAsync(cancellationToken);
            foreach (var path in paths)
            {
                membershipIds.UnionWith(await db.BusinessUnitMembers
                    .Where(m => db.BusinessUnits.Any(u => u.Id == m.BusinessUnitId && u.Path.StartsWith(path)))
                    .Select(m => m.MembershipId).ToListAsync(cancellationToken));
            }
        }

        if (membershipIds.Count == 0)
        {
            return [];
        }

        return (await db.Memberships.Where(m => membershipIds.Contains(m.Id)).Select(m => m.UserId).ToListAsync(cancellationToken)).ToHashSet();
    }

    /// <summary>Paylaşım panelinin seçebileceği roller, ekipler ve açık iş birimleri; tür sonra ada göre.</summary>
    public static async Task<IReadOnlyList<ShareGroupOption>> OptionsAsync(ModulixDbContext db, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().Select(r => new { r.Id, r.Name }).ToListAsync(cancellationToken);
        var teams = await db.Teams.AsNoTracking().Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken);
        var units = await db.BusinessUnits.AsNoTracking().Where(u => u.IsActive).Select(u => new { u.Id, u.Name }).ToListAsync(cancellationToken);
        return
        [
            .. roles.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => new ShareGroupOption(ShareTargetKind.Role, x.Id, x.Name)),
            .. teams.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => new ShareGroupOption(ShareTargetKind.Team, x.Id, x.Name)),
            .. units.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => new ShareGroupOption(ShareTargetKind.BusinessUnit, x.Id, x.Name)),
        ];
    }
}
