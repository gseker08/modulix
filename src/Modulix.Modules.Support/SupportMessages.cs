using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Support;

/// <summary>
/// Destek modülünün sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-3). Anahtarlar <c>support.server.*</c>; Türkçe metin
/// <c>tr.json</c>'daki kalıpla aynıdır (birim testle denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler
/// (alan biçimleri, izin, eşzamanlılık) <see cref="ServerMessages"/>'tadır, burada yinelenmez.
/// </summary>
internal static class SupportMessages
{
    /// <summary>Kayıt alanı biçimi.</summary>
    public static class Field
    {
        public static ServerMessage SignedMoney => ServerMessage.Of("support.server.field.signed-money", "En fazla iki ondalıklı bir tutar girin.");
    }

    /// <summary>Bağlı kayıt alanlarında "bulunamadı" iletileri (başka tenant'ın kaydı da bulunamadı sayılır).</summary>
    public static class NotFound
    {
        public static ServerMessage Party => ServerMessage.Of("support.server.not-found.party", "Müşteri bulunamadı.");

        public static ServerMessage Person => ServerMessage.Of("support.server.not-found.person", "Kişi bulunamadı.");

        public static ServerMessage Category => ServerMessage.Of("support.server.not-found.category", "Kategori bulunamadı.");

        public static ServerMessage ParentCategory => ServerMessage.Of("support.server.not-found.parent-category", "Üst kategori bulunamadı.");

        public static ServerMessage RelatedTicket => ServerMessage.Of("support.server.not-found.related-ticket", "İlişkili talep bulunamadı.");

        public static ServerMessage Queue => ServerMessage.Of("support.server.not-found.queue", "Kuyruk bulunamadı.");

        public static ServerMessage Calendar => ServerMessage.Of("support.server.not-found.calendar", "Takvim bulunamadı.");
    }
}
