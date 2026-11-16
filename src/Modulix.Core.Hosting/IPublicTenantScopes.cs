using Microsoft.Extensions.DependencyInjection;

namespace Modulix.Core.Hosting;

/// <summary>
/// Oturumsuz uç noktanın tenant kapsamı (ADR-0046). Tenant adresteki anahtardan (tenant kimliği, N biçimi ya da kısa ad) çözülür; yalnızca erişime açık
/// firma (Active ve Trial, ADR-0028) bulunur. Kapsam sistem kimliğiyle (<paramref name="identity"/>: modülün olay aboneliği, izinleri oradan gelir) kurulur.
/// Kimliğin modülü kapalıysa ya da (<paramref name="write"/> açıkken) salt okunursa kapsam açılmaz. Bulunamayan, kapalı ve lisanssız durumlar dışarıya
/// ayırt edilmez: hepsinde <c>null</c> döner. Kapsamı çağıran kapatır.
/// </summary>
public interface IPublicTenantScopes
{
    Task<AsyncServiceScope?> OpenAsync(string tenantKey, string identity, bool write, string? correlationId, CancellationToken cancellationToken);
}
