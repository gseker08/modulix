using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Crm;

/// <summary>
/// CRM'in sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-1): kayıt alanlarının biçim iletileri, bağlı kayıt denetimleri ve
/// CRM iş kurallarının iletileri. Anahtarlar <c>crm.server.*</c>; Türkçe metin <c>tr.json</c>'daki kalıpla aynıdır (birim testle
/// denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler (<see cref="ServerMessages"/>) burada yinelenmez.
/// </summary>
internal static class CrmMessages
{
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

    /// <summary>Teklif kayıtları, kalemleri, kuralları, onay ve paylaşım iletileri.</summary>
    public static class Quote
    {
        public static ServerMessage NotFound => ServerMessage.Of("crm.server.quote.not-found", "Teklif bulunamadı.");

        public static ServerMessage SubmitDraftOnly => ServerMessage.Of("crm.server.quote.submit-draft-only", "Yalnızca taslak teklif onaya gönderilebilir.");

        public static ServerMessage SubmitNeedsLines => ServerMessage.Of("crm.server.quote.submit-needs-lines", "Onaya göndermek için en az bir kalem gerekir.");

        public static ServerMessage AlreadyPending => ServerMessage.Of("crm.server.quote.already-pending", "Teklif zaten onay bekliyor.");

        public static ServerMessage NothingToWithdraw => ServerMessage.Of("crm.server.quote.nothing-to-withdraw", "Geri çekilecek bekleyen onay yok.");

        public static ServerMessage NotPending => ServerMessage.Of("crm.server.quote.not-pending", "Teklif onay beklemiyor.");

        public static ServerMessage PresentDraftOnly =>
            ServerMessage.Of("crm.server.quote.present-draft-only", "Yalnızca taslak teklif müşteriye sunulabilir.");

        public static ServerMessage PresentNeedsLines => ServerMessage.Of("crm.server.quote.present-needs-lines", "Sunmak için en az bir kalem gerekir.");

        public static ServerMessage PresentExpired =>
            ServerMessage.Of("crm.server.quote.present-expired", "Geçerlilik tarihi geçmiş teklif sunulamaz; tarihi güncelleyin.");

        public static ServerMessage ApprovalRequired =>
            ServerMessage.Of("crm.server.quote.approval-required", "İç onay tamamlanmadan teklif müşteriye sunulamaz.");

        public static ServerMessage Expired => ServerMessage.Of("crm.server.quote.expired", "Teklifin geçerlilik süresi dolmuş.");

        public static ServerMessage ShareDraft => ServerMessage.Of("crm.server.quote.share-draft", "Taslak teklif paylaşılamaz; önce müşteriye sunun.");

        public static ServerMessage ShareExpired =>
            ServerMessage.Of("crm.server.quote.share-expired", "Teklifin geçerlilik süresi dolmuş; paylaşım bağlantısı üretilemez.");

        public static ServerMessage CancelNotAllowed => ServerMessage.Of("crm.server.quote.cancel-not-allowed", "Bu durumdaki teklif iptal edilemez.");

        public static ServerMessage UnknownAction => ServerMessage.Of("crm.server.quote.unknown-action", "Bilinmeyen eylem.");

        public static ServerMessage AlreadyAccepted => ServerMessage.Of("crm.server.quote.already-accepted", "Teklif daha önce kabul edilmiş.");

        public static ServerMessage AlreadyRejected => ServerMessage.Of("crm.server.quote.already-rejected", "Teklif daha önce reddedilmiş.");

        public static ServerMessage Cancelled => ServerMessage.Of("crm.server.quote.cancelled", "Teklif iptal edilmiş.");

        public static ServerMessage Superseded => ServerMessage.Of("crm.server.quote.superseded", "Bu sürüm yeni bir revizyonla geçersizleşmiş.");

        public static ServerMessage NotPresented => ServerMessage.Of("crm.server.quote.not-presented", "Teklif henüz müşteriye sunulmamış.");

