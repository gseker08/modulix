using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Records;

/// <summary><see cref="IRecordExportAudit"/> uygulaması: denetim yazıcısı ve iş birimiyle tek kayıt yazar.</summary>
public sealed class RecordExportAudit(ITenantContextAccessor tenant, IAuditWriter audit, IUnitOfWork unitOfWork) : IRecordExportAudit
{
    public async Task RecordAsync(
        string entityKey, int count, IReadOnlyDictionary<string, string>? filters, JsonObject? details = null, CancellationToken cancellationToken = default)
    {
        if (tenant.Current is not { } current)
        {
            return;
        }

        var changes = new JsonObject { ["count"] = count };
        foreach (var (key, value) in details ?? [])
        {
            changes[key] = value?.DeepClone();
        }

        changes["filters"] = string.Join('&', (filters ?? new Dictionary<string, string>()).Select(f => $"{f.Key}={f.Value}"));
        audit.Add(new AuditEntry(current.TenantId, current.UserId, entityKey, Guid.Empty, AuditAction.Exported, changes, ExecutionSource.Api, null));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
