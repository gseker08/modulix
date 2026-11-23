using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Events;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Persistence;
using Modulix.Infrastructure.Platform;

namespace Modulix.Api.Jobs;

/// <summary>
/// <see cref="IGrantedExecution"/> uygulaması: işin içinden, işin kimliğiyle ama verilen izin kümesiyle ayrı bir tenant kapsamı açar
/// (<see cref="EventHandlingScope.Begin"/>'in verilmiş izin desteği; otomasyon çalıştırıcısıyla aynı mekanizma).
/// </summary>
internal sealed class GrantedExecution(ITenantScopeFactory tenantScopes, ITenantContextAccessor tenant, EventHandlingScope handling, ModuleCatalog catalog) : IGrantedExecution
{
    public async Task<T> RunAsync<T>(IReadOnlyCollection<string> grantedPermissions, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        if (tenant.Current is not { } current || handling.Event is not { } cause || handling.Subscription is not { } subscription)
        {
            throw new InvalidOperationException("Verilmiş izinle çalıştırma yalnızca bir iş ya da olay işleyicisinin içinden yapılabilir.");
        }

        var declared = catalog.FindSubscription(subscription)?.Subscription.Permissions ?? [];
        var granted = GrantedPermissionRules.Validate(grantedPermissions, declared);

        await using var scope = tenantScopes.Create(new TenantContext(current.TenantId, Guid.Empty, Guid.Empty));
        scope.ServiceProvider.GetRequiredService<EventHandlingScope>().Begin(cause, subscription, granted);
        var result = await work(scope.ServiceProvider, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().SaveChangesAsync(cancellationToken);
        return result;
    }
}
