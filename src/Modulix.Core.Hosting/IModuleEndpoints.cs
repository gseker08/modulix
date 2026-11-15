using Microsoft.AspNetCore.Routing;

namespace Modulix.Core.Hosting;

/// <summary>
/// Modülün kendi HTTP uç noktaları (ADR-0046). Modül sınıfı <c>IModule</c> ile birlikte uygular; host modülleri gezer ve
/// bu arayüzü uygulayanların uç noktalarını eşler. Genel kayıt uçları (<c>OwnedRecords</c>'tan üretilenler) bunun dışındadır.
/// </summary>
public interface IModuleEndpoints
{
    /// <summary>
    /// Modülün uç noktalarını eşler. <paramref name="routes"/> host tarafından <c>/api/&lt;modül anahtarı&gt;</c> grubu olarak
    /// açılmış, oturum ve modül lisans kontrolüyle (<c>RequireModule</c>, ADR-0028) sarılmış gelir; modül bunları yeniden eklemez.
    /// İzin kontrolü (<c>RequirePermission</c>) uç nokta başına modüldedir.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder routes);
}
