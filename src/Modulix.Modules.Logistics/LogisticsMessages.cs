using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Logistics;

/// <summary>
/// Lojistik modülünün sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-8). Anahtarlar <c>logistics.server.*</c>; Türkçe metin <c>tr.json</c>'daki
/// kalıpla aynıdır (birim testle denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler (alan biçimleri, izin, eşzamanlılık)
/// <see cref="ServerMessages"/>'tadır.
/// </summary>
internal static class LogisticsMessages
{
    /// <summary>Kayıt alanı biçimi.</summary>
    public static class Field
    {
        public static ServerMessage MaxDecimals(int scale) =>
            ServerMessage.Of("logistics.server.field.max-decimals", "En fazla {0} ondalık basamak girilebilir.", scale);

        public static ServerMessage Moment =>
            ServerMessage.Of("logistics.server.field.moment", "Tarih ve saat ISO 8601 biçiminde olmalıdır (ör. 2026-11-12T08:30:00+03:00).");
    }

    /// <summary>Bağlı kayıt alanlarında "bulunamadı" iletileri.</summary>
    public static class NotFound
    {
        public static ServerMessage Carrier =>
            ServerMessage.Of("logistics.server.notfound.carrier", "Taşıyıcı firma bulunamadı.");

        public static ServerMessage Person =>
            ServerMessage.Of("logistics.server.notfound.person", "Kişi bulunamadı.");

        public static ServerMessage Customer =>
            ServerMessage.Of("logistics.server.notfound.customer", "Müşteri firma bulunamadı.");

        public static ServerMessage Sender =>
            ServerMessage.Of("logistics.server.notfound.sender", "Gönderen firma bulunamadı.");

        public static ServerMessage Receiver =>
            ServerMessage.Of("logistics.server.notfound.receiver", "Alıcı firma bulunamadı.");

        public static ServerMessage Request =>
            ServerMessage.Of("logistics.server.notfound.request", "Taşıma talebi bulunamadı.");

        public static ServerMessage Trip =>
            ServerMessage.Of("logistics.server.notfound.trip", "Sefer bulunamadı.");

        public static ServerMessage Supplier =>
            ServerMessage.Of("logistics.server.notfound.supplier", "Tedarikçi bulunamadı.");

        public static ServerMessage Driver =>
            ServerMessage.Of("logistics.server.notfound.driver", "Sürücü bulunamadı.");

        public static ServerMessage CoDriver =>
            ServerMessage.Of("logistics.server.notfound.co-driver", "İkinci sürücü bulunamadı.");

        public static ServerMessage Shipment =>
            ServerMessage.Of("logistics.server.notfound.shipment", "Sevkiyat bulunamadı.");

        public static ServerMessage Vehicle =>
            ServerMessage.Of("logistics.server.notfound.vehicle", "Araç bulunamadı.");
    }

    /// <summary>Taşıyıcı anlaşması ve taşıyıcı rolü.</summary>
    public static class Carrier
    {
        public static ServerMessage CurrencyFormat =>
            ServerMessage.Of("logistics.server.carrier.currency-format", "Üç harfli para birimi girin (ör. TRY).");

        public static ServerMessage ValidToBeforeFrom =>
            ServerMessage.Of("logistics.server.carrier.valid-to-before-from", "Bitiş tarihi başlangıç tarihinden önce olamaz.");

        public static ServerMessage RoleMissing =>
            ServerMessage.Of("logistics.server.carrier.role-missing", "Seçilen firma \"Taşıyıcı\" ilişki rolüne sahip değil; önce firma kaydından rolü ekleyin.");

        public static ServerMessage AgreementNoTaken =>
            ServerMessage.Of("logistics.server.carrier.agreement-no-taken", "Bu taşıyıcıda aynı numaralı başka bir anlaşma var.");

        public static ServerMessage Overlap(string agreement) =>
            ServerMessage.Of("logistics.server.carrier.overlap", "Bu taşıyıcının \"{0}\" anlaşması aynı tarihlerde geçerli; tarihleri değiştirin ya da diğer anlaşmayı pasife alın.", agreement);
    }

    /// <summary>Araç seçimi (Filo modülü).</summary>
    public static class Vehicle
    {
        public static ServerMessage FleetClosed =>
            ServerMessage.Of("logistics.server.vehicle.fleet-closed", "Filo modülü bu firmada açık değil; araç seçilemez.");

