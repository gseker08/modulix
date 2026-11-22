using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Authorization;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Modules;
using Modulix.Core.Records;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Authorization;

internal sealed class RoleMenuLayouts(ModulixDbContext db, ITenantContextAccessor tenantAccessor, IAuditWriter audit) : IRoleMenuLayouts
{
    public async Task<IReadOnlyList<RoleMenuEntry>?> GetAsync(Guid roleId, CancellationToken cancellationToken)
    {
        if (!await db.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId, cancellationToken))
        {
            return null;
        }

        return await LoadAsync(roleId, cancellationToken);
    }

    public async Task<IReadOnlyList<RoleMenuEntry>?> SaveAsync(
        Guid roleId, IReadOnlyCollection<RoleMenuEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var tenant = tenantAccessor.Current ?? throw new InvalidOperationException("Aktif tenant yok.");
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            return null;
        }

        if (role.IsSystem)
        {
            throw new RoleConflictException($"'{role.Name}' sistem rolüdür; yönetici menüyü her zaman tam görür.");
        }

        if (entries.Select(e => e.ItemKey).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            throw new ArgumentException("Bir menü öğesi birden fazla kez verilemez.", nameof(entries));
        }

        var rows = entries.Select(e => new RoleMenuItem(roleId, e.ItemKey, e.Order, e.Hidden)).ToList();

        var existing = await db.RoleMenuItems.Where(i => i.RoleId == roleId).ToListAsync(cancellationToken);
        var before = Snapshot(existing.Select(i => new RoleMenuEntry(i.ItemKey, i.SortOrder, i.Hidden)));
        var after = Snapshot(entries);

        // Değişmeyen düzen yazılmaz (denetim kaydı da yoktur).
        if (before.ToJsonString() == after.ToJsonString())
        {
            return await LoadAsync(roleId, cancellationToken);
        }

        db.RoleMenuItems.RemoveRange(existing);
        db.RoleMenuItems.AddRange(rows);
        audit.Add(new AuditEntry(
            tenant.TenantId,
            tenant.UserId,
            CoreModule.AuditedEntityKeys.Role,
            roleId,
            AuditAction.Updated,
            RecordDiff.Compute(new JsonObject { ["name"] = role.Name, ["menu"] = before }, new JsonObject { ["name"] = role.Name, ["menu"] = after }),
            ExecutionSource.Api,
            Activity.Current?.TraceId.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(roleId, cancellationToken);
    }

    public async Task<IReadOnlyList<RoleMenuLayoutRow>> GetForRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) =>
        roleIds.Count == 0
            ? []
            : await db.RoleMenuItems.AsNoTracking()
                .Where(i => roleIds.Contains(i.RoleId))
                .OrderBy(i => i.RoleId).ThenBy(i => i.ItemKey)
                .Select(i => new RoleMenuLayoutRow(i.RoleId, i.ItemKey, i.SortOrder, i.Hidden))
                .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<RoleMenuEntry>> LoadAsync(Guid roleId, CancellationToken cancellationToken) =>
        await db.RoleMenuItems.AsNoTracking()
            .Where(i => i.RoleId == roleId)
            .OrderBy(i => i.SortOrder == null).ThenBy(i => i.SortOrder).ThenBy(i => i.ItemKey)
            .Select(i => new RoleMenuEntry(i.ItemKey, i.SortOrder, i.Hidden))
            .ToListAsync(cancellationToken);

    // Denetim kaydında okunur biçim: "crm.companies: sıra 2", "crm.leads: gizli".
    private static JsonArray Snapshot(IEnumerable<RoleMenuEntry> entries) => new([.. entries
        .OrderBy(e => e.ItemKey, StringComparer.Ordinal)
        .Select(e => JsonValue.Create($"{e.ItemKey}: {string.Join(", ", new[] { e.Order is { } o ? $"sıra {o}" : null, e.Hidden ? "gizli" : null }.Where(x => x is not null))}".TrimEnd(' ', ':')))]);
}
