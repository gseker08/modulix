using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Warehouse;

/// <summary>
/// Depo ve Antrepo modülünün sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-4). Anahtarlar <c>warehouse.server.*</c>; Türkçe metin <c>tr.json</c>'daki
/// kalıpla aynıdır (birim testle denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler (alan biçimleri, izin, eşzamanlılık) <see cref="ServerMessages"/>'tadır.
/// </summary>
internal static class WarehouseMessages
{
    /// <summary>Kayıt alanı biçimi.</summary>
    public static class Field
    {
        public static ServerMessage MaxDecimals(int scale) =>
            ServerMessage.Of("warehouse.server.field.max-decimals", "En fazla {0} ondalık basamak girilebilir.", scale);
    }

    /// <summary>Bağlı kayıt alanlarında "bulunamadı" iletileri.</summary>
    public static class NotFound
    {
        public static ServerMessage StockItem =>
            ServerMessage.Of("warehouse.server.notfound.stock-item", "Stok kartı bulunamadı.");

        public static ServerMessage DeclarationLine =>
            ServerMessage.Of("warehouse.server.notfound.declaration-line", "Beyanname satırı bulunamadı.");

        public static ServerMessage Declaration =>
            ServerMessage.Of("warehouse.server.notfound.declaration", "Beyanname bulunamadı.");

        public static ServerMessage Facility =>
            ServerMessage.Of("warehouse.server.notfound.facility", "Tesis bulunamadı.");

        public static ServerMessage ParentLocation =>
            ServerMessage.Of("warehouse.server.notfound.parent-location", "Üst lokasyon bulunamadı.");

        public static ServerMessage Zone =>
            ServerMessage.Of("warehouse.server.notfound.zone", "Alan bulunamadı.");

        public static ServerMessage RegimeProfile =>
            ServerMessage.Of("warehouse.server.notfound.regime-profile", "Rejim profili bulunamadı ya da yayında değil.");

        public static ServerMessage Lot =>
            ServerMessage.Of("warehouse.server.notfound.lot", "Parti bulunamadı.");

        public static ServerMessage SourceLocation =>
            ServerMessage.Of("warehouse.server.notfound.source-location", "Kaynak lokasyon bulunamadı.");

        public static ServerMessage TargetLocation =>
            ServerMessage.Of("warehouse.server.notfound.target-location", "Hedef lokasyon bulunamadı.");

        public static ServerMessage ReversedMovement =>
            ServerMessage.Of("warehouse.server.notfound.reversed-movement", "Geri alınacak hareket bulunamadı.");
    }

    /// <summary>Stok hareketi (defter).</summary>
    public static class Movement
    {
        public static ServerMessage Immutable =>
            ServerMessage.Of("warehouse.server.movement.immutable", "Stok hareketi değiştirilemez ya da silinemez (defter yalnızca eklenir). Düzeltmek için düzeltme türünde bir ters kayıt oluşturun.");
    }

    /// <summary>Stok bakiyesi (türetilmiş kayıt).</summary>
    public static class Balance
    {
        public static ServerMessage Derived =>
            ServerMessage.Of("warehouse.server.balance.derived", "Bakiye stok hareket defterinden türetilir; doğrudan değiştirilemez. Stok hareketi kaydedin.");
    }

    /// <summary>Kod biçimi.</summary>
    public static class Code
    {
        public static ServerMessage LettersDigitsUnderscore =>
            ServerMessage.Of("warehouse.server.code.letters-digits-underscore", "Kod yalnızca harf, rakam ve alt çizgi içerebilir.");
    }

    /// <summary>Tesis kayıtları.</summary>
    public static class Facility
    {
        public static ServerMessage CodeTaken =>
            ServerMessage.Of("warehouse.server.facility.code-taken", "Bu kod başka bir tesiste kullanılıyor.");

        public static ServerMessage CustomsInUse =>
            ServerMessage.Of("warehouse.server.facility.customs-in-use", "Gümrük denetimli alanı olan tesiste gümrük uzantısı kapatılamaz.");

        public static ServerMessage MixedInUse =>
            ServerMessage.Of("warehouse.server.facility.mixed-in-use", "Tesiste serbest ve gümrük denetimli alanlar birlikte duruyor; önce birini kaldırın.");

