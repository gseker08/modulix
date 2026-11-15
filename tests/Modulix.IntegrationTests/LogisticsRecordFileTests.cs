using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Files;
using Modulix.Shared.Logistics;
using Modulix.Shared.Records;
using static Modulix.Modules.Logistics.LogisticsModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0038 bölüm 27: taşıma talebi, sevkiyat, sefer ve durak belgeleri süreç kapanana kadar eklenir ve silinir, kapanınca donar (403).
/// Mutlu yol (açık kayda belge eklenir ve silinir; yoldaki sevkiyata ve planlanan sefere eklenir), kural (karara bağlanan talep, teslim edilen sevkiyat,
/// iptal edilen sefer ve durağı: ekleme ve silme 403, okuma ve indirme sürer), tenant izolasyonu (başka tenant ekleyemez, silemez, indiremez: 404) ve
/// yetki (yalnızca okuyan açık kayda bile ekleyemez ve silemez: 403).
/// </summary>
public class LogisticsRecordFileTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Requests = "/api/records/logistics.transport-request";
    private const string Shipments = "/api/records/logistics.shipment";
    private const string Trips = "/api/records/logistics.trip";
    private const string Stops = "/api/records/logistics.trip-stop";
    private const string Parties = "/api/records/core.party";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n");

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Documents_change_while_the_process_is_open_and_freeze_once_it_is_closed()
    {
        var s = await SetUpAsync();
        using var manager = await LoginAsync(s.Manager);

        // Talep: yeniyken belge eklenir ve silinir; karara bağlanınca donar.
        var request = await NewRequestAsync(manager, s.CustomerId);
        var order = await UploadAsync(manager, LogisticsAccess.TransportRequestEntityKey, request.Id, "siparis.pdf");
        var wrong = await UploadAsync(manager, LogisticsAccess.TransportRequestEntityKey, request.Id, "yanlis.pdf");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/files/{wrong.Id}?version={wrong.Version}")).StatusCode);
        var approved = await ActAsync(manager, "requests", request, "approve");
        await AssertFrozenAsync(manager, LogisticsAccess.TransportRequestEntityKey, request.Id, order, "Karara bağlanan");

        // Sevkiyat: planlanırken ve yoldayken belge eklenir; teslim sonucu girilince donar.
        var shipment = await GetAsync(manager, $"{Shipments}/{approved.Values["shipmentId"]!.GetValue<string>()}");
        var waybill = await UploadAsync(manager, LogisticsAccess.ShipmentEntityKey, shipment.Id, "irsaliye.pdf");
        var trip = await PostAsync(manager, Trips, new JsonObject { ["plannedStart"] = Iso(Today.AddDays(1)) });
        var assigned = await manager.PostAsJsonAsync($"/api/logistics/trips/{trip.Id}/shipments", new AssignShipmentRequest(shipment.Id));
        Assert.True(assigned.StatusCode == HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
        shipment = (await assigned.Content.ReadFromJsonAsync<RecordResponse>())!;
        shipment = await ActAsync(manager, "shipments", await ActAsync(manager, "shipments", shipment, "startLoading"), "depart");
        var cmr = await UploadAsync(manager, LogisticsAccess.ShipmentEntityKey, shipment.Id, "cmr.pdf");
        var delivered = await manager.PostAsJsonAsync($"/api/logistics/shipments/{shipment.Id}/delivery",
            new RecordDeliveryRequest(shipment.Version, "delivered", "Ali", false, false, null, null));
        Assert.True(delivered.StatusCode == HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());
        await AssertFrozenAsync(manager, LogisticsAccess.ShipmentEntityKey, shipment.Id, waybill, "Teslim sonucu girilen");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/files/{cmr.Id}/content")).StatusCode);

        // Sefer ve durak: sefer açıkken belge eklenir (sevkiyat teslim edilmiş olsa da); sefer iptal edilince ikisi de donar.
        var stop = (await ListAsync(manager, Stops, $"tripId={trip.Id}"))[0];
        var tripDoc = await UploadAsync(manager, LogisticsAccess.TripEntityKey, trip.Id, "yol-belgesi.pdf");
        var stopDoc = await UploadAsync(manager, LogisticsAccess.TripStopEntityKey, stop.Id, "durak-tutanagi.pdf");
        trip = await GetAsync(manager, $"{Trips}/{trip.Id}");
        Assert.Equal("cancelled", (await ActAsync(manager, "trips", trip, "cancel", "Sefer kapatıldı")).Values["status"]!.GetValue<string>());
        await AssertFrozenAsync(manager, LogisticsAccess.TripEntityKey, trip.Id, tripDoc, "Sefer kapandığı");
        await AssertFrozenAsync(manager, LogisticsAccess.TripStopEntityKey, stop.Id, stopDoc, "Sefer kapandığı");
    }

    [Fact]
    public async Task Another_tenants_logistics_documents_cannot_be_added_downloaded_or_deleted()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var managerA = await LoginAsync(a.Manager);
        using var managerB = await LoginAsync(b.Manager);
        var request = await NewRequestAsync(managerA, a.CustomerId);
        var trip = await PostAsync(managerA, Trips, new JsonObject { ["plannedStart"] = Iso(Today.AddDays(1)) });
        var requestDoc = await UploadAsync(managerA, LogisticsAccess.TransportRequestEntityKey, request.Id, "siparis.pdf");
        var tripDoc = await UploadAsync(managerA, LogisticsAccess.TripEntityKey, trip.Id, "yol-belgesi.pdf");

        foreach (var (key, id) in new[] { (LogisticsAccess.TransportRequestEntityKey, request.Id), (LogisticsAccess.TripEntityKey, trip.Id) })
        {
            using var form = Form(key, id, "yabanci.pdf");
            Assert.Equal(HttpStatusCode.NotFound, (await managerB.PostAsync("/api/files", form)).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await managerB.GetAsync($"/api/files?entityKey={key}&recordId={id}")).StatusCode);
        }

        foreach (var doc in new[] { requestDoc, tripDoc })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await managerB.GetAsync($"/api/files/{doc.Id}/content")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await managerB.DeleteAsync($"/api/files/{doc.Id}?version={doc.Version}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await managerA.GetAsync($"/api/files/{doc.Id}/content")).StatusCode);
        }
    }

    [Fact]
    public async Task Readers_cannot_add_or_delete_documents_even_while_the_process_is_open()
    {
        var s = await SetUpAsync();
        using var manager = await LoginAsync(s.Manager);
        using var reader = await LoginAsync(s.Reader);
        var request = await NewRequestAsync(manager, s.CustomerId);
        var trip = await PostAsync(manager, Trips, new JsonObject { ["plannedStart"] = Iso(Today.AddDays(1)) });
        var requestDoc = await UploadAsync(manager, LogisticsAccess.TransportRequestEntityKey, request.Id, "siparis.pdf");
        var tripDoc = await UploadAsync(manager, LogisticsAccess.TripEntityKey, trip.Id, "yol-belgesi.pdf");

        foreach (var (key, id, doc) in new[] { (LogisticsAccess.TransportRequestEntityKey, request.Id, requestDoc), (LogisticsAccess.TripEntityKey, trip.Id, tripDoc) })
        {
            using var form = Form(key, id, "okuyan.pdf");
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsync("/api/files", form)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"/api/files/{doc.Id}?version={doc.Version}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/files/{doc.Id}/content")).StatusCode);
        }
    }

    // --- yardımcılar ---

    private sealed record Setup(Guid TenantId, Guid CustomerId, ModulixUser Manager, ModulixUser Reader);

    private async Task<Setup> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "logistics", enabled: true);
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true); // müşteri firmayı kayıt ucundan açmak için
        var admin = await factory.CreateUserAsync(tenant);
        var manager = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, [RequestsRead, RequestsWrite, RequestsApprove, ShipmentsRead, ShipmentsWrite, TripsRead, TripsWrite], Scope.All, manager);
        await factory.CreateRoleAsync(tenant.Id, [RequestsRead, ShipmentsRead, TripsRead], Scope.All, reader);

        using var adminClient = await LoginAsync(admin);
        var customer = await PostAsync(adminClient, Parties, new JsonObject { ["name"] = $"Müşteri {Guid.NewGuid():N}"[..20], ["taxNumber"] = Guid.NewGuid().ToString("N")[..10] });
        return new Setup(tenant.Id, customer.Id, manager, reader);
    }

    private static Task<RecordResponse> NewRequestAsync(HttpClient manager, Guid customerId) => PostAsync(manager, Requests, new JsonObject
    {
        ["customerPartyId"] = customerId.ToString(),
        ["pickupCity"] = "Ankara",
        ["deliveryCity"] = "İzmir",
        ["pickupDate"] = Iso(Today.AddDays(1)),
        ["cargoDescription"] = "24 palet gıda",
        ["weightKg"] = 12_000,
    });

    /// <summary>Kapanan kayda belge eklenemez, var olan silinemez (403, kuralın iletisiyle); belge listelenir ve indirilir.</summary>
    private static async Task AssertFrozenAsync(HttpClient client, string entityKey, Guid recordId, StoredFileDto existing, string message)
    {
        using var late = Form(entityKey, recordId, "sonradan.pdf");
        var denied = await client.PostAsync("/api/files", late);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains(message, (await denied.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/files/{existing.Id}?version={existing.Version}")).StatusCode);

        var files = (await client.GetFromJsonAsync<List<StoredFileDto>>($"/api/files?entityKey={entityKey}&recordId={recordId}"))!;
        Assert.Contains(existing.Id, files.Select(f => f.Id));
        Assert.DoesNotContain("sonradan.pdf", files.Select(f => f.FileName));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/files/{existing.Id}/content")).StatusCode);
    }

    private static MultipartFormDataContent Form(string entityKey, Guid recordId, string fileName)
    {
        var file = new ByteArrayContent([.. Pdf, .. Encoding.ASCII.GetBytes(fileName + Guid.NewGuid())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent
        {
            { new StringContent(entityKey), "entityKey" },
            { new StringContent(recordId.ToString()), "recordId" },
            { file, "file", fileName },
        };
    }

    private static async Task<StoredFileDto> UploadAsync(HttpClient client, string entityKey, Guid recordId, string fileName)
    {
        using var form = Form(entityKey, recordId, fileName);
        var response = await client.PostAsync("/api/files", form);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{fileName}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<StoredFileDto>())!;
    }

    private static async Task<RecordResponse> ActAsync(HttpClient client, string kind, RecordResponse record, string action, string? reason = null)
    {
        var response = await client.PostAsJsonAsync($"/api/logistics/{kind}/{record.Id}/actions", new RunLogisticsActionRequest(action, record.Version, reason));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{action}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<IReadOnlyList<RecordResponse>> ListAsync(HttpClient client, string url, string? query) =>
        (await client.GetFromJsonAsync<RecordPageResponse>(query is null ? url : $"{url}?{query}"))!.Items;

    private static async Task<RecordResponse> GetAsync(HttpClient client, string url) => (await client.GetFromJsonAsync<RecordResponse>(url))!;

    private static async Task<RecordResponse> PostAsync(HttpClient client, string url, JsonObject values)
    {
        var response = await client.PostAsJsonAsync(url, new RecordWriteRequest(values));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }
}
