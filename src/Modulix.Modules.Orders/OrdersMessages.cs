using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Orders;

/// <summary>
/// Sipariş modülünün sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-6). Anahtarlar <c>orders.server.*</c>; Türkçe metin <c>tr.json</c>'daki
/// kalıpla aynıdır (birim testle denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler (alan biçimleri, izin, eşzamanlılık)
/// <see cref="ServerMessages"/>'tadır.
/// </summary>
internal static class OrdersMessages
{
    /// <summary>Kayıt alanı biçimi.</summary>
    public static class Field
    {
        public static ServerMessage SignedMoney =>
            ServerMessage.Of("orders.server.field.signed-money", "En fazla iki ondalıklı bir tutar girin.");
    }

    /// <summary>Bağlı kayıt alanlarında "bulunamadı" iletileri.</summary>
    public static class NotFound
    {
        public static ServerMessage Customer =>
            ServerMessage.Of("orders.server.notfound.customer", "Müşteri bulunamadı.");

        public static ServerMessage Person =>
            ServerMessage.Of("orders.server.notfound.person", "Kişi bulunamadı.");

        public static ServerMessage Order =>
            ServerMessage.Of("orders.server.notfound.order", "Sipariş bulunamadı.");

        public static ServerMessage OrderLine =>
            ServerMessage.Of("orders.server.notfound.order-line", "Sipariş kalemi bulunamadı.");

        public static ServerMessage Job =>
            ServerMessage.Of("orders.server.notfound.job", "İş bulunamadı.");

        public static ServerMessage Handover =>
            ServerMessage.Of("orders.server.notfound.handover", "Devir kaydı bulunamadı.");
    }

    /// <summary>Sipariş kaydı.</summary>
    public static class Order
    {
        public static ServerMessage FromQuoteOnly =>
            ServerMessage.Of("orders.server.order.from-quote-only", "Sipariş yalnızca kabul edilmiş tekliften oluşturulur.");

        public static ServerMessage FinalLocked =>
            ServerMessage.Of("orders.server.order.final-locked", "Sonuçlanmış sipariş değiştirilemez.");

        public static ServerMessage PersonMismatch =>
            ServerMessage.Of("orders.server.order.person-mismatch", "Kişi seçilen müşteriyle ilişkili değil.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("orders.server.order.no-delete", "Sipariş silinemez; geçmişi korunur. Vazgeçildiyse siparişi iptal edin.");
    }

    /// <summary>Sipariş kalemi.</summary>
    public static class Line
    {
        public static ServerMessage FromQuoteOnly =>
            ServerMessage.Of("orders.server.line.from-quote-only", "Sipariş kalemi yalnızca tekliften sipariş oluşturulurken eklenir.");

        public static ServerMessage FinalLocked =>
            ServerMessage.Of("orders.server.line.final-locked", "Sonuçlanmış siparişin kalemi değiştirilemez.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("orders.server.line.no-delete", "Sipariş kalemi silinemez; siparişi iptal edin.");
    }

    /// <summary>Sipariş değişiklik talebi.</summary>
    public static class Change
    {
        public static ServerMessage ViaActionOnly =>
            ServerMessage.Of("orders.server.change.via-action-only", "Değişiklik talebi yalnızca sipariş ekranındaki talep işlemiyle açılır.");

        public static ServerMessage StatusViaDecision =>
            ServerMessage.Of("orders.server.change.status-via-decision", "Değişiklik talebi yalnızca karar işlemleriyle değişir.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("orders.server.change.no-delete", "Değişiklik talebi silinemez; geçmişi korunur. Bekleyen talep geri çekilebilir.");
    }

    /// <summary>Teslimat.</summary>
    public static class Delivery
    {
        public static ServerMessage ViaPlanOnly =>
            ServerMessage.Of("orders.server.delivery.via-plan-only", "Teslimat yalnızca sipariş ekranındaki teslimat planlama işlemiyle oluşur.");

        public static ServerMessage PlannedOnly =>
            ServerMessage.Of("orders.server.delivery.planned-only", "Yalnızca planlı teslimatın planı ve notu düzenlenir; teslim edilen ya da sonuçlanan teslimat değişmez.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("orders.server.delivery.no-delete", "Teslimat silinemez; geçmişi korunur. Planlı teslimat iptal edilir, başarısız teslimat yeniden planlanır.");
    }

