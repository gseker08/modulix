# Veritabanı işletim rehberi (ADR-0002, ADR-0020)

Bu belge tek paylaşımlı veritabanı mimarisinin işletimini anlatır: yerel kurulum, yeni tenant açılışı,
migration'lar, gizli/yetki yapılandırması, yedekleme ve eski tenant veritabanlarının temizliği.

## Mimari özet

- **Tek veritabanı:** `Modulix`. Bütün tenant'lar aynı veritabanındadır; tenant başına ayrı veritabanı yoktur
  (ADR-0009 geri alındı, ADR-0020).
- **Platform şeması (`platform`):** tenant kayıtları, kullanıcılar/kimlik, üyelikler, modül erişimleri, oturumlar,
  e-posta kuyruğu, outbox ve olay teslimleri. Row-Level Security dışındadır; `TenantId` taşıyanlar tenant bağlamı
  kurulmadan okunmak zorunda olanlardır (giriş, firma listesi, işleyiciler).
- **Tenant şemaları (`core`, `meta`, `crm`, ...):** her tablo `TenantId` taşır. Katman 1: EF global query filter;
  katman 2: SQL Server Row-Level Security (`core.TenantIsolationPolicy`). Uygulama her bağlantı açılışında aktif
  tenant'ı salt okunur oturum değeri olarak yazar; değer yoksa hiçbir tenant satırı görünmez ve yazılamaz.
- **Tek EF bağlamı (Dilim 2):** `ModulixDbContext` kimlik, platform ve tenant tablolarını birlikte taşır. Tek migration
  zinciri vardır (`Persistence/Migrations`, geçmiş tablosu `platform.__EFMigrationsHistory`). `MembershipId` taşıyan tenant
  tabloları `platform.Memberships`'e bileşik yabancı anahtarla bağlıdır.
- **İki bağlantı:** uygulama (`modulix_app`, yalnızca veri) ve migration (`modulix_migrator`, DDL). Tenant başına
  bağlantı veya kimlik bilgisi yoktur.

## Yerel kurulum

```bash
# SQL Server + Mailpit + S3 test deposu (S3 adaptörünün entegrasyon testleri için; .env'de S3_SECRET_KEY)
docker compose up -d --wait sqlserver mailpit s3
# Veritabanı, şemalar ve kullanıcılar
docker compose run --rm db-init
```

Windows'ta tek adım: `powershell -ExecutionPolicy Bypass -File scripts\db-up.ps1` — ayrıca migration'ları uygular ve
örnek tenant'ları (acme, beta) oluşturur.

Yeni bir modül şeması eklenince (ör. `contracts`, ADR-0022; `orders`, ADR-0023; `support`, ADR-0024) şema ve uygulama rolünün veri yetkisi iki yerde tanımlanır:
`docker/sqlserver/init.sql` (yeni ortam) ve modülün ilk migration'ındaki idempotent `GRANT` (mevcut veritabanı). Yalnızca
`init.sql` güncellenirse mevcut veritabanında uygulama kullanıcısı yeni tablolara yazamaz (INSERT permission denied).

Bağlantı cümleleri `db-up.ps1` tarafından user-secrets'a yazılır (kod deposuna şifre girmez); CI'da ortam
değişkenlerinden gelir. İki ad kullanılır: `ConnectionStrings:Modulix` (app) ve `ConnectionStrings:ModulixMigrations`
(migrator). Uygulama süreci veri erişimi için yalnızca app bağlantısını kullanır; migrator bağlantısı `migrate`
komutu içindir.

## Gizli ve yetki yapılandırması

