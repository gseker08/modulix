using Modulix.Modules.Crm;

namespace Modulix.Api.Crm;

/// <summary>
/// Host'ta kalan CRM uç noktaları: teklif ve satış planlama (<c>/api/crm/...</c>). Diğer CRM uç noktaları modüldedir
/// (<c>Modulix.Modules.Crm/Endpoints</c>, ADR-0046); bunlar host bağımlılıkları (e-posta, raporlama, ayar servisleri) kapandıkça taşınır.
/// </summary>
public static class CrmEndpoints
{
    public static IEndpointRouteBuilder MapCrmEndpoints(this IEndpointRouteBuilder app)
    {
        var crm = app.MapGroup("/api/crm")
            .RequireAuthorization()
            .RequireModule(CrmModule.ModuleKey)
            .WithTags("CRM");

        QuoteEndpoints.Map(crm);
        SalesPlanningEndpoints.Map(crm);
        return app;
    }
}