    /// <summary>Hizmet başlangıç kaydı.</summary>
    public static class ServiceStart
    {
        public static ServerMessage ViaPlanOnly =>
            ServerMessage.Of("orders.server.service-start.via-plan-only", "Hizmet başlangıç kaydı yalnızca sipariş ekranındaki planlama işlemiyle oluşur.");

        public static ServerMessage CompletedLocked =>
            ServerMessage.Of("orders.server.service-start.completed-locked", "Tamamlanmış hizmetin başlangıç kaydı değiştirilemez.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("orders.server.service-start.no-delete", "Hizmet başlangıç kaydı silinemez; geçmişi korunur.");
    }

    /// <summary>Operasyon işi ve görevleri.</summary>
    public static class Job
    {
        public static ServerMessage ViaHandoverOnly =>
            ServerMessage.Of("orders.server.job.via-handover-only", "Operasyon işi yalnızca devir kabulünden ya da sipariş ekranındaki iş açma işleminden oluşur.");

        public static ServerMessage ClosedLocked =>
            ServerMessage.Of("orders.server.job.closed-locked", "Tamamlanmış ya da iptal edilmiş iş değiştirilemez.");

        public static ServerMessage EndBeforeStart =>
            ServerMessage.Of("orders.server.job.end-before-start", "Planlanan bitiş başlangıçtan önce olamaz.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("orders.server.job.no-delete", "Operasyon işi silinemez; geçmişi korunur. Vazgeçildiyse işi iptal edin.");

        public static ServerMessage TasksClosedLocked =>
            ServerMessage.Of("orders.server.job.tasks-closed-locked", "Tamamlanmış ya da iptal edilmiş işin görevleri değiştirilemez.");

        public static ServerMessage TemplateTitleLocked =>
            ServerMessage.Of("orders.server.job.template-title-locked", "Şablondan gelen görevin başlığı değiştirilemez.");

        public static ServerMessage TemplateTaskNoDelete =>
            ServerMessage.Of("orders.server.job.template-task-no-delete", "Şablondan gelen görev silinemez; yalnızca tamamlandı olarak işaretlenir.");
    }

    /// <summary>Operasyon devri ve kontrol listesi.</summary>
    public static class Handover
    {
        public static ServerMessage AcceptedLocked =>
            ServerMessage.Of("orders.server.handover.accepted-locked", "Kabul edilen devir değiştirilemez; değişiklik için sipariş değişiklik talebi açın.");

        public static ServerMessage SubmittedLocked =>
            ServerMessage.Of("orders.server.handover.submitted-locked", "Operasyona gönderilen devir karara bağlanana kadar değiştirilemez.");

        public static ServerMessage OrderFinalLocked =>
            ServerMessage.Of("orders.server.handover.order-final-locked", "Sonuçlanmış siparişin devri değiştirilemez.");

        public static ServerMessage ViaPrepareOnly =>
            ServerMessage.Of("orders.server.handover.via-prepare-only", "Devir kaydı yalnızca sipariş ekranındaki devir hazırlama işlemiyle açılır.");

        public static ServerMessage TargetEndBeforeStart =>
            ServerMessage.Of("orders.server.handover.target-end-before-start", "Hedef bitiş tarihi başlangıçtan önce olamaz.");

        public static ServerMessage OrderFinalNoPrepare =>
            ServerMessage.Of("orders.server.handover.order-final-no-prepare", "Sonuçlanmış sipariş için devir hazırlanamaz.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("orders.server.handover.no-delete", "Devir kaydı silinemez; geçmişi korunur.");

        public static ServerMessage ItemTextLocked =>
            ServerMessage.Of("orders.server.handover.item-text-locked", "Varsayılan kontrol listesi maddesinin metni değiştirilemez.");

        public static ServerMessage ItemNoDelete =>
            ServerMessage.Of("orders.server.handover.item-no-delete", "Varsayılan kontrol listesi maddesi silinemez; yalnızca tamamlandı olarak işaretlenir.");
    }

    /// <summary>Eylem uçları için ortak iletiler.</summary>
    public static class Workflow
    {
        public static ServerMessage UnknownAction =>
            ServerMessage.Of("orders.server.workflow.unknown-action", "Bilinmeyen eylem.");

        public static ServerMessage NotNow =>
            ServerMessage.Of("orders.server.workflow.not-now", "Bu işlem şu an yapılamaz.");

