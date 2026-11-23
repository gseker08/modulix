using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Marketing;

/// <summary>
/// Pazarlama modülünün sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-7). Anahtarlar <c>marketing.server.*</c>; Türkçe metin <c>tr.json</c>'daki
/// kalıpla aynıdır (birim testle denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler (alan biçimleri, izin, eşzamanlılık)
/// <see cref="ServerMessages"/>'tadır.
/// </summary>
internal static class MarketingMessages
{
    /// <summary>Kayıt alanı biçimi.</summary>
    public static class Field
    {
        public static ServerMessage SignedMoney =>
            ServerMessage.Of("marketing.server.field.signed-money", "En fazla iki ondalıklı bir tutar girin.");
    }

    /// <summary>Bağlı kayıt alanlarında "bulunamadı" iletileri.</summary>
    public static class NotFound
    {
        public static ServerMessage Campaign =>
            ServerMessage.Of("marketing.server.notfound.campaign", "Kampanya bulunamadı.");

        public static ServerMessage Segment =>
            ServerMessage.Of("marketing.server.notfound.segment", "Segment bulunamadı.");
    }

    /// <summary>Kampanya kaydı.</summary>
    public static class Campaign
    {
        public static ServerMessage CodeFormat =>
            ServerMessage.Of("marketing.server.campaign.code-format", "Kod 2-40 karakter olmalı; küçük harf, rakam ve tire kullanılabilir (ör. antrepo-2026).");

        public static ServerMessage CodeLocked =>
            ServerMessage.Of("marketing.server.campaign.code-locked", "Kod oluşturulduktan sonra değiştirilemez.");

        public static ServerMessage TargetLocked =>
            ServerMessage.Of("marketing.server.campaign.target-locked", "Hedef türü oluşturulduktan sonra değiştirilemez.");

        public static ServerMessage ChannelLocked =>
            ServerMessage.Of("marketing.server.campaign.channel-locked", "Kanal oluşturulduktan sonra değiştirilemez.");

        public static ServerMessage RoleRequired =>
            ServerMessage.Of("marketing.server.campaign.role-required", "Rol kuralında aranan rol zorunludur.");

        public static ServerMessage CostCurrency =>
            ServerMessage.Of("marketing.server.campaign.cost-currency", "Para birimi 3 harfli kod olmalıdır (ör. TRY).");

        public static ServerMessage MaxPerTarget =>
            ServerMessage.Of("marketing.server.campaign.max-per-target", "Hedef başına alıcı sınırı en az 1 olmalıdır (sınır yoksa boş bırakın).");

        public static ServerMessage EndBeforeStart =>
            ServerMessage.Of("marketing.server.campaign.end-before-start", "Bitiş tarihi başlangıçtan önce olamaz.");

        public static ServerMessage TopicInvalid =>
            ServerMessage.Of("marketing.server.campaign.topic-invalid", "Konu tanımlı ve açık olmalıdır (Yönetim → İletişim tercihleri → Konular).");

        public static ServerMessage ArchivedLocked =>
            ServerMessage.Of("marketing.server.campaign.archived-locked", "Arşivlenen kampanya düzenlenemez; önce geri alın.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("marketing.server.campaign.no-delete", "Hedef kitlesi, dondurulmuş kitlesi ya da yanıtı olan kampanya silinemez; arşivleyin.");

        public static ServerMessage UtmFormat =>
            ServerMessage.Of("marketing.server.campaign.utm-format", "Kaynak/UTM değeri harf, rakam, nokta, alt çizgi, tire ya da ~ içerebilir (en fazla 80 karakter, boşluk yok).");
    }

    /// <summary>Kampanya hedef kitlesi ve anlık görüntüleri.</summary>
    public static class Audience
    {
        public static ServerMessage ExactlyOne =>
            ServerMessage.Of("marketing.server.audience.exactly-one", "Segment ya da hedef kayıttan tam biri seçilmelidir.");

        public static ServerMessage SourceLocked =>
            ServerMessage.Of("marketing.server.audience.source-locked", "Kaynak oluşturulduktan sonra değiştirilemez; kaydı silip yeniden ekleyin.");

        public static ServerMessage ArchivedLocked =>
            ServerMessage.Of("marketing.server.audience.archived-locked", "Arşivlenen kampanyanın hedef kitlesi değiştirilemez.");

        public static ServerMessage SegmentInaccessible =>
            ServerMessage.Of("marketing.server.audience.segment-inaccessible", "Segment bulunamadı ya da erişiminiz yok.");

        public static ServerMessage TargetMismatch =>
            ServerMessage.Of("marketing.server.audience.target-mismatch", "Segmentin hedef türü kampanyanınkiyle aynı olmalıdır; karışık türler birleştirilmez.");

        public static ServerMessage SegmentArchived =>
            ServerMessage.Of("marketing.server.audience.segment-archived", "Arşivlenen segment yeni kampanyada kullanılamaz.");

        public static ServerMessage TargetInaccessible =>
            ServerMessage.Of("marketing.server.audience.target-inaccessible", "Hedef kayıt bulunamadı ya da erişiminiz yok.");

        public static ServerMessage AlreadyIn =>
            ServerMessage.Of("marketing.server.audience.already-in", "Bu kaynak kampanyanın hedef kitlesinde zaten var.");

