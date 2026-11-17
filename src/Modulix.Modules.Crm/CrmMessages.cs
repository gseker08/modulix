using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Crm;

/// <summary>
/// CRM'in sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-1): kayıt alanlarının biçim iletileri, bağlı kayıt denetimleri ve
/// CRM iş kurallarının iletileri. Anahtarlar <c>crm.server.*</c>; Türkçe metin <c>tr.json</c>'daki kalıpla aynıdır (birim testle
/// denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler (<see cref="ServerMessages"/>) burada yinelenmez.
/// </summary>
internal static class CrmMessages
{
    /// <summary>Alan biçimi iletileri (<c>RecordField</c>).</summary>
    public static class Field
    {
        public static ServerMessage NotEmpty => ServerMessage.Of("crm.server.field.not-empty", "Bu alan boş bırakılamaz.");

        public static ServerMessage CodeNameList =>
            ServerMessage.Of("crm.server.field.code-name-list", "Kod ve ad çiftlerinden oluşan liste bekleniyor.");

        public static ServerMessage Money =>
            ServerMessage.Of("crm.server.field.money", "0 veya daha büyük, en fazla iki ondalıklı bir tutar girin.");

        public static ServerMessage PositiveDecimal =>
            ServerMessage.Of("crm.server.field.positive-decimal", "Sıfırdan büyük, en fazla on ondalıklı bir sayı girin.");

        public static ServerMessage Rate =>
            ServerMessage.Of("crm.server.field.rate", "0 ile 100 arasında, en fazla dört ondalıklı bir oran girin.");

        public static ServerMessage Quantity =>
            ServerMessage.Of("crm.server.field.quantity", "0 veya daha büyük, en fazla altı ondalıklı bir sayı girin.");

        public static ServerMessage Currency => ServerMessage.Of("crm.server.field.currency", "Üç harfli para birimi kodu girin (ör. TRY).");

        public static ServerMessage Time => ServerMessage.Of("crm.server.field.time", "Zaman ISO 8601 biçiminde olmalıdır.");

        public static ServerMessage NotInList => ServerMessage.Of("crm.server.field.not-in-list", "Listede olmayan değer.");

        public static ServerMessage Retired => ServerMessage.Of("crm.server.field.retired", "Bu değer artık kullanılmıyor.");

        public static ServerMessage Percent => ServerMessage.Of("crm.server.field.percent", "0 ile 100 arasında bir tam sayı girin.");

        public static ServerMessage Minutes(int max) => ServerMessage.Of("crm.server.field.minutes", "1 ile {0} arasında dakika girin.", max);

        public static ServerMessage IntegerRange(int min, int max) =>
            ServerMessage.Of("crm.server.field.integer-range", "{0} ile {1} arasında bir tam sayı girin.", min, max);

        public static ServerMessage PartyKind => ServerMessage.Of("crm.server.field.party-kind", "Organization veya Individual olmalıdır.");

        public static ServerMessage Flag => ServerMessage.Of("crm.server.field.flag", "true veya false olmalıdır.");

        public static ServerMessage KeyList => ServerMessage.Of("crm.server.field.key-list", "Metin listesi olmalıdır.");

        public static ServerMessage InvalidValue(string value) => ServerMessage.Of("crm.server.field.invalid-value", "Geçersiz değer: {0}.", value);

        public static ServerMessage TooManyValues => ServerMessage.Of("crm.server.field.too-many-values", "Çok fazla değer seçildi.");

        public static ServerMessage OneOf(string allowed) => ServerMessage.Of("crm.server.field.one-of", "Şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage Phone => ServerMessage.Of("crm.server.field.phone", "Geçerli bir telefon numarası girin.");

        public static ServerMessage Country => ServerMessage.Of("crm.server.field.country", "İki harfli ülke kodu girin (ör. TR).");

        public static ServerMessage Url => ServerMessage.Of("crm.server.field.url", "Geçerli bir web adresi girin.");

        public static ServerMessage CreateOnly => ServerMessage.Of("crm.server.field.create-only", "Oluşturulduktan sonra değiştirilemez.");

        public static ServerMessage InvalidSort => ServerMessage.Of("crm.server.field.invalid-sort", "Geçersiz sıralama.");

        public static ServerMessage NotMember => ServerMessage.Of("crm.server.field.not-member", "Kullanıcı bu kuruluşun üyesi değil.");

        public static ServerMessage EndBeforeStart => ServerMessage.Of("crm.server.field.end-before-start", "Bitiş tarihi başlangıçtan önce olamaz.");

        /// <summary>Eşzamanlılık: okunan sürüm güncel değil (sürümler iletide).</summary>
        public static ServerMessage Concurrency(long expected, long current) =>
            ServerMessage.Of("crm.server.field.concurrency",
                "Kayıt siz düzenlerken değişti (beklenen sürüm {0}, güncel sürüm {1}). Güncel halini alıp tekrar deneyin.", expected, current);
    }