        public static ServerMessage SelfApproval =>
            ServerMessage.Of("orders.server.workflow.self-approval", "Kendi onaya gönderdiğiniz siparişi onaylayamazsınız.");
    }

    /// <summary>Sipariş geçiş kuralları.</summary>
    public static class Transition
    {
        public static ServerMessage EditPending =>
            ServerMessage.Of("orders.server.transition.edit-pending", "Onay bekleyen siparişin ticari alanları değiştirilemez; önce taslağa geri alın.");

        public static ServerMessage EditApproved =>
            ServerMessage.Of("orders.server.transition.edit-approved", "Onaylanmış siparişin ticari alanları değiştirilemez; değişiklik talebi açın.");

        public static ServerMessage EditFinal =>
            ServerMessage.Of("orders.server.transition.edit-final", "Sonuçlanmış siparişin ticari alanları değiştirilemez.");

        public static ServerMessage NotDraft =>
            ServerMessage.Of("orders.server.transition.not-draft", "Yalnızca taslak sipariş onaya gönderilebilir.");

        public static ServerMessage NoLines =>
            ServerMessage.Of("orders.server.transition.no-lines", "Onaya göndermek için en az bir etkin kalem gerekir.");

        public static ServerMessage NotPending =>
            ServerMessage.Of("orders.server.transition.not-pending", "Yalnızca onay bekleyen sipariş için yapılabilir.");

        public static ServerMessage AlreadyCancelled =>
            ServerMessage.Of("orders.server.transition.already-cancelled", "Sipariş zaten iptal edilmiş.");

        public static ServerMessage ClosedNoCancel =>
            ServerMessage.Of("orders.server.transition.closed-no-cancel", "Kapatılmış sipariş iptal edilemez.");

        public static ServerMessage NotApproved =>
            ServerMessage.Of("orders.server.transition.not-approved", "Yalnızca onaylanmış sipariş kapatılabilir.");

        public static ServerMessage ObligationRemaining =>
            ServerMessage.Of("orders.server.transition.obligation-remaining", "Gerçekleşme tamamlanmadığı için sipariş kapatılamaz; kalan yükümlülük var. Yetkiniz varsa gerekçeyle zorla kapatabilirsiniz.");

        public static ServerMessage NothingRemaining =>
            ServerMessage.Of("orders.server.transition.nothing-remaining", "Gerçekleşme tamamlandı; zorla kapatma gerekmez, normal kapatın.");
    }

    /// <summary>Sipariş değişiklik talebi doğrulaması.</summary>
    public static class ChangeRule
    {
        public static ServerMessage OnlyApproved =>
            ServerMessage.Of("orders.server.change-rule.only-approved", "Değişiklik talebi yalnızca onaylanmış sipariş için açılır; taslak sipariş doğrudan düzenlenir.");

        public static ServerMessage NoDifference =>
            ServerMessage.Of("orders.server.change-rule.no-difference", "Mevcut siparişten farklı en az bir değişiklik gerekir.");

        public static ServerMessage FieldLocked =>
            ServerMessage.Of("orders.server.change-rule.field-locked", "Bu alan değişiklik talebiyle değiştirilemez.");

        public static ServerMessage TextTooLong =>
            ServerMessage.Of("orders.server.change-rule.text-too-long", "Metin çok uzun.");

        public static ServerMessage PlannedBeforeStart =>
            ServerMessage.Of("orders.server.change-rule.planned-before-start", "Planlanan teslim tarihi başlangıç tarihinden önce olamaz.");

        public static ServerMessage LineMissing =>
            ServerMessage.Of("orders.server.change-rule.line-missing", "Değişiklikteki kalemlerden biri bu siparişte yok.");

        public static ServerMessage LineDuplicate(string name) =>
            ServerMessage.Of("orders.server.change-rule.line-duplicate", "'{0}' birden çok kez verilemiş.", name);

        public static ServerMessage LineNegative(string name) =>
            ServerMessage.Of("orders.server.change-rule.line-negative", "'{0}' miktarı negatif olamaz.", name);

        public static ServerMessage LineIncrease(string name, string current) =>
            ServerMessage.Of("orders.server.change-rule.line-increase", "'{0}' miktarı artırılamaz (şu an {1}); ek kapsam yeni bir tekliften ayrı sipariş olarak açılır.", name, current);

        public static ServerMessage LineDelivered(string name, string delivered) =>
            ServerMessage.Of("orders.server.change-rule.line-delivered", "'{0}' için {1} zaten teslim edildi; miktar bunun altına indirilemez.", name, delivered);

