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
}
