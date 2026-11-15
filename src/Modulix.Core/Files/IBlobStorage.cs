namespace Modulix.Core.Files;

/// <summary>
/// Dosya içeriği depolama adaptörü (ADR-0040 K4). Üst veri veritabanındadır; içerik burada. Anahtar çağıranın verdiği rastgele dizgidir
/// (<see cref="StoredFile.NewStorageKey"/>); adaptör anahtarı yola çevirirken yol çıkışına izin vermez ve tenant başına ayrı bir kök kullanır.
/// İki adaptör vardır: yerel disk (tek düğüm) ve S3 uyumlu nesne deposu (çok düğüm; <see cref="FileStorageOptions.Provider"/>).
/// </summary>
public interface IBlobStorage
{
    /// <summary>İçeriği yazar. Var olan bir anahtarı ezmez (<see cref="IOException"/>); yarım yazılmış içerik görünmez.</summary>
    Task PutAsync(Guid tenantId, string key, Stream content, CancellationToken cancellationToken);

    /// <summary>İçeriği açar; yoksa <c>null</c>.</summary>
    Task<Stream?> OpenReadAsync(Guid tenantId, string key, CancellationToken cancellationToken);

    /// <summary>İçeriği siler; yoksa sessizce geçer.</summary>
    Task DeleteAsync(Guid tenantId, string key, CancellationToken cancellationToken);

    /// <summary>Tenant'ın bütün içeriğini <paramref name="directory"/> altına, anahtar adıyla kopyalar (tenant dışa aktarma). Kopyalanan dosya sayısını döner.</summary>
    Task<int> ExportTenantAsync(Guid tenantId, string directory, CancellationToken cancellationToken);

    /// <summary>Tenant'ın bütün içeriğini siler (tenant silme). Silinen dosya sayısını döner.</summary>
    Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Deponun kullanılabilir olduğunu doğrular (yerel kök oluşturulabilir, kova erişilebilir); değilse hata fırlatır. <c>/health/ready</c> kullanır.</summary>
    Task CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Dosya içeriğinin nerede tutulacağı.</summary>
public enum BlobStorageProvider
{
    /// <summary>Yerel disk (<see cref="FileStorageOptions.LocalRoot"/>). Yalnızca tek düğümlü dağıtım için.</summary>
    Local,

    /// <summary>S3 uyumlu nesne deposu (AWS S3, Cloudflare R2, Versity, Ceph RGW vb.; <see cref="FileStorageOptions.S3"/>). Çok düğümlü dağıtım bunu kullanır.</summary>
    S3,
}

/// <summary>Dosya deposu yapılandırması (<c>Storage</c> bölümü).</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Tek dosya için en büyük boyut (bayt). Varsayılan 10 MB.</summary>
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Depo türü (<c>Storage:Provider</c>): <c>Local</c> (varsayılan) ya da <c>S3</c>.</summary>
    public BlobStorageProvider Provider { get; set; } = BlobStorageProvider.Local;

    /// <summary>
    /// Yerel disk adaptörünün kökü. Boşsa uygulama veri dizini altında <c>blobs</c>. Web köküne ve statik dosya sunumuna dışında olmalıdır;
    /// yedeğe dahil edilmelidir (docs/tenant-isletim.md).
    /// </summary>
    public string? LocalRoot { get; set; }

    /// <summary>S3 adaptörünün ayarları (<c>Storage:S3</c>); yalnızca <see cref="Provider"/> <c>S3</c> iken okunur.</summary>
    public S3StorageOptions S3 { get; set; } = new();
}

/// <summary>
/// S3 uyumlu nesne deposu ayarları. Erişim anahtarları repoya yazılmaz: ortam değişkeni (<c>Storage__S3__SecretKey</c>) ya da gizli bilgi deposuyla verilir;
/// ikisi de boşsa AWS'nin varsayılan kimlik zinciri (rol, ortam, profil) kullanılır.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>Kova adı (zorunlu). Kova önceden oluşturulur; uygulama kova oluşturmaz.</summary>
    public string? Bucket { get; set; }

    /// <summary>AWS dışı uç nokta (ör. <c>https://hesap.r2.cloudflarestorage.com</c>, <c>http://localhost:9000</c>). Boşsa AWS S3 bölge uç noktası.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Bölge. AWS'de kovanın bölgesi (varsayılan <c>eu-central-1</c>); başka uç noktada imza bölgesi (varsayılan <c>us-east-1</c>).</summary>
    public string? Region { get; set; }

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Nesne anahtarlarının önüne eklenen kök (ör. <c>modulix/</c>); kova başka verilerle paylaşılıyorsa kullanılır.</summary>
    public string? Prefix { get; set; }

    /// <summary>Yol biçimli adres (<c>uç-nokta/kova/anahtar</c>). Boşsa <see cref="ServiceUrl"/> verildiğinde açık, AWS'de kapalı.</summary>
    public bool? ForcePathStyle { get; set; }
}