        public static ServerMessage AllZero =>
            ServerMessage.Of("orders.server.change-rule.all-zero", "Tüm kalemler sıfırlanıyorsa değişiklik talebi yerine siparişi iptal edin.");
    }

    /// <summary>Teslimat ve hizmet başlangıcı kuralları.</summary>
    public static class FulfillmentRule
    {
        public static ServerMessage PlanNeedLine =>
            ServerMessage.Of("orders.server.fulfillment-rule.plan-need-line", "Teslimat için en az bir kalem ve miktar girin.");

        public static ServerMessage PlanDuplicate =>
            ServerMessage.Of("orders.server.fulfillment-rule.plan-duplicate", "Aynı kalem birden çok kez verilemez.");

        public static ServerMessage PlanLineMissing =>
            ServerMessage.Of("orders.server.fulfillment-rule.plan-line-missing", "Teslimattaki kalemlerden biri bu siparişte yok ya da iptal edilmiş.");

        public static ServerMessage PlanNotProduct(string name) =>
            ServerMessage.Of("orders.server.fulfillment-rule.plan-not-product", "'{0}' bir ürün kalemi değil; hizmet kalemleri başlangıç kaydıyla izlenir.", name);

        public static ServerMessage PlanQuantityPositive(string name) =>
            ServerMessage.Of("orders.server.fulfillment-rule.plan-quantity-positive", "'{0}' miktarı sıfırdan büyük olmalıdır.", name);

        public static ServerMessage PlanCapacity(string name, string max, string remaining, string planned) =>
            ServerMessage.Of("orders.server.fulfillment-rule.plan-capacity", "'{0}' için en fazla {1} planlanabilir (kalan {2}, başka teslimatlarda planlı {3}).", name, max, remaining, planned);

        public static ServerMessage DeliverPositive =>
            ServerMessage.Of("orders.server.fulfillment-rule.deliver-positive", "Teslim edilen miktar sıfırdan büyük olmalıdır; teslim edilemediyse teslimatı başarısız işaretleyin.");

        public static ServerMessage DeliverNegative(string name) =>
            ServerMessage.Of("orders.server.fulfillment-rule.deliver-negative", "'{0}' miktarı negatif olamaz.", name);

        public static ServerMessage DeliverExcess(string name, string remaining, string entered) =>
            ServerMessage.Of("orders.server.fulfillment-rule.deliver-excess", "'{0}' için fazla teslimat girilemez: kalan {1}, girilen {2}.", name, remaining, entered);

        public static ServerMessage OrderNotApproved =>
            ServerMessage.Of("orders.server.fulfillment-rule.order-not-approved", "Teslimat yalnızca onaylanmış siparişte kaydedilir.");

        public static ServerMessage NotPlanned =>
            ServerMessage.Of("orders.server.fulfillment-rule.not-planned", "Yalnızca planlı teslimat için yapılabilir.");

        public static ServerMessage NotFailed =>
            ServerMessage.Of("orders.server.fulfillment-rule.not-failed", "Yalnızca başarısız teslimat yeniden planlanır.");

        public static ServerMessage AlreadyRescheduled =>
            ServerMessage.Of("orders.server.fulfillment-rule.already-rescheduled", "Bu başarısız teslimat zaten yeniden planlanmış.");

        public static ServerMessage ServiceAlreadyStarted =>
            ServerMessage.Of("orders.server.fulfillment-rule.service-already-started", "Hizmet zaten başlamış.");

        public static ServerMessage ServiceAlreadyCompleted =>
            ServerMessage.Of("orders.server.fulfillment-rule.service-already-completed", "Hizmet zaten tamamlanmış.");

        public static ServerMessage ConfirmNeedsStarted =>
            ServerMessage.Of("orders.server.fulfillment-rule.confirm-needs-started", "Müşteri başlangıç onayı yalnızca başlamış hizmet için girilir.");

        public static ServerMessage CompleteBeforeStart =>
            ServerMessage.Of("orders.server.fulfillment-rule.complete-before-start", "Hizmet başlamadan tamamlanamaz.");

        public static ServerMessage StartBlocked(string first) =>
            ServerMessage.Of("orders.server.fulfillment-rule.start-blocked", "Başlangıç engelleniyor: {0}", first);