        public static ServerMessage ReviseDraft =>
            ServerMessage.Of("crm.server.quote.revise-draft", "Taslak teklif zaten düzenlenebilir; revizyon gerekmez.");

        public static ServerMessage ReviseAccepted =>
            ServerMessage.Of("crm.server.quote.revise-accepted", "Kabul edilmiş teklif yeniden yazılamaz; sonraki ticari değişiklik ayrı süreçtir.");

        public static ServerMessage ReviseNotAllowed => ServerMessage.Of("crm.server.quote.revise-not-allowed", "Bu durumdaki tekliften revizyon açılamaz.");

        public static ServerMessage RevisionReasonRequired => ServerMessage.Of("crm.server.quote.revision-reason-required", "Revizyon gerekçesi zorunludur.");

        public static ServerMessage ReasonRequired => ServerMessage.Of("crm.server.quote.reason-required", "Gerekçe zorunludur.");

        public static ServerMessage ActionNotAllowed => ServerMessage.Of("crm.server.quote.action-not-allowed", "Bu işlem şu an yapılamaz.");

        public static ServerMessage OwnApproval => ServerMessage.Of("crm.server.quote.own-approval", "Kendi gönderdiğiniz teklifi onaylayamazsınız.");

        public static ServerMessage NotLinkedToOpportunity => ServerMessage.Of("crm.server.quote.not-linked-to-opportunity", "Teklif bu fırsata bağlı değil.");

        public static ServerMessage PrimaryNeedsPresented =>
            ServerMessage.Of("crm.server.quote.primary-needs-presented",
                "Yalnızca müşteriye sunulmuş ya da kabul edilmiş teklif esas teklif olabilir.");

        public static ServerMessage CompareSameFamily =>
            ServerMessage.Of("crm.server.quote.compare-same-family", "Yalnızca aynı teklif ailesinin iki farklı revizyonu karşılaştırılır.");

        public static ServerMessage AcceptedNotFound => ServerMessage.Of("crm.server.quote.accepted-not-found", "Kabul edilmiş teklif bulunamadı.");

        public static ServerMessage InvalidPdf => ServerMessage.Of("crm.server.quote.invalid-pdf", "Üretilen belge geçerli bir PDF değil.");

        public static ServerMessage DocumentNotFound => ServerMessage.Of("crm.server.quote.document-not-found", "Belge bulunamadı.");

        public static ServerMessage RecipientNameTooLong => ServerMessage.Of("crm.server.quote.recipient-name-too-long", "Alıcı adı çok uzun.");

        public static ServerMessage LinkDays(int max) =>
            ServerMessage.Of("crm.server.quote.link-days", "Bağlantı süresi 1-{0} gün olmalıdır.", max);

        public static ServerMessage LinkNeedsDocument =>
            ServerMessage.Of("crm.server.quote.link-needs-document", "Bağlantı için önce müşteri belgesi (PDF) üretilmelidir.");

        public static ServerMessage RevokeReasonRequired => ServerMessage.Of("crm.server.quote.revoke-reason-required", "İptal gerekçesi zorunludur.");

        public static ServerMessage LinkNotFound => ServerMessage.Of("crm.server.quote.link-not-found", "Bağlantı bulunamadı.");

        public static ServerMessage InvalidDeliveryStatus => ServerMessage.Of("crm.server.quote.invalid-delivery-status", "Geçersiz gönderim durumu.");

        public static ServerMessage ShareNotAllowed => ServerMessage.Of("crm.server.quote.share-not-allowed", "Bu teklif paylaşılamaz.");

        public static ServerMessage RevisionAndCopy => ServerMessage.Of("crm.server.quote.revision-and-copy", "Revizyon ve çoğaltma birlikte verilemez.");

