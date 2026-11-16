namespace Modulix.Core.Contracts.Tenancy;

/// <summary>Aktif tenant'ın herkese görünen bilgisi: kısa adı (herkese açık bağlantılarda kullanılır) ve kayıtlı adı.</summary>
public sealed record TenantInfo(string Slug, string Name);

/// <summary>
/// Aktif tenant'ın bilgisi (ADR-0046): modül, ör. müşteriye giden bağlantıyı kurarken firmanın kısa adını ister; platform tablosunu doğrudan görmez.
/// Aktif tenant yoksa <c>null</c>.
/// </summary>
public interface ICurrentTenantInfo
{
    Task<TenantInfo?> GetAsync(CancellationToken cancellationToken);
}