        public static ServerMessage StartBlockedMore(string first, int more) =>
            ServerMessage.Of("orders.server.fulfillment-rule.start-blocked-more", "Başlangıç engelleniyor: {0} (+{1} engel daha)", first, more);
    }

    /// <summary>Teslimat kanıtı dosyası.</summary>
    public static class Evidence
    {
        public static ServerMessage FileName =>
            ServerMessage.Of("orders.server.evidence.file-name", "Dosya adı geçersiz (en fazla 120 karakter, yol içeremez).");

        public static ServerMessage Empty =>
            ServerMessage.Of("orders.server.evidence.empty", "Dosya boş.");

        public static ServerMessage TooBig(int megabytes) =>
            ServerMessage.Of("orders.server.evidence.too-big", "Dosya en fazla {0} MB olabilir.", megabytes);

        public static ServerMessage Type =>
            ServerMessage.Of("orders.server.evidence.type", "Yalnızca PDF, PNG ve JPEG dosyaları yüklenebilir.");

        public static ServerMessage Mismatch =>
            ServerMessage.Of("orders.server.evidence.mismatch", "Dosya içeriği bildirilen türle uyuşmuyor.");
    }

    /// <summary>Devir geçiş kuralları.</summary>
    public static class HandoverRule
    {
        public static ServerMessage OrderNotApproved =>
            ServerMessage.Of("orders.server.handover-rule.order-not-approved", "Devir yalnızca onaylanmış siparişten operasyona gönderilebilir.");

        public static ServerMessage AlreadySent =>
            ServerMessage.Of("orders.server.handover-rule.already-sent", "Devir zaten operasyona gönderilmiş.");

        public static ServerMessage Accepted =>
            ServerMessage.Of("orders.server.handover-rule.accepted", "Devir operasyon tarafından kabul edilmiş.");

        public static ServerMessage NotSent =>
            ServerMessage.Of("orders.server.handover-rule.not-sent", "Yalnızca operasyona gönderilmiş ve henüz karara bağlanmamış devir için yapılabilir.");

        public static ServerMessage MissingInfo(string first) =>
            ServerMessage.Of("orders.server.handover-rule.missing-info", "Zorunlu bilgi ya da belgeler eksik: {0}", first);

        public static ServerMessage MissingInfoMore(string first, int more) =>
            ServerMessage.Of("orders.server.handover-rule.missing-info-more", "Zorunlu bilgi ya da belgeler eksik: {0} (+{1} eksik daha)", first, more);
    }

    /// <summary>Operasyon işi geçiş kuralları.</summary>
    public static class JobRule
    {
        public static ServerMessage AssignFinal =>
            ServerMessage.Of("orders.server.job-rule.assign-final", "Tamamlanmış ya da iptal edilmiş işe atama yapılamaz.");

        public static ServerMessage AlreadyRunning =>
            ServerMessage.Of("orders.server.job-rule.already-running", "İş zaten devam ediyor.");

        public static ServerMessage StartFinal =>
            ServerMessage.Of("orders.server.job-rule.start-final", "Tamamlanmış ya da iptal edilmiş iş başlatılamaz.");

        public static ServerMessage OrderNotApproved =>
            ServerMessage.Of("orders.server.job-rule.order-not-approved", "Sipariş onaylı olmadığı için iş başlatılamaz.");

        public static ServerMessage Unassigned =>
            ServerMessage.Of("orders.server.job-rule.unassigned", "Önce sorumlu kullanıcı ya da ekip atanmalı.");

        public static ServerMessage Dependency(int count) =>
            ServerMessage.Of("orders.server.job-rule.dependency", "Bağımlı olduğu {0} iş tamamlanmadan başlatılamaz.", count);

        public static ServerMessage NotInProgress =>
            ServerMessage.Of("orders.server.job-rule.not-in-progress", "Yalnızca devam eden iş tamamlanabilir.");

        public static ServerMessage TasksLeft(int count) =>
            ServerMessage.Of("orders.server.job-rule.tasks-left", "{0} zorunlu görev tamamlanmadan iş tamamlanamaz.", count);

        public static ServerMessage CancelFinal =>
            ServerMessage.Of("orders.server.job-rule.cancel-final", "Tamamlanmış ya da zaten iptal edilmiş iş iptal edilemez.");
    }

    /// <summary>Miktar iletisi bağımsız değişkeni: en çok dört ondalık, kültürden bağımsız.</summary>
    public static string Q(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
