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
        public static ServerMessage ImpactOneOf(string allowed) =>
            ServerMessage.Of("support.server.matrix.impact-one-of", "Etki şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage UrgencyOneOf(string allowed) =>
            ServerMessage.Of("support.server.matrix.urgency-one-of", "Aciliyet şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage PriorityOneOf(string allowed) =>
            ServerMessage.Of("support.server.matrix.priority-one-of", "Öncelik şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage OneValuePerCell =>
            ServerMessage.Of("support.server.matrix.one-value-per-cell", "Her etki × aciliyet hücresi için tek değer verilebilir.");
    }
}
