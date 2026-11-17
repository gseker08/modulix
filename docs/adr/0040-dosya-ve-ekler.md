# ADR-0040: Dosya ve ekler (Ç4, dilim 1)

- **Durum:** Kabul edildi, 11 Kasım 2026 (Dilim 1 bu belgeyle birlikte teslim edilir)
- **Karar:** Kayıtlara bağlı dosyalar için Core'da tek, modülden bağımsız bir dosya deposu kurulur: metadata `core.StoredFiles` tablosunda (tenant verisi, RLS altında), içerik bir **depolama adaptörü** arkasında (dilim 1'de yerel disk). Dosyaya erişim **bağlı olduğu kaydın erişimidir**: okuma ve indirme kaydı okuyabilene, yükleme ve silme kaydı güncelleyebilene açıktır. Nesne anahtarı rastgeledir; hiçbir uç nokta anahtarı ya da diski göstermez.

## Problem

Dosya eki bugün her modülde ayrı ve geçici çözülüyor: Destek ekleri ve teklif PDF'i veritabanında `byte[]` (5 MB sınırı, `AttachmentRules`), sipariş teslim kanıtı küçük bir tabloda (ADR-0023), sözleşme belgeleri `byte[]`. Aktivite, fırsat, antrepo vekâlet belgesi, içe aktarma kuyruğu, rentcar teslim fotoğrafı ve lojistik teslim kanıtı (ADR-0015, 0017, 0031, 0032, 0033, 0038) genel bir depo bekliyor. Yol haritası Ç4 bunu D1 pilot şartı sayıyor.

Ç4 beş alt kalemdir (depolama, güvenli yükleme, erişim, yaşam döngüsü, mobil). Hepsi tek adımda teslim edilemez; bu ADR **dilim 1'i** (Ç4.1 + Ç4.3'ün çekirdeği + Ç4.2'nin yarısı) karara bağlar, kalanı sınırlarıyla yazar.

## Seçenekler

| Seçenek | Artı | Eksi |
| --- | --- | --- |
| Her modül kendi `byte[]` ek tablosunu tutsun (mevcut) | Kod yok | Veritabanı şişer, yedek ve geri yükleme ağırlaşır; her modül doğrulamayı, taramayı ve silmeyi ayrı yazar; tenant dışa aktarma dosya içeriğini JSON'a gömer |
| Modül başına dosya tablosu + ortak depolama adaptörü | Modül sahipliği net | Aynı metadata, doğrulama ve yetki kodu her modülde tekrar edilir; sonradan birleştirmek migration ister |
| **Core'da tek metadata tablosu + adaptör, erişim bağlı kaydın erişimi (seçildi)** | Doğrulama, tarama, kota ve yaşam döngüsü tek yerde; modüller yalnızca kayıt anahtarını verir (K5: modüller birbirinin tablosuna dokunmaz); yetki kaydın yetkisinden türediği için ayrı bir izin matrisi gerekmez | Her modülün dosya yetkisini ayrı daraltması (ör. yalnızca yöneticiler indirsin) dilim 1'de yoktur; sınıf alanı bunun için ayrılmıştır |

## Karar

### K1 — Veri modeli

`core.StoredFiles` (`ITenantEntity` + `ICompanyShared`; şirket kapsamı bağlı kaydın kapsamından gelir):

| Alan | Anlam |
| --- | --- |
| `Id` | Dosyanın kimliği (UUIDv7); API'de yalnızca bu görünür |
| `EntityKey`, `RecordId` | Bağlı kayıt (ör. `crm.company`, kimlik). Kayıt türü kayıt deposu olan bir tür olmalıdır |
| `FileName` | Güvenli ad (yol, denetim karakteri ve ayırıcılar atılmış, en çok 200 karakter) |
| `ContentType`, `SizeBytes`, `Sha256` | Tür uzantı + imzayla belirlenir (K3); istemcinin bildirdiği tür kullanılmaz |
| `StorageKey` | Adaptörün nesne anahtarı: 32 bayt kriptografik rastgele, onaltılık. Ad, kimlik ya da içerikten türetilmez |
| `Classification` | Erişim sınıfı. Dilim 1'de tek değer: `Internal` (yalnızca personel). Portal/paylaşım dilimi yeni sınıf ekler; sınıfı bilmeyen indirme yolu reddeder |
| `ScanStatus` | `NotScanned` (dilim 1), ileride `Pending`, `Clean`, `Infected`. `Pending` ve `Infected` indirilemez |
| `Version` | Silme için eşzamanlılık belirteci |
| `UploadedByUserId`, `CreatedAt` | Yükleyen ve zaman |