    /// <summary>Bağlı kayıt denetimleri.</summary>
    public static class Lookup
    {
        public static ServerMessage CompanyNotFound => ServerMessage.Of("crm.server.lookup.company-not-found", "Firma bulunamadı.");

        public static ServerMessage ContactNotFound => ServerMessage.Of("crm.server.lookup.contact-not-found", "Kişi bulunamadı.");

        public static ServerMessage CampaignNotFound => ServerMessage.Of("crm.server.lookup.campaign-not-found", "Kampanya bulunamadı.");

        public static ServerMessage ContactNotOfCompany =>
            ServerMessage.Of("crm.server.lookup.contact-not-of-company", "Kişi seçilen firmayla ilişkili değil.");

        public static ServerMessage ContactNotRelated => ServerMessage.Of("crm.server.lookup.contact-not-related", "Kişi bu firmayla ilişkili değil.");

        public static ServerMessage OpportunityNotOfCompany =>
            ServerMessage.Of("crm.server.lookup.opportunity-not-of-company", "Fırsat seçilen firmaya ait değil.");

        public static ServerMessage LocationNotOfCompany =>
            ServerMessage.Of("crm.server.lookup.location-not-of-company", "Lokasyon bu firmaya ait değil.");

        public static ServerMessage RelationNotOfContact =>
            ServerMessage.Of("crm.server.lookup.relation-not-of-contact", "İlişki bu kişiye ait değil.");
    }

    /// <summary>Firma, firma ilişkisi, lokasyon, adres ve iletişim noktası iletileri.</summary>
    public static class Company
    {
        public static ServerMessage AlreadyInCrm => ServerMessage.Of("crm.server.company.already-in-crm", "Bu firma CRM'de zaten kayıtlı.");

        public static ServerMessage TaxNumberTaken =>
            ServerMessage.Of("crm.server.company.tax-number-taken", "Bu vergi numarasıyla kayıtlı bir firma var.");

        /// <summary>Kapsam dışı kaydı gizleyen rollerde çakışma iletisine eklenen yönlendirme (ADR-0026 K9).</summary>
        public static ServerMessage AlreadyInCrmReferral =>
            ServerMessage.Of("crm.server.company.already-in-crm.referral",
                "Bu firma CRM'de zaten kayıtlı. Kaydı göremiyorsanız yetkili (merkez) kullanıcıya başvurun.");

        public static ServerMessage TaxNumberTakenReferral =>
            ServerMessage.Of("crm.server.company.tax-number-taken.referral",
                "Bu vergi numarasıyla kayıtlı bir firma var. Kaydı göremiyorsanız yetkili (merkez) kullanıcıya başvurun.");

        public static ServerMessage NationalIdLength =>
            ServerMessage.Of("crm.server.company.national-id-length", "T.C. kimlik numarası 11 haneli olmalıdır.");

        public static ServerMessage TaxNumberLength => ServerMessage.Of("crm.server.company.tax-number-length", "Vergi numarası 10 haneli olmalıdır.");

        public static ServerMessage HasDirectory =>
            ServerMessage.Of("crm.server.company.has-directory", "Firmanın lokasyonları veya firma ilişkileri var; önce onları kaldırın.");

        public static ServerMessage SelfRelation => ServerMessage.Of("crm.server.company.self-relation", "Bir firma kendisine bağlanamaz.");

        public static ServerMessage RelationExists =>
            ServerMessage.Of("crm.server.company.relation-exists", "Bu iki firma arasında zaten aktif bir ilişki var.");

        public static ServerMessage RelationCycle => ServerMessage.Of("crm.server.company.relation-cycle", "Bu bağ firma hiyerarşisinde döngü oluşturur.");

        public static ServerMessage ContactAlreadyLinked =>
            ServerMessage.Of("crm.server.company.contact-already-linked", "Bu kişi bu firmayla zaten ilişkili.");