- `modulix_app`: yalnızca `SELECT/INSERT/UPDATE/DELETE/EXECUTE`; DDL yok, güvenlik politikasını kapatamaz.
  `core.AuditLog` üzerinde `UPDATE/DELETE` ayrıca reddedilir (append-only; baseline migration'ı). Tablo aylık bölümlüdür (ADR-0052); bölüm boşaltma `ALTER` ister, uygulama rolünde yoktur.
- `modulix_migrator`: `db_owner`. Sunucu düzeyinde `dbcreator` gerekmez.
- Şifreler `.env` + user-secrets/ortam değişkeninde; kodda, logda veya PR'da yer almaz. Üretimde secret yöneticisi
  (ör. Key Vault) referansı tercih edilir; yerel ve üretim yapılandırması ayrıdır.

## Yeni tenant açılışı

Platform yönetimi yeni tenant açarken (`POST /api/platform/tenants` veya `IPlatformAdministration.CreateTenantAsync`):

1. Platform kaydı oluşur (`platform.Tenants`, `Status = Active`).
2. Tenant bağlamında başlangıç verisi tek işlemde yazılır: sistem rolü, varsayılan şirket, şablon roller, denetim kaydı.
3. Modül hakları ve yönetici daveti platform bağlamında yazılır.

Ayrı bir kurulum (provisioning) adımı veya sağlık kontrolü yoktur. Dilim 2 ile adım 1–3 tek işlemdir: hata olursa yarım kurulmuş tenant kalmaz.

**Firma durumları (ADR-0028):** `Active`, `Trial` (erişim açık), `Suspended` (erişim durur, geçici), `Closed`. Erişimin açık olduğu durumlar
`TenantStatusRules.Operational`'dadır; işleyiciler, genel uç noktalar ve üyelik sorguları buna göre süzer. Yeni durum eklenirse karar
oraya yazılır. Askıya alma gerekçe ister; hiçbir durum geçişi veri silmez (silme yalnızca `tenant-delete`). Platform işlemleri
`platform.PlatformAuditLog` tablosuna da yazılır (ekleme-yalnız, firma silinse de saklanır; `tenant-export`/`tenant-delete` kapsamı dışındadır,
çünkü sütunu `TenantId` değil `TargetTenantId`'dir).

## Migration'lar

Tek zincir vardır (`Persistence/Migrations`).

```bash
# Bekleyen migration'lar
dotnet run --project src/Modulix.Api -- migrate
```

ADR-0020 Dilim 2 zinciri sıfırdan üretti (tek baseline). İki zincirli sürümden gelen yerel bir veritabanı yükseltilemez;
geliştirme verisi korunmayacağı için veritabanını sıfırlayıp yeniden kurun:

```bash
docker compose down -v
powershell -ExecutionPolicy Bypass -File scriptsdb-up.ps1
```

Tek veritabanı bütün tenant'ları etkiler; bu yüzden migration'lar **geriye uyumlu ve kesintisiz** yazılır: önce ekle,
sonra veriyi taşı, en son kaldır; uzun süren veri adımları ayrı migration'a alınır. Migration'daki veri geri doldurma
adımları (UPDATE / INSERT...SELECT) RLS yüzünden oturum değeri olmadan 0 satır etkiler; bu adımlar politikayı geçici
kapatıp açar (`ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = OFF/ON)`, OpportunityManagement
örneği).

Bu kural CI'da denetlenir: migration'a dokunan PR'da `.github/workflows/migration-compat.yml` bir önceki sürümün
entegrasyon testlerini yeni şemaya karşı koşar (README > CI). Kırmızı sonuç, eski sürümün dağıtım sırasında yeni şemada
çalışamayacağı anlamına gelir.

Yeni tenant tablosu eklerken: migration'a girer ve RLS politikasına eklenir (`TenantIsolationSql.AddTable`),
aksi halde `TableInventoryTests` kırılır.

```bash
# Yeni migration
dotnet ef migrations add <Ad> --project src/Modulix.Infrastructure --startup-project src/Modulix.Api
```

## Okuma anlık görüntüsü (ADR-0058)

Veritabanında `READ_COMMITTED_SNAPSHOT` açık olmalıdır. Kapalıyken okuyucular, başka bir tenant'ın henüz onaylanmamış satırlarını beklerken iki işlem birbirine kilitlenebilir (SQL Server hata 1205; kurban seçilen istek 500 döner). Yerel ve CI kurulumunda `docker/sqlserver/init.sql` bunu açar (`db-init`); `DatabaseSetupTests` denetler.

**Canlı (var olan) veritabanı için tek seferlik adım:**

```sql
-- Durum
SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'Modulix';
-- Aç (master'dan, db_owner/sysadmin ile). Açılırken diğer bağlantılar geri alınır: kısa bakım penceresinde yapın.
ALTER DATABASE [Modulix] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
```

- Uygulama kodu değişmez; geri alma `OFF` ile aynı komuttur.
- **Unutulursa görünür:** ayar kapalıyken `/health` "Degraded" döner ve uygulama günlüğüne uyarı yazar (`read-snapshot` denetimi). `/health/ready` etkilenmez; örnek trafik almaya devam eder. Açtıktan sonra `/health` yeniden "Healthy" olur.
- Sürüm deposu `tempdb`'de tutulur: açtıktan sonra `tempdb` boyutunu ve uzun süren işlemleri izleyin.
- `sp_getapplock` kilitleri (`ITransactionLock`) ve sürüm belirteçleri (`rowversion`) etkilenmez.

## Kayıt görünürlük tavanı (ADR-0030)

Platform yöneticisi bir firma için kayıt türü başına görünürlük tavanı koyar (`PUT /api/platform/tenants/{id}/scope-limits`; `platform.TenantScopeLimits`, RLS dışı). Tavan firmanın bir sonraki isteğinde geçerlidir; rol kayıtları değişmez, okuma tarafı kapsamı tavana indirir.

- **Önce etkiyi görün:** `POST .../scope-limits/preview` (aynı gövde; ekranda "Etkiyi göster") hiçbir şey yazmaz ve tür başına tavanı aşan rol sayısını, bugünkü tavanlara göre **yeni kısılacak** ve **serbest kalacak** etkin üye ile yönetici sayısını verir. Hesap çalışma zamanındaki kapsam yoluyla aynıdır (rol istisnası, ekip/departman rolü, yöneticinin `AppliesToAdministrators` koşulu); destek erişimi sayılmaz. Üyelik başına birkaç sorgu yapar (roller üyelik başına bir kez okunur, tür sayısıyla çarpılmaz); çok kalabalık bir firmada yavaş olabilir, bu yüzden yalnızca istendiğinde çağrılır. `AppliesToAdministrators` işaretlemeden önce "yönetici kısılacak" sayısına bakın.
- **Daraltmak:** önce `GET` ile mevcut kümeyi ve tavan konabilecek türleri okuyun; `PUT` tüm kümeyi değiştirir (listede olmayan türün tavanı kalkar). Yanıttaki `AffectedRoleCount`, kapsamı yeni tavanı aşan yönetici olmayan rol sayısıdır; bu roller okuma tarafında hemen kısılır. Gerekçeyi (`Reason`) yazın; firmanın `core.tenant` ve platform denetim kaydına önce/sonra değeriyle düşer.
- **Kaldırmak/gevşetmek:** türü `PUT` listesinden çıkarın ya da `MaxScope`'u genişletin; eski görünürlük bir sonraki istekte döner. Rol kayıtları hiç değişmediği için ek işlem gerekmez.
- **Yöneticiyi bağlamak:** `AppliesToAdministrators = true` yalnızca o tür için Yönetici rolünü de tavana indirir. Yönetici kendi kayıtlarının altına inmez; yanlış bir tavan firmayı kilitlerse tavanı gevşetin (kurtarma yolu budur).
- **Ekip/departman:** ikisi birbirini kapsamaz; tavan "Ekip" iken departman kapsamlı rol "yalnızca kendi kayıtları"na düşer ve tersi. Tavanı koymadan önce firmanın rollerinin kapsamlarına bakın.
- **Firma yöneticisine ne görünür:** rol ekranında (Kapsam ve düzey → "Kayıt türüne göre farklı kapsam") tavanı aşan seçenekler kapalıdır ve "(platform sınırını aşar)" yazar. Tavandan sonra daralan bir rolün kaydı değişmez, yalnızca okuma kısılır; rol ekranı o türde uyarı gösterir ve "Sınırı aşan türleri sınıra indir" düğmesi tavan değerini istisna olarak yazar. Rolün tavanlı türdeki etkin kapsamı (istisna ya da varsayılan) tavanı aşarsa kayıt 409 ile reddedilir ve ileti türleri sayar; güncellemede yalnızca bu istekte değişen kapsamlar denetlenir, ad ve izin düzenlemeleri kilitlenmez.
- **Denetim:** platform değişikliği firmanın `core.tenant` kaydında (`scopeLimits`, "Kayıt görünürlüğü") ve platform denetiminde (`tenant.scope-limits-changed`); rol istisnaları `core.role` kaydında (`scopeOverrides`, "Kayıt türüne göre kapsam") önce/sonra görünür. Yönetici olmayan rol yöneticisi kendi kapsamından geniş istisna veremez (403); sistem rolüne (Yönetici) istisna yazılamaz (409).
- **Ekran:** Platform → Firmalar → firma detayı → "Kayıt görünürlüğü" bölümü bu `GET`/`PUT`'i kullanır (tür başına seçici, gerekçe zorunlu, kayıtta etkilenen rol sayısı gösterilir). Modülü sonradan kapanan türün mevcut tavanı listede görünmez ama kümeyle birlikte korunur; yeni tür için tavan konamaz. Yetki: Operasyon rolü (`PlatformAccessRules`).
- **Dışa aktarma ve silme:** tablolar katalogdan okunur; `tenant-export` ve `tenant-delete` `platform.TenantScopeLimits` ve `core.RoleScopeOverrides` satırlarını kendiliğinden kapsar.

## Hız sınırları ve ters vekil (ADR-0054)

Uygulamanın hız sınırı sayaçları her sunucunun belleğindedir. Tek sunucuda başka bir şey gerekmez. Birden çok sunucuda iki ayar yapılır:

1. **Sunucu payı:** `RateLimiting:Nodes` sunucu sayısına ayarlanır (ör. ortam değişkeni `RateLimiting__Nodes=3`). Dakikalık ayarlar küme toplamıdır ve her sunucu `ceil(ayar / Nodes)` kadarını uygular:
   - `Auth:RateLimitPerMinute` (30)
   - `RateLimiting:UserPerMinute` (600) ve `RateLimiting:TenantPerMinute` (3000)
   - `CommunicationPublic:RateLimitPerMinute` (30)
   - `LeadIntake:RateLimitPerMinute` (20)
   - `QuotePublic:RateLimitPerMinute` (60)
   - `SupportSurveyPublic:RateLimitPerMinute` (30)
   - `ContractSignature:RateLimitPerMinute` (120)

   Otomatik ölçeklemede en büyük sunucu sayısı yazılır; sınır daha sıkı olur, gevşemez.
2. **Ortak IP sınırı ters vekilde:** vekil bütün istekleri gördüğü için IP başına sınırı tek yerde ve kesin uygular. Uygulamanın IP sınırları ikinci hattır. Kişi ve firma başına sınır yalnızca uygulamadadır (vekil oturumu çözemez).

Vekil arkasında uygulamaya gerçek istemci IP'si bildirilir: `ForwardedHeaders:Enabled=true` ve vekilin adresi `ForwardedHeaders:KnownProxies` (IP) ya da `ForwardedHeaders:KnownNetworks` (CIDR) ile verilir. Verilmezse bütün istekler vekilin IP'sinden sayılır ve IP sınırı bütün kullanıcıları birlikte keser.

Örnek nginx yapılandırması (değerler uygulama varsayılanlarıyla aynıdır; vekil tek katmanlı olmalıdır, birden çok nginx örneği kendi sayacını tutar):

```nginx
# http bloğu
limit_req_zone $binary_remote_addr zone=modulix_auth:10m   rate=30r/m;   # oturumsuz kimlik uçları
limit_req_zone $binary_remote_addr zone=modulix_public:10m rate=60r/m;   # herkese açık form, teklif, anket, tercih sayfası
limit_req_zone $binary_remote_addr zone=modulix_api:20m    rate=1200r/m; # bir IP'den gelen bütün API (ofis NAT'ı için geniş)
limit_req_status 429;

upstream modulix { server 10.0.0.11:8080; server 10.0.0.12:8080; server 10.0.0.13:8080; }

# server bloğu
location /api/auth/   { limit_req zone=modulix_auth burst=10 nodelay;  proxy_pass http://modulix; include modulix_proxy.conf; }
location /api/public/ { limit_req zone=modulix_public burst=20 nodelay; proxy_pass http://modulix; include modulix_proxy.conf; }
location /api/        { limit_req zone=modulix_api burst=200 nodelay;   proxy_pass http://modulix; include modulix_proxy.conf; }
location /            { proxy_pass http://modulix; include modulix_proxy.conf; }

# modulix_proxy.conf
proxy_set_header Host              $host;
proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $scheme;
```

Bulut ağ geçidinde (Azure Front Door, AWS WAF, Cloudflare) aynı üç kural hız sınırı kuralı olarak tanımlanır: `/api/auth/`, `/api/public/` ve `/api/` için IP başına. Ofislerden çıkan istekler tek IP'yi paylaşabilir (NAT). `/api/` kuralı bu yüzden geniş tutulur; dar sınır uygulamanın kişi başına sınırıdır.

## Arka plan işleri

E-posta işleyicisi platform kuyruğunu işler. Olay işleyicisi her turda tek sorguyla işi olan tenant'ları bulur (outbox/teslim tabloları RLS dışıdır) ve yalnızca
onların bağlamını kurup (oturum değeri → RLS) olayları işler; sağlık kontrolü ve platform olay ekranı da bütün tenant'ları
tek sorguyla okur. Teklif süre dolumu işleyicisi CRM modülü açık tenant'larda tenant bağlamıyla döner (teklif tabloları
RLS altındadır, çapraz tenant okunamaz). Teklif süre dolumu kilidi tenant başınadır (`sp_getapplock`, kaynak adı tenant kimliğini içerir). Sözleşme zamanlanmış işleri (`ContractJobsWorker`; yenileme/fesih/yürürlük geçişi, hatırlatma ve eskalasyon, imza süre dolumu, taahhüt değerlendirmesi) sözleşme modülü açık her tenant'ı ayrı tenant bağlamı ve `contracts.scheduler` sistem kimliğiyle, tenant başına `sp_getapplock` kilidiyle çalıştırır; birden çok örnek aynı tenant'ı aynı anda işlemez. `ContractJobs:WorkerEnabled` (varsayılan `true`) ve `ContractJobs:PollIntervalSeconds` (varsayılan 900) ile yapılandırılır; testler işçiyi kapatır. Otomatik uzatma tenant ayarıyla açılana kadar (`contracts.renewal.allow-auto`) hiçbir sözleşme kendiliğinden uzatılmaz. Outbox ve teslim tabloları RLS dışıdır; `ModulixDbContext` bu iki tabloya aktif tenant sorgu filtresi uygular, ham SQL de `TenantId` ile süzer.

## Yedekleme, dışa aktarma ve silme

**Yedek:** tek veritabanı (`Modulix`) bütün tenant'ları içerir; SQL Server tam + log yedeği yeterlidir. Tenant başına yedek yoktur;
bir tenant'ın verisi aşağıdaki dışa aktarma ile alınır. Geri yüklemede bütün tenant'lar aynı ana döner; tek tenant'ı geri almak
gerekirse yedek ayrı bir sunucuya yüklenir ve o tenant oradan dışa aktarılır.

**Dosya içerikleri (ADR-0040):** kayıtlara eklenen dosyaların üst verisi `core.StoredFiles` tablosunda, içeriği ise depolama dizinindedir
(`Storage:LocalRoot`; boşsa uygulamanın çalışma dizini altında `App_Data/blobs`). **Veritabanı yedeği dosya içeriğini kapsamaz**: dizin de
aynı zamanda yedeklenmeli ve geri yüklemede veritabanıyla aynı ana döndürülmelidir (aksi halde üst veri olup içeriği olmayan dosyalar 404 verir).
Dizin web köküne ve statik dosya sunumuna dışında kalmalı; konteynerde kalıcı bir birime bağlanmalıdır. Yerel disk yalnızca tek düğümlü kurulum içindir.
Tek dosya sınırı `Storage:MaxFileBytes` (varsayılan 10 MB).

**Çok düğümlü dağıtım: S3 uyumlu nesne deposu.** `Storage:Provider=S3` ile içerik bir kovada tutulur (AWS S3, Cloudflare R2, Ceph RGW, Versity vb.):
`Storage:S3:Bucket` (zorunlu; kovayı uygulama oluşturmaz), `Storage:S3:ServiceUrl` (AWS dışı uç nokta; boşsa AWS bölge uç noktası),
`Storage:S3:Region`, `Storage:S3:Prefix` (kova paylaşılıyorsa kök, ör. `modulix`), `Storage:S3:ForcePathStyle`. Erişim anahtarları
(`Storage__S3__AccessKey`, `Storage__S3__SecretKey`) ortam değişkeni ya da gizli bilgi deposuyla verilir; ikisi de boşsa AWS'nin varsayılan
kimlik zinciri (rol, ortam, profil) kullanılır. Nesne anahtarı `{önek}/{tenantId}/{ilk iki karakter}/{depolama anahtarı}` düzenindedir; yazma
koşulludur (var olan nesne ezilmez). Kova erişilemezse `/health/ready` 503 döner. Kovada sürümleme ve sunucu tarafı şifreleme açık tutulmalı,
kova herkese açık olmamalıdır; yedek kovanın sürümleme/çoğaltma özelliğiyle veritabanı yedeğiyle aynı ana döndürülebilecek şekilde alınır.
Yerelden S3'e geçişte mevcut içerik `{kök}/{tenantId}/…` dizininden aynı göreli yolla (`{önek}/` altına) kopyalanır.
`tenant-export` içeriği `<dizin>/files/` altına depolama anahtarı adıyla kopyalar (anahtar ↔ dosya adı eşlemesi `core.StoredFiles.json`'dadır);
`tenant-delete` veritabanı işlemi kesinleştikten sonra tenant'ın içerik dizinini de siler. İçerik silinemezse komut hata verir ve tekrar çalıştırılmalıdır.

**Sözleşme verisi (dilim 9):** kullanıcıların kaydettiği liste görünümleri ortak `core.GridViews` tablosundadır (ADR-0036; eski `contracts.ContractSavedViews` tablosu `ContractSavedViewsToGridViews` migration'ıyla taşınıp kaldırıldı), `TenantId` taşır; tenant dışa aktarma ve silme kapsamındadır. Sözleşme listesi CSV çıkışı (`contracts.contracts.export` izni) uygulama içinden yapılır ve her çıkış denetim kaydına (`Exported`: kim, kaç satır, hangi süzgeç) yazılır; tenant verisinin bütünüyle alınması için `tenant-export` kullanılır.

**Denetim kaydı arşivi (`audit-archive`, ADR-0052):** `core.AuditLog` aylık bölümlüdür. Komut ayda bir zamanlanmış görev olarak çalıştırılır (ör. her ayın 2'si gece). Migrator bağlantısıyla çalışır ve şu işleri yapar:
- Sıcak saklama süresinden (`AuditArchive:HotMonths`, varsayılan 24) eski her dolu bölüm için tenant başına gzip'li JSON Lines dosyasını nesne deposuna, kaydını `core.AuditArchives`'e yazar ve bölümü boşaltır.
- Boş eski sınırları birleştirir, ileriye `AuditArchive:FutureMonths` (24) ay sınır açar.

Sayı tutmayan dönem (örneğin tenant kaydı olmayan satır) boşaltılmaz; komut çıkış kodu 1 ile biter ve günlüğe hata yazar. Neden giderilip komut yeniden çalıştırılır; tekrar çalıştırmak güvenlidir. Aynı anda tek çalışma olur. Arşiv dosyaları uygulama düzeyinde şifrelenmez: nesne deposunda sunucu tarafı şifreleme ve sürümleme açık, kova kapalı olmalıdır. Arşiv dosyaları tenant'ın depo kökünde durduğu için `tenant-export` ve `tenant-delete` onları da kapsar. Komut hiç çalıştırılmazsa veri kaybolmaz; yalnızca tablo büyür.

```bash
dotnet run --project src/Modulix.Api -- audit-archive
```

**Dışa aktarma (`tenant-export`):** bir tenant'ın bütün satırlarını `TenantId` ile tablo başına bir JSON dosyasına yazar.
Tablolar koddan değil veritabanı kataloğundan bulunur (`TenantId` kolonu taşıyan her tablo: tenant şemaları ve platform
şemasındaki üyelik, modül hakkı, e-posta, outbox, teslim); yeni tablo kendiliğinden kapsama girer. `tenant.json` tenant kaydını,
`manifest.json` tablo ve satır sayılarını içerir. Migration kullanıcısıyla, oturum tenant'ı yazılarak çalışır (RLS ikinci katman).

```bash
dotnet run --project src/Modulix.Api -- tenant-export acme --dir exports/acme
```

**Silme (`tenant-delete`):** aynı tabloları tek işlemde boşaltır ve tenant kaydını siler; yabancı anahtar sırası hatadan
öğrenilir (silinemeyen tablo sonraki turda denenir). Geri alınamaz: komut kısa adın `--confirm` ile tekrar yazılmasını ister,
önce dışa aktarın. Kullanıcı hesapları (`platform.Users`) kişiye aittir, silinmez; yalnızca üyelikleri kalkar. Hesap silme
ayrı bir kişisel veri sürecidir. Silme uygulama günlüğüne uyarı olarak yazılır.

```bash
dotnet run --project src/Modulix.Api -- tenant-delete acme --confirm acme
```

Runbook (tenant kapatma): 1) firma kapatılır (`Status = Closed`, giriş durur); 2) sözleşme süresi boyunca veri bekletilir;
3) `tenant-export` ile dışa aktarılıp güvenli yere alınır; 4) `tenant-delete --confirm` çalıştırılır; 5) çıktı (tablo/satır
sayıları) kayıt altına alınır.

## Eski tenant veritabanlarının temizliği

ADR-0009 döneminde oluşan `modulix_tenant_<...>` veritabanları artık kullanılmaz; gerçek veri içermiyorlarsa toplu
silinebilir (yalnızca bu ön ekli veritabanları hedeflenir):

```sql
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
FROM sys.databases WHERE name LIKE 'modulix_tenant_%';
EXEC sys.sp_executesql @sql;
```

Entegrasyon testleri artık tenant veritabanı oluşturmaz; test fabrikası kendi açtığı tenant'ların platform kayıtlarını
Dispose'ta siler, tenant iş satırları rastgele `TenantId` ile veritabanında kalır ve RLS yüzünden kimseye görünmez.
