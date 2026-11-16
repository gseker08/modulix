namespace Modulix.Core.Contracts.Modules;

/// <summary>
/// Modül tenant için yazılabilir mi (ADR-0028)? Uç nokta süzgeçleri (<c>RequireModule</c>) bununla sorar; süre evresinin
/// ayrıntısı (ek süre, bitiş tarihi) çekirdekteki <c>IModuleLicense</c>'ta kalır.
/// </summary>
public interface IModuleWriteAccess
{
    /// <summary>Modül açıksa ve salt okunur değilse <c>true</c>.</summary>
    Task<bool> IsWritableAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken);
}