        public static ServerMessage PrimaryContactExists =>
            ServerMessage.Of("crm.server.company.primary-contact-exists",
                "Bu firmada zaten bir ana irtibat var; önce onun ana irtibat işaretini kaldırın.");

        public static ServerMessage LocationCodeTaken =>
            ServerMessage.Of("crm.server.company.location-code-taken", "Bu kod firmanın başka bir lokasyonunda kullanılıyor.");

        public static ServerMessage PrimaryAddressExists =>
            ServerMessage.Of("crm.server.company.primary-address-exists",
                "Bu sahipte zaten bir ana adres var; önce onun ana adres işaretini kaldırın.");

        public static ServerMessage ContactPointPhone =>
            ServerMessage.Of("crm.server.company.contact-point-phone",
                "Geçerli bir telefon numarası girin (7-15 rakam; dahili numara nota yazılır).");

        public static ServerMessage ContactPointPhoneLength(int max) =>
            ServerMessage.Of("crm.server.company.contact-point-phone-length", "Telefon en fazla {0} karakter olabilir.", max);

        public static ServerMessage ContactPointExists =>
            ServerMessage.Of("crm.server.company.contact-point-exists", "Bu iletişim noktası aynı sahipte zaten kayıtlı.");

        public static ServerMessage PrimaryContactPointExists =>
            ServerMessage.Of("crm.server.company.primary-contact-point-exists",
                "Bu sahipte bu kanal için zaten bir ana kayıt var; önce onun ana işaretini kaldırın.");

        public static ServerMessage IndustryNotInCatalog => ServerMessage.Of("crm.server.company.industry-not-in-catalog", "Katalogda olmayan sektör.");

        public static ServerMessage IndustryRetired => ServerMessage.Of("crm.server.company.industry-retired", "Bu sektör artık kullanılmıyor.");

        public static ServerMessage UnknownServiceActivities(string activities) =>
            ServerMessage.Of("crm.server.company.unknown-service-activities", "Katalogda olmayan faaliyet: {0}.", activities);

        public static ServerMessage ServiceNotActivity(string codes) =>
            ServerMessage.Of("crm.server.company.service-not-activity",
                "Hizmet kapsamı için sektör veya grup değil, faaliyet seçilmeli: {0}.", codes);

        public static ServerMessage InactiveServiceActivities(string activities) =>
            ServerMessage.Of("crm.server.company.inactive-service-activities", "Pasif faaliyet eklenemez: {0}.", activities);

        public static ServerMessage MergeChoice =>
            ServerMessage.Of("crm.server.company.merge-choice", "Değer \"source\" veya \"target\" olmalıdır.");
    }

    /// <summary>Kişi iletileri.</summary>
    public static class Contact
    {
        public static ServerMessage AlreadyInCrm => ServerMessage.Of("crm.server.contact.already-in-crm", "Bu kişi CRM'de zaten kayıtlı.");
    }

    /// <summary>Müşteri adayı (lead) kayıtları, iş akışı, dönüşüm, form ve puan kuralı iletileri.</summary>
    public static class Lead
    {
        public static ServerMessage AssignOpenOnly => ServerMessage.Of("crm.server.lead.assign-open-only", "Yalnızca açık müşteri adayları atanabilir.");

        public static ServerMessage ContactingNotAllowed =>
            ServerMessage.Of("crm.server.lead.contacting-not-allowed",
                "İletişime geçilebilmesi için müşteri adayı yeni ya da daha sonra takipte olmalıdır.");

        public static ServerMessage AlreadyQualified => ServerMessage.Of("crm.server.lead.already-qualified", "Müşteri adayı zaten nitelikli.");

        public static ServerMessage QualifyNotAllowed =>
            ServerMessage.Of("crm.server.lead.qualify-not-allowed", "Bu durumdaki müşteri adayı nitelendirilemez.");

        public static ServerMessage AlreadyNurturing =>
            ServerMessage.Of("crm.server.lead.already-nurturing",
                "Müşteri adayı zaten daha sonra takipte (tarihi değiştirmek için yeniden takibe alın).");

        public static ServerMessage NurtureNotAllowed => ServerMessage.Of("crm.server.lead.nurture-not-allowed", "Bu durumdaki müşteri adayı takibe alınamaz.");

