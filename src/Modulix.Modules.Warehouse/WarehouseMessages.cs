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

        public static ServerMessage Importer =>
            ServerMessage.Of("warehouse.server.notfound.importer", "İthalatçı bulunamadı.");

        public static ServerMessage Broker =>
            ServerMessage.Of("warehouse.server.notfound.broker", "Gümrük müşaviri bulunamadı.");

        public static ServerMessage Owner =>
            ServerMessage.Of("warehouse.server.notfound.owner", "Malik bulunamadı.");

        public static ServerMessage BillTo =>
            ServerMessage.Of("warehouse.server.notfound.bill-to", "Faturalanan taraf bulunamadı.");
    }

    /// <summary>Stok hareketi (defter).</summary>
    public static class Movement
    {
        public static ServerMessage Immutable =>
            ServerMessage.Of("warehouse.server.movement.immutable", "Stok hareketi değiştirilemez ya da silinemez (defter yalnızca eklenir). Düzeltmek için düzeltme türünde bir ters kayıt oluşturun.");

        public static ServerMessage IdempotentDuplicate(Guid id) =>
            ServerMessage.Of("warehouse.server.movement.idempotent-duplicate", "Bu anahtarla bir hareket zaten işlendi (hareket: {0}); tekrar kaydedilmedi.", id);

        public static ServerMessage CategoryMissing =>
            ServerMessage.Of("warehouse.server.movement.category-missing", "Hareket türünün kategorisi tanımlı değil.");

        public static ServerMessage ReversalInherited =>
            ServerMessage.Of("warehouse.server.movement.reversal-inherited", "Düzeltme hareketinde bu bilgi özgün hareketten gelir; verilmez.");

        public static ServerMessage ReversalTargetRequired =>
            ServerMessage.Of("warehouse.server.movement.reversal-target-required", "Düzeltme hareketi hangi hareketi geri aldığını belirtmelidir.");

        public static ServerMessage ReversalOfReversal =>
            ServerMessage.Of("warehouse.server.movement.reversal-of-reversal", "Bir düzeltme hareketi geri alınamaz; gerekirse yeni bir hareket kaydedin.");

        public static ServerMessage AlreadyReversed =>
            ServerMessage.Of("warehouse.server.movement.already-reversed", "Bu hareket zaten geri alınmış.");

        public static ServerMessage OnlyAdjustmentReverses =>
            ServerMessage.Of("warehouse.server.movement.only-adjustment-reverses", "Yalnızca düzeltme türündeki hareket başka bir hareketi geri alabilir.");

        public static ServerMessage QuantityPositive =>
            ServerMessage.Of("warehouse.server.movement.quantity-positive", "Miktar zorunludur ve sıfırdan büyük olmalıdır.");

        public static ServerMessage LotInactive =>
            ServerMessage.Of("warehouse.server.movement.lot-inactive", "Pasif parti yeni hareket alamaz.");

        public static ServerMessage TargetInactive =>
            ServerMessage.Of("warehouse.server.movement.target-inactive", "Pasif lokasyona stok girişi yapılamaz.");

        public static ServerMessage InboundNoSource =>
            ServerMessage.Of("warehouse.server.movement.shape.inbound-no-source", "Giriş hareketinde kaynak lokasyon verilmez.");

        public static ServerMessage InboundTargetRequired =>
            ServerMessage.Of("warehouse.server.movement.shape.inbound-target-required", "Giriş için hedef lokasyon zorunludur.");

        public static ServerMessage OutboundNoTarget =>
            ServerMessage.Of("warehouse.server.movement.shape.outbound-no-target", "Çıkış hareketinde hedef lokasyon verilmez.");

        public static ServerMessage OutboundSourceRequired =>
            ServerMessage.Of("warehouse.server.movement.shape.outbound-source-required", "Çıkış için kaynak lokasyon zorunludur.");

        public static ServerMessage TransferBothRequired =>
            ServerMessage.Of("warehouse.server.movement.shape.transfer-both-required", "Transfer için kaynak ve hedef lokasyon zorunludur.");

        public static ServerMessage TransferSame =>
            ServerMessage.Of("warehouse.server.movement.shape.transfer-same", "Transferde kaynak ve hedef lokasyon aynı olamaz.");

        public static ServerMessage NotDirect =>
            ServerMessage.Of("warehouse.server.movement.shape.not-direct", "Bu hareket türü doğrudan kaydedilemez.");
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

        public static ServerMessage StockCode =>
            ServerMessage.Of("warehouse.server.code.stock-code", "Kod yalnızca harf, rakam ve - _ . / karakterlerini içerebilir.");
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

        public static ServerMessage NoLevel =>
            ServerMessage.Of("warehouse.server.location.no-level", "Tesiste lokasyon seviyesi tanımlı değil.");

        public static ServerMessage DepthMissing(int depth) =>
            ServerMessage.Of("warehouse.server.location.depth-missing", "Tesiste {0}. seviye tanımlı değil.", depth);

        public static ServerMessage ValueLength(string name, int min, int max) =>
            ServerMessage.Of("warehouse.server.location.value-length", "{0} değeri {1} ile {2} karakter arasında olmalıdır.", name, min, max);

        public static ServerMessage ValueLetters(string name) =>
            ServerMessage.Of("warehouse.server.location.value-letters", "{0} değeri yalnızca harf içerebilir.", name);

        public static ServerMessage ValueDigits(string name) =>
            ServerMessage.Of("warehouse.server.location.value-digits", "{0} değeri yalnızca rakam içerebilir.", name);

        public static ServerMessage ValueAlphanumeric(string name) =>
            ServerMessage.Of("warehouse.server.location.value-alphanumeric", "{0} değeri yalnızca harf ve rakam içerebilir.", name);

        public static ServerMessage CodeExists(string code) =>
            ServerMessage.Of("warehouse.server.location.code-exists", "{0} kodlu lokasyon zaten var.", code);
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

    /// <summary>Stok kartı kayıtları.</summary>
    public static class StockItem
    {
        public static ServerMessage CodeTaken =>
            ServerMessage.Of("warehouse.server.stock-item.code-taken", "Bu kod başka bir stok kartında kullanılıyor.");
    }

    /// <summary>Parti kayıtları.</summary>
    public static class Lot
    {
        public static ServerMessage NoPattern =>
            ServerMessage.Of("warehouse.server.lot.no-pattern", "Parti numarası yalnızca harf, rakam ve - _ . / karakterlerini içerebilir.");

        public static ServerMessage NoTaken =>
            ServerMessage.Of("warehouse.server.lot.no-taken", "Bu parti numarası başka bir partide kullanılıyor.");

        public static ServerMessage HasMovements =>
            ServerMessage.Of("warehouse.server.lot.has-movements", "Partinin stok hareketleri var; hareket defteri silinmez. Partiyi pasife alın.");
    }

    /// <summary>Gümrük beyannamesi kayıtları.</summary>
    public static class Declaration
    {
        public static ServerMessage RegistrationNoPattern =>
            ServerMessage.Of("warehouse.server.declaration.registration-no-pattern", "Tescil numarası yalnızca harf, rakam ve - _ . / karakterlerini içerebilir.");

        public static ServerMessage RegistrationNoTaken =>
            ServerMessage.Of("warehouse.server.declaration.registration-no-taken", "Bu tescil numarası başka bir beyannamede kullanılıyor.");

        public static ServerMessage ExternalReferenceTaken =>
            ServerMessage.Of("warehouse.server.declaration.external-reference-taken", "Bu dış referans başka bir beyannamede kullanılıyor.");

        public static ServerMessage RegistrationNoRule(string violations, string? profileName, int? versionNumber) =>
            ServerMessage.Of("warehouse.server.declaration.registration-no-rule", "{0} (rejim profili: {1}, sürüm {2})", violations, profileName, versionNumber);

        public static ServerMessage HasLines =>
            ServerMessage.Of("warehouse.server.declaration.has-lines", "Beyannamenin satırları var; önce satırları silin.");
    }

    /// <summary>Beyanname satırı kayıtları.</summary>
    public static class DeclarationLine
    {
        public static ServerMessage GtipRequired =>
            ServerMessage.Of("warehouse.server.declaration-line.gtip-required", "GTİP zorunludur.");

        public static ServerMessage NetOverGross =>
            ServerMessage.Of("warehouse.server.declaration-line.net-over-gross", "Net kilo brüt kilodan büyük olamaz.");

        public static ServerMessage Cancelled =>
            ServerMessage.Of("warehouse.server.declaration-line.cancelled", "İptal edilmiş beyannamenin satırları değiştirilemez.");

        public static ServerMessage LineNoTaken =>
            ServerMessage.Of("warehouse.server.declaration-line.line-no-taken", "Bu satır numarası beyannamede zaten var.");

        public static ServerMessage CancelledDelete =>
            ServerMessage.Of("warehouse.server.declaration-line.cancelled-delete", "İptal edilmiş beyannamenin satırları silinemez.");
    }

    /// <summary>Beyanname ve vekâletteki taraf (ithalatçı, gümrük müşaviri) denetimleri.</summary>
    public static class Party
    {
        public static ServerMessage ImporterRole =>
            ServerMessage.Of("warehouse.server.party.importer-role", "Seçilen firma \"İthalatçı / malik\" ilişki rolüne sahip değil; önce firma kaydından rolü ekleyin.");

        public static ServerMessage BrokerRole =>
            ServerMessage.Of("warehouse.server.party.broker-role", "Seçilen firma \"Gümrük müşaviri\" ilişki rolüne sahip değil; önce firma kaydından rolü ekleyin.");

        public static ServerMessage SameParty =>
            ServerMessage.Of("warehouse.server.party.same-party", "İthalatçı ve gümrük müşaviri aynı firma olamaz.");
    }

    /// <summary>Vekâlet kayıtları.</summary>
    public static class PowerOfAttorney
    {
        public static ServerMessage EndBeforeStart =>
            ServerMessage.Of("warehouse.server.power-of-attorney.end-before-start", "Bitiş tarihi başlangıç tarihinden önce olamaz.");
    }

    /// <summary>Rejim-profil bağlama kayıtları.</summary>
    public static class RegimeBinding
    {
        public static ServerMessage AlreadyBound =>
            ServerMessage.Of("warehouse.server.regime-binding.already-bound", "Bu rejim zaten bir profile bağlı; mevcut bağlamayı düzenleyin.");
    }

    /// <summary>Gümrük uç noktası iletileri.</summary>
    public static class Customs
    {
        public static ServerMessage GtipEmpty =>
            ServerMessage.Of("warehouse.server.customs.gtip-empty", "GTİP kodu boş olamaz.");

        public static ServerMessage RegimeAndRegistrationRequired =>
            ServerMessage.Of("warehouse.server.customs.regime-registration-required", "Rejim ve tescil numarası gerekli.");
    }


    /// <summary>Depo panosu.</summary>
    public static class Dashboard
    {
        public static ServerMessage Period =>
            ServerMessage.Of("warehouse.server.dashboard.period", "Dönem 7, 14 ya da 30 gün olabilir.");
    }
}
