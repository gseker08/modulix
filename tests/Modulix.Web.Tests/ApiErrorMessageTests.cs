using System.Net;
using System.Text;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// 422 yanıtının kullanıcıya giden iletisi: iş kuralı reddi (alan hatası yok) somut iletiyi başlıkta taşır ve gösterilir; alan hatası olan
/// doğrulama sayfa üstünde genel metin verir, somut ileti alanın altında kalır. Önceden ilk durumda da genel metin gösteriliyordu
/// ("Ret için gerekçe yazın." yerine "Lütfen işaretli alanları düzeltin."). İstemci tarafı iletidir: tenant izolasyonu ve yetki sunucuda sınanır.
/// </summary>
public class ApiErrorMessageTests
{
    private const string Generic = "Lütfen işaretli alanları düzeltin.";

    [Fact]
    public async Task A_business_rule_rejection_shows_the_title_as_the_message()
    {
        var result = await PostAsync("""{"title":"Ret için gerekçe yazın.","status":422}""");

        Assert.False(result.IsSuccess);
        Assert.Equal("Ret için gerekçe yazın.", result.Error);
    }

    [Fact]
    public async Task Field_errors_keep_the_generic_banner_and_carry_the_field_messages()
    {
        var result = await PostAsync("""{"title":"Doğrulama hatası","status":422,"errors":{"quantity":["Miktar zorunludur."]}}""");

        Assert.Equal(Generic, result.Error);
        Assert.Equal(["Miktar zorunludur."], result.FieldErrors["quantity"]);
    }

    [Fact]
    public async Task An_explicit_message_extension_wins_over_the_title()
    {
        var result = await PostAsync("""{"title":"Başlık","message":"Somut ileti.","status":422}""");

        Assert.Equal("Somut ileti.", result.Error);
    }

    [Fact]
    public async Task A_body_without_a_title_falls_back_to_the_generic_banner()
    {
        var result = await PostAsync("{}");

        Assert.Equal(Generic, result.Error);
        Assert.Equal(Generic, (await PostAsync("not json")).Error);
    }

    private static async Task<ApiResult<object>> PostAsync(string body)
    {
        var api = new ApiClient(new HttpClient(new Handler(body)) { BaseAddress = new Uri("https://localhost/") });
        return await api.PostAsync<object>("/api/x", new { });
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
            });
    }
}
