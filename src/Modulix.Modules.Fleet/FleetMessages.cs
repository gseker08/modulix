using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Fleet;

/// <summary>
/// Filo modülünün sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-4). Anahtarlar <c>fleet.server.*</c>; Türkçe metin <c>tr.json</c>'daki kalıpla
/// aynıdır (birim testle denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler (alan biçimleri, izin, eşzamanlılık) <see cref="ServerMessages"/>'tadır.
/// </summary>
internal static class FleetMessages
{
    /// <summary>Kayıt alanı biçimi.</summary>
    public static class Field
    {
        public static ServerMessage MaxDecimals(int scale) =>
            ServerMessage.Of("fleet.server.field.max-decimals", "En fazla {0} ondalık basamak girilebilir.", scale);
    }

    /// <summary>Bağlı kayıt alanlarında "bulunamadı" iletileri.</summary>
    public static class NotFound
    {
        public static ServerMessage Vehicle =>
            ServerMessage.Of("fleet.server.notfound.vehicle", "Araç bulunamadı.");
    }

    /// <summary>Bakım kayıtları.</summary>
    public static class Maintenance
    {
        public static ServerMessage PerformedInFuture =>
            ServerMessage.Of("fleet.server.maintenance.performed-in-future", "Bakım tarihi ileri bir gün olamaz; planlanan bakımı \"Sonraki bakım tarihi\" alanına yazın.");

        public static ServerMessage NextDueDateOrder =>
            ServerMessage.Of("fleet.server.maintenance.next-due-date-order", "Sonraki bakım tarihi bakım tarihinden sonra olmalı.");

        public static ServerMessage NextDueKmOrder =>
            ServerMessage.Of("fleet.server.maintenance.next-due-km-order", "Sonraki bakım kilometresi bakım kilometresinden büyük olmalı.");

        public static ServerMessage CurrencyRequired =>
            ServerMessage.Of("fleet.server.maintenance.currency-required", "Tutar girildiyse para birimi zorunludur.");
    }

    /// <summary>Araç kayıtları.</summary>
    public static class Vehicle
    {
        public static ServerMessage PlateFormat =>
            ServerMessage.Of("fleet.server.vehicle.plate-format", "Plaka yalnızca harf ve rakam içerebilir (boşluklar kaldırılır).");

        public static ServerMessage PlateTaken =>
            ServerMessage.Of("fleet.server.vehicle.plate-taken", "Bu plaka başka bir araçta kayıtlı.");

        public static ServerMessage VinFormat =>
            ServerMessage.Of("fleet.server.vehicle.vin-format", "Şasi numarası 17 karakter olmalı; I, O ve Q harflerini içermez.");

        public static ServerMessage VinTaken =>
            ServerMessage.Of("fleet.server.vehicle.vin-taken", "Bu şasi numarası başka bir araçta kayıtlı.");

        public static ServerMessage CarrierRequired =>
            ServerMessage.Of("fleet.server.vehicle.carrier-required", "Taşeron araçta taşıyıcı firma seçilmelidir.");

        public static ServerMessage CarrierOnlySubcontract =>
            ServerMessage.Of("fleet.server.vehicle.carrier-only-subcontract", "Taşıyıcı firma yalnızca taşeron araçta seçilir.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("fleet.server.vehicle.no-delete", "Araçlar silinmez; kullanım dışı aracın durumunu \"Pasif\" yapın.");
    }
}
