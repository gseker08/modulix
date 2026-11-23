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
using Modulix.Shared.Records;
using static Modulix.Modules.Fleet.FleetModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0057: kayıt notu ve notun ekleri. Mutlu yol (araca not, düzenleme, nota dosya eki, not silinince ek erişilemez), doğrulama (zorunlu alanlar,
/// bilinmeyen bağlı tür, nota not, bağlı kayıt değişmez, bağlı kayıtsız liste), tenant izolasyonu (başka tenant'ın kaydına not eklenemez, notu ve eki
/// görünmez) ve yetki (nota erişim bağlı kaydın erişimidir: okuyan görür ama yazamaz, kaydı göremeyen notu da göremez, notu ve ekini yalnızca yazarı değiştirir).
/// </summary>
public class NoteTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Notes = "/api/records/core.note";
    private const string Vehicles = "/api/records/fleet.vehicle";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n");

    [Fact]
    public async Task A_note_is_its_own_record_with_its_own_files()
    {
        var s = await SetUpAsync();
        using var writer = await LoginAsync(s.Writer);
        var truck = await PostAsync(writer, Vehicles, new JsonObject { ["plate"] = "34NOT01", ["kindKey"] = "Truck" });

        var first = await PostAsync(writer, Notes, Note(truck.Id, "Lastikler değişti."));
        var second = await PostAsync(writer, Notes, Note(truck.Id, "Servise gidecek."));
        Assert.Equal(s.Writer.Id.ToString(), first.Values["createdByUserId"]!.GetValue<string>());

        // Yeni not önce gelir; yalnızca bu kaydın notları listelenir.
        Assert.Equal([second.Id, first.Id], (await ListAsync(writer, truck.Id)).Select(n => n.Id));
        var other = await PostAsync(writer, Vehicles, new JsonObject { ["plate"] = "34NOT02", ["kindKey"] = "Truck" });
        Assert.Empty(await ListAsync(writer, other.Id));

        // Yazarı notu düzenler.
        var edit = await writer.PutAsJsonAsync($"{Notes}/{first.Id}", new RecordWriteRequest(new JsonObject { ["body"] = "Lastikler değişti, balans yapıldı." }, first.Version));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        // Notun eki not kaydına bağlanır; aracın dosyaları ayrıdır.
        using (var form = Form(first.Id, "Servis fişi.pdf"))
        {
            Assert.Equal(HttpStatusCode.Created, (await writer.PostAsync("/api/files", form)).StatusCode);
        }

        var files = (await writer.GetFromJsonAsync<List<StoredFileDto>>($"/api/files?entityKey=core.note&recordId={first.Id}"))!;
        Assert.Equal("Servis fişi.pdf", files.Single().FileName);
        Assert.Empty((await writer.GetFromJsonAsync<List<StoredFileDto>>($"/api/files?entityKey=fleet.vehicle&recordId={truck.Id}"))!);
        Assert.Equal(HttpStatusCode.OK, (await writer.GetAsync($"/api/files/{files[0].Id}/content")).StatusCode);

        // Not silinince eki de erişilemez olur.
        var current = (await writer.GetFromJsonAsync<RecordResponse>($"{Notes}/{first.Id}"))!;
        Assert.Equal(HttpStatusCode.NoContent, (await writer.DeleteAsync($"{Notes}/{first.Id}?version={current.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writer.GetAsync($"/api/files/{files[0].Id}/content")).StatusCode);
        Assert.Equal([second.Id], (await ListAsync(writer, truck.Id)).Select(n => n.Id));
    }

    [Fact]
    public async Task Notes_need_a_parent_record_and_a_body()
    {
        var s = await SetUpAsync();
        using var writer = await LoginAsync(s.Writer);
        var truck = await PostAsync(writer, Vehicles, new JsonObject { ["plate"] = "34NOT03", ["kindKey"] = "Truck" });

        var missing = await ValidationProblemAsync(await writer.PostAsJsonAsync(Notes, new RecordWriteRequest(new JsonObject())));
        Assert.True(missing.Errors.ContainsKey("parentEntityKey") && missing.Errors.ContainsKey("parentId") && missing.Errors.ContainsKey("body"));

        var tooLong = await ValidationProblemAsync(await writer.PostAsJsonAsync(Notes, new RecordWriteRequest(Note(truck.Id, new string('x', 4001)))));
        Assert.Equal(["En fazla 4000 karakter olabilir."], tooLong.Errors["body"]);

        var unknownType = Note(truck.Id, "Not");
        unknownType["parentEntityKey"] = "yok.kayit";
        Assert.Equal(["Bu kayıt türüne not eklenemez."], (await ValidationProblemAsync(await writer.PostAsJsonAsync(Notes, new RecordWriteRequest(unknownType)))).Errors["parentEntityKey"]);

        // Nota not eklenmez.
        var note = await PostAsync(writer, Notes, Note(truck.Id, "Ana not"));
        var onNote = Note(note.Id, "Not üstüne not");
        onNote["parentEntityKey"] = "core.note";
        Assert.Equal(["Bu kayıt türüne not eklenemez."], (await ValidationProblemAsync(await writer.PostAsJsonAsync(Notes, new RecordWriteRequest(onNote)))).Errors["parentEntityKey"]);

        var unknownParent = await ValidationProblemAsync(await writer.PostAsJsonAsync(Notes, new RecordWriteRequest(Note(Guid.NewGuid(), "Not"))));
        Assert.Equal(["Kayıt bulunamadı."], unknownParent.Errors["parentId"]);

        // Bağlı kayıt oluşturulduktan sonra değişmez; bağlı kayıtsız liste istenemez.
        var move = Note(Guid.NewGuid(), "Not");
        var moved = await ValidationProblemAsync(await writer.PutAsJsonAsync($"{Notes}/{note.Id}", new RecordWriteRequest(move, note.Version)));
        Assert.Equal(["Oluşturulduktan sonra değiştirilemez."], moved.Errors["parentId"]);
        var noParent = await ValidationProblemAsync(await writer.GetAsync(Notes));
        Assert.True(noParent.Errors.ContainsKey("parentEntityKey") && noParent.Errors.ContainsKey("parentId"));
    }

    [Fact]
    public async Task One_tenant_cannot_see_or_write_notes_of_another()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var writerA = await LoginAsync(a.Writer);
        using var writerB = await LoginAsync(b.Writer);
        var truck = await PostAsync(writerA, Vehicles, new JsonObject { ["plate"] = "34NOT04", ["kindKey"] = "Truck" });
        var note = await PostAsync(writerA, Notes, Note(truck.Id, "A firmasının notu"));
        using (var form = Form(note.Id, "Gizli.pdf"))
        {
            Assert.Equal(HttpStatusCode.Created, (await writerA.PostAsync("/api/files", form)).StatusCode);
        }

        var file = (await writerA.GetFromJsonAsync<List<StoredFileDto>>($"/api/files?entityKey=core.note&recordId={note.Id}"))!.Single();

        // B tenant'ı A'nın kaydına not ekleyemez, notu listeleyemez, okuyamaz, değiştirip silemez; ekini göremez.
        var write = await ValidationProblemAsync(await writerB.PostAsJsonAsync(Notes, new RecordWriteRequest(Note(truck.Id, "Sızma"))));
        Assert.Equal(["Kayıt bulunamadı."], write.Errors["parentId"]);
        Assert.Empty(await ListAsync(writerB, truck.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await writerB.GetAsync($"{Notes}/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writerB.PutAsJsonAsync($"{Notes}/{note.Id}", new RecordWriteRequest(new JsonObject { ["body"] = "x" }, note.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writerB.DeleteAsync($"{Notes}/{note.Id}?version={note.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writerB.GetAsync($"/api/files/{file.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writerB.GetAsync($"/api/files?entityKey=core.note&recordId={note.Id}")).StatusCode);
    }

    [Fact]
    public async Task Note_access_follows_the_parent_record_and_only_the_author_changes_it()
    {
        var s = await SetUpAsync();
        using var writer = await LoginAsync(s.Writer);
        using var otherWriter = await LoginAsync(s.OtherWriter);
        using var reader = await LoginAsync(s.Reader);
        using var outsider = await LoginAsync(s.Outsider);
        var truck = await PostAsync(writer, Vehicles, new JsonObject { ["plate"] = "34NOT05", ["kindKey"] = "Truck" });
        var note = await PostAsync(writer, Notes, Note(truck.Id, "Yalnızca yazarı değiştirir"));

        // Aracı okuyan notu görür ama not ekleyemez.
        Assert.Equal([note.Id], (await ListAsync(reader, truck.Id)).Select(n => n.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Notes, new RecordWriteRequest(Note(truck.Id, "Okuyucu notu")))).StatusCode);

        // Aracı göremeyen notu da göremez: liste boş, tekil okuma 404.
        Assert.Empty(await ListAsync(outsider, truck.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{Notes}/{note.Id}")).StatusCode);

        // Aracı yazabilen başkasının notunu değiştiremez, silemez, ek ekleyemez; yazarın ekini silemez.
        Assert.Equal(HttpStatusCode.Forbidden, (await otherWriter.PutAsJsonAsync($"{Notes}/{note.Id}", new RecordWriteRequest(new JsonObject { ["body"] = "Ele geçirildi" }, note.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherWriter.DeleteAsync($"{Notes}/{note.Id}?version={note.Version}")).StatusCode);
        using (var form = Form(note.Id, "Yabancı.pdf"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await otherWriter.PostAsync("/api/files", form)).StatusCode);
        }

        using (var form = Form(note.Id, "Yazarın.pdf"))
        {
            Assert.Equal(HttpStatusCode.Created, (await writer.PostAsync("/api/files", form)).StatusCode);
        }

        var file = (await otherWriter.GetFromJsonAsync<List<StoredFileDto>>($"/api/files?entityKey=core.note&recordId={note.Id}"))!.Single();
        Assert.Equal(HttpStatusCode.Forbidden, (await otherWriter.DeleteAsync($"/api/files/{file.Id}?version={file.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await writer.DeleteAsync($"/api/files/{file.Id}?version={file.Version}")).StatusCode);
    }

    private sealed record Setup(Guid TenantId, ModulixUser Writer, ModulixUser OtherWriter, ModulixUser Reader, ModulixUser Outsider);

    private async Task<Setup> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "fleet", enabled: true);
        var writer = await factory.CreateUserAsync(tenant);
        var otherWriter = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        var outsider = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [VehiclesRead, VehiclesWrite], Scope.All, writer);
        await factory.CreateRoleAsync(tenant.Id, [VehiclesRead, VehiclesWrite], Scope.All, otherWriter);
        await factory.CreateRoleAsync(tenant.Id, [VehiclesRead], Scope.All, reader);
        return new Setup(tenant.Id, writer, otherWriter, reader, outsider);
    }

    private static JsonObject Note(Guid parentId, string body) => new()
    {
        ["parentEntityKey"] = "fleet.vehicle",
        ["parentId"] = parentId.ToString(),
        ["body"] = body,
    };

    private static MultipartFormDataContent Form(Guid noteId, string fileName)
    {
        var file = new ByteArrayContent([.. Pdf, .. Encoding.ASCII.GetBytes(Guid.NewGuid().ToString())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent
        {
            { new StringContent("core.note"), "entityKey" },
            { new StringContent(noteId.ToString()), "recordId" },
            { file, "file", fileName },
        };
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<IReadOnlyList<RecordResponse>> ListAsync(HttpClient client, Guid parentId) =>
        (await client.GetFromJsonAsync<RecordPageResponse>($"{Notes}?parentEntityKey=fleet.vehicle&parentId={parentId}"))!.Items;

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
