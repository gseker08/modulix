using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Modulix.Shared.Records;

namespace Modulix.IntegrationTests;

/// <summary>Testlerde kur yazma yardımcısı (ADR-0049 dilim 5a: kurlar <c>core.exchange-rate</c> kayıt türüdür, CRM'de değil).</summary>
internal static class ExchangeRateTestData
{
    public const string Url = "/api/records/core.exchange-rate";

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string from, string to, decimal rate, DateOnly validOn, string? note = null) =>
        client.PostAsJsonAsync(Url, new RecordWriteRequest(new JsonObject
        {
            ["fromCurrency"] = from,
            ["toCurrency"] = to,
            ["rate"] = rate,
            ["validOn"] = validOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["note"] = note,
        }));

    /// <summary>Kuru yazar ve başarılı olmasını bekler.</summary>
    public static async Task CreateAsync(HttpClient client, string from, string to, decimal rate, DateOnly validOn, string? note = null)
    {
        var response = await PostAsync(client, from, to, rate, validOn, note);
        response.EnsureSuccessStatusCode();
    }
}
