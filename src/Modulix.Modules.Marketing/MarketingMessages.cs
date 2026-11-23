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

    /// <summary>Segment koşulu doğrulaması.</summary>
    public static class Condition
    {
        public static ServerMessage Empty =>
            ServerMessage.Of("marketing.server.condition.empty", "Koşul boş olamaz.");

        public static ServerMessage NotJson =>
            ServerMessage.Of("marketing.server.condition.not-json", "Koşul geçerli bir JSON değil.");

        public static ServerMessage MaxLeaves(int max) =>
            ServerMessage.Of("marketing.server.condition.max-leaves", "En fazla {0} koşul kullanılabilir.", max);

        public static ServerMessage NotObject =>
            ServerMessage.Of("marketing.server.condition.not-object", "Koşul bir nesne olmalıdır.");

        public static ServerMessage MaxDepth(int max) =>
            ServerMessage.Of("marketing.server.condition.max-depth", "İç içe grup derinliği en fazla {0} olabilir.", max);

        public static ServerMessage NodeShape =>
            ServerMessage.Of("marketing.server.condition.node-shape", "Düğüm ya da grup (op, items) ya da koşul (field, operator, values) olmalıdır.");

        public static ServerMessage GroupOp =>
            ServerMessage.Of("marketing.server.condition.group-op", "Grup operatörü 'and' ya da 'or' olmalıdır.");

        public static ServerMessage GroupItems =>
            ServerMessage.Of("marketing.server.condition.group-items", "Grup en az bir koşul içermelidir.");

        public static ServerMessage BadOperator =>
            ServerMessage.Of("marketing.server.condition.bad-operator", "Geçersiz operatör.");

        public static ServerMessage ValueType =>
            ServerMessage.Of("marketing.server.condition.value-type", "Değer metin, sayı ya da evet/hayır olmalıdır.");

        public static ServerMessage ValuesList =>
            ServerMessage.Of("marketing.server.condition.values-list", "Değerler bir liste olmalıdır.");

        public static ServerMessage FieldUnknown =>
            ServerMessage.Of("marketing.server.condition.field-unknown", "Alan bulunamadı ya da bu segment türünde kullanılamıyor.");

        public static ServerMessage OperatorNotAllowed(string label) =>
            ServerMessage.Of("marketing.server.condition.operator-not-allowed", "'{0}' alanında bu operatör kullanılamaz.", label);

        public static ServerMessage ValueCountExact(int count) =>
            ServerMessage.Of("marketing.server.condition.value-count-exact", "Bu operatör {0} değer ister.", count);

        public static ServerMessage ValueCountRange(int min, int max) =>
            ServerMessage.Of("marketing.server.condition.value-count-range", "Bu operatör {0}-{1} değer ister.", min, max);

        public static ServerMessage RangeOrder =>
            ServerMessage.Of("marketing.server.condition.range-order", "Başlangıç değeri bitişten büyük olamaz.");

        public static ServerMessage ValueLength(int max) =>
            ServerMessage.Of("marketing.server.condition.value-length", "Değer 1-{0} karakter olmalıdır.", max);

        public static ServerMessage WindowDays(int max) =>
            ServerMessage.Of("marketing.server.condition.window-days", "Pencere 1-{0} arası gün sayısı olmalıdır.", max);

        public static ServerMessage BoolValue =>
            ServerMessage.Of("marketing.server.condition.bool-value", "true ya da false olmalıdır.");

        public static ServerMessage IdValue =>
            ServerMessage.Of("marketing.server.condition.id-value", "Geçerli bir kimlik olmalıdır.");

        public static ServerMessage DaysRange(int max) =>
            ServerMessage.Of("marketing.server.condition.days-range", "Gün sayısı 1-{0} arası olmalıdır.", max);

        public static ServerMessage UnsupportedType =>
            ServerMessage.Of("marketing.server.condition.unsupported-type", "Desteklenmeyen alan tipi.");

        public static ServerMessage Required =>
            ServerMessage.Of("marketing.server.condition.required", "Koşul ya da segment gerekli.");

        public static ServerMessage ConditionRequired =>
            ServerMessage.Of("marketing.server.condition.condition-required", "Koşul gerekli.");

        public static ServerMessage DraftRequired =>
            ServerMessage.Of("marketing.server.condition.draft-required", "Yayımlamak için taslak koşul yazılmalıdır.");
    }

    /// <summary>Segment servisi.</summary>
    public static class SegmentService
    {
        public static ServerMessage InvalidTarget =>
            ServerMessage.Of("marketing.server.segment-service.invalid-target", "Geçersiz hedef türü.");

        public static ServerMessage ArchivedPublish =>
            ServerMessage.Of("marketing.server.segment-service.archived-publish", "Arşivlenen segment yayımlanamaz; önce geri alın.");

        public static ServerMessage NoDraftChange =>
            ServerMessage.Of("marketing.server.segment-service.no-draft-change", "Taslakta yayımlanmamış değişiklik yok.");

        public static ServerMessage OnlyActiveRefresh =>
            ServerMessage.Of("marketing.server.segment-service.only-active-refresh", "Yalnızca etkin segment yenilenir.");

        public static ServerMessage NotActive =>
            ServerMessage.Of("marketing.server.segment-service.not-active", "Segment etkin değil; yalnızca yayımlanmış (etkin) segment hedef kitlede kullanılır.");

        public static ServerMessage TargetMismatch =>
            ServerMessage.Of("marketing.server.segment-service.target-mismatch", "Segmentin hedef türü kampanyanınkiyle aynı değil.");

        public static ServerMessage AlreadyArchived =>
            ServerMessage.Of("marketing.server.segment-service.already-archived", "Segment zaten arşivde.");

        public static ServerMessage OnlyArchivedRestore =>
            ServerMessage.Of("marketing.server.segment-service.only-archived-restore", "Yalnızca arşivlenen segment geri alınır.");

        public static ServerMessage ConditionInvalid =>
            ServerMessage.Of("marketing.server.segment-service.condition-invalid", "Koşul geçersiz.");

        public static ServerMessage PublishedUnreadable =>
            ServerMessage.Of("marketing.server.segment-service.published-unreadable", "Yayımlanmış koşul okunamadı.");

        public static ServerMessage TooLarge(string count, string limit) =>
            ServerMessage.Of("marketing.server.segment-service.too-large", "Sonuç {0} hedef; en fazla {1} kabul edilir. Segmenti daraltın.", count, limit);

        public static ServerMessage Timeout(string seconds) =>
            ServerMessage.Of("marketing.server.segment-service.timeout", "Hesaplama {0} saniyelik sınırı aştı.", seconds);
    }

    /// <summary>Kampanya hedef kitle servisi.</summary>
    public static class AudienceService
    {
        public static ServerMessage ArchivedFreeze =>
            ServerMessage.Of("marketing.server.audience-service.archived-freeze", "Arşivlenen kampanyanın hedef kitlesi dondurulamaz.");

        public static ServerMessage TooManyRows(string rows, string max) =>
            ServerMessage.Of("marketing.server.audience-service.too-many-rows", "Hedef kitle {0} satır; en fazla {1} dondurulabilir. Segmenti daraltın.", rows, max);

        public static ServerMessage AlreadyArchived =>
            ServerMessage.Of("marketing.server.audience-service.already-archived", "Kampanya zaten arşivde.");

        public static ServerMessage OnlyArchivedRestore =>
            ServerMessage.Of("marketing.server.audience-service.only-archived-restore", "Yalnızca arşivlenen kampanya geri alınır.");

        public static ServerMessage Empty =>
            ServerMessage.Of("marketing.server.audience-service.empty", "Hedef kitle boş.");

        public static ServerMessage EmptyDetail =>
            ServerMessage.Of("marketing.server.audience-service.empty-detail", "Hedef kitleye en az bir dahil edilen segment ya da hedef ekleyin.");

        public static ServerMessage TooManyTargets(string count, string max) =>
            ServerMessage.Of("marketing.server.audience-service.too-many-targets", "Hedef kitle {0} hedef; en fazla {1} kabul edilir. Segmenti daraltın.", count, max);

        public static ServerMessage NoResolver =>
            ServerMessage.Of("marketing.server.audience-service.no-resolver", "Alıcıları çözecek bir modül yok (ör. CRM bu firma için kapalı).");

        public static ServerMessage ResolverPermission =>
            ServerMessage.Of("marketing.server.audience-service.resolver-permission", "Alıcıları çözmek için kişi kayıtlarını okuma yetkisi gerekir.");
    }

    /// <summary>Kampanya yanıtı servisi.</summary>
    public static class ResponseService
    {
        public static ServerMessage KindInvalid =>
            ServerMessage.Of("marketing.server.response-service.kind-invalid", "Yanıt türü geçersiz.");

        public static ServerMessage ReceivedFuture =>
            ServerMessage.Of("marketing.server.response-service.received-future", "Yanıt zamanı gelecekte olamaz.");

        public static ServerMessage Invalid =>
            ServerMessage.Of("marketing.server.response-service.invalid", "Yanıt kaydı geçersiz.");

        public static ServerMessage DeliveryNotFound =>
            ServerMessage.Of("marketing.server.response-service.delivery-not-found", "İleti bulunamadı.");

        public static ServerMessage OnlySent =>
            ServerMessage.Of("marketing.server.response-service.only-sent", "Yanıt yalnızca gönderilmiş bir iletiye kaydedilir.");

        public static ServerMessage NotHandedOver =>
            ServerMessage.Of("marketing.server.response-service.not-handed-over", "Bu ileti sağlayıcıya verilmemiş.");

        public static ServerMessage UnknownAction =>
            ServerMessage.Of("marketing.server.response-service.unknown-action", "Bilinmeyen eylem.");

        public static ServerMessage ActionInvalid =>
            ServerMessage.Of("marketing.server.response-service.action-invalid", "Geçersiz eylem.");

        public static ServerMessage ReasonRequired =>
            ServerMessage.Of("marketing.server.response-service.reason-required", "Gerekçe zorunludur.");

        public static ServerMessage ReasonRequiredDetail =>
            ServerMessage.Of("marketing.server.response-service.reason-required-detail", "Bu işlem için gerekçe zorunludur.");

        public static ServerMessage ReasonTooLong =>
            ServerMessage.Of("marketing.server.response-service.reason-too-long", "Gerekçe çok uzun.");

        public static ServerMessage ReasonMax(int max) =>
            ServerMessage.Of("marketing.server.response-service.reason-max", "Gerekçe en fazla {0} karakter olabilir.", max);

        public static ServerMessage NotFound =>
            ServerMessage.Of("marketing.server.response-service.not-found", "Yanıt bulunamadı.");

        public static ServerMessage AlreadyLead =>
            ServerMessage.Of("marketing.server.response-service.already-lead", "Bu yanıt zaten bir müşteri adayına bağlı.");

        public static ServerMessage ActivityExists =>
            ServerMessage.Of("marketing.server.response-service.activity-exists", "Bu yanıt için zaten bir aktivite açıldı.");

        public static ServerMessage LeadNotFound =>
            ServerMessage.Of("marketing.server.response-service.lead-not-found", "Müşteri adayı bulunamadı ya da görme yetkiniz yok.");

        public static ServerMessage LeadModuleMissing =>
            ServerMessage.Of("marketing.server.response-service.lead-module-missing", "Müşteri adayı kullanılamıyor: bu firmada müşteri adayı modülü kurulu değil.");

        public static ServerMessage LeadModuleClosed =>
            ServerMessage.Of("marketing.server.response-service.lead-module-closed", "Müşteri adayı kullanılamıyor: bu firmada müşteri adayı modülü kapalı.");

        public static ServerMessage LeadOpenFailed =>
            ServerMessage.Of("marketing.server.response-service.lead-open-failed", "Müşteri adayı açılamadı.");

        public static ServerMessage ActivityModuleClosed =>
            ServerMessage.Of("marketing.server.response-service.activity-module-closed", "Aktivite kullanılamıyor: bu firmada aktivite modülü kapalı.");

        public static ServerMessage ActivityOpenFailed =>
            ServerMessage.Of("marketing.server.response-service.activity-open-failed", "Aktivite açılamadı.");

        public static ServerMessage AddressUnresolvable =>
            ServerMessage.Of("marketing.server.response-service.address-unresolvable", "Adres artık çözülemiyor (iletişim noktası pasif ya da silinmiş); çıkışı iletişim tercihleri ekranından elle kaydedin.");

        public static ServerMessage CampaignOrDeliveryMissing =>
            ServerMessage.Of("marketing.server.response-service.campaign-or-delivery-missing", "Kampanya ya da ileti bulunamadı.");
    }

    /// <summary>Kampanya raporları.</summary>
    public static class Report
    {
        public static ServerMessage PermissionsBoth(string first, string second) =>
            ServerMessage.Of("marketing.server.report.permissions-both", "Bu işlem için '{0}' ve '{1}' izinleri gerekir.", first, second);
    }

    /// <summary>Sayı iletisi bağımsız değişkeni: binlik ayraçlı, kültürden bağımsız.</summary>
    public static string N(int value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