        public static ServerMessage ReviseLatestOnly =>
            ServerMessage.Of("crm.server.quote.revise-latest-only", "Yalnızca ailenin son revizyonundan yeni revizyon açılır.");

        public static ServerMessage DraftRevisionExists =>
            ServerMessage.Of("crm.server.quote.draft-revision-exists", "Bu ailede zaten açık bir taslak revizyon var.");

        public static ServerMessage NotDraftLocked =>
            ServerMessage.Of("crm.server.quote.not-draft-locked", "Taslak olmayan teklif değiştirilemez; yeni revizyon açın.");

        public static ServerMessage DiscountDenied => ServerMessage.Of("crm.server.quote.discount-denied", "İndirim uygulama yetkiniz yok.");

        public static ServerMessage ExchangeRateTogether =>
            ServerMessage.Of("crm.server.quote.exchange-rate-together", "Kur, raporlama para birimi ve kur tarihi birlikte verilmelidir.");

        public static ServerMessage ReportingCurrencyDifferent =>
            ServerMessage.Of("crm.server.quote.reporting-currency-different", "Raporlama para birimi teklif para biriminden farklı olmalıdır.");

        public static ServerMessage DeleteDraftOnly =>
            ServerMessage.Of("crm.server.quote.delete-draft-only", "Yalnızca taslak teklif silinebilir; diğerleri iptal edilir.");

        public static ServerMessage ContactNotOfCompany =>
            ServerMessage.Of("crm.server.quote.contact-not-of-company", "Seçilen kişi bu firmayla ilişkili değil.");

        public static ServerMessage OpportunityNotOfCompany =>
            ServerMessage.Of("crm.server.quote.opportunity-not-of-company", "Fırsat bu firmaya ait değil.");

        public static ServerMessage PriceListCurrency(string currency) =>
            ServerMessage.Of("crm.server.quote.price-list-currency", "Fiyat listesi para birimi ({0}) teklifin para biriminden farklı.", currency);

        public static ServerMessage LineLocked =>
            ServerMessage.Of("crm.server.quote.line-locked", "Taslak olmayan teklifin kalemi değiştirilemez; yeni revizyon açın.");

        public static ServerMessage LineDeleteLocked =>
            ServerMessage.Of("crm.server.quote.line-delete-locked", "Taslak olmayan teklifin kalemi silinemez; yeni revizyon açın.");

        public static ServerMessage SurchargeLineLocked =>
            ServerMessage.Of("crm.server.quote.surcharge-line-locked", "Bu kalem bir ek ücret kuralından üretildi; düzenlenemez. Kuralı değiştirin ya da kapatın.");

        public static ServerMessage SurchargeLineDeleteLocked =>
            ServerMessage.Of("crm.server.quote.surcharge-line-delete-locked", "Kuraldan üretilen kalem silinemez; ilgili kuralı kapatın ya da koşullarını değiştirin.");

        public static ServerMessage ProductNotFound => ServerMessage.Of("crm.server.quote.product-not-found", "Ürün bulunamadı.");

        public static ServerMessage ProductNotSellable =>
            ServerMessage.Of("crm.server.quote.product-not-sellable", "Pasif ya da taslak ürün yeni teklif kalemine eklenemez.");

        public static ServerMessage LineNameRequired => ServerMessage.Of("crm.server.quote.line-name-required", "Kalem adı zorunludur.");

        public static ServerMessage FreeLineDenied =>
            ServerMessage.Of("crm.server.quote.free-line-denied", "Serbest kalem ekleme ve elle fiyat girme yetkiniz yok.");

        public static ServerMessage ParentRequired => ServerMessage.Of("crm.server.quote.parent-required", "Paket bileşeninin ana satırı gerekir.");

        public static ServerMessage ParentNotFound => ServerMessage.Of("crm.server.quote.parent-not-found", "Ana satır bu teklifte bulunamadı.");

        public static ServerMessage PriceNotFound => ServerMessage.Of("crm.server.quote.price-not-found", "Ürün için fiyat bulunamadı.");