Sürüm geçmişi (aynı dosyanın yeni sürümü), saklama süresi ve legal hold dilim 3'tür; o zaman `Version` ve `ReplacesFileId` eklenir (geriye uyumlu, boş alan).

### K2 — Erişim: bağlı kaydın erişimi

- **Listele ve indir:** `IRecordPipeline.GetAsync(kayıt)` başarılıysa. Kayıt kullanıcının kapsamında yoksa ya da izni yoksa dosya **404** döner (varlığı sızmaz); modül lisansı bitmişse boru hattı zaten reddeder.
- **Yükle ve sil:** kayıt okunabilir **ve** kayıt türünde `Update` izni var **ve** modül yazılabilir (lisans salt okunur evresinde değil, ADR-0028). Kayıt okunabilir ama yazma izni yoksa 403.
- **Kayıt düzeyinde kural:** kaydı yalnızca belirli durumda ya da belirli kişi değiştirebiliyorsa (ör. masraf satırı: taslak beyan, sahibi ya da İK) ya da hiç değişmiyorsa depo aynı kuralı `IRecordWriteGuard` ile verir; yükleme ve silme tür izninden sonra buna da sorulur, kural tutmazsa 403. Kuralı vermeyen tür izinle sınırlı kalır; bu yüzden kaydın güncellemesini durum ya da kişiyle sınırlayan her deponun dosya kuralını da vermesi gerekir. Lojistik: onaylanmış masraf ve iptal edilmiş seferdeki masraf, kapanmış seferdeki teslim kanıtı (ADR-0038 bölüm 24); kapanmış talep, sevkiyat, sefer ve durak (ADR-0038 bölüm 27); İK: ADR-0042 bölüm 25.
- Dosya kimliğinden gidilirken önce metadata okunur, sonra bağlı kaydın erişimi her istekte yeniden sorulur. Dosya kimliğini bilmek yetki vermez. Tenant yalnızca oturumdan gelir; başka tenant'ın dosyası RLS ve sorgu süzgeciyle bulunamaz.
- Bağlı kayıt silinmiş ya da erişilemezse dosya da erişilemez (404). Silinen kaydın ekleri K8 ile temizlenir; tenant silme ise hepsini kapsar (K6).

### K3 — Yükleme doğrulaması