        public static ServerMessage NoTrailerAsDefault =>
            ServerMessage.Of("logistics.server.vehicle.no-trailer-as-default", "Dorse ve römork sürücüye varsayılan araç olarak atanamaz; çekici ya da kamyon seçin.");

        public static ServerMessage InactiveDriver =>
            ServerMessage.Of("logistics.server.vehicle.inactive-driver", "Pasif araç sürücüye atanamaz.");

        public static ServerMessage TowedOnly =>
            ServerMessage.Of("logistics.server.vehicle.towed-only", "Dorse alanına yalnızca dorse ya da römork seçilir.");

        public static ServerMessage NoTrailerAsTractor =>
            ServerMessage.Of("logistics.server.vehicle.no-trailer-as-tractor", "Dorse ve römork çekici olarak seçilemez; çekici ya da kamyon seçin.");

        public static ServerMessage InactiveTrip =>
            ServerMessage.Of("logistics.server.vehicle.inactive-trip", "Pasif araç sefere atanamaz.");
    }

    /// <summary>Sürücü kartı.</summary>
    public static class Driver
    {
        public static ServerMessage Duplicate =>
            ServerMessage.Of("logistics.server.driver.duplicate", "Bu kişinin zaten bir sürücü kartı var.");

        public static ServerMessage SubcontractNeedsCarrier =>
            ServerMessage.Of("logistics.server.driver.subcontract-needs-carrier", "Taşeron sürücüde taşıyıcı firma seçilmelidir.");

        public static ServerMessage CarrierOnlySubcontract =>
            ServerMessage.Of("logistics.server.driver.carrier-only-subcontract", "Taşıyıcı firma yalnızca taşeron sürücüde seçilir.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("logistics.server.driver.no-delete", "Sürücü kartları silinmez; çalışmayan sürücünün durumunu \"Pasif\" yapın.");
    }

    /// <summary>Taşıma talebi ve sevkiyat.</summary>
    public static class Cargo
    {
        public static ServerMessage DeliveryBeforePickup =>
            ServerMessage.Of("logistics.server.cargo.delivery-before-pickup", "Teslim günü yükleme gününden önce olamaz.");

        public static ServerMessage CountryFormat =>
            ServerMessage.Of("logistics.server.cargo.country-format", "Geçerli bir ülke kodu girin (ISO 3166-1 alfa-2, ör. TR, DE).");

        public static ServerMessage RequestLocked =>
            ServerMessage.Of("logistics.server.cargo.request-locked", "Onaylanan, reddedilen ya da iptal edilen talep değiştirilemez.");

        public static ServerMessage RequestNoDelete =>
            ServerMessage.Of("logistics.server.cargo.request-no-delete", "Taşıma talebi silinmez; gerekirse iptal edin.");

        public static ServerMessage ExternalRefExists =>
            ServerMessage.Of("logistics.server.cargo.external-ref-exists", "Bu referansla açılmış bir talep zaten var.");

        public static ServerMessage ShipmentLocked =>
            ServerMessage.Of("logistics.server.cargo.shipment-locked", "Yüklemesi başlamış, teslim edilmiş ya da iptal edilmiş sevkiyatın yük bilgisi değiştirilemez.");

        public static ServerMessage ShipmentNoDelete =>
            ServerMessage.Of("logistics.server.cargo.shipment-no-delete", "Sevkiyat silinmez; gerekirse iptal edin.");

        public static ServerMessage ShipmentViaRequest =>
            ServerMessage.Of("logistics.server.cargo.shipment-via-request", "Sevkiyat yalnızca taşıma talebi onaylanınca oluşur.");

        public static ServerMessage ShipmentExists =>
            ServerMessage.Of("logistics.server.cargo.shipment-exists", "Bu talep için sevkiyat zaten açılmış.");
    }

    /// <summary>Sefer masrafı.</summary>
    public static class Expense
    {
        public static ServerMessage ApprovedLocked =>
            ServerMessage.Of("logistics.server.expense.approved-locked", "Onaylanmış masraf değiştirilemez; hatalıysa yeni masraf girin.");

        public static ServerMessage CancelledNoAdd =>
            ServerMessage.Of("logistics.server.expense.cancelled-no-add", "İptal edilen sefere masraf eklenmez.");

        public static ServerMessage CancelledNoEdit =>
            ServerMessage.Of("logistics.server.expense.cancelled-no-edit", "İptal edilen seferin masrafı düzenlenmez; gerekirse masrafı reddedin.");

        public static ServerMessage AmountPositive =>
            ServerMessage.Of("logistics.server.expense.amount-positive", "Tutar sıfırdan büyük olmalıdır.");