        public static ServerMessage TaxModeMismatch =>
            ServerMessage.Of("crm.server.quote.tax-mode-mismatch", "Ürün fiyatının vergi dahil/hariç biçimi teklifinkiyle uyuşmuyor.");

        public static ServerMessage ManualPriceDenied =>
            ServerMessage.Of("crm.server.quote.manual-price-denied", "Elle birim fiyat girme yetkiniz yok; fiyat listeden gelir.");

        public static ServerMessage DelegatorRequired => ServerMessage.Of("crm.server.quote.delegator-required", "Vekâlet veren zorunludur.");

        public static ServerMessage DelegateRequired => ServerMessage.Of("crm.server.quote.delegate-required", "Vekil zorunludur.");

        public static ServerMessage SelfDelegation => ServerMessage.Of("crm.server.quote.self-delegation", "Kişi kendine vekâlet veremez.");

        public static ServerMessage RuleConditionRequired =>
            ServerMessage.Of("crm.server.quote.rule-condition-required", "En az bir koşul girin: indirim yüzdesi ya da genel toplam.");

        public static ServerMessage RuleDiscountRange =>
            ServerMessage.Of("crm.server.quote.rule-discount-range", "İndirim eşiği 0 ile 100 arasında (100 hariç) olmalıdır.");

        public static ServerMessage RuleStepRange => ServerMessage.Of("crm.server.quote.rule-step-range", "Onay adımı 1 ile 9 arasında olmalıdır.");

        public static ServerMessage RuleAmountNegative => ServerMessage.Of("crm.server.quote.rule-amount-negative", "Tutar eşiği negatif olamaz.");

        public static ServerMessage ReportPermission(string permission) =>
            ServerMessage.Of("crm.server.quote.report-permission", "Bu rapor için '{0}' izni gerekir.", permission);
    }

    /// <summary>Ürün kataloğu, birim, vergi, kur, fiyat listesi, paket ve indirim politikası iletileri.</summary>
    public static class Pricing
    {
        public static ServerMessage CategoryCycle => ServerMessage.Of("crm.server.pricing.category-cycle", "Kategori kendi altına taşınamaz (döngü).");

        public static ServerMessage SurchargeAmountRequired =>
            ServerMessage.Of("crm.server.pricing.surcharge-amount-required", "Bu kural türü için sıfırdan büyük bir tutar girin.");

        public static ServerMessage SurchargePercentRequired =>
            ServerMessage.Of("crm.server.pricing.surcharge-percent-required", "Bu kural türü için sıfırdan büyük bir yüzde girin.");

        public static ServerMessage SurchargeAmountOrPercent =>
            ServerMessage.Of("crm.server.pricing.surcharge-amount-or-percent", "Bu kural türü için yalnızca tutar ya da yalnızca yüzde girilir, ikisi birlikte olmaz.");

        public static ServerMessage SurchargeTaxRequired =>
            ServerMessage.Of("crm.server.pricing.surcharge-tax-required", "Vergi oranı girin; kuraldan üretilen teklif satırı bu oranla hesaplanır.");

        public static ServerMessage SurchargeBaseRequired =>
            ServerMessage.Of("crm.server.pricing.surcharge-base-required", "Yüzde için taban anahtarı girin.");

        public static ServerMessage SurchargeGroupRequired =>
            ServerMessage.Of("crm.server.pricing.surcharge-group-required", "Asgari bedel kuralı için grup girin.");

        public static ServerMessage SurchargeDurationRequired =>
            ServerMessage.Of("crm.server.pricing.surcharge-duration-required", "Süre aşımı kuralı için süre anahtarı girin.");

        public static ServerMessage SurchargeMinimumNoMultiplier =>
            ServerMessage.Of("crm.server.pricing.surcharge-minimum-no-multiplier", "Asgari bedel kuralı çarpan anahtarı alamaz.");

