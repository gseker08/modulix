using Modulix.Core.Modules;

namespace Modulix.Api;

public static class ModuleEndpointMapping
{
    /// <summary>
    /// Modüllerin kendi uç noktalarını eşler (ADR-0046). Host modülü tanımaz: <see cref="IModuleEndpoints"/> uygulayan her modül
    /// için <c>/api/&lt;modül anahtarı&gt;</c> grubunu açar, oturum ve modül lisans kontrolünü (<c>RequireModule</c>, ADR-0028)
    /// kendisi uygular; izin kontrolü uç nokta başına modüldedir.
    /// </summary>
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var module in app.ServiceProvider.GetRequiredService<ModuleCatalog>().Modules)
        {
            if (module is IModuleEndpoints endpoints)
            {
                var group = app.MapGroup($"/api/{module.Key}")
                    .RequireAuthorization()
                    .RequireModule(module.Key);
                endpoints.MapEndpoints(group);
            }
        }

        return app;
    }
}
