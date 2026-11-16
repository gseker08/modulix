using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Authorization;
using Modulix.Core.Files;
using Modulix.Modules.Crm;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Files;
using static Modulix.Modules.Crm.CrmModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// S3 uyumlu nesne deposu adaptörü (mimari değerlendirme A4), compose'daki yerel S3 sunucusuna (Versity Gateway) karşı.
/// Mutlu yol (yükle, indir, sil; dışa aktarma), hata (var olan anahtar ezilmez, geçersiz anahtar reddedilir, kova yoksa hazır değil),
/// tenant izolasyonu (aynı anahtar başka tenant'ta yok; tenant silme yalnızca kendi içeriğini siler). Yetki adaptörde değil
/// <c>FileStore</c>'dadır ve depo türünden bağımsızdır (<see cref="FileTests"/>).
/// Ayarlar: <c>TestStorage:S3:ServiceUrl</c>, <c>AccessKey</c>, <c>SecretKey</c> (yerelde scripts\db-up.ps1 user-secrets'a yazar, CI'da ortam değişkeni).
/// </summary>
public sealed class S3BlobStorageTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n");

    private static string NewKey() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private (string Url, string AccessKey, string SecretKey) Settings()
    {
        IConfiguration configuration = factory.Services.GetRequiredService<IConfiguration>();
        string? url = configuration["TestStorage:S3:ServiceUrl"];
        string? secret = configuration["TestStorage:S3:SecretKey"];
        Assert.False(
            string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret),
            "S3 test deposu yapılandırılmamış: scripts\\db-up.ps1 çalıştırın (compose 's3' servisi ve TestStorage:S3:* ayarları).");
        return (url!, configuration["TestStorage:S3:AccessKey"] ?? "modulix", secret!);
    }

    /// <summary>Test için ayrı bir kova açar ve bu kovayı kullanan bir sunucu döner.</summary>
    private async Task<WebApplicationFactory<Program>> S3ServerAsync(string? bucket = null, bool createBucket = true)
    {
        var (url, accessKey, secretKey) = Settings();
        bucket ??= "modulix-test-" + Guid.NewGuid().ToString("N")[..12];
        if (createBucket)
        {
            using AmazonS3Client admin = new(
                new BasicAWSCredentials(accessKey, secretKey),
                new AmazonS3Config { ServiceURL = url, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
            await admin.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
        }

        return factory.WithWebHostBuilder(builder => builder
            .UseSetting("Storage:Provider", "S3")
            .UseSetting("Storage:S3:ServiceUrl", url)
            .UseSetting("Storage:S3:Bucket", bucket)
            .UseSetting("Storage:S3:AccessKey", accessKey)
            .UseSetting("Storage:S3:SecretKey", secretKey)
            .UseSetting("Storage:S3:Prefix", "modulix"));
    }

    private static async Task<byte[]?> ReadAsync(IBlobStorage storage, Guid tenantId, string key)
    {
        await using Stream? stream = await storage.OpenReadAsync(tenantId, key, CancellationToken.None);
        if (stream is null)
        {
            return null;
        }

        using MemoryStream copy = new();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    // ---- 1. Mutlu yol

    [Fact]
    public async Task Files_are_uploaded_downloaded_and_deleted_through_the_api_on_s3()
    {
        using WebApplicationFactory<Program> server = await S3ServerAsync();
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, CrmModule.ModuleKey, enabled: true);
        var user = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, CompaniesWrite], RecordScope.All, user);

        HttpClient client = server.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password))).StatusCode);
        var created = await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest($"S3 Ltd. {Guid.NewGuid():N}"[..20]));
        CompanyDto company = (await created.Content.ReadFromJsonAsync<CompanyDto>())!;

        ByteArrayContent content = new(Pdf);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using MultipartFormDataContent form = new()
        {
            { new StringContent("crm.company"), "entityKey" },
            { new StringContent(company.Id.ToString()), "recordId" },
            { content, "file", "teklif.pdf" },
        };
        var upload = await client.PostAsync("/api/files", form);
        Assert.True(upload.StatusCode == HttpStatusCode.Created, $"{(int)upload.StatusCode}: {await upload.Content.ReadAsStringAsync()}");
        StoredFileDto file = (await upload.Content.ReadFromJsonAsync<StoredFileDto>())!;

        // İçerik yerel diske değil kovaya yazılır.
        Assert.False(Directory.Exists(Path.Combine(factory.BlobRoot, tenant.Id.ToString("N"))));

        var download = await client.GetAsync($"/api/files/{file.Id}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/files/{file.Id}?version={file.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/files/{file.Id}/content")).StatusCode);

        // Kovada da kalmaz (tenant dışa aktarma boş döner).
        using IServiceScope scope = server.Services.CreateScope();
        IBlobStorage storage = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
        string directory = Path.Combine(Path.GetTempPath(), "modulix-s3-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, await storage.ExportTenantAsync(tenant.Id, directory, CancellationToken.None));
    }

    [Fact]
    public async Task Tenant_export_copies_and_tenant_delete_removes_only_that_tenants_objects()
    {
        using WebApplicationFactory<Program> server = await S3ServerAsync();
        IBlobStorage storage = server.Services.GetRequiredService<IBlobStorage>();
        Guid tenant = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        string first = NewKey();
        string second = NewKey();
        string foreign = NewKey();
        await storage.PutAsync(tenant, first, new MemoryStream(Pdf), CancellationToken.None);
        await storage.PutAsync(tenant, second, new MemoryStream([1, 2, 3]), CancellationToken.None);
        await storage.PutAsync(other, foreign, new MemoryStream([4, 5]), CancellationToken.None);

        string directory = Path.Combine(Path.GetTempPath(), "modulix-s3-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(2, await storage.ExportTenantAsync(tenant, directory, CancellationToken.None));
            Assert.Equal(new[] { first, second }.Order(), Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order());
            Assert.Equal(Pdf, await File.ReadAllBytesAsync(Path.Combine(directory, first)));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        Assert.Equal(2, await storage.DeleteTenantAsync(tenant, CancellationToken.None));
        Assert.Null(await ReadAsync(storage, tenant, first));
        Assert.Equal(new byte[] { 4, 5 }, await ReadAsync(storage, other, foreign));
        Assert.Equal(0, await storage.DeleteTenantAsync(tenant, CancellationToken.None));
    }

    [Fact]
    public async Task The_readiness_check_passes_when_the_bucket_is_reachable()
    {
        using WebApplicationFactory<Program> server = await S3ServerAsync();
        var response = await server.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- 2. Hata ve doğrulama

    [Fact]
    public async Task An_existing_key_is_not_overwritten_and_invalid_keys_are_rejected()
    {
        using WebApplicationFactory<Program> server = await S3ServerAsync();
        IBlobStorage storage = server.Services.GetRequiredService<IBlobStorage>();
        Guid tenant = Guid.NewGuid();
        string key = NewKey();
        await storage.PutAsync(tenant, key, new MemoryStream(Pdf), CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => storage.PutAsync(tenant, key, new MemoryStream([9, 9]), CancellationToken.None));
        Assert.Equal(Pdf, await ReadAsync(storage, tenant, key));

        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(tenant, "../" + key[3..], new MemoryStream(Pdf), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(tenant, key.ToUpperInvariant(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(Guid.Empty, NewKey(), new MemoryStream(Pdf), CancellationToken.None));

        // Olmayan nesneyi silmek hata değildir; okumak null döner.
        string missing = NewKey();
        await storage.DeleteAsync(tenant, missing, CancellationToken.None);
        Assert.Null(await ReadAsync(storage, tenant, missing));
    }

    [Fact]
    public async Task The_readiness_check_fails_when_the_bucket_is_missing()
    {
        using WebApplicationFactory<Program> server = await S3ServerAsync("modulix-yok-" + Guid.NewGuid().ToString("N")[..12], createBucket: false);
        var response = await server.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        // Kova adı ya da uç nokta yanıtta yoktur.
        Assert.DoesNotContain("modulix-yok", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---- 3. Tenant izolasyonu

    [Fact]
    public async Task The_same_key_under_another_tenant_is_not_readable_or_deletable()
    {
        using WebApplicationFactory<Program> server = await S3ServerAsync();
        IBlobStorage storage = server.Services.GetRequiredService<IBlobStorage>();
        Guid owner = Guid.NewGuid();
        Guid intruder = Guid.NewGuid();
        string key = NewKey();
        await storage.PutAsync(owner, key, new MemoryStream(Pdf), CancellationToken.None);

        Assert.Null(await ReadAsync(storage, intruder, key));
        await storage.DeleteAsync(intruder, key, CancellationToken.None);
        Assert.Equal(0, await storage.DeleteTenantAsync(intruder, CancellationToken.None));
        Assert.Equal(Pdf, await ReadAsync(storage, owner, key));
    }
}