        public static ServerMessage SurchargeKeyInvalid =>
            ServerMessage.Of("crm.server.pricing.surcharge-key-invalid", "Anahtar yalnızca harf, rakam, nokta, alt çizgi ve tire içerebilir (en çok 100 karakter).");

        public static ServerMessage SurchargeListInvalid =>
            ServerMessage.Of("crm.server.pricing.surcharge-list-invalid", "Liste geçersiz: çarpan anahtarları geçerli ve tekrarsız, koşullar anahtar, işleç ve değer içermelidir.");

        public static ServerMessage CategoryInUse =>
            ServerMessage.Of("crm.server.pricing.category-in-use", "Alt kategorisi ya da ürünü olan kategori silinemez; pasifleştirin.");

        public static ServerMessage UnitFactorPositive =>
            ServerMessage.Of("crm.server.pricing.unit-factor-positive", "Dönüşüm katsayısı sıfırdan büyük olmalıdır.");

        public static ServerMessage UnitCodeTaken => ServerMessage.Of("crm.server.pricing.unit-code-taken", "Bu birim kodu zaten kullanılıyor.");

        public static ServerMessage UnitInUseLocked =>
            ServerMessage.Of("crm.server.pricing.unit-in-use-locked",
                "Ürünlerde kullanılan birimin grubu ve katsayısı değiştirilemez; yeni birim tanımlayın.");

        public static ServerMessage UnitInUse => ServerMessage.Of("crm.server.pricing.unit-in-use", "Ürünlerde kullanılan birim silinemez.");

        public static ServerMessage SaleEndBeforeStart => ServerMessage.Of("crm.server.pricing.sale-end-before-start", "Satış bitişi başlangıçtan önce olamaz.");

        public static ServerMessage QuantityPrecision =>
            ServerMessage.Of("crm.server.pricing.quantity-precision", "Miktar hassasiyeti 0 ile 4 arasında olmalıdır.");

        public static ServerMessage RecurringPeriodRequired =>
            ServerMessage.Of("crm.server.pricing.recurring-period-required", "Dönemsel ücret için dönem seçin.");

        public static ServerMessage ServiceDurationServiceOnly =>
            ServerMessage.Of("crm.server.pricing.service-duration-service-only", "Hizmet süresi yalnızca hizmet kartında girilir.");

        public static ServerMessage ProductCodeTaken =>
            ServerMessage.Of("crm.server.pricing.product-code-taken", "Bu ürün kodu bu kuruluşta zaten kullanılıyor.");

        public static ServerMessage StatusTransition(string from, string to) =>
            ServerMessage.Of("crm.server.pricing.status-transition", "'{0}' durumundan '{1}' durumuna geçilemez.", from, to);

        public static ServerMessage ProductDeleteDraftOnly =>
            ServerMessage.Of("crm.server.pricing.product-delete-draft-only",
                "Taslak dışındaki ürün silinmez; pasifleştirin ya da satıştan kaldırın.");

        public static ServerMessage ValidFromRequired => ServerMessage.Of("crm.server.pricing.valid-from-required", "Geçerlilik başlangıcı zorunludur.");

        public static ServerMessage TaxRateOverlap =>
            ServerMessage.Of("crm.server.pricing.tax-rate-overlap", "Bu vergi kodu için geçerlilik aralığı mevcut bir kayıtla çakışıyor.");

        public static ServerMessage TaxRateInUse =>
            ServerMessage.Of("crm.server.pricing.tax-rate-in-use",
                "Ürünlerde kullanılan vergi kodunun oranı silinemez; bitiş tarihi verin ya da pasifleştirin.");

        public static ServerMessage PriceListLocked =>
            ServerMessage.Of("crm.server.pricing.price-list-locked",
                "Yayımlanmış ya da arşivlenmiş fiyat listesi değiştirilemez; yeni liste açın.");

