using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Contracts;

/// <summary>
/// Sözleşmeler modülünün sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-2). Anahtarlar <c>contracts.server.*</c>; Türkçe metin
/// <c>tr.json</c>'daki kalıpla aynıdır (birim testle denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler
/// (alan biçimleri, izin, eşzamanlılık) <see cref="ServerMessages"/>'tadır, burada yinelenmez.
/// </summary>
internal static class ContractsMessages
{
    /// <summary>Sözleşme kaydı, tarafları, kalemleri ve bağlı kayıt denetimleri.</summary>
    public static class Contract
    {
        public static ServerMessage NotFound => ServerMessage.Of("contracts.server.contract.not-found", "Sözleşme bulunamadı.");

        public static ServerMessage CustomerNotFound => ServerMessage.Of("contracts.server.contract.customer-not-found", "Müşteri bulunamadı.");

        public static ServerMessage PartyNotFound => ServerMessage.Of("contracts.server.contract.party-not-found", "Taraf bulunamadı.");

        public static ServerMessage PersonNotFound => ServerMessage.Of("contracts.server.contract.person-not-found", "Kişi bulunamadı.");

        public static ServerMessage PreviousNotFound =>
            ServerMessage.Of("contracts.server.contract.previous-not-found", "Önceki sözleşme bulunamadı.");

        public static ServerMessage SelfPrevious => ServerMessage.Of("contracts.server.contract.self-previous", "Sözleşme kendisinin öncülü olamaz.");

        public static ServerMessage PersonNotOfCustomer =>
            ServerMessage.Of("contracts.server.contract.person-not-of-customer", "Kişi seçilen müşteriyle ilişkili değil.");

        public static ServerMessage PartyOrPersonRequired => ServerMessage.Of("contracts.server.contract.party-or-person-required", "Taraf ya da kişi seçin.");

        public static ServerMessage PersonNotOfParty =>
            ServerMessage.Of("contracts.server.contract.person-not-of-party", "Kişi seçilen tarafla ilişkili değil.");

        public static ServerMessage HasRecordedCommitments =>
            ServerMessage.Of("contracts.server.contract.has-recorded-commitments", "Gerçekleşmesi kaydedilmiş taahhüdü olan sözleşme silinemez.");

        public static ServerMessage InvalidFile => ServerMessage.Of("contracts.server.contract.invalid-file", "Dosya içeriği geçersiz.");

        public static ServerMessage InvalidFileBase64 =>
            ServerMessage.Of("contracts.server.contract.invalid-file-base64", "Dosya içeriği geçersiz (base64).");

        public static ServerMessage FileNotFound => ServerMessage.Of("contracts.server.contract.file-not-found", "Dosya bulunamadı.");

        public static ServerMessage ReasonRequired => ServerMessage.Of("contracts.server.contract.reason-required", "Gerekçe zorunludur.");
    }

    /// <summary>Onay kuralı ve vekâlet kayıtları, onay görünümü.</summary>
    public static class Approval
    {
        public static ServerMessage RuleConditionRequired =>
            ServerMessage.Of("contracts.server.approval.rule-condition-required",
                "En az bir koşul girin: tutar, indirim, süre, standart dışı madde ya da risk düzeyi.");

        public static ServerMessage RuleDiscountRange =>
            ServerMessage.Of("contracts.server.approval.rule-discount-range", "İndirim eşiği 0 ile 100 arasında (100 hariç) olmalıdır.");

        public static ServerMessage RuleAmountNegative => ServerMessage.Of("contracts.server.approval.rule-amount-negative", "Tutar eşiği negatif olamaz.");

        public static ServerMessage RuleTermNegative => ServerMessage.Of("contracts.server.approval.rule-term-negative", "Süre eşiği negatif olamaz.");

        public static ServerMessage RuleStepRange => ServerMessage.Of("contracts.server.approval.rule-step-range", "Onay adımı 1 ile 9 arasında olmalıdır.");

        public static ServerMessage RuleCurrency =>
            ServerMessage.Of("contracts.server.approval.rule-currency", "Para birimi üç harfli kod olmalıdır (ör. TRY).");

        public static ServerMessage DelegatorRequired => ServerMessage.Of("contracts.server.approval.delegator-required", "Vekâlet veren zorunludur.");

        public static ServerMessage SelfDelegation => ServerMessage.Of("contracts.server.approval.self-delegation", "Kişi kendine vekâlet veremez.");

        public static ServerMessage EndAfterStart => ServerMessage.Of("contracts.server.approval.end-after-start", "Bitiş, başlangıçtan sonra olmalıdır.");
    }

