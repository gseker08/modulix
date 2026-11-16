using System.Text.Json.Nodes;

namespace Modulix.Core.Contracts.Records;

/// <summary>
/// Liste dışa aktarımının (CSV) denetim kaydı: kişisel veri çıkışı kim, kaç kayıt ve hangi süzgeçle yaptı (ADR-0046; modül uç noktaları Core'un
/// denetim yazıcısını doğrudan görmez). Kayıt çağıranın tenant ve kullanıcısıyla, <c>Exported</c> eylemiyle yazılır ve hemen kaydedilir; oturumsuz
/// çağrıda hiçbir şey yazılmaz.
/// </summary>
public interface IRecordExportAudit
{
    /// <param name="entityKey">Dışa aktarılan kayıt türü.</param>
    /// <param name="count">Dosyaya giren kayıt sayısı.</param>
    /// <param name="filters">Listenin süzgeçleri; denetim kaydına <c>ad=değer&amp;…</c> biçiminde yazılır.</param>
    /// <param name="details">Ek bilgi (ör. kesildi mi, tutar sütunları var mı); sayıdan sonra, süzgeçten önce yazılır.</param>
    Task RecordAsync(
        string entityKey, int count, IReadOnlyDictionary<string, string>? filters, JsonObject? details = null, CancellationToken cancellationToken = default);
}
