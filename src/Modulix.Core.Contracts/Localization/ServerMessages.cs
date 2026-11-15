namespace Modulix.Core.Contracts.Localization;

/// <summary>
/// Ortak sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18a): kayıt boru hattının, ortak alan doğrulamasının ve erişim
/// denetiminin iletileri. Türkçe metin <c>tr.json</c>'daki kalıpla aynıdır (birim testle denetlenir); HTTP sınırında
/// isteğin diline çevrilir. Modüle özgü iletiler kendi modülleriyle (dilim 18d+) taşınır.
/// </summary>
public static class ServerMessages
{
    /// <summary>Kayıt boru hattının iletileri.</summary>
    public static class Record
    {
        public static ServerMessage NoActiveTenant => ServerMessage.Of("server.record.no-active-tenant", "Aktif tenant yok.");

        public static ServerMessage UnknownEntity(string entityKey) =>
            ServerMessage.Of("server.record.unknown-entity", "Bilinmeyen kayıt türü: {0}", entityKey);

        public static ServerMessage FeatureNotLicensed =>
            ServerMessage.Of("server.record.feature-not-licensed", "Bu özellik tenant'ın lisansında yok.");

        public static ServerMessage PermissionDenied => ServerMessage.Of("server.record.permission-denied", "Bu işlem için yetkiniz yok.");

        public static ServerMessage NotFound => ServerMessage.Of("server.record.not-found", "Kayıt bulunamadı.");

        public static ServerMessage ValidationFailed => ServerMessage.Of("server.record.validation-failed", "Doğrulama hatası.");

        public static ServerMessage HiddenFieldDenied =>
            ServerMessage.Of("server.record.hidden-field-denied", "Görme yetkiniz olmayan bir alanı değiştiremezsiniz.");

        public static ServerMessage HiddenFieldMergeDenied =>
            ServerMessage.Of("server.record.hidden-field-merge-denied", "Görme yetkiniz olmayan alanları olan kayıtları birleştiremezsiniz.");

        public static ServerMessage OwnerOutOfScope =>
            ServerMessage.Of("server.record.owner-out-of-scope", "Bu kişiyi sorumlu atayamazsınız: kayıt kapsamınızın dışında.");

        public static ServerMessage TooManyFilters(int max) =>
            ServerMessage.Of("server.record.too-many-filters", "En fazla {0} filtre verilebilir.", max);

        public static ServerMessage FilterDenied => ServerMessage.Of("server.record.filter-denied", "Bu alana göre süzme yetkiniz yok.");

        public static ServerMessage SortDenied => ServerMessage.Of("server.record.sort-denied", "Bu alana göre sıralama yetkiniz yok.");

        public static ServerMessage IdRequired => ServerMessage.Of("server.record.id-required", "Güncellenecek kaydın kimliği zorunludur.");

        public static ServerMessage CompanyImmutable =>
            ServerMessage.Of("server.record.company-immutable", "Kaydın şirketi değiştirilemez; taşıma ayrı bir işlemdir.");

        public static ServerMessage CompanyRequired =>
            ServerMessage.Of("server.record.company-required", "Kayıt oluşturmak için erişebileceğiniz bir şirket seçili olmalı.");

        public static ServerMessage CompanyDenied => ServerMessage.Of("server.record.company-denied", "Bu şirkete erişiminiz yok.");

        public static ServerMessage AnonymousNotAllowed =>
            ServerMessage.Of("server.record.anonymous-not-allowed", "Bu kayıt türü anonim yazılamaz.");

        public static ServerMessage NotMergeable => ServerMessage.Of("server.record.not-mergeable", "Bu kayıt türü birleştirilemez.");

        public static ServerMessage MergeSelf => ServerMessage.Of("server.record.merge-self", "Kayıt kendisiyle birleştirilemez.");

        /// <summary>Eşzamanlılık: kayıt okunduktan sonra başkası değiştirdi.</summary>
        public static ServerMessage Concurrency =>
            ServerMessage.Of("server.record.concurrency", "Kayıt siz düzenlerken değişti. Güncel halini alıp tekrar deneyin.");

        public static ServerMessage DuplicateKey => ServerMessage.Of("server.record.duplicate-key", "Aynı anahtarla bir kayıt zaten var.");

        public static ServerMessage HasDependents =>
            ServerMessage.Of("server.record.has-dependents", "Bu kayda bağlı başka kayıtlar olduğu için işlem yapılamadı.");

        public static ServerMessage VersionRequiredTitle => ServerMessage.Of("server.record.version-required.title", "Kaydın sürümü gerekli.");

        public static ServerMessage VersionRequiredDetail =>
            ServerMessage.Of("server.record.version-required.detail", "Güncelleme ve silme isteğinde okuduğunuz kaydın version değeri gönderilmelidir.");
    }

