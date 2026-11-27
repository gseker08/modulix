using Modulix.Core.Contracts.Modules;

namespace Modulix.Modules.Crm;

/// <summary>
/// CRM formlarının varsayılan düzenleri (ADR-0021). Alan anahtarları kayıt alanlarının teknik adlarıdır; arayüzdeki
/// editörler her anahtar için kendi girdisini çizer, yer ve genişlik buradan (ya da firmanın düzeninden) gelir.
/// Kaydın kimliğini taşıyan alanlar ve yalnızca ekranda anlamı olan onay kutuları sabittir (gizlenemez, zorunluluğu değişmez).
/// </summary>
public static class CrmForms
{
    private const string General = FormDefinition.DefaultTab;
    private const string Extras = FormDefinition.ExtrasSection;

    // Her kayıt formu sekmelidir (arayüz kuralı, CLAUDE.md → Arayüz tasarım kuralları): bölümler anlamlı sekmelere dağıtılır.
    // Sekme anahtarları firmanın düzeninde saklanır; "finans" gibi anahtarlar firmaların eklediği sekmelere bırakılmıştır.
    public static IReadOnlyList<FormDefinition> All { get; } =
    [
        new(CrmModule.Entities.Company, "Firma",
        [new(General, "Genel bilgiler"), new("iletisim", "İletişim"), new("notlar", "Ek bilgiler")],
        [
            new("temel", "Temel bilgiler", "building"),
            new("iletisim", "Genel iletişim bilgileri", "phone", Tab: "iletisim"),
            new(Extras, "Ek bilgiler", "square-pen", Tab: "notlar"),
        ],
        [
            new("name", "Firma adı", "temel", 6, Required: true, Fixed: true),
            new("kind", "Tür", "temel", 3, Fixed: true),
            new("status", "Durum", "temel", 3, Fixed: true),
            new("ownerId", "Sorumlu", "temel", 4),
            new("industry", "Sektör", "temel", 4),
            new("source", "Kaynak", "temel", 4),
            new("taxOffice", "Vergi dairesi", "temel", 4),
            new("taxNumber", "Vergi no / T.C. kimlik no", "temel", 4),
            new("tags", "Etiketler", "temel", 4),
            new("website", "Web sitesi", "iletisim", 6),
            new("preferredLanguage", "Yazışma dili", "iletisim", 6),
            // Yeni firmada hızlı giriş: kayıt ana adres/telefon/e-posta kaydını açar (ADR-0012). Kayıtlı firmada bu dört alan
            // çizilmez; yerlerini aşağıdaki liste alanları alır (ana kayıtların aynasıdır, API'de durur).
            new("address", "Adres", "iletisim", 12),
            new("phone", "Telefon", "iletisim", 4),
            new("email", "E-posta", "iletisim", 4),
            new("city", "Şehir", "iletisim", 4),
            // Kayıtlı firmada çoklu kayıt listeleri (kendi uçlarıyla anında kaydedilir; formun Kaydet düğmesine bağlı değildir).
            new("addresses", "Adresler", "iletisim", 12),
            new("contactPoints", "Telefon ve e-postalar", "iletisim", 12),
            new("locations", "Lokasyonlar", "iletisim", 12),
        ]),

        new(CrmModule.Entities.Contact, "Kişi",
        [new(General, "Kişi bilgileri"), new("iletisim", "İletişim"), new("notlar", "Ek bilgiler")],
        [
            new("kisi", "Kişi bilgileri", "user"),
            new("iletisim", "İletişim", "phone", Tab: "iletisim"),
            new(Extras, "Ek bilgiler", "square-pen", Tab: "notlar"),
        ],
        [
            new("firstName", "Ad", "kisi", 6, Required: true, Fixed: true),
            new("lastName", "Soyad", "kisi", 6, Required: true, Fixed: true),
            new("title", "Unvan", "kisi", 4),
            new("companyId", "Firma", "kisi", 4),
            new("ownerId", "Sorumlu", "kisi", 4),
            new("email", "E-posta (ana)", "iletisim", 6),
            new("phone", "Telefon (ana)", "iletisim", 6),
            new("preferredLanguage", "Yazışma dili", "iletisim", 6),
        ]),

        new(CrmModule.Entities.Opportunity, "Fırsat",
        [new(General, "Genel bilgiler"), new("tutar", "Tutar ve zamanlama"), new("detay", "Detaylar"), new("notlar", "Açıklama ve ek bilgiler")],
        [
            new("temel", "Temel bilgiler", "target"),
            new("tutar", "Tutar ve zamanlama", "coins", Tab: "tutar"),
            new("detay", "Detaylar", "clipboard-list", Tab: "detay"),
            new("aciklama", "Açıklama", "message-square", Tab: "notlar"),
            new(Extras, "Ek bilgiler", "square-pen", Tab: "notlar"),
        ],
        [
            new("title", "Fırsat adı", "temel", 12, Required: true, Fixed: true),
            new("companyId", "Müşteri (firma)", "temel", 6, Required: true, Fixed: true),
            new("contactId", "İlgili kişi", "temel", 6),
            new("stage", "Aşama", "temel", 4, Fixed: true),
            new("priority", "Öncelik", "temel", 4, Fixed: true),
            new("type", "Fırsat tipi", "temel", 4),
            new("source", "Kaynak", "temel", 6),
            new("campaignId", "Kampanya", "temel", 6),
            new("ownerId", "Satış sorumlusu", "temel", 6),
            new("amount", "Tahmini tutar", "tutar", 4),
            new("probability", "Olasılık (%)", "tutar", 4),
            new("currency", "Para birimi", "tutar", 4, Fixed: true),
            new("expectedCloseDate", "Tahmini kapanış tarihi", "tutar", 4),
            new("targetDecisionDate", "Hedef karar tarihi", "tutar", 4),
            new("customerBudget", "Müşteri bütçesi", "tutar", 4),
            new("budgetStatus", "Bütçe durumu", "tutar", 4, Fixed: true),
            new("nextAction", "Sonraki aksiyon", "tutar", 4),
            new("nextActionDate", "Aksiyon tarihi", "tutar", 4),
            new("needDescription", "İhtiyaç", "detay", 6),
            new("currentProblem", "Mevcut sorun", "detay", 6),
            new("expectedBenefit", "Beklenen fayda", "detay", 6),
            new("scope", "Kapsam", "detay", 6),
            new("serviceScope", "Hizmet kapsamı", "detay", 12),
            new("successCriteria", "Başarı ölçütleri", "detay", 6),
            new("risks", "Riskler ve engeller", "detay", 6),
            new("decisionProcess", "Karar süreci", "detay", 12),
            new("description", "Açıklama", "aciklama", 12),
        ]),

        new(CrmModule.Entities.Lead, "Müşteri adayı",
        [new(General, "Talep ve sahip"), new("eslestirme", "Eşleştirme ve bütçe"), new("ihtiyac", "İhtiyaç ve ek bilgiler")],
        [
            new("talep", "Talep", "inbox"),
            new("sahip", "Talep sahibi", "user"),
            new("eslestirme", "Eşleştirme ve sahiplik", "user-check", Tab: "eslestirme"),
            new("butce", "Bütçe ve zamanlama", "coins", Tab: "eslestirme"),
            new("ihtiyac", "İhtiyaç ve açıklama", "message-square", Tab: "ihtiyac"),
            new(Extras, "Ek bilgiler", "square-pen", Tab: "ihtiyac"),
        ],
        [
            new("subject", "Konu", "talep", 12, Required: true, Fixed: true),
            new("priority", "Öncelik", "talep", 4, Fixed: true),
            new("source", "Kaynak", "talep", 4),
            new("sourceDetail", "Kaynak ayrıntısı", "talep", 4),
            new("campaignId", "Kampanya", "talep", 6),
            new("productInterest", "İlgilenilen ürün/hizmet", "talep", 6),
            new("industry", "Sektör", "talep", 6),
            new("serviceScope", "Hizmet kapsamı", "talep", 12),
            new("firstName", "Ad", "sahip", 4),
            new("lastName", "Soyad", "sahip", 4),
            new("jobTitle", "Unvan", "sahip", 4),
            new("companyName", "Firma adı", "sahip", 6),
            new("email", "E-posta", "sahip", 6),
            new("phone", "Telefon", "sahip", 4),
            new("website", "Web sitesi", "sahip", 4),
            new("city", "Şehir", "sahip", 3),
            new("country", "Ülke (2 harf)", "sahip", 3),
            new("ownerId", "Sorumlu", "eslestirme", 4, Fixed: true),
            new("companyId", "Mevcut müşteri firması", "eslestirme", 4),
            new("contactId", "Mevcut kişi", "eslestirme", 4),
            new("estimatedBudget", "Tahmini bütçe", "butce", 4),
            new("currency", "Para birimi", "butce", 4, Fixed: true),
            new("targetPurchaseDate", "Hedef satın alma tarihi", "butce", 4),
            new("needDescription", "İhtiyaç", "ihtiyac", 12),
            new("description", "Açıklama / talep metni", "ihtiyac", 12),
        ]),

        new(CrmModule.Entities.Activity, "Aktivite",
        [new(General, "Aktivite ve zaman"), new("iliskili", "İlişkili kayıtlar"), new("yer", "Yer ve ayrıntılar")],
        [
            new("aktivite", "Aktivite", "activity"),
            new("zaman", "Zaman", "calendar"),
            new("iliskili", "İlişkili kayıtlar", "users", Tab: "iliskili"),
            new("yer", "Yer ve ayrıntılar", "map-pin", Tab: "yer"),
            new(Extras, "Ek bilgiler", "square-pen", Tab: "yer"),
        ],
        [
            new("subject", "Konu", "aktivite", 12, Required: true, Fixed: true),
            new("type", "Aktivite türü", "aktivite", 4, Fixed: true),
            new("priority", "Öncelik", "aktivite", 4, Fixed: true),
            new("dueAt", "Son tarih", "aktivite", 4),
            new("isAllDay", "Tüm gün", "zaman", 12, Fixed: true),
            new("startAt", "Başlangıç", "zaman", 6),
            new("endAt", "Bitiş", "zaman", 6),
            new("estimatedMinutes", "Tahmini süre (dk)", "zaman", 4),
            new("ownerId", "Atanan kullanıcı", "iliskili", 6, Fixed: true),
            new("companyId", "İlgili firma", "iliskili", 6),
            new("contactId", "İlgili kişi", "iliskili", 6),
            new("opportunityId", "Fırsat", "iliskili", 6),
            new("location", "Konum", "yer", 6),
            new("onlineMeetingUrl", "Toplantı bağlantısı", "yer", 6),
            new("isPrivate", "Özel", "yer", 12, Fixed: true),
            new("logAsDone", "Geçmişte gerçekleşti", "yer", 12, Fixed: true),
            new("notes", "Açıklama", "yer", 12),
        ]),

        // Teklif başlığı (ADR-0019). Satırlar form değil kendi editörüyle düzenlenir; toplamlar ve durumlar sunucudan gelir, formda yer almaz.
        // Sektör profilleri (ADR-0029/0041) bu forma kendi özel alanlarını ekler; "ek" bölümü varsayılan yerleridir.
        new(CrmModule.Entities.Quote, "Teklif",
        [new(General, "Genel bilgiler"), new("fiyat", "Fiyat ve geçerlilik"), new("kosul", "Koşullar ve notlar"), new("ek", "Ek bilgiler")],
        [
            new("temel", "Temel bilgiler", "file-text"),
            new("fiyat", "Fiyat ve geçerlilik", "coins", Tab: "fiyat"),
            new("indirim", "Belge indirimi", "percent", Tab: "fiyat"),
            new("kosul", "Ticari koşullar", "clipboard-list", Tab: "kosul"),
            new("not", "İç notlar", "message-square", Tab: "kosul"),
            new(Extras, "Ek bilgiler", "square-pen", Tab: "ek"),
        ],
        [
            new("title", "Teklif başlığı", "temel", 12, Required: true, Fixed: true),
            new("companyId", "Firma", "temel", 6, Required: true, Fixed: true),
            new("contactId", "Kişi", "temel", 6),
            new("opportunityId", "Fırsat", "temel", 6),
            new("ownerId", "Sorumlu", "temel", 6),
            new("priceListId", "Fiyat listesi", "fiyat", 6),
            new("currency", "Para birimi", "fiyat", 3, Fixed: true),
            new("validUntil", "Geçerlilik", "fiyat", 3),
            new("reportingCurrency", "Gösterge para birimi", "fiyat", 3),
            new("priceIncludesTax", "Fiyatlandırma", "fiyat", 6, Fixed: true),
            new("documentDiscountKind", "Belge indirimi", "indirim", 6),
            new("documentDiscountValue", "İndirim değeri", "indirim", 6),
            new("paymentTerms", "Ödeme koşulları", "kosul", 12),
            new("deliveryTerms", "Teslimat koşulları", "kosul", 12),
            new("commercialTerms", "Ticari koşullar", "kosul", 12),
            new("internalNotes", "İç notlar", "not", 12),
        ]),
    ];
}