        public static ServerMessage CustomerListCompany =>
            ServerMessage.Of("crm.server.pricing.customer-list-company", "Müşteriye özel liste için firma seçin.");

        public static ServerMessage SegmentRequired =>
            ServerMessage.Of("crm.server.pricing.segment-required", "Segment/kanal listesi için segment adı girin.");

        public static ServerMessage PublishNeedsLines => ServerMessage.Of("crm.server.pricing.publish-needs-lines", "Satırı olmayan fiyat listesi yayımlanamaz.");

        public static ServerMessage PublishedDelete =>
            ServerMessage.Of("crm.server.pricing.published-delete", "Yayımlanmış fiyat listesi silinemez; arşive alın.");

        public static ServerMessage ListHasLines =>
            ServerMessage.Of("crm.server.pricing.list-has-lines", "Satırları olan liste silinemez; önce satırları silin.");

        public static ServerMessage PriceListNotFound => ServerMessage.Of("crm.server.pricing.price-list-not-found", "Fiyat listesi bulunamadı.");

        public static ServerMessage DraftLinesOnly =>
            ServerMessage.Of("crm.server.pricing.draft-lines-only", "Yalnızca taslak fiyat listesinin satırı değiştirilebilir.");

        public static ServerMessage DraftLinesDeleteOnly =>
            ServerMessage.Of("crm.server.pricing.draft-lines-delete-only", "Yalnızca taslak fiyat listesinin satırı silinebilir.");

        public static ServerMessage MaxAboveMin => ServerMessage.Of("crm.server.pricing.max-above-min", "Üst sınır, alt sınırdan büyük olmalıdır.");

        public static ServerMessage PackageSelf => ServerMessage.Of("crm.server.pricing.package-self", "Paket kendisini bileşen olarak içeremez.");

        public static ServerMessage ComponentExists =>
            ServerMessage.Of("crm.server.pricing.component-exists", "Bu bileşen pakette zaten var; miktarını güncelleyin.");

        public static ServerMessage PercentOrAmount => ServerMessage.Of("crm.server.pricing.percent-or-amount", "Yüzde ya da tutardan tam birini girin.");

        public static ServerMessage PercentPositive => ServerMessage.Of("crm.server.pricing.percent-positive", "Yüzde sıfırdan büyük olmalıdır.");

        public static ServerMessage AmountPositive => ServerMessage.Of("crm.server.pricing.amount-positive", "Tutar sıfırdan büyük olmalıdır.");

        public static ServerMessage AmountCurrency => ServerMessage.Of("crm.server.pricing.amount-currency", "Tutar indirimi için para birimi seçin.");
    }

    /// <summary>Satış hedefi, tahmin ve satış analizi iletileri.</summary>
    public static class Planning
    {
        public static ServerMessage TargetPositive => ServerMessage.Of("crm.server.planning.target-positive", "Hedef sıfırdan büyük olmalıdır.");

        public static ServerMessage CountWhole => ServerMessage.Of("crm.server.planning.count-whole", "Adet hedefi tam sayı olmalıdır.");

        public static ServerMessage TargetOverlap =>
            ServerMessage.Of("crm.server.planning.target-overlap", "Bu kapsam, metrik ve para birimi için dönem mevcut bir hedefle çakışıyor.");

        public static ServerMessage PeriodRequired => ServerMessage.Of("crm.server.planning.period-required", "Dönem başı ve sonu zorunludur.");

        public static ServerMessage PeriodOrder => ServerMessage.Of("crm.server.planning.period-order", "Dönem sonu başından önce olamaz.");

        public static ServerMessage PeriodMaxDays(int days) =>
            ServerMessage.Of("crm.server.planning.period-max-days", "Hedef dönemi en fazla {0} gün olabilir.", days);

        public static ServerMessage UserTargetUser => ServerMessage.Of("crm.server.planning.user-target-user", "Kullanıcı hedefinde kullanıcı zorunludur.");