        public static ServerMessage DisqualifyOpenOnly =>
            ServerMessage.Of("crm.server.lead.disqualify-open-only", "Yalnızca açık müşteri adayları elenebilir.");

        public static ServerMessage ConvertedCannotReopen =>
            ServerMessage.Of("crm.server.lead.converted-cannot-reopen", "Dönüştürülmüş müşteri adayı yeniden açılamaz.");

        public static ServerMessage ReopenDisqualifiedOnly =>
            ServerMessage.Of("crm.server.lead.reopen-disqualified-only", "Yalnızca elenmiş müşteri adayı yeniden açılabilir.");

        public static ServerMessage AlreadyConverted => ServerMessage.Of("crm.server.lead.already-converted", "Müşteri adayı zaten dönüştürülmüş.");

        /// <summary>Zaten dönüştürülmüş adayın çakışma iletisi; oluşan fırsatın kimliğiyle.</summary>
        public static ServerMessage AlreadyConvertedTo(string opportunityId) =>
            ServerMessage.Of("crm.server.lead.already-converted-to", "Müşteri adayı zaten dönüştürülmüş. Fırsat: {0}", opportunityId);

        public static ServerMessage ConvertQualifiedOnly =>
            ServerMessage.Of("crm.server.lead.convert-qualified-only", "Yalnızca nitelikli müşteri adayı dönüştürülebilir.");

        public static ServerMessage MissingNeed =>
            ServerMessage.Of("crm.server.lead.missing-need", "İhtiyaç: açıklama, ihtiyaç ya da ilgilenilen ürün/hizmet girilmeli.");

        public static ServerMessage MissingContact =>
            ServerMessage.Of("crm.server.lead.missing-contact", "İletişim: e-posta, telefon ya da mevcut kişi girilmeli.");

        public static ServerMessage MissingRequester =>
            ServerMessage.Of("crm.server.lead.missing-requester", "Talep sahibi: ad, firma adı ya da mevcut firma/kişi girilmeli.");

        public static ServerMessage QualifyIncomplete =>
            ServerMessage.Of("crm.server.lead.qualify-incomplete", "Müşteri adayı nitelendirilemedi: eksik bilgiler var.");

        public static ServerMessage DisqualificationReasonRequired =>
            ServerMessage.Of("crm.server.lead.disqualification-reason-required", "Eleme nedeni zorunludur.");

        public static ServerMessage FollowUpRequired =>
            ServerMessage.Of("crm.server.lead.follow-up-required",
                "Takip tarihi girin ya da müşteri adayına vadeli bir takip aktivitesi ekleyin.");

        public static ServerMessage FollowUpInFuture => ServerMessage.Of("crm.server.lead.follow-up-in-future", "Takip tarihi gelecekte olmalıdır.");

        public static ServerMessage ReopenReasonRequired =>
            ServerMessage.Of("crm.server.lead.reopen-reason-required", "Yeniden açma gerekçesi zorunludur.");

        public static ServerMessage ConvertPermissionDenied =>
            ServerMessage.Of("crm.server.lead.convert-permission-denied", "Müşteri adayı dönüştürme izniniz yok.");

        public static ServerMessage ConvertCompanyChoice =>
            ServerMessage.Of("crm.server.lead.convert-company-choice",
                "Mevcut bir firma seçin ya da yeni firma oluşturmayı seçin (ikisinden tam biri).");

        public static ServerMessage ConvertContactChoice =>
            ServerMessage.Of("crm.server.lead.convert-contact-choice", "Mevcut kişi ile yeni kişi oluşturma birlikte seçilemez.");

        public static ServerMessage ConvertConcurrency(long expected, long current) =>
            ServerMessage.Of("crm.server.lead.convert-concurrency",
                "Müşteri adayı siz düzenlerken değişti (beklenen sürüm {0}, güncel sürüm {1}). Güncel halini alıp tekrar deneyin.",
                expected, current);

        public static ServerMessage ConvertIncomplete =>
            ServerMessage.Of("crm.server.lead.convert-incomplete", "Müşteri adayı dönüştürülemedi: eksik bilgiler var.");

        public static ServerMessage NewContactName => ServerMessage.Of("crm.server.lead.new-contact-name", "Yeni kişi için ad ve soyad gerekir.");

        public static ServerMessage IdentityRequired =>
            ServerMessage.Of("crm.server.lead.identity-required",
                "Talep sahibini tanıtan en az bir bilgi girin (ad, firma adı, e-posta, telefon ya da mevcut firma/kişi).");

