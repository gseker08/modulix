using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Records;

namespace Modulix.Core.Tests.Records;

/// <summary>Liste dışa aktarımının denetim kaydı (ADR-0046): modül uç noktaları Core'un denetim yazıcısını bu arayüzle kullanır.</summary>
public class RecordExportAuditTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task The_export_is_written_with_the_callers_identity_count_details_and_filters()
    {
        var audit = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var export = new RecordExportAudit(new FixedTenant(new TenantContext(TenantId, UserId, Guid.Empty)), audit, unitOfWork);

        await export.RecordAsync(
            "contracts.contract", 12, new Dictionary<string, string> { ["status"] = "Active", ["ownerId"] = "me" }, new JsonObject { ["truncated"] = true, ["money"] = false });

        var entry = Assert.Single(audit.Entries);
        Assert.Equal((TenantId, UserId, "contracts.contract", Guid.Empty, AuditAction.Exported, ExecutionSource.Api), (entry.TenantId, entry.UserId, entry.EntityKey, entry.RecordId, entry.Action, entry.Source));
        Assert.Equal(["count", "truncated", "money", "filters"], entry.Changes.Select(p => p.Key));
        Assert.Equal((12, true, false, "status=Active&ownerId=me"), (
            entry.Changes["count"]!.GetValue<int>(), entry.Changes["truncated"]!.GetValue<bool>(), entry.Changes["money"]!.GetValue<bool>(),
            entry.Changes["filters"]!.GetValue<string>()));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Without_filters_or_details_only_the_count_and_an_empty_filter_are_written()
    {
        var audit = new FakeAuditWriter();
        await new RecordExportAudit(new FixedTenant(new TenantContext(TenantId, UserId, Guid.Empty)), audit, new FakeUnitOfWork())
            .RecordAsync("crm.lead", 0, null);

        Assert.Equal("""{"count":0,"filters":""}""", Assert.Single(audit.Entries).Changes.ToJsonString());
    }

    [Fact]
    public async Task Without_a_session_nothing_is_written()
    {
        var audit = new FakeAuditWriter();
        var unitOfWork = new FakeUnitOfWork();

        await new RecordExportAudit(new FixedTenant(null), audit, unitOfWork).RecordAsync("crm.lead", 3, null);

        Assert.Empty(audit.Entries);
        Assert.Equal(0, unitOfWork.Saves);
    }

    private sealed class FixedTenant(TenantContext? current) : ITenantContextAccessor
    {
        public TenantContext? Current => current;
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Add(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