        public static ServerMessage TenantTargetNoUser => ServerMessage.Of("crm.server.planning.tenant-target-no-user", "Tenant hedefinde kullanıcı verilmez.");

        public static ServerMessage AmountCurrency => ServerMessage.Of("crm.server.planning.amount-currency", "Tutar hedefinde para birimi zorunludur.");

        public static ServerMessage CountNoCurrency => ServerMessage.Of("crm.server.planning.count-no-currency", "Adet hedefinde para birimi olmaz.");

        public static ServerMessage ForecastByWorkflow =>
            ServerMessage.Of("crm.server.planning.forecast-by-workflow", "Tahmin yalnızca tahmin ekranından (iş akışıyla) oluşturulur.");

        public static ServerMessage NoteDraftOnly => ServerMessage.Of("crm.server.planning.note-draft-only", "Not yalnızca taslak tahminde değiştirilir.");

        public static ServerMessage DeleteDraftOnly =>
            ServerMessage.Of("crm.server.planning.delete-draft-only", "Yalnızca taslak tahmin silinebilir; sunulan ve kilitli tahmin korunur.");

        public static ServerMessage ReasonRequired => ServerMessage.Of("crm.server.planning.reason-required", "Gerekçe zorunludur.");

        public static ServerMessage ReasonMaxLength(int max) =>
            ServerMessage.Of("crm.server.planning.reason-max-length", "Gerekçe en fazla {0} karakter olabilir.", max);

        public static ServerMessage UserForecastUser =>
            ServerMessage.Of("crm.server.planning.user-forecast-user",
                "Kullanıcı tahmininde kullanıcı zorunludur ve bu kuruluşun etkin üyesi olmalıdır.");

        public static ServerMessage TenantForecastNoUser =>
            ServerMessage.Of("crm.server.planning.tenant-forecast-no-user", "Tenant tahmininde kullanıcı verilmez.");

        public static ServerMessage ForecastExists =>
            ServerMessage.Of("crm.server.planning.forecast-exists",
                "Bu dönem, kapsam ve para birimi için tahmin zaten var; kilitliyse gerekçeyle yeniden açın.");

        public static ServerMessage RefreshDraftOnly => ServerMessage.Of("crm.server.planning.refresh-draft-only", "Yalnızca taslak tahmin yenilenir.");

        public static ServerMessage LineNotFound => ServerMessage.Of("crm.server.planning.line-not-found", "Tahmin satırı bulunamadı.");

        public static ServerMessage CategoryOneOf(string allowed) =>
            ServerMessage.Of("crm.server.planning.category-one-of", "Kategori şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage CategoryUnchanged => ServerMessage.Of("crm.server.planning.category-unchanged", "Kategori zaten bu değerde.");

        public static ServerMessage AmountNotAdjustable =>
            ServerMessage.Of("crm.server.planning.amount-not-adjustable",
                "Tutarı ya da kuru olmayan satırın tutarı düzeltilemez; önce fırsat tutarını/kuru tamamlayıp yenileyin.");

        public static ServerMessage AdjustmentFormat =>
            ServerMessage.Of("crm.server.planning.adjustment-format",
                "Düzeltme, en fazla iki ondalıklı bir sayı (delta) olmalıdır; boş bırakmak düzeltmeyi kaldırır.");

        public static ServerMessage AdjustedNegative =>
            ServerMessage.Of("crm.server.planning.adjusted-negative", "Düzeltme sonrası nihai tutar negatif olamaz.");

        public static ServerMessage ProbabilityFormat =>
            ServerMessage.Of("crm.server.planning.probability-format",
                "Olasılık 0-100 arası tam sayı olmalıdır; boş bırakmak düzeltmeyi kaldırır.");

        public static ServerMessage NoteMaxLength(int max) =>
            ServerMessage.Of("crm.server.planning.note-max-length", "Not en fazla {0} karakter olabilir.", max);

        public static ServerMessage ReopenLockedOnly => ServerMessage.Of("crm.server.planning.reopen-locked-only", "Yalnızca kilitli tahmin yeniden açılır.");

        public static ServerMessage OpenVersionExists =>
            ServerMessage.Of("crm.server.planning.open-version-exists",
                "Bu dönem için zaten açık bir sürüm var; önce onu kilitleyin ya da silin.");

        public static ServerMessage StatusTransition(string from, string to) =>
            ServerMessage.Of("crm.server.planning.status-transition", "Tahmin '{0}' durumundan '{1}' durumuna geçirilemez.", from, to);

        public static ServerMessage ForecastPermission(string permission) =>
            ServerMessage.Of("crm.server.planning.forecast-permission", "Tahmin için '{0}' izni gerekir.", permission);

        public static ServerMessage AnalysisPermission(string permission) =>
            ServerMessage.Of("crm.server.planning.analysis-permission", "Bu analiz için '{0}' izni gerekir.", permission);
    }