        public static ServerMessage AssignPermissionRequired =>
            ServerMessage.Of("crm.server.lead.assign-permission-required", "Başka birine atamak için atama izni gerekir.");

        public static ServerMessage FormOrigins =>
            ServerMessage.Of("crm.server.lead.form-origins",
                "Her kaynak https://alanadi[:port] biçiminde olmalı (yol, joker ya da boşluk içermez); virgül ya da satırla ayırın.");

        public static ServerMessage ScoreFieldRequired => ServerMessage.Of("crm.server.lead.score-field-required", "Puanlanacak alanı seçin.");

        public static ServerMessage ScoreOperatorNotAllowed(string field) =>
            ServerMessage.Of("crm.server.lead.score-operator-not-allowed", "\"{0}\" alanı için bu karşılaştırma kullanılamaz.", field);

        public static ServerMessage ScoreValueRequired => ServerMessage.Of("crm.server.lead.score-value-required", "Karşılaştırılacak değeri girin.");

        public static ServerMessage ScorePointsNonZero =>
            ServerMessage.Of("crm.server.lead.score-points-non-zero", "Puan sıfır olamaz; -100 ile 100 arasında bir değer girin.");

        public static ServerMessage ReportRangeTitle => ServerMessage.Of("crm.server.lead.report-range.title", "Geçersiz tarih aralığı.");

        public static ServerMessage ReportRangeDetail => ServerMessage.Of("crm.server.lead.report-range.detail", "Başlangıç, bitişten önce olmalıdır.");
    }

    /// <summary>Fırsat kayıtları, aşama/kapanış iş akışı ve fırsat raporu iletileri.</summary>
    public static class Opportunity
    {
        public static ServerMessage NotFound => ServerMessage.Of("crm.server.opportunity.not-found", "Fırsat bulunamadı.");

        public static ServerMessage StageOnHold =>
            ServerMessage.Of("crm.server.opportunity.stage-on-hold", "Beklemedeki fırsatın aşaması değiştirilemez; önce beklemeden çıkarın.");

        public static ServerMessage StageClosed =>
            ServerMessage.Of("crm.server.opportunity.stage-closed", "Kapanmış fırsatın aşaması değiştirilemez; önce yeniden açın.");

        public static ServerMessage CloseOnHold =>
            ServerMessage.Of("crm.server.opportunity.close-on-hold", "Beklemedeki fırsat kapatılamaz; önce beklemeden çıkarın.");

        public static ServerMessage AlreadyClosed => ServerMessage.Of("crm.server.opportunity.already-closed", "Fırsat zaten kapanmış.");

        public static ServerMessage AlreadyOnHold => ServerMessage.Of("crm.server.opportunity.already-on-hold", "Fırsat zaten beklemede.");

        public static ServerMessage HoldClosed => ServerMessage.Of("crm.server.opportunity.hold-closed", "Kapanmış fırsat beklemeye alınamaz.");

        public static ServerMessage ResumeOnHoldOnly =>
            ServerMessage.Of("crm.server.opportunity.resume-on-hold-only", "Yalnızca beklemedeki fırsat beklemeden çıkarılır.");

        public static ServerMessage ReopenClosedOnly =>
            ServerMessage.Of("crm.server.opportunity.reopen-closed-only",
                "Yalnızca kapanmış (kazanılan, kaybedilen ya da iptal edilen) fırsat yeniden açılır.");

        public static ServerMessage UnknownStage =>
            ServerMessage.Of("crm.server.opportunity.unknown-stage", "Listede olmayan ya da kullanılmayan aşama.");

        public static ServerMessage StageNeedsClose =>
            ServerMessage.Of("crm.server.opportunity.stage-needs-close",
                "Kazanıldı/kaybedildi aşamasına geçmek için fırsatı kapatın (kazan / kaybet).");

        public static ServerMessage CloseDateInFuture =>
            ServerMessage.Of("crm.server.opportunity.close-date-in-future", "Gerçek kapanış tarihi gelecekte olamaz.");

        public static ServerMessage WonAmountNegative =>
            ServerMessage.Of("crm.server.opportunity.won-amount-negative", "Kazanılan tutar negatif olamaz.");

        public static ServerMessage NoWonStage =>
            ServerMessage.Of("crm.server.opportunity.no-won-stage", "Kazanıldı kategorisinde etkin bir aşama bulunamadı.");