    /// <summary>Taahhüt kayıtları, gerçekleşmeler ve değerlendirme.</summary>
    public static class Commitment
    {
        public static ServerMessage NotFound => ServerMessage.Of("contracts.server.commitment.not-found", "Taahhüt bulunamadı.");

        public static ServerMessage DependencyNotFound =>
            ServerMessage.Of("contracts.server.commitment.dependency-not-found", "Bağımlı olunan taahhüt bulunamadı.");

        public static ServerMessage ContractClosed =>
            ServerMessage.Of("contracts.server.commitment.contract-closed",
                "Süresi dolmuş, feshedilmiş ya da iptal edilmiş sözleşmeye taahhüt eklenemez ya da değiştirilemez.");

        public static ServerMessage Closed =>
            ServerMessage.Of("contracts.server.commitment.closed", "Tamamlanmış ya da iptal edilmiş taahhüt düzenlenemez.");

        public static ServerMessage ScheduleLocked =>
            ServerMessage.Of("contracts.server.commitment.schedule-locked",
                "Gerçekleşmesi kaydedilmiş taahhütte takvim ve ölçüm değiştirilemez; önceki kayıtlar yeniden yazılmaz. Yeni bir taahhüt açın.");

        public static ServerMessage RecordedDelete =>
            ServerMessage.Of("contracts.server.commitment.recorded-delete",
                "Gerçekleşmesi kaydedilmiş taahhüt silinemez (kayıtlar korunur); iptal edin.");

        public static ServerMessage HasDependents =>
            ServerMessage.Of("contracts.server.commitment.has-dependents", "Başka bir taahhüt bu taahhüde bağlı; önce bağı kaldırın.");

        public static ServerMessage StartRequired => ServerMessage.Of("contracts.server.commitment.start-required", "Başlangıç tarihi zorunludur.");

        public static ServerMessage DueBeforeStart => ServerMessage.Of("contracts.server.commitment.due-before-start", "Son tarih başlangıçtan önce olamaz.");

        public static ServerMessage RecurrenceRequired =>
            ServerMessage.Of("contracts.server.commitment.recurrence-required", "Dönemsel taahhüt için tekrar kuralı seçin.");

        public static ServerMessage RecurrenceEndOrder =>
            ServerMessage.Of("contracts.server.commitment.recurrence-end-order", "Tekrar bitişi ilk son tarihten önce olamaz.");

        public static ServerMessage OneOffNoRecurrence =>
            ServerMessage.Of("contracts.server.commitment.one-off-no-recurrence", "Tek seferlik taahhütte tekrar kuralı olmaz.");

        public static ServerMessage DirectionRequired =>
            ServerMessage.Of("contracts.server.commitment.direction-required", "Ölçüm için yön (en az / en çok / tam) seçin.");

        public static ServerMessage TargetRequired => ServerMessage.Of("contracts.server.commitment.target-required", "Ölçüm için hedef değer girin.");

        public static ServerMessage MetricNameRequired =>
            ServerMessage.Of("contracts.server.commitment.metric-name-required",
                "Hedef, tolerans, birim ya da yön girildiyse ölçüt adı da girilmelidir.");

        public static ServerMessage SelfDependency => ServerMessage.Of("contracts.server.commitment.self-dependency", "Taahhüt kendine bağlı olamaz.");

        public static ServerMessage DependencyCycle => ServerMessage.Of("contracts.server.commitment.dependency-cycle", "Bağımlılık döngü oluşturuyor.");

        public static ServerMessage DependencySameContract =>
            ServerMessage.Of("contracts.server.commitment.dependency-same-contract", "Bağımlı olunan taahhüt aynı sözleşmeden olmalıdır.");

        public static ServerMessage OccurrenceClosed =>
            ServerMessage.Of("contracts.server.commitment.occurrence-closed",
                "Tamamlanmış, muaf tutulmuş ya da iptal edilmiş gerçekleşme değiştirilemez; kayıtlar yeniden yazılmaz.");

        public static ServerMessage CompletedInFuture =>
            ServerMessage.Of("contracts.server.commitment.completed-in-future", "Tamamlanma tarihi gelecekte olamaz.");

        public static ServerMessage CompletedBeforeStart =>
            ServerMessage.Of("contracts.server.commitment.completed-before-start", "Tamamlanma tarihi taahhüdün başlangıcından önce olamaz.");

        public static ServerMessage ActualRequired(string metric) =>
            ServerMessage.Of("contracts.server.commitment.actual-required", "'{0}' için gerçekleşen değeri girin.", metric);

        public static ServerMessage ActualNegative => ServerMessage.Of("contracts.server.commitment.actual-negative", "Gerçekleşen değer negatif olamaz.");

