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

    /// <summary>Durum geçişi kuralları (<c>ContractRuleResult</c>) ve iş akışı.</summary>
    public static class Transition
    {
        public static ServerMessage NotDraftContent =>
            ServerMessage.Of("contracts.server.lifecycle.not-draft-content", "Sözleşme taslak durumunda değil; içerik değiştirilemez.");

        public static ServerMessage ApprovalPendingContent =>
            ServerMessage.Of("contracts.server.lifecycle.approval-pending-content", "Onay bekleyen içerik değiştirilemez; önce onayı geri çekin.");

        public static ServerMessage SignatureContent =>
            ServerMessage.Of("contracts.server.lifecycle.signature-content",
                "İmzaya gönderilmiş ya da imzalanmış içerik değiştirilemez; yeni revizyon açın.");

        public static ServerMessage InForceContent =>
            ServerMessage.Of("contracts.server.lifecycle.in-force-content",
                "Yürürlüğe girmiş sözleşme yerinde değiştirilemez; ek protokol ya da yeni revizyon açın.");

        public static ServerMessage SignedDelete =>
            ServerMessage.Of("contracts.server.lifecycle.signed-delete", "İmzalı sözleşme silinemez; iptal ya da fesih sürecini kullanın.");

        public static ServerMessage InForceDelete => ServerMessage.Of("contracts.server.lifecycle.in-force-delete", "Yürürlük kaydı olan sözleşme silinemez.");

        public static ServerMessage ReviewDraftOnly => ServerMessage.Of("contracts.server.lifecycle.review-draft-only", "Yalnızca taslak incelemeye gönderilir.");

        public static ServerMessage ReadyInReviewOnly =>
            ServerMessage.Of("contracts.server.lifecycle.ready-in-review-only", "Yalnızca incelemedeki sözleşme yayıma hazır işaretlenir.");

        public static ServerMessage AlreadyDraft => ServerMessage.Of("contracts.server.lifecycle.already-draft", "Sözleşme zaten taslak.");

        public static ServerMessage DraftApprovalPending =>
            ServerMessage.Of("contracts.server.lifecycle.draft-approval-pending", "Onay beklerken taslağa dönülemez; önce onayı geri çekin.");

        public static ServerMessage DraftSignature =>
            ServerMessage.Of("contracts.server.lifecycle.draft-signature",
                "İmzaya gönderilmiş ya da imzalanmış sürüm taslağa dönemez; yeni revizyon açın.");

        public static ServerMessage SubmitNotReady =>
            ServerMessage.Of("contracts.server.lifecycle.submit-not-ready", "Onaya göndermek için sözleşme yayıma hazır olmalıdır.");

        public static ServerMessage AlreadyPending => ServerMessage.Of("contracts.server.lifecycle.already-pending", "Onay zaten bekliyor.");

        public static ServerMessage AlreadyApproved => ServerMessage.Of("contracts.server.lifecycle.already-approved", "Bu sürüm zaten onaylı.");

        public static ServerMessage SubmitSignature =>
            ServerMessage.Of("contracts.server.lifecycle.submit-signature", "İmza sürecindeki sürüm yeniden onaya gönderilemez.");

        public static ServerMessage NoPendingApproval => ServerMessage.Of("contracts.server.lifecycle.no-pending-approval", "Bekleyen onay yok.");

        public static ServerMessage NoPendingVersion => ServerMessage.Of("contracts.server.lifecycle.no-pending-version", "Onay bekleyen sürüm yok.");

        public static ServerMessage CancelInForce =>
            ServerMessage.Of("contracts.server.lifecycle.cancel-in-force", "Yürürlük kaydı olan sözleşme iptal edilmez; fesih sürecini kullanın.");

        public static ServerMessage CancelSigned =>
            ServerMessage.Of("contracts.server.lifecycle.cancel-signed", "İmzalı sürüm iptal edilmez; imza iptali ve fesih ayrı süreçlerdir.");

        public static ServerMessage ReviseInProgress =>
            ServerMessage.Of("contracts.server.lifecycle.revise-in-progress",
                "Onay ya da imza beklerken revizyon açılmaz; önce süreci sonuçlandırın.");

        public static ServerMessage ReviseNotSigned =>
            ServerMessage.Of("contracts.server.lifecycle.revise-not-signed",
                "Revizyon imzalı ya da yürürlükteki sözleşme içindir; imzalanmamış sürüm taslağa döndürülerek düzenlenir.");

        public static ServerMessage DraftRevisionOpen => ServerMessage.Of("contracts.server.lifecycle.draft-revision-open", "Açık bir taslak revizyon zaten var.");

        public static ServerMessage UnknownAction => ServerMessage.Of("contracts.server.lifecycle.unknown-action", "Bilinmeyen eylem.");

        public static ServerMessage MissingParty => ServerMessage.Of("contracts.server.lifecycle.missing-party", "Müşteri seçilmeden ilerlenemez.");

        public static ServerMessage MissingStart => ServerMessage.Of("contracts.server.lifecycle.missing-start", "Başlangıç tarihi girilmeden ilerlenemez.");

        public static ServerMessage MissingEnd =>
            ServerMessage.Of("contracts.server.lifecycle.missing-end", "Bitiş tarihi girilmeli ya da sözleşme belirsiz süreli işaretlenmelidir.");

        public static ServerMessage Closed =>
            ServerMessage.Of("contracts.server.lifecycle.closed", "Süresi dolmuş, feshedilmiş ya da iptal edilmiş sözleşmede bu işlem yapılamaz.");

        public static ServerMessage SelfApproval =>
            ServerMessage.Of("contracts.server.lifecycle.self-approval", "Kendi onaya gönderdiğiniz sözleşmeyi onaylayamazsınız.");

        public static ServerMessage NotYourStep => ServerMessage.Of("contracts.server.lifecycle.not-your-step", "Bu adımda karar veremezsiniz.");

        public static ServerMessage ActionNotAllowed => ServerMessage.Of("contracts.server.lifecycle.action-not-allowed", "Bu işlem şu an yapılamaz.");

        public static ServerMessage VersionNotFound => ServerMessage.Of("contracts.server.lifecycle.version-not-found", "Sürüm bulunamadı.");

        public static ServerMessage CompareVersionNotFound =>
            ServerMessage.Of("contracts.server.lifecycle.compare-version-not-found", "Karşılaştırılacak sürüm bulunamadı.");
    }

    /// <summary>İmza süreci, imzacılar ve imzalı belge dosyası.</summary>
    public static class Signature
    {
        public static ServerMessage ApprovedReadyOnly =>
            ServerMessage.Of("contracts.server.signature.approved-ready-only", "Yalnızca onaylanmış ve yayıma hazır sözleşme imzaya gönderilir.");

        public static ServerMessage AlreadySigned =>
            ServerMessage.Of("contracts.server.signature.already-signed", "Bu sürüm zaten imzalandı; değişiklik için yeni revizyon açın.");

        public static ServerMessage RequestOpen => ServerMessage.Of("contracts.server.signature.request-open", "Açık bir imza isteği var; önce onu sonlandırın.");

        public static ServerMessage ContractClosed =>
            ServerMessage.Of("contracts.server.signature.contract-closed", "Süresi dolmuş, feshedilmiş ya da iptal edilmiş sözleşme imzaya gönderilemez.");

        public static ServerMessage MethodOneOf => ServerMessage.Of("contracts.server.signature.method-one-of", "Yöntem Manual ya da Provider olmalıdır.");

        public static ServerMessage ProviderDisabled =>
            ServerMessage.Of("contracts.server.signature.provider-disabled",
                "Bu firmada imza sağlayıcısı etkin değil (Sözleşme ayarları → İmza sağlayıcısı).");

        public static ServerMessage ModeOneOf => ServerMessage.Of("contracts.server.signature.mode-one-of", "Kip Sequential ya da Parallel olmalıdır.");

        public static ServerMessage ValidityDays => ServerMessage.Of("contracts.server.signature.validity-days", "Geçerlilik 1 ile 90 gün arasında olmalıdır.");

        public static ServerMessage DocumentNotFinal =>
            ServerMessage.Of("contracts.server.signature.document-not-final",
                "İmzaya yalnızca güncel sürümün kesin (dondurulmuş sürümden üretilmiş) belgesi gönderilir.");

        public static ServerMessage SampleDocument =>
            ServerMessage.Of("contracts.server.signature.sample-document",
                "Örnek metinden üretilen belge imzaya gönderilemez; kurumunuzun onaylı şablonunu kullanın.");

        public static ServerMessage RequestInvalid => ServerMessage.Of("contracts.server.signature.request-invalid", "İmza isteği doğrulanamadı.");

        public static ServerMessage ProviderManaged =>
            ServerMessage.Of("contracts.server.signature.provider-managed",
                "Bu istek sağlayıcı yöntemiyle yürüyor; imza sağlayıcıdan gelir. Manuel kayıt yalnızca manuel isteklerde yapılır.");

        public static ServerMessage SignatoryNotFound => ServerMessage.Of("contracts.server.signature.signatory-not-found", "İmzacı bulunamadı.");

        public static ServerMessage CannotSignNow => ServerMessage.Of("contracts.server.signature.cannot-sign-now", "Bu imzacı şimdi imzalayamaz.");

        public static ServerMessage SelfVerify =>
            ServerMessage.Of("contracts.server.signature.self-verify", "Kendi imzanızı doğrulayamazsınız; başka bir yetkili doğrulamalıdır.");

        public static ServerMessage SignedInFuture => ServerMessage.Of("contracts.server.signature.signed-in-future", "İmza tarihi gelecekte olamaz.");

        public static ServerMessage SignedBeforeRequest =>
            ServerMessage.Of("contracts.server.signature.signed-before-request", "İmza tarihi isteğin açıldığı tarihten önce olamaz.");

        public static ServerMessage RecordInvalid => ServerMessage.Of("contracts.server.signature.record-invalid", "İmza kaydı doğrulanamadı.");

        public static ServerMessage ProviderDecline =>
            ServerMessage.Of("contracts.server.signature.provider-decline", "Sağlayıcı yöntemli istekte ret sağlayıcıdan gelir.");

        public static ServerMessage DeclineNotAllowed => ServerMessage.Of("contracts.server.signature.decline-not-allowed", "Bu imzacı için ret kaydedilemez.");

        public static ServerMessage ReminderNotDue =>
            ServerMessage.Of("contracts.server.signature.reminder-not-due",
                "Hatırlatma yalnızca sırası gelmiş, henüz imzalamamış imzacı için kaydedilir.");

        public static ServerMessage ReminderHourly =>
            ServerMessage.Of("contracts.server.signature.reminder-hourly", "Aynı imzacıya en fazla saatte bir hatırlatma kaydedilir.");

        public static ServerMessage NotExpiredYet => ServerMessage.Of("contracts.server.signature.not-expired-yet", "İmza isteğinin süresi henüz dolmadı.");

        public static ServerMessage SimulationOnly =>
            ServerMessage.Of("contracts.server.signature.simulation-only", "Simülasyon yalnızca simülasyon sağlayıcısıyla açılmış istekte kullanılabilir.");

        public static ServerMessage EventNotApplied => ServerMessage.Of("contracts.server.signature.event-not-applied", "Olay uygulanamadı.");

        public static ServerMessage ProviderNotFound => ServerMessage.Of("contracts.server.signature.provider-not-found", "Sağlayıcı bulunamadı.");

        public static ServerMessage EventIdRequired =>
            ServerMessage.Of("contracts.server.signature.event-id-required", "Olay kimliği zorunludur (en fazla 100 karakter).");

        public static ServerMessage UnknownEventType => ServerMessage.Of("contracts.server.signature.unknown-event-type", "Bilinmeyen olay türü.");

        public static ServerMessage EventSequence => ServerMessage.Of("contracts.server.signature.event-sequence", "Olay numarası 1 ya da büyük olmalıdır.");

        public static ServerMessage EventInvalid => ServerMessage.Of("contracts.server.signature.event-invalid", "Olay doğrulanamadı.");

        public static ServerMessage RequestNotFound => ServerMessage.Of("contracts.server.signature.request-not-found", "İstek bulunamadı.");

        public static ServerMessage NoOpenRequest => ServerMessage.Of("contracts.server.signature.no-open-request", "Açık bir imza isteği yok.");

        public static ServerMessage RequestExpired =>
            ServerMessage.Of("contracts.server.signature.request-expired",
                "İmza isteğinin süresi doldu; önce isteği sonlandırın (süre dolumu) ve gerekirse yeniden başlatın.");

        public static ServerMessage SignatoryCount => ServerMessage.Of("contracts.server.signature.signatory-count", "En az bir, en fazla yirmi imzacı girin.");

        public static ServerMessage SignatoryKind =>
            ServerMessage.Of("contracts.server.signature.signatory-kind", "İmzacı türü Internal ya da External olmalıdır.");

        public static ServerMessage CapacityTooLong => ServerMessage.Of("contracts.server.signature.capacity-too-long", "İmza sıfatı çok uzun.");

        public static ServerMessage InternalMember =>
            ServerMessage.Of("contracts.server.signature.internal-member", "İç imzacı bu firmanın etkin üyesi olmalıdır.");

        public static ServerMessage DuplicateSignatory => ServerMessage.Of("contracts.server.signature.duplicate-signatory", "Aynı kişi iki kez imzacı olamaz.");

        public static ServerMessage ExternalName =>
            ServerMessage.Of("contracts.server.signature.external-name", "Dış imzacının adı zorunludur (2-150 karakter).");

        public static ServerMessage ExternalEmailRequired =>
            ServerMessage.Of("contracts.server.signature.external-email-required", "Sağlayıcı yönteminde dış imzacının e-postası zorunludur.");

        public static ServerMessage RequiredSignatory =>
            ServerMessage.Of("contracts.server.signature.required-signatory", "En az bir zorunlu imzacı olmalıdır.");

        public static ServerMessage AlreadySignedBySignatory =>
            ServerMessage.Of("contracts.server.signature.already-signed-by-signatory", "Bu imzacı zaten imzaladı.");

        public static ServerMessage SignatoryClosed =>
            ServerMessage.Of("contracts.server.signature.signatory-closed", "Bu imzacı için imza artık alınamaz.");

        public static ServerMessage SequentialOrder =>
            ServerMessage.Of("contracts.server.signature.sequential-order", "Sıralı imzada önce önceki zorunlu imzacılar imzalamalıdır.");

        public static ServerMessage FileNameRequired => ServerMessage.Of("contracts.server.signature.file-name-required", "Dosya adı zorunludur.");

        public static ServerMessage FileEmpty => ServerMessage.Of("contracts.server.signature.file-empty", "Dosya boş.");

        public static ServerMessage FileTooLarge => ServerMessage.Of("contracts.server.signature.file-too-large", "Dosya en fazla 10 MB olabilir.");

        public static ServerMessage FileType =>
            ServerMessage.Of("contracts.server.signature.file-type", "Yalnızca PDF, PNG ya da JPEG dosyası yüklenebilir.");
    }

    /// <summary>Yenileme ve fesih.</summary>
    public static class Renewal
    {
        public static ServerMessage ReasonFromList =>
            ServerMessage.Of("contracts.server.renewal.reason-from-list", "Yenilememe nedeni listeden seçilmelidir.");

        public static ServerMessage AlreadyDecided(string outcome) =>
            ServerMessage.Of("contracts.server.renewal.already-decided", "Bu dönem için karar zaten verilmiş ({0}).", outcome);

        public static ServerMessage DraftSignedOnly =>
            ServerMessage.Of("contracts.server.renewal.draft-signed-only",
                "Yenileme taslağı yalnızca imzalı ve yürürlükteki (ya da yeni süresi dolmuş) sözleşme için açılır.");

        public static ServerMessage Indefinite =>
            ServerMessage.Of("contracts.server.renewal.indefinite", "Belirsiz süreli sözleşmenin yenilenecek bir dönemi yoktur.");

        public static ServerMessage TermMonths => ServerMessage.Of("contracts.server.renewal.term-months", "Süre 1 ile 120 ay arasında olmalıdır.");

        public static ServerMessage StartAfterEnd =>
            ServerMessage.Of("contracts.server.renewal.start-after-end", "Yeni dönem, mevcut dönemin bitişinden sonra başlamalıdır.");

        public static ServerMessage TerminationReasonRequired =>
            ServerMessage.Of("contracts.server.renewal.termination-reason-required", "Fesih gerekçesi zorunludur.");

        public static ServerMessage TerminationBeforeStart =>
            ServerMessage.Of("contracts.server.renewal.termination-before-start", "Fesih tarihi sözleşme başlangıcından önce olamaz.");

        public static ServerMessage TerminationAfterEnd =>
            ServerMessage.Of("contracts.server.renewal.termination-after-end", "Fesih tarihi sözleşme bitişinden sonra olamaz.");

        public static ServerMessage ShortNotice(int days) =>
            ServerMessage.Of("contracts.server.renewal.short-notice",
                "Fesih tarihi bildirim süresinin ({0} gün) altında; yine de feshetmek için kısa bildirimi onaylayın.", days);

        public static ServerMessage TerminateSignedOnly =>
            ServerMessage.Of("contracts.server.renewal.terminate-signed-only",
                "Yalnızca imzalı ve yürürlükteki sözleşme feshedilir (taslak ya da imzasız sözleşme iptal edilir).");
    }

    /// <summary>Kabul edilmiş tekliften sözleşme.</summary>
    public static class FromQuote
    {
        public static ServerMessage PartyNotMatched =>
            ServerMessage.Of("contracts.server.from-quote.party-not-matched",
                "Teklifin müşterisi ortak kayıtla eşleşmedi; sözleşme boş taslaktan açılmalıdır.");

        public static ServerMessage LinesRequired =>
            ServerMessage.Of("contracts.server.from-quote.lines-required", "Sözleşmeye alınacak en az bir teklif kalemi seçin.");

        public static ServerMessage LineNotInQuote => ServerMessage.Of("contracts.server.from-quote.line-not-in-quote", "Seçilen kalemlerden biri bu teklifte yok.");

        public static ServerMessage StartRequired =>
            ServerMessage.Of("contracts.server.from-quote.start-required", "Süre (ay) verildiğinde başlangıç tarihi de girilmelidir.");

        public static ServerMessage LinesTaken(string lines) =>
            ServerMessage.Of("contracts.server.from-quote.lines-taken", "Seçilen teklif kalemleri zaten bir sözleşmede: {0}.", lines);

        public static ServerMessage LineTaken => ServerMessage.Of("contracts.server.from-quote.line-taken", "Kalem başka bir sözleşmede kullanılıyor.");

        public static ServerMessage SourceUnavailable =>
            ServerMessage.Of("contracts.server.from-quote.source-unavailable", "Teklif kaynağı kullanılamıyor; kabul edilmiş teklif bulunamadı.");

        public static ServerMessage AcceptedNotFound =>
            ServerMessage.Of("contracts.server.from-quote.accepted-not-found", "Kabul edilmiş teklif bulunamadı.");
    }
}