    /// <summary>Teklif paylaşımı ve müşteri belgesi.</summary>
    public static class Sharing
    {
        public static ServerMessage DraftNoDocument =>
            ServerMessage.Of("crm.server.sharing.draft-no-document", "Taslak teklif için müşteri belgesi üretilemez; önce müşteriye sunun.");

        public static ServerMessage DocumentRace =>
            ServerMessage.Of("crm.server.sharing.document-race", "Belge başka bir istekte üretildi; sayfayı yenileyin.");

        public static ServerMessage LinkRevoked =>
            ServerMessage.Of("crm.server.sharing.link-revoked", "Bağlantı zaten iptal edilmiş.");
    }

    /// <summary>Satış tahmini sürümleri.</summary>
    public static class Forecast
    {
        public static ServerMessage CompareNoPrevious =>
            ServerMessage.Of("crm.server.forecast.compare-no-previous", "Karşılaştırılacak önceki sürüm yok; bir sürüm seçin.");

        public static ServerMessage CompareMismatch =>
            ServerMessage.Of("crm.server.forecast.compare-mismatch", "Yalnızca aynı dönem, kapsam ve para birimindeki sürümler karşılaştırılır.");

        public static ServerMessage LinesNeedPermission =>
            ServerMessage.Of("crm.server.forecast.lines-need-permission", "Satırları görmek için fırsat ve tutar izni gerekir.");

        public static ServerMessage ScopeExceeds =>
            ServerMessage.Of("crm.server.forecast.scope-exceeds", "Bu tahminin kapsamı veri kapsamınızı aşıyor; anlık görüntü eksik kalacağından işlem yapılamaz.");
    }

    /// <summary>Uç noktalar.</summary>
    public static class Endpoint
    {
        public static ServerMessage BodyTooLarge =>
            ServerMessage.Of("crm.server.endpoint.body-too-large", "Gövde çok büyük.");

        public static ServerMessage JsonRequired =>
            ServerMessage.Of("crm.server.endpoint.json-required", "JSON gönderin.");

        public static ServerMessage InvalidBody =>
            ServerMessage.Of("crm.server.endpoint.invalid-body", "Geçersiz istek gövdesi.");

        public static ServerMessage FormNotFound =>
            ServerMessage.Of("crm.server.endpoint.form-not-found", "Form bulunamadı.");

        public static ServerMessage SubmissionRejected =>
            ServerMessage.Of("crm.server.endpoint.submission-rejected", "Gönderim kabul edilmedi.");

        public static ServerMessage LinkInvalid =>
            ServerMessage.Of("crm.server.endpoint.link-invalid", "Bağlantı geçersiz.");

        public static ServerMessage QuoteStateChanged =>
            ServerMessage.Of("crm.server.endpoint.quote-state-changed", "Teklif durumu değişti.");
    }
}
