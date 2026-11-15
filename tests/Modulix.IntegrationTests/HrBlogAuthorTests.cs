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
using Modulix.Shared.Hr;
using Modulix.Shared.Records;
using static Modulix.Modules.Hr.HrModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0042 bölüm 27: çalışanın blog yazısı ve onay akışı. Mutlu yol (yazar taslağı yazar, ek ekler, onaya gönderir; yöneten gerekçeyle geri çevirir,
/// yazar düzeltip yeniden gönderir, yöneten yayınlar; okur görür), doğrulama ve kural (yazar yayınlayamaz, onay bekleyen ve yayındaki yazı ile ekleri
/// kilitli, gerekçesiz geri çevirme, durum dışı eylem, bilinmeyen eylem), tenant izolasyonu (başka tenant yazıyı görmez ve karar veremez) ve yetki
/// (yazma izni olmayan yazamaz; yazar başkasının taslağını görmez, değiştiremez; yazar karar veremez).
/// </summary>
public class HrBlogAuthorTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Posts = "/api/records/hr.blog-post";
    private const string Actions = "/api/hr/blog-posts";
    private const string Files = "/api/files";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n");

    [Fact]
    public async Task Author_writes_and_submits_and_the_manager_returns_then_publishes()
    {
        var s = await SetUpAsync();
        using var hr = await LoginAsync(s.Hr);
        using var author = await LoginAsync(s.Author);
        using var reader = await LoginAsync(s.Reader);

        var draft = await PostAsync(author, Posts, new JsonObject { ["title"] = "Gönüllülük günü", ["body"] = "İlk taslak." });
        Assert.Equal(("draft", s.Author.Id.ToString()), (Text(draft, "status"), Text(draft, "authorUserId")));
        var photo = await UploadAsync(author, draft.Id, "afis.pdf");
        draft = await PutAsync(author, draft.Id, new JsonObject { ["body"] = "Düzeltilmiş taslak." });

        // Yazar yalnızca gönderme eylemini görür; okur taslağı görmez.
        Assert.Equal(["submit"], (await ActionsAsync(author, draft.Id)).Select(a => a.Action));
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"{Posts}/{draft.Id}")).StatusCode);

        var submitted = await ActAsync(author, draft, "submit");
        Assert.Equal("submitted", Text(submitted, "status"));
        Assert.NotNull(Text(submitted, "submittedAt"));

        // Onay bekleyen yazı ve ekleri yazar için kilitlidir.
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsJsonAsync($"{Posts}/{draft.Id}",
            new RecordWriteRequest(new JsonObject { ["title"] = "Değişti" }, submitted.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await author.DeleteAsync($"{Posts}/{draft.Id}?version={submitted.Version}")).StatusCode);
        using (var form = Form(draft.Id, "sonradan.pdf"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync(Files, form)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await author.DeleteAsync($"{Files}/{photo.Id}?version={photo.Version}")).StatusCode);

        // Yöneten onay kuyruğunda görür ve gerekçeyle geri çevirir.
        Assert.Equal(draft.Id, Assert.Single(await ListAsync(hr, "status=submitted")).Id);
        var returned = await ActAsync(hr, submitted, "return", "Fotoğraf ekleyin.");
        Assert.Equal(("returned", "Fotoğraf ekleyin."), (Text(returned, "status"), Text(returned, "reviewNote")));

        // Geri çevrilen yazı yeniden düzenlenir, eki eklenir ve gönderilir; gerekçe silinir.
        await UploadAsync(author, draft.Id, "fotograf.pdf");
        await PutAsync(author, draft.Id, new JsonObject { ["summary"] = "Fotoğraflarla." });
        var resubmitted = await ActAsync(author, await GetAsync(author, draft.Id), "submit");
        Assert.Equal(("submitted", (string?)null), (Text(resubmitted, "status"), Text(resubmitted, "reviewNote")));

        var published = await ActAsync(hr, resubmitted, "publish");
        Assert.Equal("published", Text(published, "status"));
        Assert.True(published.Values["isPublished"]!.GetValue<bool>());
        Assert.NotNull(Text(published, "publishedAt"));

        // Okur görür, ekleri indirir; yayındaki yazı yazar için kilitlidir.
        Assert.Equal(draft.Id, Assert.Single(await ListAsync(reader, "isPublished=true")).Id);
        Assert.Equal(2, (await reader.GetFromJsonAsync<List<StoredFileDto>>($"{Files}?entityKey=hr.blog-post&recordId={draft.Id}"))!.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsJsonAsync($"{Posts}/{draft.Id}",
            new RecordWriteRequest(new JsonObject { ["title"] = "Yayından sonra" }, published.Version))).StatusCode);
        Assert.DoesNotContain(await ActionsAsync(author, draft.Id), a => a.Allowed);
    }

    [Fact]
    public async Task Authors_cannot_publish_and_decisions_need_a_submitted_post_and_a_reason()
    {
        var s = await SetUpAsync();
        using var hr = await LoginAsync(s.Hr);
        using var author = await LoginAsync(s.Author);

        // Yazar yayınlayamaz: ne oluştururken ne de sonradan.
        var problem = await ValidationProblemAsync(await author.PostAsJsonAsync(Posts,
            new RecordWriteRequest(new JsonObject { ["title"] = "Doğrudan", ["body"] = "Metin", ["isPublished"] = true })));
        Assert.Contains("isPublished", problem.Errors.Keys);
        var draft = await PostAsync(author, Posts, new JsonObject { ["title"] = "Taslak", ["body"] = "Metin" });
        Assert.Contains("isPublished", (await ValidationProblemAsync(await author.PutAsJsonAsync($"{Posts}/{draft.Id}",
            new RecordWriteRequest(new JsonObject { ["isPublished"] = true }, draft.Version)))).Errors.Keys);

        // Taslak için karar verilmez; bilinmeyen eylem reddedilir.
        Assert.Equal(HttpStatusCode.Conflict, (await RunAsync(hr, draft, "publish")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RunAsync(author, draft, "approve")).StatusCode);

        var submitted = await ActAsync(author, draft, "submit");
        Assert.Equal(HttpStatusCode.Conflict, (await RunAsync(author, submitted, "submit")).StatusCode);

        // Geri çevirme gerekçe ister; yazı onay bekler durumda kalır.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RunAsync(hr, submitted, "return", " ")).StatusCode);
        Assert.Equal("submitted", Text(await GetAsync(hr, draft.Id), "status"));

        // Eski sürümle karar verilmez.
        var published = await ActAsync(hr, submitted, "publish");
        Assert.Equal(HttpStatusCode.Conflict, (await RunAsync(hr, submitted, "return", "Geç kaldı")).StatusCode);
        Assert.Equal("published", Text(await GetAsync(hr, published.Id), "status"));
    }

    [Fact]
    public async Task Another_tenant_cannot_see_or_decide_on_a_submitted_post()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var authorA = await LoginAsync(a.Author);
        using var hrB = await LoginAsync(b.Hr);
        var submitted = await ActAsync(authorA, await PostAsync(authorA, Posts, new JsonObject { ["title"] = "A yazısı", ["body"] = "Metin" }), "submit");

        Assert.Equal(HttpStatusCode.NotFound, (await hrB.GetAsync($"{Posts}/{submitted.Id}")).StatusCode);
        Assert.Empty(await ListAsync(hrB, "status=submitted"));
        Assert.Equal(HttpStatusCode.NotFound, (await hrB.GetAsync($"{Actions}/{submitted.Id}/actions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RunAsync(hrB, submitted, "publish")).StatusCode);
        Assert.Equal("submitted", Text(await GetAsync(authorA, submitted.Id), "status"));
    }

    [Fact]
    public async Task Only_writers_write_only_their_own_posts_and_only_managers_decide()
    {
        var s = await SetUpAsync();
        using var author = await LoginAsync(s.Author);
        using var colleague = await LoginAsync(s.Colleague);
        using var reader = await LoginAsync(s.Reader);

        // Yazma izni olmayan okur yazı açamaz.
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Posts,
            new RecordWriteRequest(new JsonObject { ["title"] = "Okur", ["body"] = "Metin" }))).StatusCode);

        // Yazar başkasının taslağını görmez, değiştiremez, eklerine dokunamaz, gönderemez.
        var draft = await PostAsync(author, Posts, new JsonObject { ["title"] = "Benim", ["body"] = "Metin" });
        Assert.Equal(HttpStatusCode.NotFound, (await colleague.GetAsync($"{Posts}/{draft.Id}")).StatusCode);
        Assert.DoesNotContain(await ListAsync(colleague, null), p => p.Id == draft.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await colleague.PutAsJsonAsync($"{Posts}/{draft.Id}",
            new RecordWriteRequest(new JsonObject { ["title"] = "Başkası" }, draft.Version))).StatusCode);
        using (var form = Form(draft.Id, "yabanci.pdf"))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.PostAsync(Files, form)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await RunAsync(colleague, draft, "submit")).StatusCode);

        // Yazar kendi yazısı için karar veremez ve karar eylemlerini görmez.
        var submitted = await ActAsync(author, draft, "submit");
        Assert.DoesNotContain(await ActionsAsync(author, draft.Id), a => a.Action is "publish" or "return");
        Assert.Equal(HttpStatusCode.Forbidden, (await RunAsync(author, submitted, "publish")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RunAsync(author, submitted, "return", "Kendime")).StatusCode);
        Assert.Equal("submitted", Text(await GetAsync(author, draft.Id), "status"));
    }

    // --- yardımcılar ---

    private sealed record Setup(Guid TenantId, ModulixUser Hr, ModulixUser Author, ModulixUser Colleague, ModulixUser Reader);

    /// <summary>İK (sistem rolü), iki yazar (okuma + yazma, kendi kapsamı) ve yalnızca okuyan çalışan.</summary>
    private async Task<Setup> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "hr", enabled: true);
        var hrUser = await factory.CreateUserAsync(tenant);
        var author = await factory.CreateUserAsync(tenant);
        var colleague = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(hrUser.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, [BlogRead, BlogWrite], Scope.Own, author, colleague);
        await factory.CreateRoleAsync(tenant.Id, [BlogRead], Scope.Own, reader);
        return new Setup(tenant.Id, hrUser, author, colleague, reader);
    }

    private static MultipartFormDataContent Form(Guid postId, string fileName)
    {
        var file = new ByteArrayContent([.. Pdf, .. Encoding.ASCII.GetBytes(fileName + Guid.NewGuid())]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent
        {
            { new StringContent("hr.blog-post"), "entityKey" },
            { new StringContent(postId.ToString()), "recordId" },
            { file, "file", fileName },
        };
    }

    private static async Task<StoredFileDto> UploadAsync(HttpClient client, Guid postId, string fileName)
    {
        using var form = Form(postId, fileName);
        var response = await client.PostAsync(Files, form);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{fileName}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<StoredFileDto>())!;
    }

    private static string? Text(RecordResponse record, string field) => record.Values[field]?.GetValue<string>();

    private static async Task<List<HrActionDto>> ActionsAsync(HttpClient client, Guid postId) =>
        (await client.GetFromJsonAsync<List<HrActionDto>>($"{Actions}/{postId}/actions"))!;

    private static Task<HttpResponseMessage> RunAsync(HttpClient client, RecordResponse post, string action, string? reason = null) =>
        client.PostAsJsonAsync($"{Actions}/{post.Id}/actions", new RunHrActionRequest(action, post.Version, reason));

    private static async Task<RecordResponse> ActAsync(HttpClient client, RecordResponse post, string action, string? reason = null)
    {
        var response = await RunAsync(client, post, action, reason);
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

    private static async Task<IReadOnlyList<RecordResponse>> ListAsync(HttpClient client, string? query) =>
        (await client.GetFromJsonAsync<RecordPageResponse>(query is null ? Posts : $"{Posts}?{query}"))!.Items;

    private static async Task<RecordResponse> GetAsync(HttpClient client, Guid id) => (await client.GetFromJsonAsync<RecordResponse>($"{Posts}/{id}"))!;

    private static async Task<RecordResponse> PostAsync(HttpClient client, string url, JsonObject values)
    {
        var response = await client.PostAsJsonAsync(url, new RecordWriteRequest(values));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private static async Task<RecordResponse> PutAsync(HttpClient client, Guid id, JsonObject values)
    {
        var current = await GetAsync(client, id);
        var response = await client.PutAsJsonAsync($"{Posts}/{id}", new RecordWriteRequest(values, current.Version));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private static async Task<ValidationProblemDetails> ValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
    }
}
