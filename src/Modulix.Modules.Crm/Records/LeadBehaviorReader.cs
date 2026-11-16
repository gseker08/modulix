using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Leads;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Müşteri adayı davranış sayaçlarını (<see cref="LeadBehavior"/>, ADR-0050 dilim 2) CRM'in kendi tablolarından türetir. Salt okunurdur; puan bir
/// sunucu kararı olduğu için okuma çağıranın kapsamına bağlı değildir, ama özel aktiviteler sayılmaz (puan varlıklarını sezdirmesin).
/// </summary>
internal sealed class LeadBehaviorReader(IModuleData data)
{
    private const string MeetingType = "Meeting";

    /// <summary>Tek adayın sayaçları (aday kaydedilirken).</summary>
    public async Task<LeadBehavior> ForLeadAsync(Lead lead, CancellationToken cancellationToken)
    {
        List<ContactRow> activities = await Rows(CompletedActivities().Where(a => a.LeadId == lead.Id)).ToListAsync(cancellationToken);
        int repeats = await data.Set<Lead>().AsNoTracking()
            .Where(l => l.Id != lead.Id
                && ((lead.NormalizedEmail != null && l.NormalizedEmail == lead.NormalizedEmail)
                    || (lead.NormalizedPhone != null && l.NormalizedPhone == lead.NormalizedPhone)))
            .CountAsync(cancellationToken);
        return Count(activities, repeats);
    }

    /// <summary>
    /// Tenant'ın bütün adaylarının sayaçları (toplu yeniden puanlama). Aday kimliği listesiyle süzülmez (SQL parametre sınırı); tamamlanmış aday
    /// aktiviteleri ve adayların iletişim anahtarları birer sorguyla okunur, sayım bellekte yapılır. Listede olmayan adayın sayaçları sıfırdır.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, LeadBehavior>> ForTenantAsync(CancellationToken cancellationToken)
    {
        List<ContactRow> activities = await Rows(CompletedActivities()).ToListAsync(cancellationToken);
        var keys = await data.Set<Lead>().AsNoTracking()
            .Where(l => l.NormalizedEmail != null || l.NormalizedPhone != null)
            .Select(l => new { l.Id, l.NormalizedEmail, l.NormalizedPhone })
            .ToListAsync(cancellationToken);

        ILookup<string, Guid> byEmail = keys.Where(k => k.NormalizedEmail != null).ToLookup(k => k.NormalizedEmail!, k => k.Id);
        ILookup<string, Guid> byPhone = keys.Where(k => k.NormalizedPhone != null).ToLookup(k => k.NormalizedPhone!, k => k.Id);
        Dictionary<Guid, int> repeats = [];
        foreach (var key in keys)
        {
            IEnumerable<Guid> same = (key.NormalizedEmail is { } email ? byEmail[email] : [])
                .Concat(key.NormalizedPhone is { } phone ? byPhone[phone] : []);
            int count = same.Where(id => id != key.Id).Distinct().Count();
            if (count > 0)
            {
                repeats[key.Id] = count;
            }
        }

        Dictionary<Guid, LeadBehavior> result = [];
        foreach (IGrouping<Guid, ContactRow> group in activities.GroupBy(a => a.LeadId))
        {
            result[group.Key] = Count([.. group], repeats.GetValueOrDefault(group.Key));
        }

        foreach ((Guid id, int count) in repeats.Where(r => !result.ContainsKey(r.Key)))
        {
            result[id] = new LeadBehavior(0, 0, count);
        }

        return result;
    }

    private IQueryable<Activity> CompletedActivities() =>
        data.Set<Activity>().AsNoTracking().Where(a => a.LeadId != null && a.CompletedAt != null && !a.IsPrivate);

    // Projeksiyon en sonda: süzgeçler entity üzerinde kalsın ki SQL'e çevrilsin.
    private static IQueryable<ContactRow> Rows(IQueryable<Activity> source) =>
        source.Select(a => new ContactRow(a.LeadId!.Value, a.Type, a.Outcome));

    private static LeadBehavior Count(IReadOnlyCollection<ContactRow> activities, int repeats)
    {
        List<ContactRow> contacts = [.. activities.Where(a => LeadRules.CountsAsContact(a.Type, a.Outcome, completed: true))];
        return new LeadBehavior(contacts.Count, contacts.Count(a => a.Type == MeetingType), repeats);
    }

    private sealed record ContactRow(Guid LeadId, string Type, string? Outcome);
}