        public static ServerMessage LossReasonRequired =>
            ServerMessage.Of("crm.server.opportunity.loss-reason-required", "Kaybetme nedeni zorunludur.");

        public static ServerMessage UnknownLossReason =>
            ServerMessage.Of("crm.server.opportunity.unknown-loss-reason", "Listede olmayan ya da kullanılmayan kaybetme nedeni.");

        public static ServerMessage OtherReasonText =>
            ServerMessage.Of("crm.server.opportunity.other-reason-text", "\"Diğer\" nedeni için açıklama girin.");

        public static ServerMessage CompetitorReasonOnly =>
            ServerMessage.Of("crm.server.opportunity.competitor-reason-only",
                "Kazanan rakip yalnızca \"rakibe kaybedildi\" kategorisindeki nedenlerle belirtilir.");

        public static ServerMessage CompetitorNotListed =>
            ServerMessage.Of("crm.server.opportunity.competitor-not-listed", "Rakip bu fırsatın rakipleri arasında değil.");

        public static ServerMessage RecontactInFuture =>
            ServerMessage.Of("crm.server.opportunity.recontact-in-future", "Yeniden iletişim tarihi gelecekte olmalıdır.");

        public static ServerMessage RecontactRequired =>
            ServerMessage.Of("crm.server.opportunity.recontact-required", "Takip aktivitesi için yeniden iletişim tarihi girin.");

        public static ServerMessage FollowUpPermission =>
            ServerMessage.Of("crm.server.opportunity.follow-up-permission", "Takip aktivitesi oluşturmak için aktivite yazma izni gerekir.");

        public static ServerMessage NoLostStage =>
            ServerMessage.Of("crm.server.opportunity.no-lost-stage", "Kaybedildi kategorisinde etkin bir aşama bulunamadı.");

        public static ServerMessage CancelReasonRequired =>
            ServerMessage.Of("crm.server.opportunity.cancel-reason-required", "İptal gerekçesi zorunludur.");

        public static ServerMessage HoldReasonRequired =>
            ServerMessage.Of("crm.server.opportunity.hold-reason-required", "Beklemeye alma gerekçesi zorunludur.");

        public static ServerMessage ReviewDateInFuture =>
            ServerMessage.Of("crm.server.opportunity.review-date-in-future", "Yeniden değerlendirme tarihi gelecekte olmalıdır.");

        public static ServerMessage ReopenReasonRequired =>
            ServerMessage.Of("crm.server.opportunity.reopen-reason-required", "Yeniden açma gerekçesi zorunludur.");

        public static ServerMessage ReopenStage =>
            ServerMessage.Of("crm.server.opportunity.reopen-stage", "Yeniden açılan fırsat için etkin, açık bir aşama seçin.");

        public static ServerMessage ExpectedCloseInPast =>
            ServerMessage.Of("crm.server.opportunity.expected-close-in-past", "Tahmini kapanış tarihi geçmişte olamaz.");

        public static ServerMessage ProbabilityRange =>
            ServerMessage.Of("crm.server.opportunity.probability-range", "Olasılık 0 ile 100 arasında olmalıdır.");

        public static ServerMessage NewMustBeOpen =>
            ServerMessage.Of("crm.server.opportunity.new-must-be-open",
                "Yeni fırsat açık bir aşamada başlamalıdır (kazanıldı/kaybedildi aşaması seçilemez).");

        public static ServerMessage StageByActionOnly =>
            ServerMessage.Of("crm.server.opportunity.stage-by-action-only",
                "Aşama ve durum yalnızca aşama değiştirme / kapatma eylemleriyle değişir.");

        public static ServerMessage StageStatusMismatch => ServerMessage.Of("crm.server.opportunity.stage-status-mismatch", "Aşama ile durum çelişiyor.");

        public static ServerMessage WonAmountRequired =>
            ServerMessage.Of("crm.server.opportunity.won-amount-required", "Kazanılan tutar girilmeli (fırsat tutarı da bilinmiyor).");

        public static ServerMessage StageFieldRequired(string stage) =>
            ServerMessage.Of("crm.server.opportunity.stage-field-required", "\"{0}\" aşamasına geçmek için bu alan dolu olmalı.", stage);

        public static ServerMessage ContactAlreadyLinked =>
            ServerMessage.Of("crm.server.opportunity.contact-already-linked", "Bu kişi bu fırsata zaten bağlı.");

