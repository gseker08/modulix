using Microsoft.AspNetCore.Http;

namespace Modulix.Core.Hosting;

/// <summary>
/// İsteğin aktif tenant'ı var mı? Tenant oturumdan belirlenir ve her istekte doğrulanır; bunu host yapar ve bu arayüzle
/// uç nokta süzgeçlerine bildirir.
/// </summary>
public interface IActiveTenantGuard
{
    /// <summary>Aktif tenant varsa <c>null</c>; yoksa döndürülecek hata yanıtı (403).</summary>
    IResult? Check();
}

/// <summary>Aktif tenant ister; yoksa <see cref="IActiveTenantGuard"/>'ın hata yanıtını döner.</summary>
public sealed class ActiveTenantFilter(IActiveTenantGuard guard) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        guard.Check() is { } problem ? ValueTask.FromResult<object?>(problem) : next(context);
}
