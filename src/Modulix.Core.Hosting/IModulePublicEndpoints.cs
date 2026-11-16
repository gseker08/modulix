using Microsoft.AspNetCore.Routing;

namespace Modulix.Core.Hosting;

/// <summary>
/// Modülün oturumsuz (herkese açık) uç noktaları (ADR-0046): bağlantı belirteciyle açılan anket, form ya da paylaşım sayfaları. Host
/// <c>/api/public</c> grubunu oturumsuz (<c>AllowAnonymous</c>) açar ve modüle verir; modül yolunu kendisi yazar (ör. <c>/support-survey/{tenant}</c>).
/// Bu uçlarda oturum ve modül kontrolü yoktur: tenant <see cref="IPublicTenantScopes"/> ile çözülür (lisans orada denetlenir), kötüye kullanıma karşı
/// hız sınırı ve gövde boyutu sınırı modüldedir. Mimari test bu uçları gerekçeli istisna listesinde tutar.
/// </summary>
public interface IModulePublicEndpoints
{
    void MapPublicEndpoints(IEndpointRouteBuilder routes);
}