        public static ServerMessage PrimaryContactExists =>
            ServerMessage.Of("crm.server.opportunity.primary-contact-exists", "Bu fırsatta zaten bir ana irtibat var; önce onun işaretini kaldırın.");

        public static ServerMessage AlternativeCurrencyRequired =>
            ServerMessage.Of("crm.server.opportunity.alternative-currency-required", "Alternatif teklif tutarı için para birimi girin.");
    }

    /// <summary>Aktivite kayıtları, durum geçişleri, zamanlama, ilişki ve katılımcı iletileri.</summary>
    public static class Activity
    {
        public static ServerMessage NotFound => ServerMessage.Of("crm.server.activity.not-found", "Aktivite bulunamadı.");

        public static ServerMessage LeadNotFound => ServerMessage.Of("crm.server.activity.lead-not-found", "Müşteri adayı bulunamadı.");

        public static ServerMessage AlreadyInStatus => ServerMessage.Of("crm.server.activity.already-in-status", "Aktivite zaten bu durumda.");

        public static ServerMessage StatusNotForType => ServerMessage.Of("crm.server.activity.status-not-for-type", "Bu tür aktivite bu duruma geçemez.");

        public static ServerMessage SentEmailLocked => ServerMessage.Of("crm.server.activity.sent-email-locked", "Gönderilmiş e-posta değiştirilemez.");

        public static ServerMessage TransitionNotAllowed =>
            ServerMessage.Of("crm.server.activity.transition-not-allowed", "Bu durumdan istenen duruma geçilemez.");

        public static ServerMessage AllDayStartRequired =>
            ServerMessage.Of("crm.server.activity.all-day-start-required", "Tüm gün etkinliğinde başlangıç tarihi zorunludur.");

        public static ServerMessage AllDayNoTime => ServerMessage.Of("crm.server.activity.all-day-no-time", "Tüm gün etkinliği saat içermez.");

        public static ServerMessage DateRangeAllDayOnly =>
            ServerMessage.Of("crm.server.activity.date-range-all-day-only", "Tarih aralığı yalnızca tüm gün etkinliğinde kullanılır.");

        public static ServerMessage EndNeedsStart => ServerMessage.Of("crm.server.activity.end-needs-start", "Bitiş verilmişse başlangıç da verilmelidir.");

        public static ServerMessage EndAfterStart => ServerMessage.Of("crm.server.activity.end-after-start", "Bitiş, başlangıçtan sonra olmalıdır.");

        public static ServerMessage BlockReasonRequired => ServerMessage.Of("crm.server.activity.block-reason-required", "Bloke nedeni zorunludur.");

        public static ServerMessage CancelReasonRequired => ServerMessage.Of("crm.server.activity.cancel-reason-required", "İptal gerekçesi zorunludur.");

        public static ServerMessage ReopenReasonRequired =>
            ServerMessage.Of("crm.server.activity.reopen-reason-required", "Yeniden açma gerekçesi zorunludur.");

        public static ServerMessage AlreadyOpen => ServerMessage.Of("crm.server.activity.already-open", "Aktivite zaten açık.");

        public static ServerMessage RescheduleReasonRequired =>
            ServerMessage.Of("crm.server.activity.reschedule-reason-required", "Erteleme gerekçesi zorunludur.");

        public static ServerMessage RescheduleClosed =>
            ServerMessage.Of("crm.server.activity.reschedule-closed", "Kapanmış aktivite ertelenemez; önce yeniden açın.");

        public static ServerMessage Conflicts(int count) =>
            ServerMessage.Of("crm.server.activity.conflicts", "Bu saatte aynı sorumlunun {0} çakışan aktivitesi var.", count);

        public static ServerMessage ReassignClosed => ServerMessage.Of("crm.server.activity.reassign-closed", "Kapanmış aktivite yeniden atanamaz.");

        public static ServerMessage TaskNoOutcome => ServerMessage.Of("crm.server.activity.task-no-outcome", "Görevin sonuç kodu olmaz.");