        public static ServerMessage CurrencyRequired =>
            ServerMessage.Of("logistics.server.expense.currency-required", "Para birimi seçilmelidir.");

        public static ServerMessage VatExceeds =>
            ServerMessage.Of("logistics.server.expense.vat-exceeds", "KDV tutarı toplam tutarı aşamaz.");

        public static ServerMessage DateFuture =>
            ServerMessage.Of("logistics.server.expense.date-future", "Masraf tarihi ileri bir gün olamaz.");

        public static ServerMessage LitersRequired =>
            ServerMessage.Of("logistics.server.expense.liters-required", "Yakıt masrafında litre zorunludur.");

        public static ServerMessage LitersFuelOnly =>
            ServerMessage.Of("logistics.server.expense.liters-fuel-only", "Litre yalnızca yakıt masrafında girilir.");

        public static ServerMessage OdometerFuelOnly =>
            ServerMessage.Of("logistics.server.expense.odometer-fuel-only", "Kilometre yalnızca yakıt masrafında girilir.");

        public static ServerMessage ChargeFineOnly =>
            ServerMessage.Of("logistics.server.expense.charge-fine-only", "Sürücüye rücu yalnızca trafik cezasında işaretlenir.");

        public static ServerMessage TransferPendingOnly =>
            ServerMessage.Of("logistics.server.expense.transfer-pending-only", "Yalnızca beklemedeki masraf aktarılır; reddedilen masrafı önce yeniden onaya gönderin.");

        public static ServerMessage TransferCancelledOnly =>
            ServerMessage.Of("logistics.server.expense.transfer-cancelled-only", "Yalnızca iptal edilen seferin masrafı başka sefere aktarılır.");

        public static ServerMessage TransferTargetCancelled =>
            ServerMessage.Of("logistics.server.expense.transfer-target-cancelled", "İptal edilen sefere masraf aktarılmaz.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("logistics.server.expense.no-delete", "Masraf silinmez; hatalı masrafı reddedin.");
    }

    /// <summary>Sefer ve durakları.</summary>
    public static class Trip
    {
        public static ServerMessage PlannedOnlyEdit =>
            ServerMessage.Of("logistics.server.trip.planned-only-edit", "Yalnızca planlanan sefer düzenlenir; hazır seferi önce planlamaya geri alın.");

        public static ServerMessage PlannedEndBeforeStart =>
            ServerMessage.Of("logistics.server.trip.planned-end-before-start", "Bitiş günü başlangıç gününden önce olamaz.");

        public static ServerMessage CoDriverNeedsFirst =>
            ServerMessage.Of("logistics.server.trip.co-driver-needs-first", "Önce birinci sürücüyü seçin.");

        public static ServerMessage CoDriverSame =>
            ServerMessage.Of("logistics.server.trip.co-driver-same", "İkinci sürücü birinci sürücüyle aynı olamaz.");

        public static ServerMessage DriverInactive =>
            ServerMessage.Of("logistics.server.trip.driver-inactive", "Pasif sürücü sefere atanamaz.");

        public static ServerMessage SettlementCarrierOnly =>
            ServerMessage.Of("logistics.server.trip.settlement-carrier-only", "Hakediş yalnızca taşıyıcı firma seçili (taşeron) seferde girilir.");

        public static ServerMessage StopPlannedOnly =>
            ServerMessage.Of("logistics.server.trip.stop-planned-only", "Durak yalnızca planlanan sefere eklenir; hazır seferi önce planlamaya geri alın.");

        public static ServerMessage ArrivedWhenRunning =>
            ServerMessage.Of("logistics.server.trip.arrived-when-running", "Varış saati sefer yoldayken girilir.");

        public static ServerMessage StopsClosed =>
            ServerMessage.Of("logistics.server.trip.stops-closed", "Kapanan seferin durakları değiştirilemez.");

        public static ServerMessage StopPlanChange =>
            ServerMessage.Of("logistics.server.trip.stop-plan-change", "Durak planı yalnızca sefer planlanırken değişir; hazır seferi önce planlamaya geri alın.");

        public static ServerMessage ArrivalOnlyRunning =>
            ServerMessage.Of("logistics.server.trip.arrival-only-running", "Varış saati yalnızca yoldaki sefere girilir.");

        public static ServerMessage StopTypeLocked =>
            ServerMessage.Of("logistics.server.trip.stop-type-locked", "Sevkiyat durağının türü değiştirilemez.");