        public static ServerMessage SnapshotViaAction =>
            ServerMessage.Of("marketing.server.audience.snapshot-via-action", "Dondurulmuş hedef kitle yalnızca hedef kitle eylemiyle oluşur.");

        public static ServerMessage SnapshotLocked =>
            ServerMessage.Of("marketing.server.audience.snapshot-locked", "Dondurulmuş hedef kitle değiştirilemez.");

        public static ServerMessage SnapshotNoDelete =>
            ServerMessage.Of("marketing.server.audience.snapshot-no-delete", "Dondurulmuş hedef kitle silinemez.");
    }

    /// <summary>Segment kaydı, üyeleri ve sürümleri.</summary>
    public static class Segment
    {
        public static ServerMessage CodeFormat =>
            ServerMessage.Of("marketing.server.segment.code-format", "Kod 2-40 karakter olmalı; küçük harf, rakam ve tire kullanılabilir (ör. tekstil-marmara).");

        public static ServerMessage CodeLocked =>
            ServerMessage.Of("marketing.server.segment.code-locked", "Kod oluşturulduktan sonra değiştirilemez.");

        public static ServerMessage TargetLocked =>
            ServerMessage.Of("marketing.server.segment.target-locked", "Hedef türü oluşturulduktan sonra değiştirilemez.");

        public static ServerMessage KindLocked =>
            ServerMessage.Of("marketing.server.segment.kind-locked", "Segment türü (statik/dinamik) oluşturulduktan sonra değiştirilemez.");

        public static ServerMessage StaticNoCondition =>
            ServerMessage.Of("marketing.server.segment.static-no-condition", "Statik segmentin koşulu olmaz; üyeler elle seçilir.");

        public static ServerMessage ArchivedLocked =>
            ServerMessage.Of("marketing.server.segment.archived-locked", "Arşivlenen segment düzenlenemez; önce geri alın.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("marketing.server.segment.no-delete", "Yayımlanmış ya da üyesi olan segment silinemez; arşivleyin.");

        public static ServerMessage MemberTargetLocked =>
            ServerMessage.Of("marketing.server.segment.member-target-locked", "Hedef oluşturulduktan sonra değiştirilemez; üyeyi silip yeniden ekleyin.");

        public static ServerMessage MemberArchivedLocked =>
            ServerMessage.Of("marketing.server.segment.member-archived-locked", "Arşivlenen segmentin üyeleri değiştirilemez.");

        public static ServerMessage MemberStaticOnly =>
            ServerMessage.Of("marketing.server.segment.member-static-only", "Statik segmentte yalnızca dahil edilen üye bulunur.");

        public static ServerMessage MemberAlreadyIn =>
            ServerMessage.Of("marketing.server.segment.member-already-in", "Bu kayıt segmentte zaten var.");

        public static ServerMessage VersionViaPublish =>
            ServerMessage.Of("marketing.server.segment.version-via-publish", "Sürüm yalnızca yayım eylemiyle oluşur.");

        public static ServerMessage VersionLocked =>
            ServerMessage.Of("marketing.server.segment.version-locked", "Yayımlanmış sürüm değiştirilemez.");

        public static ServerMessage VersionNoDelete =>
            ServerMessage.Of("marketing.server.segment.version-no-delete", "Yayımlanmış sürüm silinemez.");
    }

    /// <summary>Kampanya içeriği.</summary>
    public static class Content
    {
        public static ServerMessage PublishedLocked =>
            ServerMessage.Of("marketing.server.content.published-locked", "Yayımlanmış içerik sürümü değiştirilemez; yeni sürüm oluşturun.");

        public static ServerMessage ArchivedNoAdd =>
            ServerMessage.Of("marketing.server.content.archived-no-add", "Arşivlenen kampanyaya içerik eklenemez.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("marketing.server.content.no-delete", "İçerik sürümü silinemez.");
    }

    /// <summary>Kampanya çalıştırması.</summary>
    public static class Run
    {
        public static ServerMessage SubmittedLocked =>
            ServerMessage.Of("marketing.server.run.submitted-locked", "Onaya gönderilmiş çalıştırma düzenlenemez; önce taslağa alın (onay yeniden alınır).");

        public static ServerMessage ArchivedLocked =>
            ServerMessage.Of("marketing.server.run.archived-locked", "Arşivlenen kampanyada çalıştırma oluşturulamaz ya da düzenlenemez.");

        public static ServerMessage SnapshotMissing =>
            ServerMessage.Of("marketing.server.run.snapshot-missing", "Hedef kitle anlık görüntüsü bu kampanyada bulunamadı.");

        public static ServerMessage ContentMissing =>
            ServerMessage.Of("marketing.server.run.content-missing", "İçerik sürümü bu kampanyada bulunamadı.");

        public static ServerMessage ScheduledPast =>
            ServerMessage.Of("marketing.server.run.scheduled-past", "Planlanan zaman geçmişte olamaz.");

        public static ServerMessage OnlyDraftDelete =>
            ServerMessage.Of("marketing.server.run.only-draft-delete", "Yalnızca taslak çalıştırma silinir; diğerleri iptal edilir.");
    }

    /// <summary>Kampanya yanıtı.</summary>
    public static class Response
    {
        public static ServerMessage NoDelete =>
            ServerMessage.Of("marketing.server.response.no-delete", "Yanıt kaydı silinmez; kapatın.");
    }
}