        public static ServerMessage OutcomeOneOf(string allowed) =>
            ServerMessage.Of("crm.server.activity.outcome-one-of", "Sonuç şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage CompletedInFuture =>
            ServerMessage.Of("crm.server.activity.completed-in-future", "Tamamlanma zamanı gelecekte olamaz.");

        public static ServerMessage BlockedCannotComplete =>
            ServerMessage.Of("crm.server.activity.blocked-cannot-complete", "Bloke görev tamamlanamaz; önce engeli kaldırın.");

        public static ServerMessage NextStepSubjectRequired =>
            ServerMessage.Of("crm.server.activity.next-step-subject-required", "Sonraki adımın konusu zorunludur.");

        public static ServerMessage NotBlocked => ServerMessage.Of("crm.server.activity.not-blocked", "Aktivite bloke değil.");

        public static ServerMessage FamilyChangeLocked =>
            ServerMessage.Of("crm.server.activity.family-change-locked",
                "Başlamış ya da kapanmış bir aktivitenin türü başka bir aileye değiştirilemez.");

        public static ServerMessage TimedBlockMaxDays(int days) =>
            ServerMessage.Of("crm.server.activity.timed-block-max-days", "Zaman bloğu en fazla {0} gün olabilir.", days);

        public static ServerMessage AllDayMaxDays(int days) =>
            ServerMessage.Of("crm.server.activity.all-day-max-days", "Tüm gün etkinliği en fazla {0} gün sürebilir.", days);

        public static ServerMessage InvalidTimeZone =>
            ServerMessage.Of("crm.server.activity.invalid-time-zone", "Geçerli bir saat dilimi kimliği girin (ör. Europe/Istanbul).");

        public static ServerMessage TimeZoneRequired =>
            ServerMessage.Of("crm.server.activity.time-zone-required", "Saatli etkinlik için saat dilimi zorunludur.");

        public static ServerMessage ClosedScheduleLocked =>
            ServerMessage.Of("crm.server.activity.closed-schedule-locked", "Kapanmış aktivitenin zamanı değiştirilemez; önce yeniden açın.");

        public static ServerMessage RelationRequired =>
            ServerMessage.Of("crm.server.activity.relation-required", "Aktivite ve ilişkilendirilecek kayıt zorunludur.");

        public static ServerMessage ActivityRequired => ServerMessage.Of("crm.server.activity.activity-required", "Aktivite zorunludur.");

        public static ServerMessage RelationRecordLocked =>
            ServerMessage.Of("crm.server.activity.relation-record-locked", "Bağlı kayıt değiştirilemez; ilişkiyi silip yenisini ekleyin.");

        public static ServerMessage RelationTypeNotAllowed =>
            ServerMessage.Of("crm.server.activity.relation-type-not-allowed", "Bu kayıt türü aktiviteye bağlanamaz.");

        public static ServerMessage RelationExists => ServerMessage.Of("crm.server.activity.relation-exists", "Bu kayıt aktiviteye zaten bağlı.");

        public static ServerMessage PrimaryRelationExists =>
            ServerMessage.Of("crm.server.activity.primary-relation-exists",
                "Aktivitenin zaten birincil bir ilişkisi var; önce onu birincil olmaktan çıkarın.");

        public static ServerMessage ParticipantOneOf =>
            ServerMessage.Of("crm.server.activity.participant-one-of", "Katılımcı olarak tam olarak biri verilmelidir: kullanıcı, kişi ya da e-posta.");

        public static ServerMessage ParticipantLocked =>
            ServerMessage.Of("crm.server.activity.participant-locked", "Katılımcı değiştirilemez; silip yenisini ekleyin.");

        public static ServerMessage ParticipantExists => ServerMessage.Of("crm.server.activity.participant-exists", "Bu katılımcı zaten ekli.");

        public static ServerMessage BulkInvalidTitle => ServerMessage.Of("crm.server.activity.bulk-invalid.title", "Geçersiz istek.");

        public static ServerMessage BulkCount(int max) =>
            ServerMessage.Of("crm.server.activity.bulk-count", "Bir seferde 1 ile {0} arasında kayıt işlenebilir.", max);

        public static ServerMessage BulkUnknownAction => ServerMessage.Of("crm.server.activity.bulk-unknown-action", "Bilinmeyen toplu işlem.");

        public static ServerMessage BulkOwnerRequired => ServerMessage.Of("crm.server.activity.bulk-owner-required", "Yeni sorumlu zorunludur.");

        public static ServerMessage BulkShiftDays =>
            ServerMessage.Of("crm.server.activity.bulk-shift-days", "Kaydırılacak gün sayısı 1-366 arası olmalıdır.");
    }
}