        public static ServerMessage NoteMaxLength(int max) =>
            ServerMessage.Of("contracts.server.commitment.note-max-length", "Not en fazla {0} karakter olabilir.", max);

        public static ServerMessage OccurrenceInvalid =>
            ServerMessage.Of("contracts.server.commitment.occurrence-invalid", "Gerçekleşme doğrulanamadı.");

        public static ServerMessage BlockedByDependency(string due) =>
            ServerMessage.Of("contracts.server.commitment.blocked-by-dependency",
                "Bu taahhüt, bağlı olduğu taahhüdün {0} tarihli gerçekleşmesi tamamlanmadan kaydedilemez.", due);

        public static ServerMessage WaiveClosed =>
            ServerMessage.Of("contracts.server.commitment.waive-closed",
                "Tamamlanmış, muaf tutulmuş ya da iptal edilmiş gerçekleşme için muafiyet verilemez.");

        public static ServerMessage CancelActiveOnly =>
            ServerMessage.Of("contracts.server.commitment.cancel-active-only", "Yalnızca etkin taahhüt iptal edilir.");

        public static ServerMessage ClosedOccurrences =>
            ServerMessage.Of("contracts.server.commitment.closed-occurrences",
                "Tamamlanmış ya da iptal edilmiş taahhüdün gerçekleşmesi değiştirilemez.");

        public static ServerMessage OccurrenceNotFound =>
            ServerMessage.Of("contracts.server.commitment.occurrence-not-found", "Gerçekleşme bulunamadı.");
    }

    /// <summary>Sözleşme belgesi üretimi ve şablonları.</summary>
    public static class Document
    {
        public static ServerMessage PdfNotConfigured =>
            ServerMessage.Of("contracts.server.document.pdf-not-configured", "PDF üretici yapılandırılmamış; belge üretilemez.");

        public static ServerMessage NotFound => ServerMessage.Of("contracts.server.document.not-found", "Belge bulunamadı.");

        public static ServerMessage TemplateNotFound => ServerMessage.Of("contracts.server.document.template-not-found", "Şablon bulunamadı.");

        public static ServerMessage DraftPreviewPermission(string permission) =>
            ServerMessage.Of("contracts.server.document.draft-preview-permission",
                "Çalışma kopyasını önizlemek için '{0}' izni gerekir.", permission);

        public static ServerMessage TemplateTypeMismatch =>
            ServerMessage.Of("contracts.server.document.template-type-mismatch", "Bu şablon seçilen sözleşme türü için tanımlı değil.");

        public static ServerMessage TemplateInvalid(string issue) =>
            ServerMessage.Of("contracts.server.document.template-invalid", "Şablon metni geçersiz: {0}", issue);

        public static ServerMessage FinancialsPermission(string permission) =>
            ServerMessage.Of("contracts.server.document.financials-permission",
                "Bu şablon tutar değişkenleri içeriyor; '{0}' izni gerekir.", permission);

        public static ServerMessage TemplateArchivedEdit =>
            ServerMessage.Of("contracts.server.document.template-archived-edit", "Arşivlenmiş şablon düzenlenemez; önce geri alın.");

        public static ServerMessage TemplateNameTaken =>
            ServerMessage.Of("contracts.server.document.template-name-taken", "Bu adla ve dilde bir şablon zaten var.");

        public static ServerMessage TemplateInUse =>
            ServerMessage.Of("contracts.server.document.template-in-use", "Yayımlanmış ya da belgesi olan şablon silinemez; arşivleyin.");

        public static ServerMessage TemplateArchivedPublish =>
            ServerMessage.Of("contracts.server.document.template-archived-publish", "Arşivlenmiş şablon yayımlanamaz; önce geri alın.");

        public static ServerMessage TemplatePublishInvalid =>
            ServerMessage.Of("contracts.server.document.template-publish-invalid", "Şablon yayımlanamaz: metin geçersiz.");

        public static ServerMessage TemplateUnchanged =>
            ServerMessage.Of("contracts.server.document.template-unchanged", "Yayımlanmamış değişiklik yok; son yayımlanan sürümle aynı.");

        public static ServerMessage TemplateAlreadyArchived =>
            ServerMessage.Of("contracts.server.document.template-already-archived", "Şablon zaten arşivlenmiş.");

        public static ServerMessage TemplateRestoreArchivedOnly =>
            ServerMessage.Of("contracts.server.document.template-restore-archived-only", "Yalnızca arşivlenmiş şablon geri alınır.");

        public static ServerMessage TemplateVersionNotFound =>
            ServerMessage.Of("contracts.server.document.template-version-not-found", "Şablon sürümü bulunamadı.");
    }
}