        public static ServerMessage ArrivalFuture =>
            ServerMessage.Of("logistics.server.trip.arrival-future", "Varış saati ileri bir zaman olamaz.");

        public static ServerMessage SequenceTaken =>
            ServerMessage.Of("logistics.server.trip.sequence-taken", "Bu sırada başka bir durak var; boş bir sıra numarası verin.");

        public static ServerMessage StopRemovePlanning =>
            ServerMessage.Of("logistics.server.trip.stop-remove-planning", "Durak yalnızca sefer planlanırken kaldırılır.");

        public static ServerMessage ShipmentStopNoRemove =>
            ServerMessage.Of("logistics.server.trip.shipment-stop-no-remove", "Sevkiyat durağı elle kaldırılmaz; sevkiyatı seferden çıkarın.");
    }

    /// <summary>Teslim kanıtı.</summary>
    public static class Proof
    {
        public static ServerMessage Immutable =>
            ServerMessage.Of("logistics.server.proof.immutable", "Teslim kanıtı değiştirilemez ve silinemez; düzeltme için yeni kanıt girin.");

        public static ServerMessage ViaDelivery =>
            ServerMessage.Of("logistics.server.proof.via-delivery", "Teslim kanıtı yalnızca sevkiyatın teslim komutuyla oluşur.");

        public static ServerMessage OnlyRunning =>
            ServerMessage.Of("logistics.server.proof.only-running", "Teslim kanıtı yalnızca yoldaki sevkiyata yazılır.");

        public static ServerMessage ReceivedByRequired =>
            ServerMessage.Of("logistics.server.proof.received-by-required", "Teslim alanın adı zorunludur.");

        public static ServerMessage NotesRequired =>
            ServerMessage.Of("logistics.server.proof.notes-required", "Kısmi teslimde neyin teslim edilmediğini açıklayın.");

        public static ServerMessage FailureReasonRequired =>
            ServerMessage.Of("logistics.server.proof.failure-reason-required", "Başarısız teslimin nedeni zorunludur.");

        public static ServerMessage OutcomeMismatch =>
            ServerMessage.Of("logistics.server.proof.outcome-mismatch", "Teslim edilemeyen sevkiyatta teslim alan, hasar ya da eksik işaretlenmez.");

        public static ServerMessage FailureReasonOnly =>
            ServerMessage.Of("logistics.server.proof.failure-reason-only", "Başarısız neden yalnızca teslim edilemeyen sevkiyatta girilir.");
    }

    /// <summary>Tutar ve para birimi çifti.</summary>
    public static class Money
    {
        public static ServerMessage CurrencyNeeded =>
            ServerMessage.Of("logistics.server.money.currency-needed", "Tutar girildiğinde para birimi seçilmelidir.");

        public static ServerMessage AmountNeeded =>
            ServerMessage.Of("logistics.server.money.amount-needed", "Para birimi seçildiğinde tutar girilmelidir.");

        public static ServerMessage CurrencyFormat =>
            ServerMessage.Of("logistics.server.money.currency-format", "Üç harfli para birimi kodu girin (ör. TRY, EUR, USD).");
    }

    /// <summary>Eylem uçları ve iş akışı.</summary>
    public static class Workflow
    {
        public static ServerMessage NotAllowed =>
            ServerMessage.Of("logistics.server.workflow.not-allowed", "Bu eylem için yetkiniz yok.");

        public static ServerMessage ReasonRequired =>
            ServerMessage.Of("logistics.server.workflow.reason-required", "Gerekçe zorunludur.");

        public static ServerMessage ReasonTooLong =>
            ServerMessage.Of("logistics.server.workflow.reason-too-long", "Gerekçe çok uzun.");

        public static ServerMessage ReasonMax(int max) =>
            ServerMessage.Of("logistics.server.workflow.reason-max", "En fazla {0} karakter olabilir.", max);

        public static ServerMessage RequestApprove =>
            ServerMessage.Of("logistics.server.workflow.request-approve", "Yalnızca yeni talep onaylanabilir.");

        public static ServerMessage RequestReject =>
            ServerMessage.Of("logistics.server.workflow.request-reject", "Yalnızca yeni talep reddedilebilir.");

        public static ServerMessage RequestCancel =>
            ServerMessage.Of("logistics.server.workflow.request-cancel", "Yalnızca yeni talep iptal edilebilir; onaylanan talebin sevkiyatını iptal edin.");

        public static ServerMessage ShipmentStartLoading =>
            ServerMessage.Of("logistics.server.workflow.shipment-start-loading", "Yükleme yalnızca planlanmış sevkiyatta başlar.");

