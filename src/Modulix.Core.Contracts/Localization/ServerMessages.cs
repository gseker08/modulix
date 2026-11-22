namespace Modulix.Core.Contracts.Localization;

/// <summary>
/// Ortak sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18a): kayıt boru hattının, ortak alan doğrulamasının ve erişim
/// denetiminin iletileri. Türkçe metin <c>tr.json</c>'daki kalıpla aynıdır (birim testle denetlenir); HTTP sınırında
/// isteğin diline çevrilir. Modüle özgü iletiler kendi modülleriyle (dilim 18d+) taşınır.
/// </summary>
public static class ServerMessages
{
    /// <summary>Döviz kuru kaydının iletileri (ADR-0049 dilim 5a).</summary>
    public static class ExchangeRate
    {
        public static ServerMessage CurrenciesSame => ServerMessage.Of("server.exchange-rate.currencies-same", "Kaynak ve hedef para birimi aynı olamaz.");

        public static ServerMessage RatePositive => ServerMessage.Of("server.exchange-rate.rate-positive", "Kur sıfırdan büyük olmalıdır.");

        public static ServerMessage Exists =>
            ServerMessage.Of("server.exchange-rate.exists", "Bu para birimi çifti için o güne ait kur zaten var.");
    }

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

        /// <summary>Eşzamanlılık: okunan sürüm güncel değil (sürümler iletide; modüllerin kayıt depoları).</summary>
        public static ServerMessage ConcurrencyVersion(long expected, long current) =>
            ServerMessage.Of("server.record.concurrency-version",
                "Kayıt siz düzenlerken değişti (beklenen sürüm {0}, güncel sürüm {1}). Güncel halini alıp tekrar deneyin.", expected, current);

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

        // Modüllerin kayıt alanı biçim iletileri (her modülün RecordField'ı; dilim 18d). Modüller birbirine başvuramadığı için ortak
        // biçim iletileri burada tek kopyadır.

        public static ServerMessage NotEmpty => ServerMessage.Of("server.validation.not-empty", "Bu alan boş bırakılamaz.");

        public static ServerMessage CodeNameList =>
            ServerMessage.Of("server.validation.code-name-list", "Kod ve ad çiftlerinden oluşan liste bekleniyor.");

        public static ServerMessage Money =>
            ServerMessage.Of("server.validation.money", "0 veya daha büyük, en fazla iki ondalıklı bir tutar girin.");

        public static ServerMessage PositiveDecimal =>
            ServerMessage.Of("server.validation.positive-decimal", "Sıfırdan büyük, en fazla on ondalıklı bir sayı girin.");

        public static ServerMessage Rate =>
            ServerMessage.Of("server.validation.rate", "0 ile 100 arasında, en fazla dört ondalıklı bir oran girin.");

        public static ServerMessage Quantity =>
            ServerMessage.Of("server.validation.quantity", "0 veya daha büyük, en fazla altı ondalıklı bir sayı girin.");

        public static ServerMessage Currency => ServerMessage.Of("server.validation.currency", "Üç harfli para birimi kodu girin (ör. TRY).");

        public static ServerMessage Time => ServerMessage.Of("server.validation.time", "Zaman ISO 8601 biçiminde olmalıdır.");

        public static ServerMessage NotInList => ServerMessage.Of("server.validation.not-in-list", "Listede olmayan değer.");

        public static ServerMessage Retired => ServerMessage.Of("server.validation.retired", "Bu değer artık kullanılmıyor.");

        public static ServerMessage Percent => ServerMessage.Of("server.validation.percent", "0 ile 100 arasında bir tam sayı girin.");

        public static ServerMessage Minutes(int max) => ServerMessage.Of("server.validation.minutes", "1 ile {0} arasında dakika girin.", max);

        public static ServerMessage IntegerRange(int min, int max) =>
            ServerMessage.Of("server.validation.integer-range", "{0} ile {1} arasında bir tam sayı girin.", min, max);

        public static ServerMessage NonNegativeInteger =>
            ServerMessage.Of("server.validation.non-negative-integer", "0 veya daha büyük bir tam sayı girin.");

        public static ServerMessage PartyKind => ServerMessage.Of("server.validation.party-kind", "Organization veya Individual olmalıdır.");

        public static ServerMessage Flag => ServerMessage.Of("server.validation.flag", "true veya false olmalıdır.");

        public static ServerMessage KeyList => ServerMessage.Of("server.validation.key-list", "Metin listesi olmalıdır.");

        public static ServerMessage InvalidValue(string value) => ServerMessage.Of("server.validation.invalid-value", "Geçersiz değer: {0}.", value);

        public static ServerMessage TooManyValues => ServerMessage.Of("server.validation.too-many-values", "Çok fazla değer seçildi.");

        public static ServerMessage OneOf(string allowed) => ServerMessage.Of("server.validation.one-of", "Şunlardan biri olmalıdır: {0}.", allowed);

        public static ServerMessage Phone => ServerMessage.Of("server.validation.phone", "Geçerli bir telefon numarası girin.");

        public static ServerMessage Country => ServerMessage.Of("server.validation.country", "İki harfli ülke kodu girin (ör. TR).");

        public static ServerMessage Url => ServerMessage.Of("server.validation.url", "Geçerli bir web adresi girin.");

        public static ServerMessage CreateOnly => ServerMessage.Of("server.validation.create-only", "Oluşturulduktan sonra değiştirilemez.");

        public static ServerMessage InvalidSort => ServerMessage.Of("server.validation.invalid-sort", "Geçersiz sıralama.");

        public static ServerMessage NotMember => ServerMessage.Of("server.validation.not-member", "Kullanıcı bu kuruluşun üyesi değil.");

        public static ServerMessage EndBeforeStart => ServerMessage.Of("server.validation.end-before-start", "Bitiş tarihi başlangıçtan önce olamaz.");
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
