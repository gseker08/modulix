using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Modules;
using Modulix.Modules.Contracts;
using Modulix.Modules.Crm;
using Modulix.Modules.Fleet;
using Modulix.Modules.Hr;
using Modulix.Modules.Logistics;
using Modulix.Modules.Marketing;
using Modulix.Modules.Orders;
using Modulix.Modules.Support;
using Modulix.Modules.Warehouse;

/// <summary>
/// Uygulamanın modülleri. Yeni bir modül eklemek için IModule uygulamasını listeye ekleyin; API, migration
/// komutu ve 'dotnet ef migrations add' aynı listeyi kullanır. Host'ta modülü adıyla anan tek yer burası ve
/// <see cref="ModulixModuleBridges"/>'tır; <c>Program.cs</c> modüle özel satır içermez (ADR-0046 Dilim 8).
/// </summary>
internal static class ModulixModules
{
    public static IModule[] Create() => [new CrmModule(), new LogisticsModule(), new ContractsModule(), new OrdersModule(), new SupportModule(), new MarketingModule(), new WarehouseModule(), new FleetModule(), new HrModule()];
}

/// <summary>
/// Modül sözleşmesinin host tarafından sağlanan uygulamaları. Sağlayan modülün sözleşme projesindeki arayüzü
/// host karşılar (ör. aktivite kaydı Core kayıt hattına bağlıdır); kayıt <c>Program.cs</c>'e değil buraya yazılır.
/// </summary>
internal static class ModulixModuleBridges
{
    public static IServiceCollection AddModulixModuleBridges(this IServiceCollection services) =>
        services.AddScoped<Modulix.Modules.Crm.Contracts.Activities.IFollowUpActivities, Modulix.Api.HostFollowUpActivities>();
}
