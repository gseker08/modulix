using Microsoft.AspNetCore.Http;
using Modulix.Core.Contracts.Localization;

namespace Modulix.Core.Hosting;

/// <summary>Kayıt uç noktalarının ortak istek yardımcıları; host'un genel kayıt uçları ve modül uç noktaları aynı kuralı kullanır (ADR-0046).</summary>
public static class RecordRequests
{
    /// <summary>Sorgu dizesindeki süzgeçler (sayfalama ve arama dışındaki dolu değerler); yoksa <c>null</c>.</summary>
    public static IReadOnlyDictionary<string, string>? FiltersFrom(HttpRequest http)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, values) in http.Query)
        {
            if (key is not ("page" or "pageSize" or "search") && values.LastOrDefault() is { Length: > 0 } value)
            {
                filters[key] = value;
            }
        }

        return filters.Count == 0 ? null : filters;
    }

    /// <summary>Güncelleme ve silme kaydın okunan sürümünü ister; sürümsüz istek son yazanın ezmesine yol açacağı için 428 alır.</summary>
    public static IResult VersionRequired() =>
        LocalizedProblems.Problem(
            StatusCodes.Status428PreconditionRequired,
            ServerMessages.Record.VersionRequiredTitle,
            ServerMessages.Record.VersionRequiredDetail,
            new Dictionary<string, object?> { ["code"] = "VersionRequired" });
}