        public static ServerMessage ShipmentDepart =>
            ServerMessage.Of("logistics.server.workflow.shipment-depart", "Yalnızca yüklenen sevkiyat yola çıkabilir.");

        public static ServerMessage ShipmentCancel =>
            ServerMessage.Of("logistics.server.workflow.shipment-cancel", "Yola çıkmış ya da kapanmış sevkiyat iptal edilemez.");

        public static ServerMessage TripConfirm =>
            ServerMessage.Of("logistics.server.workflow.trip-confirm", "Yalnızca planlanan sefer onaylanır.");

        public static ServerMessage TripReopen =>
            ServerMessage.Of("logistics.server.workflow.trip-reopen", "Yalnızca hazır sefer planlamaya geri alınır.");

        public static ServerMessage TripStart =>
            ServerMessage.Of("logistics.server.workflow.trip-start", "Sefere yalnızca planı onaylanmış (hazır) sefer çıkar.");

        public static ServerMessage TripComplete =>
            ServerMessage.Of("logistics.server.workflow.trip-complete", "Yalnızca yoldaki sefer tamamlanır.");

        public static ServerMessage TripCancel =>
            ServerMessage.Of("logistics.server.workflow.trip-cancel", "Yola çıkmış ya da kapanmış sefer iptal edilemez.");

        public static ServerMessage ExpenseApprove =>
            ServerMessage.Of("logistics.server.workflow.expense-approve", "Yalnızca beklemedeki masraf onaylanabilir.");

        public static ServerMessage ExpenseReject =>
            ServerMessage.Of("logistics.server.workflow.expense-reject", "Yalnızca beklemedeki masraf reddedilebilir.");

        public static ServerMessage ExpenseResubmit =>
            ServerMessage.Of("logistics.server.workflow.expense-resubmit", "Yalnızca reddedilen masraf yeniden onaya gönderilir.");

        public static ServerMessage StopsNotArrived =>
            ServerMessage.Of("logistics.server.workflow.stops-not-arrived", "Bütün duraklara varış saati girilmeden sefer tamamlanamaz.");

        public static ServerMessage LoadedShipments =>
            ServerMessage.Of("logistics.server.workflow.loaded-shipments", "Yüklemesi başlamış sevkiyatı olan sefer iptal edilemez.");

        public static ServerMessage TripNotReady(string issues) =>
            ServerMessage.Of("logistics.server.workflow.trip-not-ready", "Sefer hazır değil: {0}", issues);
    }

    /// <summary>Sefer planlama.</summary>
    public static class Planner
    {
        public static ServerMessage OnlyPendingShipment =>
            ServerMessage.Of("logistics.server.planner.only-pending-shipment", "Yalnızca planlama bekleyen sevkiyat sefere eklenir.");

        public static ServerMessage ShipmentNotOnTrip =>
            ServerMessage.Of("logistics.server.planner.shipment-not-on-trip", "Sevkiyat bu seferde değil.");

        public static ServerMessage ShipmentLoadedNoRemove =>
            ServerMessage.Of("logistics.server.planner.shipment-loaded-no-remove", "Yüklemesi başlamış ya da kapanmış sevkiyat seferden çıkarılamaz.");

        public static ServerMessage TripPlannedOnly =>
            ServerMessage.Of("logistics.server.planner.trip-planned-only", "Sevkiyat yalnızca planlanan seferde eklenir ya da çıkarılır; hazır seferi önce planlamaya geri alın.");
    }

    /// <summary>Sevkiyat teslimi.</summary>
    public static class Delivery
    {
        public static ServerMessage NotAllowed =>
            ServerMessage.Of("logistics.server.delivery.not-allowed", "Teslim kaydetmek için yetkiniz yok.");

        public static ServerMessage OutcomeUnknown =>
            ServerMessage.Of("logistics.server.delivery.outcome-unknown", "Bilinmeyen teslim sonucu.");

        public static ServerMessage OnlyInTransit =>
            ServerMessage.Of("logistics.server.delivery.only-in-transit", "Yalnızca yoldaki sevkiyat için teslim kaydedilir.");
    }

    /// <summary>Operasyon panosu.</summary>
    public static class Dashboard
    {
        public static ServerMessage Forbidden =>
            ServerMessage.Of("logistics.server.dashboard.forbidden", "Panoyu görmek için talep, sevkiyat, sefer ya da sürücü okuma izni gerekir.");
    }
}