    /// <summary>Alan doğrulamasının ortak iletileri (ortak kayıtlar, özel alanlar, firmanın zorunlu alanları).</summary>
    public static class Validation
    {
        public static ServerMessage Required => ServerMessage.Of("server.validation.required", "Bu alan zorunludur.");

        public static ServerMessage UnknownField => ServerMessage.Of("server.validation.unknown-field", "Bilinmeyen alan.");

        public static ServerMessage UnknownFilter => ServerMessage.Of("server.validation.unknown-filter", "Bilinmeyen filtre.");

        public static ServerMessage InvalidFilterValue => ServerMessage.Of("server.validation.invalid-filter-value", "Geçersiz filtre değeri.");

        public static ServerMessage Text => ServerMessage.Of("server.validation.text", "Metin olmalıdır.");

        public static ServerMessage MaxLength(int max) => ServerMessage.Of("server.validation.max-length", "En fazla {0} karakter olabilir.", max);

        public static ServerMessage Number => ServerMessage.Of("server.validation.number", "Sayı olmalıdır.");

        public static ServerMessage NumberTooLarge => ServerMessage.Of("server.validation.number-too-large", "Sayı çok büyük.");

        /// <summary>Aralık; sınırlar çağıranın biçimlediği metindir (ondalıkta değişmez kültür).</summary>
        public static ServerMessage Range(string min, string max) =>
            ServerMessage.Of("server.validation.range", "{0} ile {1} arasında olmalıdır.", min, max);

        public static ServerMessage Date => ServerMessage.Of("server.validation.date", "Tarih olmalıdır.");

        public static ServerMessage DateFormat => ServerMessage.Of("server.validation.date-format", "Tarih yyyy-AA-gg biçiminde olmalıdır.");

        public static ServerMessage YesNo => ServerMessage.Of("server.validation.yes-no", "Evet ya da hayır olmalıdır.");

        public static ServerMessage Choice => ServerMessage.Of("server.validation.choice", "Bir seçenek olmalıdır.");

        public static ServerMessage ChoiceList => ServerMessage.Of("server.validation.choice-list", "Seçenek listesi olmalıdır.");

        public static ServerMessage InvalidChoice => ServerMessage.Of("server.validation.invalid-choice", "Geçersiz seçenek.");

        public static ServerMessage RecordId => ServerMessage.Of("server.validation.record-id", "Geçerli bir kayıt kimliği olmalıdır.");

        public static ServerMessage Email => ServerMessage.Of("server.validation.email", "Geçerli bir e-posta adresi girin.");

        public static ServerMessage TextList => ServerMessage.Of("server.validation.text-list", "Metin listesi bekleniyor.");

        public static ServerMessage CustomObject => ServerMessage.Of("server.validation.custom-object", "Özel alanlar nesne olmalıdır.");

        public static ServerMessage UnknownFieldType => ServerMessage.Of("server.validation.unknown-field-type", "Bilinmeyen alan tipi.");
    }

    /// <summary>Uç nokta erişim denetiminin (izin, modül, lisans, tenant) iletileri.</summary>
    public static class Access
    {
        public static ServerMessage ForbiddenTitle => ServerMessage.Of("server.access.forbidden", "Yetkiniz yok.");

        public static ServerMessage PermissionRequired(string permission) =>
            ServerMessage.Of("server.access.permission-required", "Bu işlem için '{0}' izni gerekir.", permission);

        public static ServerMessage AnyPermissionRequired(string permissions) =>
            ServerMessage.Of("server.access.any-permission-required", "Bu işlem için şu izinlerden biri gerekir: {0}.", permissions);

        public static ServerMessage ModuleDisabled(string moduleKey) =>
            ServerMessage.Of("server.access.module-disabled", "'{0}' modülü bu tenant için açık değil.", moduleKey);

        /// <summary>Lisans süresi ve ek süre bitti (ADR-0028): modül salt okunur, yazma 403.</summary>
        public static ServerMessage ModuleReadOnly(string moduleKey) =>
            ServerMessage.Of("server.access.module-read-only", "'{0}' modülünün lisans süresi doldu; modül salt okunur. Yöneticinizle iletişime geçin.", moduleKey);

        public static ServerMessage NoActiveTenantDetail =>
            ServerMessage.Of("server.access.no-active-tenant.detail", "Bir tenant seçin. Üyeliğiniz kaldırıldıysa bu tenant'a artık erişemezsiniz.");

        public static ServerMessage TwoFactorRequiredTitle =>
            ServerMessage.Of("server.access.two-factor-required.title", "Bu firma iki adımlı doğrulama istiyor.");

        public static ServerMessage TwoFactorRequiredDetail =>
            ServerMessage.Of("server.access.two-factor-required.detail", "Devam etmeden önce Hesabım sayfasından iki adımlı doğrulamayı açın.");
    }
}
