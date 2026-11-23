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
        public static ServerMessage NoteTooLong(int max) =>
            ServerMessage.Of("server.exchange-rate.note-too-long", "Not en çok {0} karakter olabilir.", max);

        public static ServerMessage SourceOneOf(string sources) =>
            ServerMessage.Of("server.exchange-rate.source-one-of", "Kaynak şunlardan biri olmalıdır: {0}.", sources);

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

    /// <summary>İş takvimi tanımının doğrulama iletileri (<c>CalendarSpecParser</c>; SLA, izin ve onay takvimleri ortak kullanır).</summary>
    public static class Calendar
    {
        public static ServerMessage TimeZone =>
            ServerMessage.Of("server.calendar.time-zone", "Geçerli bir saat dilimi kimliği girin (ör. Europe/Istanbul).");

        public static ServerMessage WeeklyWindow =>
            ServerMessage.Of("server.calendar.weekly-window", "Her çalışma penceresi için gün (0=Pazar … 6=Cumartesi), başlangıç ve daha geç bir bitiş (SS:dd) verin.");

        public static ServerMessage WeeklyJson =>
            ServerMessage.Of("server.calendar.weekly-json", "Çalışma saatleri geçerli bir JSON listesi olmalıdır.");

        public static ServerMessage WeeklyOverlap =>
            ServerMessage.Of("server.calendar.weekly-overlap", "Aynı gündeki çalışma pencereleri çakışamaz.");

        public static ServerMessage WeeklyRequired =>
            ServerMessage.Of("server.calendar.weekly-required", "7/24 değilse en az bir çalışma penceresi gerekir.");

        public static ServerMessage HolidayEntry =>
            ServerMessage.Of("server.calendar.holiday-entry", "Her tatil için tarih (yyyy-MM-dd) verin; yarım gün için başlangıç ve daha geç bir bitiş (SS:dd) gerekir.");

        public static ServerMessage HolidaysJson =>
            ServerMessage.Of("server.calendar.holidays-json", "Tatiller geçerli bir JSON listesi olmalıdır.");

        public static ServerMessage HolidayDuplicate =>
            ServerMessage.Of("server.calendar.holiday-duplicate", "Aynı tarih için tek tatil kaydı olabilir.");
    }

    public static class FieldRule
    {
        public static ServerMessage ReadOnlyNow =>
            ServerMessage.Of("server.field-rule.read-only-now", "Bu alan şu an salt okunur; değiştirilemez.");

        public static ServerMessage ApprovalNeeded(string detail) =>
            ServerMessage.Of("server.field-rule.approval-needed", "Onay gerekir (onay akışı henüz yok): {0}", detail);

        public static ServerMessage RequiredInState =>
            ServerMessage.Of("server.field-rule.required-in-state", "Bu alan, kayıt bu durumdayken zorunludur.");
    }

    public static class Automation
    {
        public static ServerMessage EntityNotRuleable =>
            ServerMessage.Of("server.automation.entity-not-ruleable", "Kayıt türü kural yazmaya açık değil ya da erişiminiz yok.");
    }

    public static class Import
    {
        public static ServerMessage DesignChanged =>
            ServerMessage.Of("server.import.design-changed", "Tasarım siz düzenlerken değişti. Sayfayı yenileyip tekrar deneyin.");

        public static ServerMessage DesignInvalid =>
            ServerMessage.Of("server.import.design-invalid", "İçe aktarma tasarımı geçersiz.");

        public static ServerMessage EntityNotFound =>
            ServerMessage.Of("server.import.entity-not-found", "Kayıt türü bulunamadı.");

        public static ServerMessage RunNotFound =>
            ServerMessage.Of("server.import.run-not-found", "İçe aktarma bulunamadı.");

        public static ServerMessage RollbackReasonField(int max) =>
            ServerMessage.Of("server.import.rollback-reason-field", "Gerekçe zorunludur (en çok {0} karakter).", max);

        public static ServerMessage RollbackReasonRequired =>
            ServerMessage.Of("server.import.rollback-reason-required", "Geri alma gerekçesi zorunludur.");

        public static ServerMessage AliasDuplicated(string alias) =>
            ServerMessage.Of("server.import.alias-duplicated", "'{0}' birden çok sütuna eşlenmiş.", alias);

        public static ServerMessage AliasTooLong(int max) =>
            ServerMessage.Of("server.import.alias-too-long", "Başlık adı en çok {0} karakter olabilir.", max);

        public static ServerMessage TooManyAliases(int max) =>
            ServerMessage.Of("server.import.too-many-aliases", "Bir sütun için en çok {0} başlık adı kaydedilebilir.", max);

        public static ServerMessage OmittedAndRequired(string label) =>
            ServerMessage.Of("server.import.omitted-and-required", "{0} hem çıkarılmış hem zorunlu olamaz.", label);

        public static ServerMessage RequiredColumn(string label) =>
            ServerMessage.Of("server.import.required-column", "{0} zorunlu sütundur, çıkarılamaz.", label);

        public static ServerMessage UnknownColumn =>
            ServerMessage.Of("server.import.unknown-column", "Bilinmeyen sütun.");
    }

    public static class Form
    {
        public static ServerMessage LayoutInvalid =>
            ServerMessage.Of("server.form.layout-invalid", "Form düzeni geçersiz.");

        public static ServerMessage NotFound =>
            ServerMessage.Of("server.form.not-found", "Form bulunamadı.");
    }

    public static class View
    {
        public static ServerMessage NameExists =>
            ServerMessage.Of("server.view.name-exists", "Bu adla bir görünümünüz zaten var.");

        public static ServerMessage NotMovable =>
            ServerMessage.Of("server.view.not-movable", "Görünüm başka bir ekrana taşınamaz.");

        public static ServerMessage NotFound =>
            ServerMessage.Of("server.view.not-found", "Görünüm bulunamadı.");

        public static ServerMessage LimitField(int max) =>
            ServerMessage.Of("server.view.limit-field", "En fazla {0} görünüm kaydedebilirsiniz.", max);

        public static ServerMessage LimitPerScreen(int max) =>
            ServerMessage.Of("server.view.limit-per-screen", "Bir ekran için en fazla {0} görünüm kaydedebilirsiniz.", max);

        public static ServerMessage Invalid =>
            ServerMessage.Of("server.view.invalid", "Görünüm geçersiz.");
    }

    public static class File
    {
        public static ServerMessage ChangedByOther =>
            ServerMessage.Of("server.file.changed-by-other", "Dosya başkası tarafından değiştirilmiş.");

        public static ServerMessage ContentNotFound =>
            ServerMessage.Of("server.file.content-not-found", "Dosya içeriği bulunamadı.");

        public static ServerMessage Scanning =>
            ServerMessage.Of("server.file.scanning", "Dosya taranıyor; tarama bitince indirilebilir.");

        public static ServerMessage NotDownloadable =>
            ServerMessage.Of("server.file.not-downloadable", "Bu dosya indirilemez.");

        public static ServerMessage NotFound =>
            ServerMessage.Of("server.file.not-found", "Dosya bulunamadı.");

        public static ServerMessage SameContent =>
            ServerMessage.Of("server.file.same-content", "Aynı içerik zaten ekli.");

        public static ServerMessage AlreadyAttached =>
            ServerMessage.Of("server.file.already-attached", "Bu dosya kayda zaten eklenmiş.");
    }

    public static class Setting
    {
        public static ServerMessage NotAvailable =>
            ServerMessage.Of("server.setting.not-available", "Bu ayar bu firmada kullanılamıyor.");

        public static ServerMessage CompanyScoped =>
            ServerMessage.Of("server.setting.company-scoped", "Bu ayar şirket başına tutulur; değer yazmak için önce bir aktif şirket seçin.");

        public static ServerMessage ValueRequired =>
            ServerMessage.Of("server.setting.value-required", "Değer zorunludur ve metin olmalıdır.");

        public static ServerMessage NotDefined =>
            ServerMessage.Of("server.setting.not-defined", "Tanımlı bir ayar değil.");

        public static ServerMessage KeyRequired =>
            ServerMessage.Of("server.setting.key-required", "Anahtar zorunludur.");

        public static ServerMessage SaveFailed =>
            ServerMessage.Of("server.setting.save-failed", "Ayarlar kaydedilemedi.");
    }

    public static class Lookup
    {
        public static ServerMessage NotFound =>
            ServerMessage.Of("server.lookup.not-found", "Lookup bulunamadı.");
    }

    public static class Holiday
    {
        public static ServerMessage DateExists =>
            ServerMessage.Of("server.holiday.date-exists", "Bu tarih tatil listesinde zaten var.");

        public static ServerMessage NameRequired(int max) =>
            ServerMessage.Of("server.holiday.name-required", "Ad zorunludur ve en çok {0} karakter olabilir.", max);
    }

    public static class Credential
    {
        public static ServerMessage OwnerTypeNotAllowed =>
            ServerMessage.Of("server.credential.owner-type-not-allowed", "Bu kayıt türüne süreli belge eklenemez.");

        public static ServerMessage NumberExists =>
            ServerMessage.Of("server.credential.number-exists", "Bu kayıtta aynı türde ve numarada bir belge zaten var.");

        public static ServerMessage TypeNotAllowed(string owner) =>
            ServerMessage.Of("server.credential.type-not-allowed", "Bu belge türü {0} kaydına eklenemez.", owner);

        public static ServerMessage LicenseReadOnly =>
            ServerMessage.Of("server.credential.license-read-only", "Modül lisansı salt okunur evrede; belge eklenemez, değiştirilemez ya da silinemez.");

        public static ServerMessage UpdateRequired =>
            ServerMessage.Of("server.credential.update-required", "Bu kaydın belgelerini değiştirmek için kayıtta güncelleme izni gerekir.");

        public static ServerMessage HasFiles =>
            ServerMessage.Of("server.credential.has-files", "Belgenin ekli dosyaları var; önce dosyaları silin ya da belgenin durumunu \"İptal edildi\" yapın.");
    }

    public static class Shared
    {
        public static ServerMessage ActivityUnknown(string codes) =>
            ServerMessage.Of("server.shared.activity-unknown", "Katalogda olmayan faaliyet: {0}.", codes);

        public static ServerMessage PlaceExists =>
            ServerMessage.Of("server.shared.place-exists", "Bu ülkede aynı adlı bir yer zaten var.");

        public static ServerMessage LatLongTogether =>
            ServerMessage.Of("server.shared.lat-long-together", "Enlem ve boylam birlikte girilmelidir.");

        public static ServerMessage EndBeforeStartDate =>
            ServerMessage.Of("server.shared.end-before-start-date", "Bitiş tarihi başlangıç tarihinden önce olamaz.");

        public static ServerMessage RelationshipExists =>
            ServerMessage.Of("server.shared.relationship-exists", "Bu firma için bu rolün ilişki kaydı zaten var.");

        public static ServerMessage RoleUnknown(string codes) =>
            ServerMessage.Of("server.shared.role-unknown", "Katalogda olmayan ilişki rolü: {0}.", codes);

        public static ServerMessage RelationshipImmutable =>
            ServerMessage.Of("server.shared.relationship-immutable", "Firma ve rol kayıt açıldıktan sonra değiştirilemez.");

        public static ServerMessage NeedNotActivity(string codes) =>
            ServerMessage.Of("server.shared.need-not-activity", "Hizmet ihtiyacı için sektör veya grup değil, faaliyet seçilmeli: {0}.", codes);

        public static ServerMessage IndustryRetired =>
            ServerMessage.Of("server.shared.industry-retired", "Bu sektör artık kullanılmıyor.");

        public static ServerMessage IndustryUnknown =>
            ServerMessage.Of("server.shared.industry-unknown", "Katalogda olmayan sektör.");
    }
}
