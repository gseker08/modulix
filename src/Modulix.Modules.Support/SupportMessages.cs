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

    /// <summary>Talep kaydı doğrulamaları.</summary>
    public static class Ticket
    {
        public static ServerMessage Closed =>
            ServerMessage.Of("support.server.ticket.closed", "Talep kapalı.");

        public static ServerMessage CreateOnlyViaWorkflow =>
            ServerMessage.Of("support.server.ticket.create-only-via-workflow", "Talep yalnızca talep oluşturma işlemiyle açılır.");

        public static ServerMessage OwnerNeedsAssignPermission =>
            ServerMessage.Of("support.server.ticket.owner-needs-assign-permission", "Talebi oluştururken sorumlu atamak için atama yetkisi gerekir.");

        public static ServerMessage QueueNeedsAssignPermission =>
            ServerMessage.Of("support.server.ticket.queue-needs-assign-permission", "Talebi oluştururken kuyruk seçmek için atama yetkisi gerekir.");

        public static ServerMessage QueueNotFoundOrInactive =>
            ServerMessage.Of("support.server.ticket.queue-not-found-or-inactive", "Kuyruk bulunamadı ya da pasif.");

        public static ServerMessage ClosedNotEditable =>
            ServerMessage.Of("support.server.ticket.closed-not-editable", "Çözülmüş, kapanmış ya da iptal edilmiş talep düzenlenemez; gerekirse önce yeniden açın.");

        public static ServerMessage ReporterRequired =>
            ServerMessage.Of("support.server.ticket.reporter-required", "Müşteri seçin ya da başvuranın adını girin.");

        public static ServerMessage PersonNotOfParty =>
            ServerMessage.Of("support.server.ticket.person-not-of-party", "Kişi seçilen müşteriyle ilişkili değil.");

        public static ServerMessage CategoryInactive =>
            ServerMessage.Of("support.server.ticket.category-inactive", "Kategori pasif.");

        public static ServerMessage CategoryFieldRequired =>
            ServerMessage.Of("support.server.ticket.category-field-required", "Bu kategori için bu alan zorunludur.");
    }

    /// <summary>Öncelik matrisi ve yönlendirme kuralı kayıtları.</summary>
    public static class Routing
    {
        public static ServerMessage PreviewInvalid =>
            ServerMessage.Of("support.server.routing.preview-invalid", "Deneme girdileri geçersiz.");

        public static ServerMessage MatrixCellExists =>
            ServerMessage.Of("support.server.routing.matrix-cell-exists", "Bu etki × aciliyet hücresi zaten tanımlı; mevcut kaydı güncelleyin.");

        public static ServerMessage RuleActionRequired =>
            ServerMessage.Of("support.server.routing.rule-action-required", "Kuralın en az bir eylemi olmalıdır: kuyruk ya da sorumlu.");

        public static ServerMessage QueueInactive =>
            ServerMessage.Of("support.server.routing.queue-inactive", "Kuyruk pasif; etkin bir kuyruk seçin.");
    }

    /// <summary>Kuyruk ve kategori kayıtları.</summary>
    public static class Catalog
    {
        public static ServerMessage DefaultQueueInactive =>
            ServerMessage.Of("support.server.catalog.default-queue-inactive", "Varsayılan kuyruk pasif olamaz; önce başka bir kuyruğu varsayılan yapın.");

        public static ServerMessage OtherQueueIsDefault =>
            ServerMessage.Of("support.server.catalog.other-queue-is-default", "Başka bir kuyruk varsayılan; önce onun varsayılan işaretini kaldırın.");

        public static ServerMessage CategorySelfParent =>
            ServerMessage.Of("support.server.catalog.category-self-parent", "Kategori kendi üstü olamaz.");

        public static ServerMessage ParentMustBeRoot =>
            ServerMessage.Of("support.server.catalog.parent-must-be-root", "Üst kategori ana kategori olmalıdır (en çok iki düzey).");

        public static ServerMessage CategoryHasChildren =>
            ServerMessage.Of("support.server.catalog.category-has-children", "Alt kategorisi olan kategori başka bir kategorinin altına taşınamaz.");
    }

    /// <summary>SLA politikası kayıtları.</summary>
    public static class Sla
    {
        public static ServerMessage PreviewInvalid =>
            ServerMessage.Of("support.server.sla.preview-invalid", "Önizleme girdileri geçersiz.");

        public static ServerMessage NoOpenInstance =>
            ServerMessage.Of("support.server.sla.no-open-instance", "Yeniden hesaplanacak açık SLA örneği yok.");

        public static ServerMessage NoOpenInstanceField =>
            ServerMessage.Of("support.server.sla.no-open-instance-field", "Açık SLA örneği yok.");

        public static ServerMessage TargetRequired =>
            ServerMessage.Of("support.server.sla.target-required", "İlk yanıt ya da çözüm hedeflerinden en az biri girilmelidir.");

        public static ServerMessage TargetTooShort =>
            ServerMessage.Of("support.server.sla.target-too-short", "Hedef 1 dakikadan az olamaz.");

        public static ServerMessage DefaultNoMatchConstraint =>
            ServerMessage.Of("support.server.sla.default-no-match-constraint", "Varsayılan (yedek) politikada eşleşme kısıtı olamaz.");

        public static ServerMessage OtherPolicyIsDefault =>
            ServerMessage.Of("support.server.sla.other-policy-is-default", "Başka bir politika varsayılan; önce onun varsayılan işaretini kaldırın.");

        public static ServerMessage ContractUnavailable =>
            ServerMessage.Of("support.server.sla.contract-unavailable", "Sözleşme bulunamadı, erişim yetkiniz yok ya da sözleşme modülü kapalı.");

        public static ServerMessage ContractNotOfParty =>
            ServerMessage.Of("support.server.sla.contract-not-of-party", "Sözleşme, politikadaki müşteriye ait değil.");

        public static ServerMessage EscalationUserNotMember(object ruleId) =>
            ServerMessage.Of("support.server.sla.escalation-user-not-member", "'{0}' kuralındaki kullanıcı bu kuruluşun üyesi değil.", ruleId);

        public static ServerMessage EscalationQueueUnavailable(object ruleId) =>
            ServerMessage.Of("support.server.sla.escalation-queue-unavailable", "'{0}' kuralındaki kuyruk bulunamadı ya da pasif.", ruleId);
    }

    /// <summary>Kategori zorunlu alan listesi doğrulaması.</summary>
    public static class Category
    {
        public static ServerMessage InvalidRequiredFields(string unknown, string allowed) =>
            ServerMessage.Of("support.server.category.invalid-required-fields", "Geçersiz alan: {0}. İzin verilenler: {1}.", unknown, allowed);
    }

    /// <summary>SLA eskalasyon kuralı listesi doğrulaması.</summary>
    public static class Escalation
    {
        public static ServerMessage InvalidJson =>
            ServerMessage.Of("support.server.escalation.invalid-json", "Eskalasyon kuralları geçerli bir JSON listesi olmalıdır.");

        public static ServerMessage TooManyRules(int max) =>
            ServerMessage.Of("support.server.escalation.too-many-rules", "En çok {0} eskalasyon kuralı tanımlanabilir.", max);

        public static ServerMessage RuleInvalid =>
            ServerMessage.Of("support.server.escalation.rule-invalid",
                "Her kural için küçük harf/rakam/tire kimliği, ölçüt (FirstResponse|Resolution), 1-500 yüzde ve geçerli eylem verin.");

        public static ServerMessage NotifyUserNeedsUser =>
            ServerMessage.Of("support.server.escalation.notify-user-needs-user", "Kullanıcıya bildirim kuralı için kullanıcı seçin.");

        public static ServerMessage ReassignNeedsTarget =>
            ServerMessage.Of("support.server.escalation.reassign-needs-target", "Yeniden atama kuralı için kullanıcı ve/veya kuyruk seçin.");

        public static ServerMessage DuplicateIds =>
            ServerMessage.Of("support.server.escalation.duplicate-ids", "Kural kimlikleri benzersiz olmalıdır.");
    }

    /// <summary>Öncelik matrisi doğrulaması.</summary>
    public static class Matrix
    {
        public static ServerMessage Invalid =>
            ServerMessage.Of("support.server.matrix.invalid", "Matris geçersiz.");

        public static ServerMessage ImpactOneOf(string allowed) =>
            ServerMessage.Of("support.server.matrix.impact-one-of", "Etki şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage UrgencyOneOf(string allowed) =>
            ServerMessage.Of("support.server.matrix.urgency-one-of", "Aciliyet şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage PriorityOneOf(string allowed) =>
            ServerMessage.Of("support.server.matrix.priority-one-of", "Öncelik şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage OneValuePerCell =>
            ServerMessage.Of("support.server.matrix.one-value-per-cell", "Her etki × aciliyet hücresi için tek değer verilebilir.");
    }

    /// <summary>Talep mesajları (ekleme doğrulamaları).</summary>
    public static class Message
    {
        public static ServerMessage BodyRequired =>
            ServerMessage.Of("support.server.message.body-required", "Mesaj metni zorunludur.");

        public static ServerMessage TicketClosed =>
            ServerMessage.Of("support.server.message.ticket-closed", "Kapanmış ya da iptal edilmiş talebe mesaj eklenemez; yeniden açın (pencere içindeyse) ya da ilişkili yeni talep açın.");

        public static ServerMessage ConfirmationOnlyResolved =>
            ServerMessage.Of("support.server.message.confirmation-only-resolved", "Müşteri teyidi yalnızca çözülmüş talepte kaydedilir.");

        public static ServerMessage NotResolved =>
            ServerMessage.Of("support.server.message.not-resolved", "Talep çözülmüş değil.");
    }

    /// <summary>Talep ekleri.</summary>
    public static class Attachment
    {
        public static ServerMessage MessageNotFound =>
            ServerMessage.Of("support.server.attachment.message-not-found", "Mesaj bulunamadı.");

        public static ServerMessage TicketClosed =>
            ServerMessage.Of("support.server.attachment.ticket-closed", "Kapanmış, iptal edilmiş ya da birleştirilmiş talebe ek eklenemez.");

        public static ServerMessage TooLarge =>
            ServerMessage.Of("support.server.attachment.too-large", "Dosya, depolama için ayarlanan boyut sınırını aşıyor.");

        public static ServerMessage MaxPerMessage(int max) =>
            ServerMessage.Of("support.server.attachment.max-per-message", "Bir mesaja en fazla {0} ek eklenebilir.", max);

        public static ServerMessage MaxPerTicket(int max) =>
            ServerMessage.Of("support.server.attachment.max-per-ticket", "Bir talebe en fazla {0} ek eklenebilir.", max);

        public static ServerMessage AlreadyOnMessage =>
            ServerMessage.Of("support.server.attachment.already-on-message", "Bu dosya mesaja zaten eklenmiş.");

        public static ServerMessage SameContent =>
            ServerMessage.Of("support.server.attachment.same-content", "Aynı içerik zaten ekli.");

        public static ServerMessage NotFound =>
            ServerMessage.Of("support.server.attachment.not-found", "Ek bulunamadı.");

        public static ServerMessage ContentNotFound =>
            ServerMessage.Of("support.server.attachment.content-not-found", "Ek içeriği bulunamadı.");
    }

    /// <summary>Talep alt işleri.</summary>
    public static class Task
    {
        public static ServerMessage NotWorking =>
            ServerMessage.Of("support.server.task.not-working", "Talep çalışılan durumda değil.");

        public static ServerMessage TitleRequired =>
            ServerMessage.Of("support.server.task.title-required", "Alt iş başlığı zorunludur.");

        public static ServerMessage AddOnlyWhenWorking =>
            ServerMessage.Of("support.server.task.add-only-when-working", "Alt iş yalnızca çalışılan talebe eklenebilir.");

        public static ServerMessage NotFound =>
            ServerMessage.Of("support.server.task.not-found", "Alt iş bulunamadı.");

        public static ServerMessage AlreadyDone =>
            ServerMessage.Of("support.server.task.already-done", "Alt iş zaten tamamlanmış.");

        public static ServerMessage AlreadyOpen =>
            ServerMessage.Of("support.server.task.already-open", "Alt iş zaten açık.");

        public static ServerMessage StatusUnchanged =>
            ServerMessage.Of("support.server.task.status-unchanged", "Durum değişmedi.");

        public static ServerMessage ReopenOnlyWhenWorking =>
            ServerMessage.Of("support.server.task.reopen-only-when-working", "Alt iş yalnızca çalışılan talepte yeniden açılabilir.");
    }

    /// <summary>Toplu talep işlemi doğrulamaları.</summary>
    public static class Bulk
    {
        public static ServerMessage ActionOneOf(string allowed) =>
            ServerMessage.Of("support.server.bulk.action-one-of", "Toplu işlem şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage SelectOne =>
            ServerMessage.Of("support.server.bulk.select-one", "En az bir talep seçin.");

        public static ServerMessage MaxItems(int max) =>
            ServerMessage.Of("support.server.bulk.max-items", "Bir seferde en fazla {0} talep işlenebilir.", max);
    }

    /// <summary>Talebe bağlanan ilişkili kayıtlar.</summary>
    public static class Link
    {
        public static ServerMessage SelectRecord =>
            ServerMessage.Of("support.server.link.select-record", "Kayıt seçin.");

        public static ServerMessage PartyRequired =>
            ServerMessage.Of("support.server.link.party-required", "İlişkili kayıt bağlamak için önce talebe müşteri bağlayın.");

        public static ServerMessage AlreadyLinked =>
            ServerMessage.Of("support.server.link.already-linked", "Bu kayıt talebe zaten bağlı.");

        public static ServerMessage AlreadyLinkedField =>
            ServerMessage.Of("support.server.link.already-linked-field", "Zaten bağlı.");

        public static ServerMessage MaxPerTicket(int max) =>
            ServerMessage.Of("support.server.link.max-per-ticket", "Bir talebe en fazla {0} kayıt bağlanabilir.", max);

        public static ServerMessage RecordUnavailable =>
            ServerMessage.Of("support.server.link.record-unavailable", "Kayıt bulunamadı, erişim yetkiniz yok ya da ilgili modül kapalı.");

        public static ServerMessage OtherParty =>
            ServerMessage.Of("support.server.link.other-party", "Farklı müşteriye ait kayıt talebe bağlanamaz.");

        public static ServerMessage NotOfTicketParty =>
            ServerMessage.Of("support.server.link.not-of-ticket-party", "Kayıt talebin müşterisine ait değil.");

        public static ServerMessage NotFound =>
            ServerMessage.Of("support.server.link.not-found", "Bağlantı bulunamadı.");

        public static ServerMessage TicketClosed =>
            ServerMessage.Of("support.server.link.ticket-closed", "Kapanmış, iptal edilmiş ya da birleştirilmiş talebin bağlantıları değiştirilemez.");
    }

    /// <summary>Talep durum geçişi ve birleştirme kuralları (eylem durumu ve engel iletileri).</summary>
    public static class Rule
    {
        public static ServerMessage NotNew =>
            ServerMessage.Of("support.server.rule.not-new", "Yalnızca yeni talep sınıflandırmaya alınabilir.");

        public static ServerMessage AssignNotWorking =>
            ServerMessage.Of("support.server.rule.assign-not-working", "Çözülmüş, kapanmış ya da iptal edilmiş talebe atama yapılamaz; önce yeniden açın.");

        public static ServerMessage TransferNotWorking =>
            ServerMessage.Of("support.server.rule.transfer-not-working", "Yalnızca çalışılan talep başka kuyruğa devredilebilir.");

        public static ServerMessage CannotStart =>
            ServerMessage.Of("support.server.rule.cannot-start", "Talep bu durumdan işleme alınamaz.");

        public static ServerMessage NoOwner =>
            ServerMessage.Of("support.server.rule.no-owner", "İşleme almak için önce talebe bir sorumlu atanmalıdır.");

        public static ServerMessage CannotWait =>
            ServerMessage.Of("support.server.rule.cannot-wait", "Yalnızca atanmış ya da işlemdeki talep bekletilebilir.");

        public static ServerMessage SameWaitStatus =>
            ServerMessage.Of("support.server.rule.same-wait-status", "Talep zaten bu bekleme durumunda.");

        public static ServerMessage NotWaiting =>
            ServerMessage.Of("support.server.rule.not-waiting", "Yalnızca beklemedeki talep sürdürülebilir.");

        public static ServerMessage CannotResolve =>
            ServerMessage.Of("support.server.rule.cannot-resolve", "Talep bu durumdan çözülemez; önce sorumlu atayıp işleme alın.");

        public static ServerMessage CloseNotResolved =>
            ServerMessage.Of("support.server.rule.close-not-resolved", "Yalnızca çözülmüş talep kapatılabilir.");

        public static ServerMessage ReopenNotClosed =>
            ServerMessage.Of("support.server.rule.reopen-not-closed", "Yalnızca çözülmüş ya da kapanmış talep yeniden açılabilir.");

        public static ServerMessage ReopenWindow(int days) =>
            ServerMessage.Of("support.server.rule.reopen-window", "Kapanıştan sonra {0} gün geçtiği için talep yeniden açılamaz; ilişkili yeni talep açın.", days);

        public static ServerMessage CancelNotWorking =>
            ServerMessage.Of("support.server.rule.cancel-not-working", "Yalnızca çalışılan talep iptal edilebilir; çözülmüş talep için yeniden açma ya da kapatma kullanın.");

        public static ServerMessage FinalPriority =>
            ServerMessage.Of("support.server.rule.final-priority", "Kapanmış ya da iptal edilmiş talebin önceliği değiştirilemez.");

        public static ServerMessage UnknownAction =>
            ServerMessage.Of("support.server.rule.unknown-action", "Bilinmeyen eylem.");

        public static ServerMessage MergeSelf =>
            ServerMessage.Of("support.server.rule.merge-self", "Talep kendisiyle birleştirilemez.");

        public static ServerMessage MergeSourceClosed =>
            ServerMessage.Of("support.server.rule.merge-source-closed", "Çözülmüş, kapanmış, iptal edilmiş ya da zaten birleştirilmiş talep birleştirilemez.");

        public static ServerMessage MergeTargetClosed =>
            ServerMessage.Of("support.server.rule.merge-target-closed", "Kapanmış, iptal edilmiş ya da birleştirilmiş talebe birleştirilemez; hedef talebi önce yeniden açın.");

        public static ServerMessage MergeDifferentCustomer =>
            ServerMessage.Of("support.server.rule.merge-different-customer", "Farklı müşterilerin talepleri birleştirilemez; ilişkili talep olarak bağlayın.");

        public static ServerMessage ActionNotAllowed =>
            ServerMessage.Of("support.server.rule.action-not-allowed", "Bu işlem şu an yapılamaz.");

        public static ServerMessage MergeNotAllowed =>
            ServerMessage.Of("support.server.rule.merge-not-allowed", "Bu talepler birleştirilemez.");

        public static ServerMessage AlreadyYours =>
            ServerMessage.Of("support.server.rule.already-yours", "Talep zaten sizin üzerinizde.");

        public static ServerMessage Owned =>
            ServerMessage.Of("support.server.rule.owned", "Talebin sorumlusu var; devralmak için atama yetkisi olan biri atamalıdır.");

        public static ServerMessage BlockingTasks(int count) =>
            ServerMessage.Of("support.server.rule.blocking-tasks", "{0} engelleyici alt iş açık; talebi çözmeden önce bunları tamamlayın.", count);
    }

    /// <summary>Çözüm alanı doğrulaması.</summary>
    public static class Resolution
    {
        public static ServerMessage CodeRequired =>
            ServerMessage.Of("support.server.resolution.code-required", "Çözüm kodu zorunludur.");

        public static ServerMessage NoteRequired =>
            ServerMessage.Of("support.server.resolution.note-required", "Çözüm açıklaması zorunludur.");

        public static ServerMessage RootCauseRequired =>
            ServerMessage.Of("support.server.resolution.root-cause-required", "Şikâyetin çözümünde kök neden zorunludur.");

        public static ServerMessage CorrectiveActionRequired =>
            ServerMessage.Of("support.server.resolution.corrective-action-required", "Şikâyetin çözümünde düzeltici aksiyon zorunludur.");
    }

    /// <summary>Talep iş akışı eylemlerinin doğrulamaları.</summary>
    public static class Workflow
    {
        public static ServerMessage ReasonRequired =>
            ServerMessage.Of("support.server.workflow.reason-required", "Gerekçe zorunludur.");

        public static ServerMessage OwnerOrQueueRequired =>
            ServerMessage.Of("support.server.workflow.owner-or-queue-required", "Sorumlu ya da kuyruk seçin.");

        public static ServerMessage ReassignReasonRequired =>
            ServerMessage.Of("support.server.workflow.reassign-reason-required", "Sorumlusu olan talebin sorumlusunu değiştirmek için gerekçe zorunludur.");

        public static ServerMessage TransferQueueRequired =>
            ServerMessage.Of("support.server.workflow.transfer-queue-required", "Devredilecek kuyruğu seçin (aktif olmalı).");

        public static ServerMessage AlreadyInQueue =>
            ServerMessage.Of("support.server.workflow.already-in-queue", "Talep zaten bu kuyrukta.");

        public static ServerMessage ResolutionIncomplete =>
            ServerMessage.Of("support.server.workflow.resolution-incomplete", "Çözüm bilgileri eksik.");

        public static ServerMessage SamePriority =>
            ServerMessage.Of("support.server.workflow.same-priority", "Talep zaten bu önceliğe sahip.");
    }

    /// <summary>Talep birleştirme.</summary>
    public static class Merge
    {
        public static ServerMessage TargetUnavailable =>
            ServerMessage.Of("support.server.merge.target-unavailable", "Hedef talep bulunamadı ya da erişim yetkiniz yok.");
    }

    /// <summary>Olası mükerrer talep araması.</summary>
    public static class Duplicate
    {
        public static ServerMessage ProbeRequired =>
            ServerMessage.Of("support.server.duplicate.probe-required", "Müşteri, e-posta ya da telefon verin.");

        public static ServerMessage ApplicantRequired =>
            ServerMessage.Of("support.server.duplicate.applicant-required", "Başvuran kimliği gerekir.");
    }

    /// <summary>Düzeltici aksiyonlar (şikâyet).</summary>
    public static class Corrective
    {
        public static ServerMessage TitleRequired =>
            ServerMessage.Of("support.server.corrective.title-required", "Aksiyon başlığı zorunludur.");

        public static ServerMessage OwnerRequired =>
            ServerMessage.Of("support.server.corrective.owner-required", "Sorumlu zorunludur ve bu kuruluşun etkin üyesi olmalıdır.");

        public static ServerMessage DueDateRequired =>
            ServerMessage.Of("support.server.corrective.due-date-required", "Son tarih zorunludur.");

        public static ServerMessage DueDatePast =>
            ServerMessage.Of("support.server.corrective.due-date-past", "Son tarih geçmişte olamaz.");

        public static ServerMessage DueDateTooFar =>
            ServerMessage.Of("support.server.corrective.due-date-too-far", "Son tarih en çok 2 yıl sonrası olabilir.");

        public static ServerMessage MaxPerTicket(int max) =>
            ServerMessage.Of("support.server.corrective.max-per-ticket", "Bir talepte en fazla {0} düzeltici aksiyon olabilir.", max);

        public static ServerMessage NotFound =>
            ServerMessage.Of("support.server.corrective.not-found", "Düzeltici aksiyon bulunamadı.");

        public static ServerMessage AlreadyInStatus =>
            ServerMessage.Of("support.server.corrective.already-in-status", "Aksiyon zaten bu durumda.");

        public static ServerMessage ResultNoteRequired =>
            ServerMessage.Of("support.server.corrective.result-note-required", "Sonuç notu zorunludur (ne yapıldı, nasıl doğrulandı).");

        public static ServerMessage ComplaintOnly =>
            ServerMessage.Of("support.server.corrective.complaint-only", "Düzeltici aksiyon yalnızca şikâyet türündeki talepte açılır.");

        public static ServerMessage NotComplaint =>
            ServerMessage.Of("support.server.corrective.not-complaint", "Talep şikâyet türünde değil.");

        public static ServerMessage TicketClosed =>
            ServerMessage.Of("support.server.corrective.ticket-closed", "İptal edilmiş ya da birleştirilmiş talepte düzeltici aksiyon değiştirilemez.");
    }

    /// <summary>Gelen kanal (e-posta vb.) mesajı doğrulaması.</summary>
    public static class Inbound
    {
        public static ServerMessage MessageIdRequired(int max) =>
            ServerMessage.Of("support.server.inbound.message-id-required", "Mesaj kimliği zorunludur (en fazla {0} karakter).", max);

        public static ServerMessage FromAddressInvalid =>
            ServerMessage.Of("support.server.inbound.from-address-invalid", "Gönderen adresi geçerli bir e-posta olmalıdır.");

        public static ServerMessage FromAddressRequired =>
            ServerMessage.Of("support.server.inbound.from-address-required", "E-posta kanalında gönderen adresi zorunludur.");
    }

    /// <summary>Ek dosya doğrulaması (ad, boyut, tür, içerik).</summary>
    public static class File
    {
        public static ServerMessage NameRequired =>
            ServerMessage.Of("support.server.file.name-required", "Dosya adı zorunludur.");

        public static ServerMessage Empty =>
            ServerMessage.Of("support.server.file.empty", "Dosya boş olamaz.");

        public static ServerMessage TooLarge(int megabytes) =>
            ServerMessage.Of("support.server.file.too-large", "Dosya en fazla {0} MB olabilir.", megabytes);

        public static ServerMessage TypeNotAllowed(string allowed) =>
            ServerMessage.Of("support.server.file.type-not-allowed", "Bu dosya türüne izin verilmez. İzinli türler: {0}.", allowed);

        public static ServerMessage DangerousName =>
            ServerMessage.Of("support.server.file.dangerous-name", "Dosya adı çalıştırılabilir ya da betik uzantısı içeremez.");

        public static ServerMessage ContentMismatch =>
            ServerMessage.Of("support.server.file.content-mismatch", "Dosya içeriği uzantısıyla uyuşmuyor.");

        public static ServerMessage BinaryInText =>
            ServerMessage.Of("support.server.file.binary-in-text", "Metin dosyası ikili içerik barındıramaz.");
    }

    /// <summary>Destek raporu girdileri.</summary>
    public static class Report
    {
        public static ServerMessage StartAfterEnd =>
            ServerMessage.Of("support.server.report.start-after-end", "Başlangıç tarihi bitişten sonra olamaz.");

        public static ServerMessage EndInFuture =>
            ServerMessage.Of("support.server.report.end-in-future", "Bitiş tarihi bugünden sonra olamaz.");

        public static ServerMessage PeriodTooLong(int max) =>
            ServerMessage.Of("support.server.report.period-too-long", "Dönem en çok {0} gün olabilir.", max);

        public static ServerMessage MinOccurrencesRange =>
            ServerMessage.Of("support.server.report.min-occurrences-range", "Tekrar eşiği 2-100 arasında olmalıdır.");
    }
}