        public static ServerMessage HasChildren =>
            ServerMessage.Of("warehouse.server.facility.has-children", "Tesise bağlı alanlar veya lokasyon seviyeleri var; önce onları silin.");
    }

    /// <summary>Alan kayıtları.</summary>
    public static class Zone
    {
        public static ServerMessage CodeTaken =>
            ServerMessage.Of("warehouse.server.zone.code-taken", "Bu kod tesisteki başka bir alanda kullanılıyor.");

        public static ServerMessage KindCategoryMissing =>
            ServerMessage.Of("warehouse.server.zone.kind-category-missing", "Alan türünün kategorisi tanımlı değil.");

        public static ServerMessage CustomsRequired =>
            ServerMessage.Of("warehouse.server.zone.customs-required", "Gümrük denetimli alan yalnızca gümrük uzantısı açık tesiste tanımlanabilir.");

        public static ServerMessage MixedNotAllowed =>
            ServerMessage.Of("warehouse.server.zone.mixed-not-allowed", "Bu tesiste serbest ve gümrük denetimli alanlar birlikte bulunamaz (tesis ayarı kapalı).");

        public static ServerMessage HasLocations =>
            ServerMessage.Of("warehouse.server.zone.has-locations", "Alana bağlı lokasyonlar var; önce lokasyonları başka alana taşıyın ya da silin.");
    }

    /// <summary>Lokasyon kayıtları.</summary>
    public static class Location
    {
        public static ServerMessage ParentOtherFacility =>
            ServerMessage.Of("warehouse.server.location.parent-other-facility", "Üst lokasyon başka bir tesise ait.");

        public static ServerMessage ZoneDiffersFromParent =>
            ServerMessage.Of("warehouse.server.location.zone-differs-from-parent", "Alt lokasyon, üst lokasyonun alanından farklı bir alanda olamaz.");

        public static ServerMessage ZoneOtherFacility =>
            ServerMessage.Of("warehouse.server.location.zone-other-facility", "Alan bu tesise ait değil.");

        public static ServerMessage ZoneLockedByChildren =>
            ServerMessage.Of("warehouse.server.location.zone-locked-by-children", "Alt lokasyonları olan lokasyonun alanı değiştirilemez.");

        public static ServerMessage HasChildren =>
            ServerMessage.Of("warehouse.server.location.has-children", "Alt lokasyonları olan lokasyon silinemez; önce alt lokasyonları silin.");
    }

    /// <summary>Lokasyon seviyesi kayıtları.</summary>
    public static class Level
    {
        public static ServerMessage KeyFormat =>
            ServerMessage.Of("warehouse.server.level.key-format", "Anahtar küçük harfle başlamalı; yalnızca harf, rakam ve alt çizgi içerebilir.");

        public static ServerMessage MinLengthOrder =>
            ServerMessage.Of("warehouse.server.level.min-length-order", "En az uzunluk, en çok uzunluktan büyük olamaz.");

        public static ServerMessage SeparatorChars(string chars) =>
            ServerMessage.Of("warehouse.server.level.separator-chars", "Ayraç yalnızca şu karakterlerden oluşabilir: {0}", chars);

        public static ServerMessage PositionTaken =>
            ServerMessage.Of("warehouse.server.level.position-taken", "Bu sırada başka bir seviye var.");

        public static ServerMessage KeyTaken =>
            ServerMessage.Of("warehouse.server.level.key-taken", "Bu anahtar tesisteki başka bir seviyede kullanılıyor.");

        public static ServerMessage AppendOnly =>
            ServerMessage.Of("warehouse.server.level.append-only", "Lokasyonu olan tesiste yeni seviye yalnızca mevcut seviyelerden sonra eklenebilir.");

        public static ServerMessage NameOnly =>
            ServerMessage.Of("warehouse.server.level.name-only", "Lokasyonu olan tesiste seviye tanımı değiştirilemez; yalnızca ad değişir.");

        public static ServerMessage InUse =>
            ServerMessage.Of("warehouse.server.level.in-use", "Bu seviyeyi kullanan lokasyonlar var; önce lokasyonları silin.");
    }
}
