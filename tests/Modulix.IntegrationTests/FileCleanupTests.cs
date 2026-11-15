using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Files;
using Modulix.Core.Contracts.Files;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Modules;
using Modulix.Core.Records;
using Modulix.Infrastructure.Events;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Shared.Auth;
using Modulix.Shared.Files;
using Modulix.Shared.Records;
using static Modulix.Modules.Hr.HrModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0040 dilim 3: silinen kaydın dosyaları ve yetim içerik. Kayıt silinince (olay abonesi) dosyalarının üst verisi silinir ve denetim kaydına
/// "arka plan işi" olarak yazılır; içerik yetim içerik temizliği turunda, bekleme süresi geçince silinir. Silinmeyen kaydın dosyası, bekleme süresi
/// içindeki içerik ve başka tenant'ın içeriği dokunulmadan kalır; kaydı silemeyen kullanıcının denemesi dosyaya dokunmaz.
/// </summary>
public sealed class FileCleanupTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Announcements = "/api/records/hr.announcement";
    private const string Files = "/api/files";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n");

    [Fact]
    public async Task Deleting_a_record_removes_its_files_and_the_sweep_removes_their_contents()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var hr = await LoginAsync(a.Hr);
        using var hrB = await LoginAsync(b.Hr);

        var doomed = await AnnouncementAsync(hr, "Silinecek");
        var kept = await AnnouncementAsync(hr, "Kalacak");
        var first = await UploadAsync(hr, doomed.Id, "a.pdf");
        var second = await UploadAsync(hr, doomed.Id, "b.pdf");
        var survivor = await UploadAsync(hr, kept.Id, "c.pdf");
        var otherTenant = await UploadAsync(hrB, (await AnnouncementAsync(hrB, "B")).Id, "d.pdf");
        var keys = await StorageKeysAsync(a.TenantId);
        var keyB = (await StorageKeysAsync(b.TenantId))[otherTenant.Id];

        var current = await hr.GetFromJsonAsync<RecordResponse>($"{Announcements}/{doomed.Id}");
        Assert.Equal(HttpStatusCode.NoContent, (await hr.DeleteAsync($"{Announcements}/{doomed.Id}?version={current!.Version}")).StatusCode);
        await DispatchAsync(a.TenantId);

        // Üst veri silindi ve denetim kaydına arka plan işi olarak yazıldı; silinmeyen kaydın dosyası duruyor.
        var remaining = await StorageKeysAsync(a.TenantId);
        Assert.Equal([survivor.Id], remaining.Keys);
        Assert.Single((await hr.GetFromJsonAsync<List<StoredFileDto>>($"{Files}?entityKey=hr.announcement&recordId={kept.Id}"))!);
        using (var scope = factory.OpenScope(a.TenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
            var audit = await db.AuditLog.AsNoTracking()
                .Where(e => e.EntityKey == CoreModule.AuditedEntityKeys.File && e.Action == AuditAction.Deleted).ToListAsync();
            Assert.Equal(new[] { first.Id, second.Id }.Order(), audit.Select(e => e.RecordId).Order());
            Assert.All(audit, e => Assert.Equal(ExecutionSource.BackgroundJob, e.Source));

            // Tekrar teslim güvenlidir: silinecek dosya kalmamıştır.
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRecordFileCleanup>()
                .RemoveForRecordAsync(new RecordRef("hr.announcement", doomed.Id), CancellationToken.None));
        }

        // İçerik bekleme süresi içinde kalır; süre geçince silinir. Silinmeyen kaydın ve başka tenant'ın içeriği kalır.
        Assert.True(BlobExists(a.TenantId, keys[first.Id]) && BlobExists(a.TenantId, keys[second.Id]));
        var runner = factory.Services.GetRequiredService<FileCleanupRunner>();
        Assert.Equal(0, await runner.RunTenantAsync(a.TenantId, TimeSpan.FromHours(1), CancellationToken.None));
        Assert.True(BlobExists(a.TenantId, keys[first.Id]));
        Assert.Equal(2, await runner.RunTenantAsync(a.TenantId, TimeSpan.Zero, CancellationToken.None));
        Assert.False(BlobExists(a.TenantId, keys[first.Id]) || BlobExists(a.TenantId, keys[second.Id]));
        Assert.True(BlobExists(a.TenantId, keys[survivor.Id]));
        Assert.True(BlobExists(b.TenantId, keyB));
        Assert.Equal(Pdf.Length + "c.pdf".Length, (await hr.GetByteArrayAsync($"{Files}/{survivor.Id}/content")).Length);
    }

    [Fact]
    public async Task A_user_who_cannot_delete_the_record_leaves_its_files_alone_and_other_tenants_are_untouched()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var hr = await LoginAsync(a.Hr);
        using var staff = await LoginAsync(a.Staff);
        using var hrB = await LoginAsync(b.Hr);

        var announcement = await AnnouncementAsync(hr, "Yayında");
        var file = await UploadAsync(hr, announcement.Id, "a.pdf");
        var current = await hr.GetFromJsonAsync<RecordResponse>($"{Announcements}/{announcement.Id}");

        // Okur duyuruyu silemez; B tenant'ı A'nın duyurusunu bulamaz. Dosya ve içerik kalır.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"{Announcements}/{announcement.Id}?version={current!.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hrB.DeleteAsync($"{Announcements}/{announcement.Id}?version={current.Version}")).StatusCode);
        await DispatchAsync(a.TenantId);
        await DispatchAsync(b.TenantId);

        var key = (await StorageKeysAsync(a.TenantId))[file.Id];
        var runner = factory.Services.GetRequiredService<FileCleanupRunner>();
        Assert.Equal(0, await runner.RunTenantAsync(a.TenantId, TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(0, await runner.RunTenantAsync(b.TenantId, TimeSpan.Zero, CancellationToken.None));
        Assert.True(BlobExists(a.TenantId, key));
        Assert.Single((await hr.GetFromJsonAsync<List<StoredFileDto>>($"{Files}?entityKey=hr.announcement&recordId={announcement.Id}"))!);
    }

    // --- yardımcılar ---

    private sealed record Setup(Guid TenantId, ModulixUser Hr, ModulixUser Staff);

    private async Task<Setup> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "hr", enabled: true);
        var hr = await factory.CreateUserAsync(tenant);
        var staff = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, hr);
        await factory.CreateRoleAsync(tenant.Id, [AnnouncementsRead], Scope.Own, staff);
        return new Setup(tenant.Id, hr, staff);
    }

    private async Task DispatchAsync(Guid tenantId)
    {
        for (var round = 0; round < 20; round++)
        {
            using var scope = factory.OpenScope(tenantId);
            var result = await scope.ServiceProvider.GetRequiredService<EventDispatcher>().RunOnceAsync(CancellationToken.None);
            if (result.Dispatched == 0 && result.Delivered == 0)
            {
                return;
            }
        }
    }

    private async Task<Dictionary<Guid, string>> StorageKeysAsync(Guid tenantId)
    {
        using var scope = factory.OpenScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        return await db.StoredFiles.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.StorageKey);
    }

    private bool BlobExists(Guid tenantId, string key) =>
        File.Exists(Path.Combine(factory.BlobRoot, tenantId.ToString("N"), key[..2], key));

    private static async Task<RecordResponse> AnnouncementAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(Announcements, new RecordWriteRequest(new JsonObject
        {
            ["title"] = title, ["body"] = "Metin", ["isPublished"] = true, ["publishOn"] = "2026-01-01",
        }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private static async Task<StoredFileDto> UploadAsync(HttpClient client, Guid recordId, string fileName)
    {
        var file = new ByteArrayContent([.. Pdf, .. Encoding.ASCII.GetBytes(fileName)]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("hr.announcement"), "entityKey" },
            { new StringContent(recordId.ToString()), "recordId" },
            { file, "file", fileName },
        };
        var response = await client.PostAsync(Files, form);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<StoredFileDto>())!;
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
