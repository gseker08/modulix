using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Audit;
using Modulix.Shared.Auth;
using Modulix.Shared.Files;
using Modulix.Shared.Logistics;
using Modulix.Shared.Lookups;
using Modulix.Shared.Records;
using Modulix.Shared.Settings;
using static Modulix.Modules.Logistics.LogisticsModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0038 dilim 8: sefer masrafı, onay ve yakıt alanları. Mutlu yol (operasyon yakıt ve geçiş masrafı girer, litre fiyatı türer, müdür onaylar ve
/// gerekçeyle reddeder, karar verilmiş masraf kilitlenir ve silinmez, denetim kaydı), doğrulama (tutar, para birimi, KDV, tarih, kategoriye bağlı
/// litre/kilometre/rücu kuralları, bilinmeyen tür ve sefer, iptal edilmiş sefer, gerekçesiz ret, çifte karar), tenant izolasyonu (başka tenant'ın
/// masrafı görünmez, onaylanamaz, başka tenant'ın seferine masraf açılamaz) ve yetki (yazma izni olmayan giremez, onay izni olmayan onaylayamaz ve
/// eylem listesinde görmez, okuma izni olmayan göremez). Makbuz ve fiş (bölüm 24): beklemede ve reddedilmişte eklenir ve silinir, onaylanmışta ve
/// iptal edilmiş seferde 403; başka tenant 404; yalnızca okuyan 403. İptal edilmiş seferin masrafı (bölüm 25): düzenleme 409, karar komutları açık;
/// başka tenant 404, okuyan 403. Gerekçeli onay ve iptal uyarısı (bölüm 26): iptal edilen seferin masrafını onaylamak gerekçe ister, iptal adımı bekleyen
/// masraf sayısını masraf okuyana söyler; başka tenant 404, onay izni olmayan 403. Başka sefere aktarma (bölüm 28): iptal edilen seferin bekleyen
/// masrafı açık sefere gerekçeyle taşınır; kurallar 409/422; başka tenant 404; masraf yazma izni olmayan 403.
/// </summary>
public class LogisticsExpenseTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Trips = "/api/records/logistics.trip";
    private const string Expenses = "/api/records/logistics.trip-expense";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Operations_enters_expenses_manager_approves_and_rejects_and_decided_expenses_are_locked()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });

        var fuel = await PostAsync(operations, Expenses, Fuel(trip.Id));
        Assert.Equal("pending", fuel.Values["status"]!.GetValue<string>());
        Assert.Equal(500m, fuel.Values["unitPrice"]!.GetValue<decimal>()); // 5000 / 10 litre
        Assert.Equal("TRY", fuel.Values["currency"]!.GetValue<string>());
        var toll = await PostAsync(operations, Expenses, Toll(trip.Id));
        Assert.Null(toll.Values["unitPrice"]);

        var page = (await operations.GetFromJsonAsync<RecordPageResponse>($"{Expenses}?tripId={trip.Id}&status=pending"))!;
        Assert.Equal(2, page.Items.Count);

        // Beklemedeki masraf düzenlenir.
        var edited = await PutAsync(operations, $"{Expenses}/{fuel.Id}", fuel.Version, new JsonObject { ["liters"] = 12.5m, ["amount"] = 6250 });
        Assert.Equal(500m, edited.Values["unitPrice"]!.GetValue<decimal>());

        var approved = await ActAsync(manager, edited, "approve");
        Assert.Equal("approved", approved.Values["status"]!.GetValue<string>());
        var rejected = await ActAsync(manager, toll, "reject", "Fiş okunmuyor");
        Assert.Equal("rejected", rejected.Values["status"]!.GetValue<string>());
        Assert.Equal("Fiş okunmuyor", rejected.Values["statusReason"]!.GetValue<string>());

        // Karar verilmiş masraf değişmez ve silinmez (onay tutara bağlıdır; K12).
        Assert.Equal(HttpStatusCode.Conflict, (await operations.PutAsJsonAsync($"{Expenses}/{approved.Id}",
            new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, approved.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.DeleteAsync($"{Expenses}/{rejected.Id}?version={rejected.Version}")).StatusCode);
        Assert.Equal(6250m, (await GetAsync(operations, $"{Expenses}/{approved.Id}")).Values["amount"]!.GetValue<decimal>());

        // Bekleyen listesi karar verilenleri içermez.
        Assert.Empty((await operations.GetFromJsonAsync<RecordPageResponse>($"{Expenses}?status=pending"))!.Items);
        Assert.Single((await operations.GetFromJsonAsync<RecordPageResponse>($"{Expenses}?status=approved"))!.Items);

        // Denetim kaydı: oluşturma, düzenleme ve onay yazılır.
        var history = (await manager.GetFromJsonAsync<AuditPageResponse>($"{Expenses}/{approved.Id}/history"))!;
        Assert.True(history.Items.Count >= 3, $"{history.Items.Count}");
    }

    [Fact]
    public async Task Invalid_expenses_decisions_without_reason_and_double_decisions_are_rejected()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });

        async Task<ValidationProblemDetails> RejectedAsync(JsonObject values) =>
            await ValidationProblemAsync(await operations.PostAsJsonAsync(Expenses, new RecordWriteRequest(values)));

        JsonObject With(JsonObject source, string key, JsonNode? value)
        {
            var copy = (JsonObject)source.DeepClone();
            copy[key] = value;
            return copy;
        }

        var toll = Toll(trip.Id);
        Assert.Contains("amount", (await RejectedAsync(With(toll, "amount", 0))).Errors.Keys);
        Assert.Contains("amount", (await RejectedAsync(With(toll, "amount", 10.123m))).Errors.Keys);
        Assert.Contains("currency", (await RejectedAsync(With(toll, "currency", null))).Errors.Keys);
        Assert.Contains("currency", (await RejectedAsync(With(toll, "currency", "TR1"))).Errors.Keys);
        Assert.Contains("vatAmount", (await RejectedAsync(With(toll, "vatAmount", 9999))).Errors.Keys);
        Assert.Contains("description", (await RejectedAsync(With(toll, "description", " "))).Errors.Keys);
        Assert.Contains("expenseDate", (await RejectedAsync(With(toll, "expenseDate", Iso(Today.AddDays(5))))).Errors.Keys);
        Assert.Contains("kindKey", (await RejectedAsync(With(toll, "kindKey", "Yok"))).Errors.Keys);
        Assert.Contains("tripId", (await RejectedAsync(With(toll, "tripId", Guid.NewGuid().ToString()))).Errors.Keys);

        // Kategoriye bağlı kurallar: yakıtta litre zorunlu, diğerlerinde litre ve kilometre yok, rücu yalnızca cezada.
        var fuel = Fuel(trip.Id);
        fuel.Remove("liters");
        Assert.Contains("liters", (await RejectedAsync(fuel)).Errors.Keys);
        Assert.Contains("liters", (await RejectedAsync(With(toll, "liters", 5))).Errors.Keys);
        Assert.Contains("odometerKm", (await RejectedAsync(With(toll, "odometerKm", 120000))).Errors.Keys);
        Assert.Contains("chargeToDriver", (await RejectedAsync(With(toll, "chargeToDriver", true))).Errors.Keys);
        var fine = await PostAsync(operations, Expenses, With(With(With(toll, "kindKey", "Fine"), "description", "Hız cezası"), "chargeToDriver", true));
        Assert.True(fine.Values["chargeToDriver"]!.GetValue<bool>());

        // İptal edilmiş sefere masraf açılmaz.
        var cancelled = await ActAsync(manager, await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) }), "cancel", "Müşteri vazgeçti", "trips");
        Assert.Equal(HttpStatusCode.Conflict, (await operations.PostAsJsonAsync(Expenses, new RecordWriteRequest(Toll(cancelled.Id)))).StatusCode);

        // Gerekçesiz ret 422; çifte karar 409.
        var pending = await PostAsync(operations, Expenses, Toll(trip.Id));
        var noReason = await manager.PostAsJsonAsync($"/api/logistics/expenses/{pending.Id}/actions", new RunLogisticsActionRequest("reject", pending.Version, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noReason.StatusCode);
        var approved = await ActAsync(manager, pending, "approve");
        var twice = await manager.PostAsJsonAsync($"/api/logistics/expenses/{approved.Id}/actions", new RunLogisticsActionRequest("approve", approved.Version, null));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        var rejectAfter = await manager.PostAsJsonAsync($"/api/logistics/expenses/{approved.Id}/actions", new RunLogisticsActionRequest("reject", approved.Version, "Geç"));
        Assert.Equal(HttpStatusCode.Conflict, rejectAfter.StatusCode);
    }

    [Fact]
    public async Task A_rejected_expense_is_corrected_with_a_document_number_and_resubmitted_then_approved_and_locked()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        using var reader = await LoginAsync(s.Reader);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });

        // Belge no kırpılır ve boş bırakılabilir; çok uzunu reddedilir.
        var toll = Toll(trip.Id);
        toll["documentNo"] = "  FŞ-2026-001 ";
        var expense = await PostAsync(operations, Expenses, toll);
        Assert.Equal("FŞ-2026-001", expense.Values["documentNo"]!.GetValue<string>());
        var tooLong = Toll(trip.Id);
        tooLong["documentNo"] = new string('9', 51);
        Assert.Contains("documentNo", (await ValidationProblemAsync(await operations.PostAsJsonAsync(Expenses, new RecordWriteRequest(tooLong)))).Errors.Keys);

        // Beklemedeki masraf yeniden gönderilemez; reddedilince giren düzeltir.
        Assert.Equal(HttpStatusCode.Conflict, (await operations.PostAsJsonAsync($"/api/logistics/expenses/{expense.Id}/actions",
            new RunLogisticsActionRequest("resubmit", expense.Version, null))).StatusCode);
        var rejected = await ActAsync(manager, expense, "reject", "Tutar fişle uyuşmuyor");
        Assert.Equal("Tutar fişle uyuşmuyor", rejected.Values["statusReason"]!.GetValue<string>());

        // Yalnızca okuma izni olan yeniden gönderemez ve düzeltemez; giren eylem listesinde "resubmit" görür, onay/ret görmez.
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync($"/api/logistics/expenses/{rejected.Id}/actions",
            new RunLogisticsActionRequest("resubmit", rejected.Version, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"{Expenses}/{rejected.Id}",
            new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, rejected.Version))).StatusCode);
        var opsActions = (await operations.GetFromJsonAsync<List<LogisticsActionDto>>($"/api/logistics/expenses/{rejected.Id}/actions"))!;
        Assert.Contains(opsActions, a => a.Action == "resubmit" && a.Allowed && !a.RequiresReason);
        Assert.DoesNotContain(opsActions, a => a.Action is "approve" or "reject");

        var corrected = await PutAsync(operations, $"{Expenses}/{rejected.Id}", rejected.Version, new JsonObject { ["amount"] = 1100, ["documentNo"] = "FŞ-2026-002" });
        Assert.Equal("rejected", corrected.Values["status"]!.GetValue<string>()); // düzeltme durumu değiştirmez
        Assert.Equal("FŞ-2026-002", corrected.Values["documentNo"]!.GetValue<string>());

        var resubmitted = await ActAsync(operations, corrected, "resubmit");
        Assert.Equal("pending", resubmitted.Values["status"]!.GetValue<string>());
        Assert.Null(resubmitted.Values["statusReason"]);

        var approved = await ActAsync(manager, resubmitted, "approve");
        Assert.Equal("approved", approved.Values["status"]!.GetValue<string>());

        // Onaylanmış masraf değişmez ve yeniden gönderilemez.
        Assert.Equal(HttpStatusCode.Conflict, (await operations.PutAsJsonAsync($"{Expenses}/{approved.Id}",
            new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, approved.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await operations.PostAsJsonAsync($"/api/logistics/expenses/{approved.Id}/actions",
            new RunLogisticsActionRequest("resubmit", approved.Version, null))).StatusCode);
        Assert.Equal(1100m, (await GetAsync(operations, $"{Expenses}/{approved.Id}")).Values["amount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Another_tenants_expenses_cannot_be_seen_decided_or_attached_to_foreign_trips()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var opsA = await LoginAsync(a.Operations);
        using var opsB = await LoginAsync(b.Operations);
        using var managerB = await LoginAsync(b.Manager);
        var tripA = await PostAsync(opsA, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expenseA = await PostAsync(opsA, Expenses, Toll(tripA.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await opsB.GetAsync($"{Expenses}/{expenseA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await opsB.PutAsJsonAsync($"{Expenses}/{expenseA.Id}",
            new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, expenseA.Version))).StatusCode);
        Assert.Empty((await opsB.GetFromJsonAsync<RecordPageResponse>(Expenses))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await managerB.PostAsJsonAsync($"/api/logistics/expenses/{expenseA.Id}/actions",
            new RunLogisticsActionRequest("approve", expenseA.Version, null))).StatusCode);

        // Reddedilmiş masrafı başka tenant yeniden gönderemez ve düzeltemez.
        using var managerA = await LoginAsync(a.Manager);
        var rejectedA = await ActAsync(managerA, expenseA, "reject", "Fiş eksik");
        Assert.Equal(HttpStatusCode.NotFound, (await opsB.PostAsJsonAsync($"/api/logistics/expenses/{rejectedA.Id}/actions",
            new RunLogisticsActionRequest("resubmit", rejectedA.Version, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await opsB.PutAsJsonAsync($"{Expenses}/{rejectedA.Id}",
            new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, rejectedA.Version))).StatusCode);

        // Başka tenant'ın seferine masraf açılamaz (sefer "bulunamadı").
        var foreign = await ValidationProblemAsync(await opsB.PostAsJsonAsync(Expenses, new RecordWriteRequest(Toll(tripA.Id))));
        Assert.Contains("tripId", foreign.Errors.Keys);

        Assert.Equal("rejected", (await GetAsync(opsA, $"{Expenses}/{expenseA.Id}")).Values["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Without_write_approve_or_read_permission_expenses_cannot_be_entered_decided_or_seen()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        using var reader = await LoginAsync(s.Reader);
        using var outsider = await LoginAsync(s.Outsider);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(operations, Expenses, Toll(trip.Id));

        // Yalnızca okuma izni olan görür ama giremez ve düzenleyemez.
        Assert.Equal(expense.Id, (await GetAsync(reader, $"{Expenses}/{expense.Id}")).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Expenses, new RecordWriteRequest(Toll(trip.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"{Expenses}/{expense.Id}",
            new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, expense.Version))).StatusCode);

        // Giren (operasyon) onaylayamaz ve eylem listesinde onay/ret yoktur; müdür ikisini de görür.
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.PostAsJsonAsync($"/api/logistics/expenses/{expense.Id}/actions",
            new RunLogisticsActionRequest("approve", expense.Version, null))).StatusCode);
        var opsActions = (await operations.GetFromJsonAsync<List<LogisticsActionDto>>($"/api/logistics/expenses/{expense.Id}/actions"))!;
        Assert.DoesNotContain(opsActions, a => a.Action is "approve" or "reject");
        var managerActions = (await manager.GetFromJsonAsync<List<LogisticsActionDto>>($"/api/logistics/expenses/{expense.Id}/actions"))!;
        Assert.Contains(managerActions, a => a.Action == "approve" && a.Allowed);
        Assert.Contains(managerActions, a => a.Action == "reject" && a.Allowed && a.RequiresReason);
        Assert.Equal("pending", (await GetAsync(manager, $"{Expenses}/{expense.Id}")).Values["status"]!.GetValue<string>());

        // Okuma izni olmayan hiçbir şey göremez.
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"{Expenses}/{expense.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(Expenses)).StatusCode);
    }

    [Fact]
    public async Task Expense_list_resolves_trip_numbers_only_for_readers_of_trips_in_the_same_tenant()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var managerA = await LoginAsync(a.Manager);
        using var readerA = await LoginAsync(a.Reader);
        using var managerB = await LoginAsync(b.Manager);
        var trip = await PostAsync(managerA, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var number = trip.Values["number"]!.GetValue<string>();

        async Task<string> ResolveAsync(HttpClient client)
        {
            var response = await client.PostAsJsonAsync($"/api/lookups/{LogisticsAccess.ExpenseTripLookup}/resolve", new ResolveLookupRequest([trip.Id]));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        // Sefer okuma izni olan kendi tenant'ında numarayı görür; yalnızca masraf okuyan ve başka tenant'ın kullanıcısı göremez.
        var own = await ResolveAsync(managerA);
        Assert.Contains(number, own);
        Assert.DoesNotContain(number, await ResolveAsync(readerA));
        Assert.DoesNotContain(number, await ResolveAsync(managerB));
    }

    // --- iptal edilmiş seferin masrafı (ADR-0038 bölüm 25) ---

    /// <summary>
    /// Mutlu yol ve kural: açık seferin bekleyen masrafı düzenlenir; sefer iptal edilince beklemedeki ve reddedilmiş masraf düzenlenmez (409) ve
    /// değişmez. Karar komutları açık kalır: bekleyen masraf reddedilerek kapatılır, reddedilen yeniden gönderilebilir.
    /// </summary>
    [Fact]
    public async Task Expenses_of_a_cancelled_trip_are_not_edited_but_can_still_be_decided()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var pending = await PostAsync(operations, Expenses, Toll(trip.Id));
        var toReject = await PostAsync(operations, Expenses, Toll(trip.Id));
        pending = await PutAsync(operations, $"{Expenses}/{pending.Id}", pending.Version, new JsonObject { ["amount"] = 1300 });
        var rejected = await ActAsync(manager, toReject, "reject", "Fiş eksik");

        Assert.Equal("cancelled", (await ActAsync(operations, trip, "cancel", "Müşteri vazgeçti", kind: "trips")).Values["status"]!.GetValue<string>());

        foreach (var expense in new[] { pending, rejected })
        {
            var denied = await operations.PutAsJsonAsync($"{Expenses}/{expense.Id}", new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, expense.Version));
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            Assert.Contains("İptal edilen seferin masrafı", (await denied.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        }

        Assert.Equal(1300m, (await GetAsync(operations, $"{Expenses}/{pending.Id}")).Values["amount"]!.GetValue<decimal>());
        Assert.Equal(1200m, (await GetAsync(operations, $"{Expenses}/{rejected.Id}")).Values["amount"]!.GetValue<decimal>());

        // Karar komutları kapıdan geçer: bekleyen reddedilir, reddedilen yeniden gönderilir.
        Assert.Equal("rejected", (await ActAsync(manager, pending, "reject", "Sefer iptal edildi")).Values["status"]!.GetValue<string>());
        Assert.Equal("pending", (await ActAsync(operations, rejected, "resubmit")).Values["status"]!.GetValue<string>());
    }

    /// <summary>
    /// Tenant izolasyonu ve yetki: iptal edilmiş seferin masrafında da sıra korunur; başka tenant 404 alır (kural iletisi kaydın varlığını sızdırmaz),
    /// yalnızca okuyan 403 alır (izin kuraldan önce sorulur).
    /// </summary>
    [Fact]
    public async Task On_a_cancelled_trip_other_tenants_get_not_found_and_readers_get_forbidden_before_the_rule()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var opsA = await LoginAsync(a.Operations);
        using var opsB = await LoginAsync(b.Operations);
        using var readerA = await LoginAsync(a.Reader);
        var trip = await PostAsync(opsA, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(opsA, Expenses, Toll(trip.Id));
        await ActAsync(opsA, trip, "cancel", "Müşteri vazgeçti", kind: "trips");

        Assert.Equal(HttpStatusCode.NotFound, (await opsB.PutAsJsonAsync($"{Expenses}/{expense.Id}",
            new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, expense.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerA.PutAsJsonAsync($"{Expenses}/{expense.Id}",
            new RecordWriteRequest(new JsonObject { ["amount"] = 1 }, expense.Version))).StatusCode);
        Assert.Equal(1200m, (await GetAsync(opsA, $"{Expenses}/{expense.Id}")).Values["amount"]!.GetValue<decimal>());
    }

    // --- iptal edilen seferin masrafı: gerekçeli onay ve uyarı (ADR-0038 bölüm 26) ---

    /// <summary>
    /// Mutlu yol ve kural: açık seferde onay gerekçe istemez; sefer iptal edilirken bekleyen masraf sayısı uyarı olarak gelir. İptalden sonra masraf
    /// kaydı "seferi iptal edildi" der, onay gerekçe ister (gerekçesiz 422, masraf beklemede kalır); gerekçeyle onaylanır ve gerekçe masrafta kalır.
    /// Ret değişmez.
    /// </summary>
    [Fact]
    public async Task Approving_an_expense_of_a_cancelled_trip_needs_a_reason_and_the_expense_says_its_trip_was_cancelled()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var fuel = await PostAsync(operations, Expenses, Fuel(trip.Id));
        var toll = await PostAsync(operations, Expenses, Toll(trip.Id));

        Assert.False(fuel.Values["tripCancelled"]!.GetValue<bool>());
        var before = await ActionAsync(manager, "expenses", fuel.Id, "approve");
        Assert.False(before.RequiresReason);
        Assert.Null(before.Warning);

        // İptal adımı bekleyen masrafları söyler; iptal engellenmez.
        var cancel = await ActionAsync(operations, "trips", trip.Id, "cancel");
        Assert.True(cancel.Allowed);
        Assert.Contains("2 bekleyen masrafı", cancel.Warning);
        await ActAsync(operations, trip, "cancel", "Müşteri vazgeçti", kind: "trips");

        // Masraf kaydı ve liste seferin iptal edildiğini söyler (kuyruktaki rozet buradan).
        Assert.True((await GetAsync(manager, $"{Expenses}/{fuel.Id}")).Values["tripCancelled"]!.GetValue<bool>());
        Assert.All((await manager.GetFromJsonAsync<RecordPageResponse>($"{Expenses}?status=pending"))!.Items,
            e => Assert.True(e.Values["tripCancelled"]!.GetValue<bool>()));

        var approve = await ActionAsync(manager, "expenses", fuel.Id, "approve");
        Assert.True(approve.Allowed);
        Assert.True(approve.RequiresReason);
        Assert.Contains("gerekçe", approve.Warning);

        var withoutReason = await manager.PostAsJsonAsync($"/api/logistics/expenses/{fuel.Id}/actions", new RunLogisticsActionRequest("approve", fuel.Version, "  "));
        Assert.Contains("reason", (await ValidationProblemAsync(withoutReason)).Errors.Keys);
        Assert.Equal("pending", (await GetAsync(manager, $"{Expenses}/{fuel.Id}")).Values["status"]!.GetValue<string>());

        var approved = await ActAsync(manager, fuel, "approve", "Yakıt iptalden önce depoda alındı");
        Assert.Equal("approved", approved.Values["status"]!.GetValue<string>());
        Assert.Equal("Yakıt iptalden önce depoda alındı", approved.Values["statusReason"]!.GetValue<string>());
        Assert.Equal("rejected", (await ActAsync(manager, toll, "reject", "Geçiş yapılmadı")).Values["status"]!.GetValue<string>());

        var history = (await manager.GetFromJsonAsync<AuditPageResponse>($"{Expenses}/{fuel.Id}/history"))!;
        Assert.Contains(history.Items, h => h.Changes.Any(c => c.Field == "status"));
    }

    /// <summary>
    /// Tenant izolasyonu ve yetki: başka tenant iptal edilen seferin masrafını göremez, eylemlerini alamaz, gerekçeyle bile onaylayamaz (404); onay
    /// izni olmayan gerekçeyle de onaylayamaz (403) ve onay eylemini görmez; seferi yöneten ama masrafları göremeyen kişiye iptal uyarısı masraf sayısını
    /// söylemez.
    /// </summary>
    [Fact]
    public async Task Cancelled_trip_expense_approval_is_hidden_from_other_tenants_and_from_users_without_the_permission()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var opsA = await LoginAsync(a.Operations);
        using var dispatcherA = await LoginAsync(a.Dispatcher);
        using var managerB = await LoginAsync(b.Manager);
        var trip = await PostAsync(opsA, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(opsA, Expenses, Toll(trip.Id));

        var dispatcherCancel = await ActionAsync(dispatcherA, "trips", trip.Id, "cancel");
        Assert.True(dispatcherCancel.Allowed);
        Assert.Null(dispatcherCancel.Warning);
        await ActAsync(dispatcherA, trip, "cancel", "Araç arızalandı", kind: "trips");

        Assert.Equal(HttpStatusCode.NotFound, (await managerB.GetAsync($"/api/logistics/expenses/{expense.Id}/actions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await managerB.PostAsJsonAsync($"/api/logistics/expenses/{expense.Id}/actions",
            new RunLogisticsActionRequest("approve", expense.Version, "Yabancı"))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await opsA.PostAsJsonAsync($"/api/logistics/expenses/{expense.Id}/actions",
            new RunLogisticsActionRequest("approve", expense.Version, "Kendi masrafım"))).StatusCode);
        Assert.DoesNotContain((await opsA.GetFromJsonAsync<List<LogisticsActionDto>>($"/api/logistics/expenses/{expense.Id}/actions"))!, x => x.Action == "approve");

        Assert.Equal("pending", (await GetAsync(opsA, $"{Expenses}/{expense.Id}")).Values["status"]!.GetValue<string>());
    }

    private static async Task<LogisticsActionDto> ActionAsync(HttpClient client, string kind, Guid id, string action) =>
        Assert.Single((await client.GetFromJsonAsync<List<LogisticsActionDto>>($"/api/logistics/{kind}/{id}/actions"))!, x => x.Action == action);

    // --- sefer iptalinde bekleyen masrafların otomatik reddi: firma ayarı (ADR-0038 bölüm 30) ---

    /// <summary>
    /// Mutlu yol: ayar "Reddedilsin" iken iptal adımının uyarısı masrafların reddedileceğini söyler; iptal bekleyen masrafları "Sefer iptal edildi:
    /// &lt;gerekçe&gt;" ile reddeder, onaylanmış ve önceden reddedilmiş masraf değişmez, denetim kaydı yazılır. Gerçek harcama yeniden onaya gönderilip
    /// başka sefere aktarılabilir.
    /// </summary>
    [Fact]
    public async Task With_the_reject_setting_cancelling_a_trip_rejects_its_pending_expenses_and_leaves_decided_ones()
    {
        var s = await SetUpAsync();
        await SetCancelPendingExpensesAsync(s.TenantId, LogisticsModuleSettings.Reject);
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var pending = await PostAsync(operations, Expenses, Fuel(trip.Id));
        var approved = await ActAsync(manager, await PostAsync(operations, Expenses, Toll(trip.Id)), "approve");
        var rejected = await ActAsync(manager, await PostAsync(operations, Expenses, Toll(trip.Id)), "reject", "Fiş eksik");

        Assert.Contains("reddedilir", (await ActionAsync(operations, "trips", trip.Id, "cancel")).Warning);
        Assert.Equal("cancelled", (await ActAsync(operations, trip, "cancel", "Müşteri vazgeçti", kind: "trips")).Values["status"]!.GetValue<string>());

        var auto = await GetAsync(manager, $"{Expenses}/{pending.Id}");
        Assert.Equal(("rejected", "Sefer iptal edildi: Müşteri vazgeçti"), (auto.Values["status"]!.GetValue<string>(), auto.Values["statusReason"]!.GetValue<string>()));
        Assert.Equal("approved", (await GetAsync(manager, $"{Expenses}/{approved.Id}")).Values["status"]!.GetValue<string>());
        Assert.Equal(rejected.Version, (await GetAsync(manager, $"{Expenses}/{rejected.Id}")).Version);
        var history = (await manager.GetFromJsonAsync<AuditPageResponse>($"{Expenses}/{pending.Id}/history"))!;
        Assert.Contains(history.Items, h => h.Changes.Any(c => c.Field == "status"));

        // Gerçek harcamaydı: yeniden onaya gönderilir ve açık sefere aktarılır.
        var resubmitted = await ActAsync(operations, auto, "resubmit");
        var open = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var moved = await operations.PostAsJsonAsync($"/api/logistics/expenses/{resubmitted.Id}/transfer",
            new TransferExpenseRequest(open.Id, resubmitted.Version, "Yakıt yeni seferde kullanıldı"));
        Assert.True(moved.StatusCode == HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Varsayılan ("Beklemede kalsın") davranış değişmez: ayar seçenekleri ve varsayılanı, iptal sonrası bekleyen masraf beklemede kalır ve uyarı
    /// onaylayanın karar vereceğini söyler. Geçersiz ayar değeri kaydedilmez (422).
    /// </summary>
    [Fact]
    public async Task By_default_cancelling_a_trip_keeps_pending_expenses_and_an_unknown_setting_value_is_refused()
    {
        var s = await SetUpAsync();
        var admin = await AdminAsync(s.TenantId);
        using var adminClient = await LoginAsync(admin);
        var setting = (await adminClient.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Settings.Single(x => x.Key == LogisticsModuleSettings.Key);
        Assert.Equal(LogisticsModuleSettings.Keep, setting.Value);
        var invalid = await adminClient.PutAsJsonAsync("/api/settings",
            new SaveSettingsRequest(new Dictionary<string, SettingWriteDto> { [LogisticsModuleSettings.Key] = new("hepsini-sil", setting.Version) }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        using var operations = await LoginAsync(s.Operations);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(operations, Expenses, Toll(trip.Id));
        Assert.Contains("beklemede kalır", (await ActionAsync(operations, "trips", trip.Id, "cancel")).Warning);
        await ActAsync(operations, trip, "cancel", "Müşteri vazgeçti", kind: "trips");

        Assert.Equal("pending", (await GetAsync(operations, $"{Expenses}/{expense.Id}")).Values["status"]!.GetValue<string>());
    }

    /// <summary>Tenant izolasyonu: ayar firmaya özgüdür. A'da "Reddedilsin" açıkken B'nin iptali B'nin masrafını reddetmez; A'nın iptali yalnızca A'nınkini.</summary>
    [Fact]
    public async Task The_reject_setting_of_one_tenant_does_not_touch_another_tenants_expenses()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        await SetCancelPendingExpensesAsync(a.TenantId, LogisticsModuleSettings.Reject);
        using var opsA = await LoginAsync(a.Operations);
        using var opsB = await LoginAsync(b.Operations);
        var tripA = await PostAsync(opsA, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var tripB = await PostAsync(opsB, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expenseA = await PostAsync(opsA, Expenses, Toll(tripA.Id));
        var expenseB = await PostAsync(opsB, Expenses, Toll(tripB.Id));

        // A, B'nin seferini iptal edemez (404); B'nin iptali B'nin ayarına (varsayılan) göre davranır.
        Assert.Equal(HttpStatusCode.NotFound, (await opsA.PostAsJsonAsync($"/api/logistics/trips/{tripB.Id}/actions",
            new RunLogisticsActionRequest("cancel", tripB.Version, "Yabancı"))).StatusCode);
        await ActAsync(opsB, tripB, "cancel", "Müşteri vazgeçti", kind: "trips");
        Assert.Equal("pending", (await GetAsync(opsB, $"{Expenses}/{expenseB.Id}")).Values["status"]!.GetValue<string>());

        await ActAsync(opsA, tripA, "cancel", "Müşteri vazgeçti", kind: "trips");
        Assert.Equal("rejected", (await GetAsync(opsA, $"{Expenses}/{expenseA.Id}")).Values["status"]!.GetValue<string>());
        Assert.Equal("pending", (await GetAsync(opsB, $"{Expenses}/{expenseB.Id}")).Values["status"]!.GetValue<string>());
    }

    /// <summary>
    /// Yetki: ret kararı firmanın ayarıdır, iptal edenin değil. Sefer yazma izni olmayan iptal edemez ve masraflar değişmez (403); seferi yöneten ama
    /// masraf izni olmayan kişinin iptali masrafları reddeder ve ona masraf sayısı söylenmez. Ayarı yalnızca yönetici değiştirir (403).
    /// </summary>
    [Fact]
    public async Task Only_a_trip_writer_can_trigger_the_automatic_rejection_and_only_an_admin_can_turn_it_on()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        var settings = (await operations.PutAsJsonAsync("/api/settings", new SaveSettingsRequest(
            new Dictionary<string, SettingWriteDto> { [LogisticsModuleSettings.Key] = new(LogisticsModuleSettings.Reject, 0) }))).StatusCode;
        Assert.Equal(HttpStatusCode.Forbidden, settings);

        await SetCancelPendingExpensesAsync(s.TenantId, LogisticsModuleSettings.Reject);
        using var outsider = await LoginAsync(s.Outsider);
        using var dispatcher = await LoginAsync(s.Dispatcher);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(operations, Expenses, Toll(trip.Id));

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync($"/api/logistics/trips/{trip.Id}/actions",
            new RunLogisticsActionRequest("cancel", trip.Version, "Yetkisiz"))).StatusCode);
        Assert.Equal("pending", (await GetAsync(operations, $"{Expenses}/{expense.Id}")).Values["status"]!.GetValue<string>());

        Assert.Null((await ActionAsync(dispatcher, "trips", trip.Id, "cancel")).Warning);
        await ActAsync(dispatcher, trip, "cancel", "Araç arızalandı", kind: "trips");
        Assert.Equal("rejected", (await GetAsync(operations, $"{Expenses}/{expense.Id}")).Values["status"]!.GetValue<string>());
    }

    private static class LogisticsModuleSettings
    {
        public const string Key = Modulix.Modules.Logistics.LogisticsModule.SettingKeys.CancelPendingExpenses;
        public const string Keep = Modulix.Modules.Logistics.LogisticsModule.CancelPendingExpenseModes.Keep;
        public const string Reject = Modulix.Modules.Logistics.LogisticsModule.CancelPendingExpenseModes.Reject;
    }

    /// <summary>Kurulumda açılan firmalar: ayar değiştirecek yönetici kullanıcı bu firmaya eklenir.</summary>
    private readonly Dictionary<Guid, Modulix.Core.Platform.Tenant> tenants = [];

    private async Task<ModulixUser> AdminAsync(Guid tenantId)
    {
        var admin = await factory.CreateUserAsync(tenants[tenantId]);
        await factory.MakeAdministratorAsync(tenantId, admin);
        return admin;
    }

    private async Task SetCancelPendingExpensesAsync(Guid tenantId, string value)
    {
        using var admin = await LoginAsync(await AdminAsync(tenantId));
        var current = (await admin.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Settings.Single(x => x.Key == LogisticsModuleSettings.Key);
        var response = await admin.PutAsJsonAsync("/api/settings",
            new SaveSettingsRequest(new Dictionary<string, SettingWriteDto> { [LogisticsModuleSettings.Key] = new(value, current.Version) }));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // --- iptal edilen seferin masrafını başka sefere aktarma (ADR-0038 bölüm 28) ---

    /// <summary>
    /// Mutlu yol: iptal edilen seferin bekleyen masrafı (makbuzuyla) açık bir sefere aktarılır. Durum beklemede kalır, gerekçe masrafta görünür,
    /// masraf yeniden düzenlenebilir, makbuz onunla gider, açık seferde onay gerekçesiz yapılır; denetim kaydı sefer değişimini gösterir.
    /// </summary>
    [Fact]
    public async Task A_pending_expense_of_a_cancelled_trip_moves_to_an_open_trip_with_its_receipt()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var cancelled = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var target = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today.AddDays(1)) });
        var fuel = await PostAsync(operations, Expenses, Fuel(cancelled.Id));
        var receipt = await UploadAsync(operations, fuel.Id, "fis.png");
        await ActAsync(operations, cancelled, "cancel", "Müşteri vazgeçti", kind: "trips");
        fuel = await GetAsync(operations, $"{Expenses}/{fuel.Id}");

        var moved = await TransferAsync(operations, fuel, target.Id, "  Yakıt depoda alındı, sevkiyat bu sefere atandı ");
        Assert.Equal(target.Id.ToString(), moved.Values["tripId"]!.GetValue<string>());
        Assert.False(moved.Values["tripCancelled"]!.GetValue<bool>());
        Assert.Equal("pending", moved.Values["status"]!.GetValue<string>());
        Assert.Equal("Yakıt depoda alındı, sevkiyat bu sefere atandı", moved.Values["statusReason"]!.GetValue<string>());
        Assert.Equal(receipt.Id, Assert.Single(await ReceiptsAsync(operations, moved.Id)).Id);
        Assert.Single((await operations.GetFromJsonAsync<RecordPageResponse>($"{Expenses}?tripId={target.Id}"))!.Items);
        Assert.Empty((await operations.GetFromJsonAsync<RecordPageResponse>($"{Expenses}?tripId={cancelled.Id}"))!.Items);

        // Açık seferde masraf yine düzenlenir ve gerekçesiz onaylanır.
        moved = await PutAsync(operations, $"{Expenses}/{moved.Id}", moved.Version, new JsonObject { ["amount"] = 5200 });
        Assert.False((await ActionAsync(manager, "expenses", moved.Id, "approve")).RequiresReason);
        Assert.Equal("approved", (await ActAsync(manager, moved, "approve")).Values["status"]!.GetValue<string>());

        var history = (await manager.GetFromJsonAsync<AuditPageResponse>($"{Expenses}/{fuel.Id}/history"))!;
        Assert.Contains(history.Items, h => h.Changes.Any(c => c.Field == "tripId"));
    }

    /// <summary>
    /// Kural ve doğrulama: gerekçe zorunlu; hedef iptal edilmiş, bilinmeyen ya da aynı sefer olamaz (422); açık seferdeki, reddedilmiş ya da
    /// onaylanmış masraf aktarılmaz (409); eski sürüm 409. Hatalı denemeler masrafı değiştirmez.
    /// </summary>
    [Fact]
    public async Task Only_a_pending_expense_of_a_cancelled_trip_moves_and_only_to_another_open_trip_with_a_reason()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var open = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var cancelled = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var alsoCancelled = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var onOpenTrip = await PostAsync(operations, Expenses, Toll(open.Id));
        var pending = await PostAsync(operations, Expenses, Toll(cancelled.Id));
        var rejected = await ActAsync(manager, await PostAsync(operations, Expenses, Toll(cancelled.Id)), "reject", "Fiş eksik");
        var approved = await ActAsync(manager, await PostAsync(operations, Expenses, Toll(cancelled.Id)), "approve");
        await ActAsync(operations, cancelled, "cancel", "Müşteri vazgeçti", kind: "trips");
        await ActAsync(operations, alsoCancelled, "cancel", "Araç arızalandı", kind: "trips");
        pending = await GetAsync(operations, $"{Expenses}/{pending.Id}");

        Assert.Contains("reason", (await ValidationProblemAsync(await PostTransferAsync(operations, pending, open.Id, "   "))).Errors.Keys);
        Assert.Contains("tripId", (await ValidationProblemAsync(await PostTransferAsync(operations, pending, alsoCancelled.Id, "Gerekçe"))).Errors.Keys);
        Assert.Contains("tripId", (await ValidationProblemAsync(await PostTransferAsync(operations, pending, Guid.NewGuid(), "Gerekçe"))).Errors.Keys);
        Assert.Contains("tripId", (await ValidationProblemAsync(await PostTransferAsync(operations, pending, cancelled.Id, "Gerekçe"))).Errors.Keys);

        Assert.Equal(HttpStatusCode.Conflict, (await PostTransferAsync(operations, onOpenTrip, open.Id, "Gerekçe")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostTransferAsync(operations, rejected, open.Id, "Gerekçe")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostTransferAsync(operations, approved, open.Id, "Gerekçe")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await operations.PostAsJsonAsync($"/api/logistics/expenses/{pending.Id}/transfer",
            new TransferExpenseRequest(open.Id, pending.Version - 1, "Gerekçe"))).StatusCode);

        var unchanged = await GetAsync(operations, $"{Expenses}/{pending.Id}");
        Assert.Equal((cancelled.Id.ToString(), pending.Version), (unchanged.Values["tripId"]!.GetValue<string>(), unchanged.Version));
    }

    /// <summary>Tenant izolasyonu: başka tenant masrafı aktaramaz (404); masraf başka tenant'ın seferine aktarılamaz (sefer "bulunamadı").</summary>
    [Fact]
    public async Task Expenses_cannot_be_moved_across_tenants()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var opsA = await LoginAsync(a.Operations);
        using var opsB = await LoginAsync(b.Operations);
        var cancelled = await PostAsync(opsA, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var openA = await PostAsync(opsA, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var tripB = await PostAsync(opsB, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(opsA, Expenses, Toll(cancelled.Id));
        await ActAsync(opsA, cancelled, "cancel", "Müşteri vazgeçti", kind: "trips");
        expense = await GetAsync(opsA, $"{Expenses}/{expense.Id}");

        Assert.Equal(HttpStatusCode.NotFound, (await PostTransferAsync(opsB, expense, tripB.Id, "Yabancı")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostTransferAsync(opsB, expense, openA.Id, "Yabancı")).StatusCode);
        Assert.Contains("tripId", (await ValidationProblemAsync(await PostTransferAsync(opsA, expense, tripB.Id, "Başka firmanın seferi"))).Errors.Keys);

        Assert.Equal(cancelled.Id.ToString(), (await GetAsync(opsA, $"{Expenses}/{expense.Id}")).Values["tripId"]!.GetValue<string>());
    }

    /// <summary>Yetki: masraf yazma izni olmayan aktaramaz (yalnızca okuyan 403); masraf okuma izni olmayan (seferi yöneten bile) 403 alır.</summary>
    [Fact]
    public async Task Without_expense_write_permission_an_expense_cannot_be_moved()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var reader = await LoginAsync(s.Reader);
        using var dispatcher = await LoginAsync(s.Dispatcher);
        var cancelled = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var open = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(operations, Expenses, Toll(cancelled.Id));
        await ActAsync(operations, cancelled, "cancel", "Müşteri vazgeçti", kind: "trips");
        expense = await GetAsync(operations, $"{Expenses}/{expense.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(reader, expense, open.Id, "Okuyan")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(dispatcher, expense, open.Id, "Seferci")).StatusCode);
        Assert.Equal(cancelled.Id.ToString(), (await GetAsync(operations, $"{Expenses}/{expense.Id}")).Values["tripId"]!.GetValue<string>());
    }

    private static Task<HttpResponseMessage> PostTransferAsync(HttpClient client, RecordResponse expense, Guid tripId, string? reason) =>
        client.PostAsJsonAsync($"/api/logistics/expenses/{expense.Id}/transfer", new TransferExpenseRequest(tripId, expense.Version, reason));

    private static async Task<RecordResponse> TransferAsync(HttpClient client, RecordResponse expense, Guid tripId, string reason)
    {
        var response = await PostTransferAsync(client, expense, tripId, reason);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    // --- makbuz ve fiş: kaydın kuralı (ADR-0038 bölüm 24, ADR-0040 K2) ---

    /// <summary>Mutlu yol: beklemedeki masrafa makbuz eklenir ve silinir; reddedilen masrafın belgesi düzeltilir (yeni fiş eklenir, eskisi silinir).</summary>
    [Fact]
    public async Task Receipts_are_added_and_removed_while_the_expense_is_pending_or_rejected()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(operations, Expenses, Toll(trip.Id));

        var wrong = await UploadAsync(operations, expense.Id, "yanlis-fis.png");
        var extra = await UploadAsync(operations, expense.Id, "ek.png");
        Assert.Equal(HttpStatusCode.NoContent, (await operations.DeleteAsync($"{Files}/{extra.Id}?version={extra.Version}")).StatusCode);
        Assert.Equal(["yanlis-fis.png"], (await ReceiptsAsync(operations, expense.Id)).Select(f => f.FileName));

        // Ret "fiş okunmuyor": giren doğru fişi ekler, yanlışını siler ve yeniden gönderir.
        var rejected = await ActAsync(manager, expense, "reject", "Fiş okunmuyor");
        await UploadAsync(operations, rejected.Id, "dogru-fis.png");
        Assert.Equal(HttpStatusCode.NoContent, (await operations.DeleteAsync($"{Files}/{wrong.Id}?version={wrong.Version}")).StatusCode);
        Assert.Equal(["dogru-fis.png"], (await ReceiptsAsync(manager, rejected.Id)).Select(f => f.FileName));
        Assert.Equal("pending", (await ActAsync(operations, rejected, "resubmit")).Values["status"]!.GetValue<string>());
    }

    /// <summary>
    /// Kural: onaylanmış masrafın ve iptal edilmiş seferdeki masrafın belgesi tür yazma izni olan kimse tarafından eklenemez ve silinemez (403);
    /// okuma ve indirme sürer.
    /// </summary>
    [Fact]
    public async Task Receipts_of_an_approved_expense_or_of_a_cancelled_trip_cannot_be_added_or_removed()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var manager = await LoginAsync(s.Manager);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(operations, Expenses, Toll(trip.Id));
        var receipt = await UploadAsync(operations, expense.Id, "fis.png");

        await ActAsync(manager, expense, "approve");
        foreach (var client in new[] { operations, manager })
        {
            var denied = await PostFileAsync(client, expense.Id, "sonradan.png");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Contains("Onaylanmış masraf", (await denied.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{Files}/{receipt.Id}?version={receipt.Version}")).StatusCode);
        }

        Assert.Equal(["fis.png"], (await ReceiptsAsync(manager, expense.Id)).Select(f => f.FileName));
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"{Files}/{receipt.Id}/content")).StatusCode);

        // İptal edilen seferin beklemedeki masrafı: belge eklenmez, silinmez.
        var cancelledTrip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var pending = await PostAsync(operations, Expenses, Toll(cancelledTrip.Id));
        var pendingReceipt = await UploadAsync(operations, pending.Id, "bekleyen.png");
        Assert.Equal("cancelled", (await ActAsync(operations, cancelledTrip, "cancel", "Müşteri vazgeçti", kind: "trips")).Values["status"]!.GetValue<string>());
        var afterCancel = await PostFileAsync(operations, pending.Id, "iptal-sonrasi.png");
        Assert.Equal(HttpStatusCode.Forbidden, afterCancel.StatusCode);
        Assert.Contains("İptal edilen sefer", (await afterCancel.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.Equal(HttpStatusCode.Forbidden, (await operations.DeleteAsync($"{Files}/{pendingReceipt.Id}?version={pendingReceipt.Version}")).StatusCode);
        Assert.Single(await ReceiptsAsync(operations, pending.Id));
    }

    /// <summary>Tenant izolasyonu: başka tenant'ın masrafına belge eklenemez; belgesi listelenmez, indirilmez, silinmez (404).</summary>
    [Fact]
    public async Task Another_tenant_cannot_list_download_add_or_remove_expense_receipts()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var opsA = await LoginAsync(a.Operations);
        using var opsB = await LoginAsync(b.Operations);
        var tripA = await PostAsync(opsA, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expenseA = await PostAsync(opsA, Expenses, Toll(tripA.Id));
        var receiptA = await UploadAsync(opsA, expenseA.Id, "fis.png");

        Assert.Equal(HttpStatusCode.NotFound, (await PostFileAsync(opsB, expenseA.Id, "yabanci.png")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await opsB.GetAsync($"{Files}?entityKey={LogisticsAccess.TripExpenseEntityKey}&recordId={expenseA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await opsB.GetAsync($"{Files}/{receiptA.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await opsB.DeleteAsync($"{Files}/{receiptA.Id}?version={receiptA.Version}")).StatusCode);

        Assert.Equal(["fis.png"], (await ReceiptsAsync(opsA, expenseA.Id)).Select(f => f.FileName));
    }

    /// <summary>Yetki: yalnızca okuyan beklemedeki masrafın belgesini görür ama ekleyemez, silemez (403); masrafı okuyamayan ulaşamaz.</summary>
    [Fact]
    public async Task Without_expense_write_permission_receipts_cannot_be_added_or_removed_even_while_pending()
    {
        var s = await SetUpAsync();
        using var operations = await LoginAsync(s.Operations);
        using var reader = await LoginAsync(s.Reader);
        using var outsider = await LoginAsync(s.Outsider);
        var trip = await PostAsync(operations, Trips, new JsonObject { ["plannedStart"] = Iso(Today) });
        var expense = await PostAsync(operations, Expenses, Toll(trip.Id));
        var receipt = await UploadAsync(operations, expense.Id, "fis.png");

        Assert.Single(await ReceiptsAsync(reader, expense.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostFileAsync(reader, expense.Id, "okuyan.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"{Files}/{receipt.Id}?version={receipt.Version}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await PostFileAsync(outsider, expense.Id, "disaridan.png")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await outsider.GetAsync($"{Files}/{receipt.Id}/content")).StatusCode);
        Assert.NotEqual(HttpStatusCode.NoContent, (await outsider.DeleteAsync($"{Files}/{receipt.Id}?version={receipt.Version}")).StatusCode);

        Assert.Single(await ReceiptsAsync(operations, expense.Id));
    }

    // --- yardımcılar ---

    private const string Files = "/api/files";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static Task<HttpResponseMessage> PostFileAsync(HttpClient client, Guid expenseId, string fileName)
    {
        var file = new ByteArrayContent([.. Png, .. Encoding.ASCII.GetBytes(Guid.NewGuid().ToString())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent
        {
            { new StringContent(LogisticsAccess.TripExpenseEntityKey), "entityKey" },
            { new StringContent(expenseId.ToString()), "recordId" },
            { file, "file", fileName },
        };
        return client.PostAsync(Files, form);
    }

    private static async Task<StoredFileDto> UploadAsync(HttpClient client, Guid expenseId, string fileName)
    {
        var response = await PostFileAsync(client, expenseId, fileName);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<StoredFileDto>())!;
    }

    private static async Task<List<StoredFileDto>> ReceiptsAsync(HttpClient client, Guid expenseId) =>
        (await client.GetFromJsonAsync<List<StoredFileDto>>($"{Files}?entityKey={LogisticsAccess.TripExpenseEntityKey}&recordId={expenseId}"))!;

    private static JsonObject Fuel(Guid tripId) => new()
    {
        ["tripId"] = tripId.ToString(), ["kindKey"] = "Fuel", ["description"] = "Dilovası istasyonu", ["expenseDate"] = Iso(Today),
        ["amount"] = 5000, ["currency"] = "try", ["vatAmount"] = 833.33m, ["liters"] = 10, ["odometerKm"] = 120_500,
    };

    private static JsonObject Toll(Guid tripId) => new()
    {
        ["tripId"] = tripId.ToString(), ["kindKey"] = "Toll", ["description"] = "Osmangazi Köprüsü", ["expenseDate"] = Iso(Today),
        ["amount"] = 1200, ["currency"] = "TRY",
    };

    private sealed record Setup(Guid TenantId, ModulixUser Operations, ModulixUser Manager, ModulixUser Reader, ModulixUser Outsider, ModulixUser Dispatcher);

    private async Task<Setup> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        tenants[tenant.Id] = tenant;
        await factory.SetModuleAsync(tenant.Id, "logistics", enabled: true);
        var operations = await factory.CreateUserAsync(tenant);
        var manager = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        var outsider = await factory.CreateUserAsync(tenant);
        var dispatcher = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [TripsRead, TripsWrite, ExpensesRead, ExpensesWrite], Scope.All, operations);
        await factory.CreateRoleAsync(tenant.Id, [TripsRead, TripsWrite, ExpensesRead, ExpensesWrite, ExpensesApprove], Scope.All, manager);
        await factory.CreateRoleAsync(tenant.Id, [ExpensesRead], Scope.All, reader);
        await factory.CreateRoleAsync(tenant.Id, [TripsRead], Scope.All, outsider);
        await factory.CreateRoleAsync(tenant.Id, [TripsRead, TripsWrite], Scope.All, dispatcher); // seferi yönetir, masrafları göremez
        return new Setup(tenant.Id, operations, manager, reader, outsider, dispatcher);
    }

    private static async Task<RecordResponse> ActAsync(HttpClient client, RecordResponse record, string action, string? reason = null, string kind = "expenses")
    {
        var response = await client.PostAsJsonAsync($"/api/logistics/{kind}/{record.Id}/actions", new RunLogisticsActionRequest(action, record.Version, reason));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{action}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private static async Task<RecordResponse> PutAsync(HttpClient client, string url, long version, JsonObject values)
    {
        var response = await client.PutAsJsonAsync(url, new RecordWriteRequest(values, version));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<RecordResponse> GetAsync(HttpClient client, string url) => (await client.GetFromJsonAsync<RecordResponse>(url))!;

    private static async Task<RecordResponse> PostAsync(HttpClient client, string url, JsonObject values)
    {
        var response = await client.PostAsJsonAsync(url, new RecordWriteRequest(values));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private static async Task<ValidationProblemDetails> ValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
    }
}
