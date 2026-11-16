using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Events;
using Modulix.Core.Modules;
using Modulix.Core.Platform;
using Modulix.Infrastructure.Persistence;
using Modulix.Infrastructure.Platform;

namespace Modulix.Api;

/// <summary><see cref="IPublicTenantScopes"/> uygulaması: platform tablosundan tenant'ı çözer, kapsamı açar, lisansı denetler ve sistem kimliğini kurar.</summary>
internal sealed class PublicTenantScopes(ModulixDbContext platform, ITenantScopeFactory scopes, ModuleCatalog catalog, TimeProvider time) : IPublicTenantScopes
{
    public async Task<AsyncServiceScope?> OpenAsync(string tenantKey, string identity, bool write, string? correlationId, CancellationToken cancellationToken)
    {
        var (moduleKey, subscription) = catalog.FindSubscription(identity)
            ?? throw new InvalidOperationException($"Sistem kimliği '{identity}' katalogda yok.");

        var key = tenantKey.Trim().ToLowerInvariant();
        var query = platform.Tenants.AsNoTracking().Where(t => TenantStatusRules.Operational.Contains(t.Status));
        var found = Guid.TryParseExact(key, "N", out var id)
            ? await query.Where(t => t.Id == id).Select(t => t.Id).FirstOrDefaultAsync(cancellationToken)
            : await query.Where(t => t.Slug == key).Select(t => t.Id).FirstOrDefaultAsync(cancellationToken);
        if (found == Guid.Empty)
        {
            return null;
        }

        var scope = scopes.Create(new TenantContext(found, Guid.Empty, Guid.Empty));

        // Lisans (ADR-0028): modül kapalıysa bağlantı çalışmaz; salt okunursa yalnızca okuma açılır.
        if (!await scope.ServiceProvider.GetRequiredService<IModuleLicense>().AllowsAsync(found, moduleKey, write, cancellationToken))
        {
            await scope.DisposeAsync();
            return null;
        }

        // Oturumsuz çağrı, sistem kimliğinin aboneliğiyle "bir olayı işliyormuş" gibi kurulur: izinleri o aboneliğin bildirdikleridir.
        var cause = new EventEnvelope(
            Guid.NewGuid(), found, subscription.EventTypes[0], 1, subscription.EntityKeys.Count > 0 ? subscription.EntityKeys[0] : string.Empty, Guid.Empty, null,
            Guid.Empty, ExecutionSource.Import, correlationId, null, 0, new JsonObject(), time.GetUtcNow());
        scope.ServiceProvider.GetRequiredService<EventHandlingScope>().Begin(cause, identity);
        return scope;
    }
}