- Boyut sınırı yapılandırmadan gelir (`Storage:MaxFileBytes`, varsayılan 10 MB); sınır aşılırsa gövde okunmadan 413 / doğrulama hatası.
- İzin verilen uzantı listesi ve dosya imzası (`%PDF`, PNG, JPEG, GIF, OOXML/OLE, düz metin) destek ekleri kuralıyla aynıdır; tür **uzantı + imza** ile belirlenir. Dosya adının ortasında tehlikeli uzantı (`fatura.exe.pdf`) ve çalıştırılabilir/komut dosyası/HTML/SVG uzantıları reddedilir. Bu kural `Modulix.Core` içindeki `FileRules` sınıfındadır; Destek'in `AttachmentRules`'ı dokunulmadan kalır (Destek ekleri sonraki dilimde bu depoya taşınır, taşıma ayrı iştir).
- Kayıt başına en çok 20 dosya; aynı kayda aynı içerik (SHA-256) ikinci kez eklenemez (çakışma). Bu, bağlantı kesilip yeniden denenen yüklemenin çift kayıt üretmemesinin dilim 1'deki karşılığıdır (Ç4.5'in tam çözümü dilim 5).
- **Zararlı içerik taraması dilim 2'dir.** Dilim 1'de dosya `NotScanned` yazılır ve indirilebilir; bu sınırlama çalışıyormuş gibi gösterilmez (arayüzde "taranmadı" etiketi dilim 2'de). `Pending`/`Infected` durumları şimdiden indirmeyi engeller, böylece tarama eklendiğinde indirme yolu değişmez.

### K4 — Depolama adaptörü

```
IBlobStorage
  PutAsync(tenantId, key, Stream)      // yazar; var olan anahtarı ezmez
  OpenReadAsync(tenantId, key)         // yok ise null
  DeleteAsync(tenantId, key)
  ExportTenantAsync(tenantId, dizin)   // tenant dışa aktarma için
  DeleteTenantAsync(tenantId)          // tenant silme için
  ListAsync(tenantId)                  // yetim temizliği için: anahtar + son değişiklik zamanı (K8)
```

- Dilim 1 uygulaması `LocalBlobStorage`: kök dizin `Storage:LocalRoot` (varsayılan veri dizini altında `blobs`; web köküne ve statik dosya sunumuna **dışında**). Yol `{kök}/{tenantId:N}/{anahtar[0..2]}/{anahtar}`; anahtar rastgele olduğundan yoldan dosya adı ya da kayıt çıkarılamaz, tahminle erişilemez. Yazma geçici dosyaya yapılıp taşınır (yarım dosya görünmez).
- Nesne depolama (S3 uyumlu) adaptörü aynı arayüzle sonra eklenir; yapılandırma anahtarı adaptörü seçer. On-prem'de yerel disk, tek imaj ilkesi korunur (müşteriye özel kod yok).
- Blob dosyaya, metadata veritabanına yazılır; ikisi tek işlemde olamaz. Sıra: blob yazılır → metadata kaydedilir; kayıt başarısızsa blob silinir. Blobsuz metadata oluşamaz; metadatasız (yetim) blob olabilir ve K8'deki tarama ile temizlenir.

### K5 — Uç noktalar ve indirme

`/api/files` (oturum + aktif tenant ister; ek izin yoktur, yetki K2'den gelir):

| Yöntem | Yol | Anlam |
| --- | --- | --- |
| `GET` | `/api/files?entityKey=&recordId=` | Kaydın dosyaları (kimlik, ad, tür, boyut, özet, tarama durumu, yükleyen, zaman) |
| `POST` | `/api/files` (multipart: `entityKey`, `recordId`, `file`) | Yükler; 201 |
| `GET` | `/api/files/{id}/content` | İçeriği indirir |
| `DELETE` | `/api/files/{id}?version=` | Siler (metadata + blob) |

İndirme yanıtı: `Content-Disposition: attachment` (satır içi gösterim yok; SVG/HTML hiçbir koşulda tarayıcıda çalışmaz), `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`, içerik türü saklanan (izin listesinden) tür. Yükleme ve silme `core.file` anahtarıyla denetim kaydına yazılır (dosya adı, boyut, özet, bağlı kayıt); indirme denetim kaydına yazılmaz (okuma).

**Roadmap'ten sapma:** Ç4.3 "kısa ömürlü imzalı URL" der. Dilim 1'de indirme oturum çerezi ister ve her istekte yetkiyi yeniden sorar; oturumu gerektiren bir bağlantıyı imzalamak ek güvence sağlamaz. İmzalı kısa ömürlü bağlantı (iptal ve TTL davranışıyla) oturumsuz tüketici olduğunda eklenir: müşteri portalı ve mobil uygulama (dilim 4).

### K6 — Tenant sınırı, dışa aktarma ve silme

- `core.StoredFiles` tenant tablosudur: RLS (`TenantIsolationSql.AddTable`), `TenantId` ile dışa aktarma/silme. Yükleyen `UploadedByUserId` ile tutulur (üyelik yabancı anahtarı yok; üyelik kalksa dosya kalır).
- `tenant-export` metadata JSON'una ek olarak blobları `<dizin>/files/` altına kopyalar; `tenant-delete` tenant'ın blob dizinini de siler (metadata silinmeden **sonra**, işlem başarıyla kesinleştikten sonra; blob silinemezse uyarı günlüğü yazılır ve komut başarısız olur ki operatör tekrar çalıştırsın).
- Aynı tenant'ın iki kaydının aynı içeriği iki ayrı blob olarak saklanır (içerik tekilleştirme yok; tenant sınırı ve silme basitliği için bilinçli).

### K7 — Kapsam dışı (sonraki dilimler)

| Dilim | İçerik |
| --- | --- |
| 1b | `AttachmentPanel` Blazor bileşeni; ilk ekran (Firma ayrıntısı), sonra fırsat, aktivite |
| 2 | Karantina ve zararlı içerik taraması (`Pending` → `Clean`/`Infected`), sıkıştırılmış dosya limitleri, `Classification` ile yeni sınıflar |
| 3 | Yaşam döngüsü: sürüm, saklama, legal hold, kota rezervasyonu (Ç12). Silinen kaydın ekleri ve yetim blob temizliği yapıldı (K8) |
| 4 | İmzalı kısa ömürlü bağlantı; portal erişimi |
| 5 | Mobil: kamera yüklemesi, yeniden deneme, devam ettirilebilir yükleme |
| Taşıma | Destek ekleri, teklif PDF'i, sözleşme belgeleri, sipariş teslim kanıtı bu depoya taşınır (her biri kendi ADR dilimidir) |

### K8 — Silinen kaydın ekleri ve yetim içerik (dilim 3, 15 Kasım 2026)

İki adımdır; ikisi de geri alınamaz silmedir, bu yüzden ayrılmıştır:

1. **Metadata (olayla, işlem içinde):** Core `core.file-cleanup` aboneliği (ADR-0007) her `record.deleted` olayını dinler (bütün kayıt türleri). `IRecordFileCleanup.RemoveForRecordAsync` silinen kayda bağlı `StoredFiles` satırlarını siler ve her biri için `core.file` denetim satırı yazar (eylem Silindi, kaynak arka plan işi). Abone olay dağıtıcısının işlemi içinde çalışır; en az bir kez teslim edilir, ikinci çalıştırma bir şey bulmaz (idempotent). İçerik burada silinmez: veritabanı işlemi geri alınırsa silinmiş içerik geri gelmez.
2. **İçerik (tarama ile):** `files.cleanup` arka plan işi (`FileCleanup:IntervalMinutes`, varsayılan 360) her etkin tenant için depodaki anahtarları listeler (`IBlobStorage.ListAsync`) ve hiçbir `StoredFiles` satırının göstermediği, `FileCleanup:OrphanGraceHours` (varsayılan 24, en az 1) saatten eski içeriği siler. Bekleme süresi yükleme sırasını korur: içerik metadata'dan önce yazıldığı için yeni yüklenen dosyanın içeriği kısa bir süre metadatasızdır. Önce eski anahtarlar, sonra bilinen anahtarlar okunur; böylece arada kaydedilen dosya silinmez. Tenant başına `sp_getapplock` ile tek düğüm çalışır; silinemeyen içerik günlüğe yazılır ve bir sonraki çalıştırmada yeniden denenir. `FileCleanup:WorkerEnabled=false` işi kapatır.

Aynı anda kayıt silme ve dosya yükleme yarışında yüklenen dosyanın metadata'sı kalabilir; bağlı kaydı olmadığı için erişilemez (K2) ve tenant silmede temizlenir.

**Kapsam dışı:** olay yayınlamadan silinen alt satırlar (bir modülün ana kaydıyla birlikte toplu sildiği satırlar) ve birleştirilen kayıtların ekleri (birleştirmede ekler hedef kayda taşınmaz). Bu satırların ekleri bağlı kayıt bulunamadığı için erişilemez kalır; taşıma ya da temizlik ilgili modülün dilimidir.

### K9 — Modüle ait dosyalar (17 Kasım 2026)

K2 erişimi bağlı kaydın erişimine bağlar. Bazı eklerin kuralı bundan dardır ve genel uçlarla ifade edilemez. İlk örnek destek talebi ekleridir (ADR-0024 §4.9): iç nota bağlı ek, talebi okuyabilen herkese değil yalnızca iç not izni olana görünür; ek yazmak talebi güncelleme izniyle değil mesaj yazma izniyle yapılır; ek silinemez. Bu ekler genel depoya K2 ile taşınsaydı `/api/files` iç not eklerini talebi okuyabilen herkese açar ve silmeye izin verirdi.

**Karar:** `core.StoredFiles` yeni bir sınıf taşır: `ModuleOwned`. Modül bu sınıftaki dosyayı `IModuleFiles` (`Core.Contracts.Files`) ile yazar ve açar.

- **Genel uçlar bu sınıfı görmez:** `IFileStore` ve `/api/files` modüle ait dosyayı listelemez; indirme ve silme, kayıtta tam yetkisi olan kullanıcı için de 404 döner. Kayıt başına dosya sınırı ve yinelenen içerik denetimi (K3) bu sınıfı saymaz.
- **Yetki, doğrulama ve sınırlar modüldedir.** `IModuleFiles` erişim denetimi yapmaz; yalnızca aktif tenant içinde çalışır (RLS ve sorgu süzgeci geçerlidir). Modül dosyayı açmadan önce kullanıcının onu görebildiğini kendi kuralıyla denetler. Açarken bağlı kayıt türü de verilir: tür tutmazsa dosya açılmaz (bir modül başka modülün dosyasını kimlikle açamaz).
- **İşlem:** `AddAsync` içeriği depoya yazar, üst veriyi ve `core.file` denetim satırını çalışan iş birimine ekler; **kaydetmeyi çağıran yapar** (kayıt boru hattı). Böylece dosya modülün kendi satırıyla aynı işlemde oluşur. İşlem geri alınırsa içerik yetim kalır; K8 taraması siler.
- **Ortak kalanlar:** depolama adaptörü (K4), tarama durumu (indirilemeyen durumlar burada da açılmaz), tenant dışa aktarma ve silme (K6), silinen kaydın ekleri ve yetim içerik temizliği (K8).
- **Sınırlar:** silme sözleşmede yoktur (ilk tüketicinin ekleri değişmezdir; gerekince eklenir). Modülün kendi tablosu dosya kimliğini tutar; iki satır arasında yabancı anahtar yoktur.

Bu karar yalnızca sözleşmeyi ekler. Destek eklerinin taşınması ayrı dilimlerdir: yeni ekler depoya yazılır ve eski içerik kopyalanır, sonra eski içerik sütunu düşürülür (iki adım, `migration-compat`).

## Sonuçlar

- Mimari testler: `Modulix.Core` yeni bir proje referansı almaz; arayüz `Modulix.Core.Contracts`'a (modüller ileride kullanır), uygulama `Modulix.Infrastructure`'a girer.
- Yeni bir kayıt türünün ek taşıması için kodda değişiklik gerekmez; kayıt deposu olması yeter. Taşımayı kapatmak isteyen tür için dilim 2'de deklaratif bayrak eklenir.
- Depolama kökü yedek kapsamına girmelidir (`docs/tenant-isletim.md` güncellenir): veritabanı yedeği tek başına dosya içeriğini kapsamaz.
- Risk: yerel disk tek düğümlüdür; çok düğümlü dağıtım nesne adaptörünü bekler. Şimdilik tek imaj, tek düğüm varsayımı belgelenir.

## Testler (dilim 1)

Mutlu yol (yükle, listele, indir, sil; denetim kaydı), doğrulama (boyut, uzantı, imza, çift uzantı, boş dosya, yinelenen içerik, kayıt başına sınır, geçersiz kayıt türü), tenant izolasyonu (B tenant'ı A'nın dosyasını listeleyemez/indiremez/silemez; dışa aktarma ve silme blobları kapsar), yetki (kaydı göremeyen 404, yazma izni olmayan 403, oturumsuz 401), nesne anahtarının rastgeleliği ve yol çıkışı (yol geçişi yok), indirme başlıkları.

## Testler (dilim 3, K8)

`FileCleanupTests` (entegrasyon): kayıt silinince yalnızca onun dosyalarının metadata'sı silinir ve denetime yazılır, ikinci çalıştırma 0 döner, tarama bekleme süresi içindeki içeriği korur, süresi geçen yetim içeriği siler, kalan dosya indirilebilir ve B tenant'ının içeriği dokunulmadan kalır; yetkisiz kullanıcı (403) ve başka tenant (404) kaydı silemez, dosya ve içerik kalır. `ModuleCatalogTests`: `record.deleted` olayının tek abonesi `core.file-cleanup`.
