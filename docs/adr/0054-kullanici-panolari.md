# ADR-0054: Kullanıcı panoları ve pano bileşenleri

- **Durum:** Kabul edildi, 17 Kasım 2026. Dilim 1a (model, servis, uç noktalar), 1b (ekran), 2 (paylaşım, varsayılan pano) ve 3 (aralıklı yenileme) teslim edildi.
- **Kaynak:** [CRM eksik süreç epikleri](../crm-eksik-surecler-epicleri.md) E2; [öncelik listesi](../oncelikli-isler.md) R2.
- **İlgili:** ADR-0035 (raporlama ve rapor merkezi), ADR-0020 (tek veritabanı, RLS).

## Bağlam

Sabit pano ekranları var (CRM panosu, satış özeti) ama kullanıcının kendi kurduğu pano yok. Rapor merkezi (ADR-0035 dilim 3) kayıtlı
raporu, sınırlı oluşturucuyu ve çağıranın yetkisiyle çalıştırmayı zaten sağlıyor. Pano bunun üstüne kurulur; yeni bir sorgu dili ya da
ikinci bir yetki yolu açılmaz.

## Karar

### 1. Model

İki tenant tablosu (`core` şeması, RLS altında, `ICompanyShared`):

- `core.Dashboards`: sahibi, adı (sahibi içinde tekil), sürüm (eşzamanlılık).
- `core.DashboardWidgets`: pano, kayıtlı rapor, gösterim türü (`Number`, `Bar`), isteğe bağlı başlık, sıra. Pano ve rapora bileşik
  yabancı anahtarla bağlıdır; pano ya da rapor silinince bileşen de silinir.
- `core.Dashboards.Visibility` (dilim 2): `Private`, `Users`, `Tenant`; kayıtlı raporlarla aynı enum (`ReportVisibility`). Var olan
  panolar `Private` kalır (sütun varsayılanı).
- `core.DashboardShares` (dilim 2): pano + kullanıcı (pano içinde tekil), paylaşan, zaman. Panoya bileşik yabancı anahtarla bağlıdır.
- `core.DashboardDefaults` (dilim 2): kullanıcı + pano; `UNIQUE(TenantId, UserId)` ile kullanıcı başına tek satır. Pano silinince satır da
  silinir.

Bileşen kendi sorgusunu taşımaz, **kayıtlı bir rapora** bağlanır. Süzgeç, gruplama ve ölçü raporun tanımındadır; kullanıcı raporu
oluşturucuda bir kez kurar, panoya koyar. Böylece doğrulama, hassas alan ve kapsam kuralları tek yerde (rapor çalıştırıcı) kalır.

### 2. Yetki

- Pano **veri erişimi vermez.** Bileşen verisi `IReportCenter.RunAsync` ile, bakan kişinin kayıt izni, veri kapsamı ve hassas alan
  kurallarıyla üretilir. Bakan kişi raporu artık göremiyorsa (paylaşım geri alındı) bileşen veri vermez; pano kalır.
- Yeni izin yok: panoyu açmak ve bileşen verisini okumak `core.reports.read`, pano oluşturmak, düzenlemek ve silmek
  `core.reports.manage` ister.
- Dilim 1'de pano yalnızca sahibine görünür. Başkasının ya da başka firmanın panosu "bulunamadı" döner (iki durum ayırt edilmez).
  Dilim 2 ile görünürlük bölüm 6'daki gibidir.

### 3. Gösterim türleri

- **Sayı:** özet raporda genel toplam satırındaki ilk ölçü (para birimi başına ayrı), tablo raporunda eşleşen kayıt sayısı.
- **Çubuk:** özet raporun grupları ve ilk ölçüsü. Gruplanmamış rapora bağlanamaz (kayıtta reddedilir).

Huni ve liste türleri sonraki dilimlerdedir.

### 4. Sınırlar

Kullanıcı başına en fazla 20 pano, panoda en fazla 12 bileşen (her bileşen bir rapor çalıştırır; rapor başına okuma sınırı ADR-0035'teki
gibidir). Bileşen verisi bileşen başına ayrı istekle gelir; ekran bileşenleri birbirinden bağımsız yükler.

### 5. Denetim

Pano oluşturma, güncelleme ve silme `core.dashboard` kaydı olarak denetime yazılır (ad ve bileşenlerin okunabilir özeti).

### 6. Paylaşım (dilim 2)

- Sahibi panoyu **özel**, **seçilen kullanıcılar** ya da **firma** olarak paylaşır. Kalıp kayıtlı raporlarınkiyle aynıdır (ADR-0035 dilim 3:
  `ReportVisibility`, paylaşım tablosu, her okumada yeniden hesaplanan görünürlük sorgusu). Yeni izin yok: paylaşmak mevcut
  `core.reports.share` iznini ister; izin tek başına yetmez, çağıran panonun sahibi de olmalıdır.
- Paylaşılan kişi panoyu **görür**; adı, bileşenleri, paylaşımı yalnızca sahibi değiştirir ve yalnızca sahibi siler. Görünen ama başkasına
  ait panoda yazma 403, görünmeyen panoda 404 döner. Paylaşım listesini (kimlerle) yalnızca sahibi görür.
- Paylaşım **veri erişimi vermez** ve raporu da paylaşmaz: bileşen verisi bakan kişinin yetkisiyle gelir; bakan kişi bileşenin raporunu
  göremiyorsa bileşen veri vermez (rapor adı boş, veri ucu 404). Ekip panosu kuran kişi raporlarını da ayrıca paylaşır; ekran bunu paylaşım
  panelinde söyler. Panoyu paylaşınca raporları kendiliğinden paylaşmak bilerek yapılmadı: bir raporun kimlere açıldığı tek yerden
  (rapor paylaşımı) okunabilmeli.
- Paylaşım kaldırılınca ya da pano özel yapılınca erişim bir sonraki istekte biter (önbellek yok). Seçilen kullanıcılar firmanın etkin
  üyesi olmalıdır (destek üyeliği sayılmaz); sahibin kendisi listeye yazılmaz.
- **Rol/departman ile paylaşım bu dilimde yok.** Epik "rol/departman" diyor, ama kayıtlı raporlarda da yalnızca kullanıcı ve firma
  düzeyi var: `ReportShare` yalnızca `UserId` taşır, role ya da iş birimine göre görünürlük hesaplayan ortak bir altyapı yok. Panoya
  özel bir rol/birim paylaşımı yazmak aynı kuralı iki yerde iki ayrı biçimde tutmak olurdu. Rol ve iş birimi (BusinessUnit/Team) hedefli
  paylaşım, rapor ve pano için **birlikte** ve tek altyapıyla dilim 4'te (ADR-0035 A-11 ekip/departman kırılımıyla) ele alınır.

### 7. Varsayılan pano (dilim 2)

- Kullanıcı **görebildiği** panolardan birini kendi varsayılanı yapar (kendisinin ya da kendisiyle paylaşılan). Kişiye özeldir: panoyu ve
  sürümünü değiştirmez, başkasının varsayılanını etkilemez; `core.reports.read` yeter. Kullanıcı başına tek varsayılan vardır, yenisi
  eskisinin yerine geçer.
- Pano listesi varsayılanı başta, kalanları ada göre verir; ekran varsayılanı işaretler.
- Kullanıcının erişimi bitince (paylaşım kaldırıldı) varsayılan işareti de silinir; pano yeniden paylaşılırsa kendiliğinden varsayılan
  olmaz.
- Varsayılan seçimi de `core.dashboard` denetim kaydına yazılır (`isDefault`; kaydın kullanıcısı seçimi yapandır).

## Uç noktalar

| Uç | İzin | Not |
|---|---|---|
| `GET /api/dashboards` | `core.reports.read` | Çağıranın görebildiği panolar (kendisinin, paylaşılan, firmaya açık); varsayılanı başta |
| `GET /api/dashboards/{id}` | `core.reports.read` | Bileşenler sırasıyla; görünmeyen raporun adı boş |
| `POST /api/dashboards` | `core.reports.manage` | Ad + bileşen listesi |
| `PUT /api/dashboards/{id}` | `core.reports.manage` | Adı ve bileşen listesini bütün olarak değiştirir; `expectedVersion` uyuşmazsa 409; sahibi değilse 403 |
| `DELETE /api/dashboards/{id}` | `core.reports.manage` | Sahibi değilse 403 |
| `PUT /api/dashboards/{id}/sharing` | `core.reports.share` | `{ visibility: private\|users\|tenant, users, expectedVersion }`; sahibi değilse 403, geçersiz `visibility` 400, kullanıcı hatası 422 (`users`) |
| `PUT /api/dashboards/{id}/default` | `core.reports.read` | `{ isDefault }`; çağıranın kendi varsayılanı; pano görünmüyorsa 404 |
| `GET /api/dashboards/{id}/widgets/{widgetId}/data` | `core.reports.read` | Raporun çalıştırma sonucu; rapor görünmüyorsa 404 |

Doğrulama hatası 422 ve alan hatasıyla döner (`name`, `widgets`, `users`). Pano özeti `visibility`, `ownerUserId`, `ownerName`, `isOwner`,
`isDefault` ve (yalnızca sahibine) `sharedWithCount` taşır; ayrıntıda `sharedWith` yalnızca sahibine dolu gelir.

## Seçenekler

1. **Bileşen kendi tanımını taşır (rapor tanımının kopyası).** Rapor merkezinden bağımsız olur ama doğrulama ve sürüm kayması iki yerde
   yönetilir. Reddedildi.
2. **Bileşen kayıtlı rapora bağlanır (bu ADR).** Tek tanım, tek yetki yolu; raporu değiştirmek panoyu da günceller.
3. **Panolar CRM modülünde.** Veri kümeleri bütün modüllerden gelir; pano rapor merkezi gibi Core'a aittir.

## Dilimler

| Dilim | İçerik |
|---|---|
| 1a | Model, servis, uç noktalar, sayı ve çubuk türleri (bu ADR) |
| 1b | Ekran: pano listesi, pano görünümü, bileşen ekleme ve sıralama |
| 2 | Paylaşım (özel, seçilen kullanıcılar, firma) ve kişiye özel varsayılan pano (teslim edildi) |
| 3 | Canlı yenileme: aralıkla (teslim edildi, 17.11.2026); iş olayıyla yenileme (sunucudan itme) ayrı karar ister, kodlanmadı |
| 4a | Rol, ekip ve iş birimi hedefli paylaşım (rapor ve pano birlikte, bölüm 6); kodlandı (17 Kasım 2026; aşağıda "Dilim 4a") |
| 4b | Ekip/departman kırılımı (ADR-0035 A-11 ile): yapıldı (18 Kasım 2026; aşağıda "Dilim 4b") |

## Dilim 4a: rol, ekip ve iş birimi hedefli paylaşım (tasarım ve uygulama, 17 Kasım 2026)

Kapsam kararı (kullanıcı, 17 Kasım): önce yalnızca hedefli paylaşım; ekip/departman kırılımı (ADR-0035 A-11) çift sayım kararı gelene kadar
bekler. İş birimi hedefinde **alt birimler dahildir**.

- **Tek altyapı, iki sahip.** Rapor ve pano paylaşımı aynı hedef modelini kullanır: paylaşım satırına hedef türü eklenir
  (`User`, `Role`, `Team`, `BusinessUnit`) ve hedef kimliği tutulur. Bugünkü `ReportShares.UserId` ve `DashboardShares.UserId` sütunları
  `TargetKind = User` ile aynen kalır; geriye uyumlu migration yalnızca sütun ekler (varsayılan `User`), önceki sürümün kodu yeni şemada
  çalışır. Yeni görünürlük değeri gerekmez: `Users` kalır, "seçilen kullanıcı ve gruplar" anlamına gelir (arayüzde "Seçilen kişi ve gruplar").
- **Görünürlük her okumada yeniden hesaplanır** (önbellek yok, bölüm 6 ile aynı): çağıranın etkin üyeliğinin rolleri (`MembershipRole`), ekipleri
  (`TeamMember`) ve iş birimi (üst birimleri dahil, yani çağıranın birimi hedef birimin kendisi ya da altındaysa) paylaşım satırlarıyla eşlenir.
  Bu eşleme tek bir ortak sorgu yardımcısındadır; `ReportCenter` ve `Dashboards` ikisi de onu çağırır, kural iki yerde yazılmaz.
- **Hedef doğrulaması yazarken:** hedef firmada var ve (birim için) etkin olmalı, kullanıcı türü `groups` ile yazılamaz (kullanıcılar `users` ile
  gelir), aynı hedef iki kez yazılmaz (tekrar yok sayılır), bir paylaşımda en fazla 50 grup, kullanıcı ve grup birlikte boş olamaz. Hedef silinirse
  (rol, ekip, birim) paylaşım satırı yabancı anahtarla düşmez; görünürlük sorgusu hedef kimliğini üyelikten çözdüğü için artık satır etkisizdir.
- **Veri erişimi yine verilmez:** hedefli paylaşım yalnızca raporu ya da panoyu görme hakkıdır; bileşen verisi bakan kişinin kendi yetkisiyle
  gelir (bölüm 6).
- **Yetki:** değişmez; paylaşmak `core.reports.share` ve sahiplik ister. Hedef listesi (rol, ekip, birim) paylaşım panelinde yalnızca
  sahibine ve yalnızca paylaşım iznine sahip olana sunulur.
- **Denetim:** paylaşım değişikliği mevcut `core.report` ve `core.dashboard` denetim kayıtlarında hedef türü ve kimlikleriyle yazılır.
- **Uygulama notu:** migration (`TargetedSharing`), model anlık görüntüsü ve tasarım dosyası bu oturumda .NET SDK olmadığı için elle yazıldı
  (`dotnet ef` çalıştırılamadı); doğrulayıcı CI'dır (derleme, migration uygulama, `migration-compat`). Sütun `TargetKind` (`nvarchar(20)`,
  varsayılan `User`) eklenir, `UserId` hedef kimliği olarak kalır; benzersiz indeks `(TenantId, ReportId|DashboardId, TargetKind, UserId)` olur.
- **Testler (dört test kuralı):** mutlu yol (ekip üyesi panoyu görür, alt birimdeki üye görür), doğrulama (bilinmeyen hedef, tekrar,
  50 sınırı), tenant izolasyonu (başka firmanın rol/ekip/birimi hedef olamaz), yetki (paylaşım izni olmayan ya da sahibi olmayan
  hedef yazamaz; hedef grubunun dışındaki kullanıcı 404 alır). Testler `ReportCenterTests.TargetedSharing.cs` içindedir.

## Ekran (dilim 1b)

- `/raporlar/panolar`: çağıranın panoları (kutu listesi) ve "Pano oluştur" formu. Rapor merkezinin başlığındaki "Panolar" düğmesiyle açılır;
  ayrı menü girdisi yoktur (rapor merkezi tek ekranlı uygulamadır).
- `/raporlar/panolar/{id}`: sayı bileşenleri üstte KPI kutuları (`DashKpi`), çubuk bileşenleri altta kartlar (`BarChart`). Yeni sınıf ya da
  token eklenmedi; CRM panosunun `dash-*` sınıfları kullanılır.
- Çubuk grafik en fazla 10 grubu çizer; birden çok para birimi varsa yalnızca ilki çizilir ve not düşülür (para birimleri toplanamaz).
- Düzenleme tek formdur: ad, bileşen satırları (rapor, gösterim, başlık, yukarı/aşağı, kaldır). Kaydedilene kadar pano değişmez. Rapor
  seçicisi çağıranın görebildiği ilk 100 raporu listeler.
- Bakan kişinin artık göremediği raporun bileşeni "Bu rapor artık size görünmüyor" yazar ve düzenlemeye alınmaz; kaydedince panodan düşer.

## Ekran (dilim 2)

- Pano görünümünde başlığın altında rozetler: "Varsayılan", (sahibi değilse) "Paylaşılan", "Sahibi: …" ve (sahibine) "Paylaşım: …".
  Rozetler mevcut `header-meta` ve `badge` sınıflarıyla çizilir; yeni sınıf ya da token eklenmedi.
- "Varsayılan yap / Varsayılanı kaldır" düğmesi panoyu görebilen herkese açıktır. "Paylaş" yalnızca sahibine ve `core.reports.share` izni
  olana, "Düzenle" yalnızca sahibine görünür; paylaşılan pano salt görüntülenir ve bunu bir satırla söyler. Arayüzde gizlemek yetki
  değildir: aynı kural sunucudadır (403).
- Paylaşım paneli rapor çalıştırma ekranındakiyle aynıdır (üç seçenek, kullanıcı arama ve işaretleme; `report-builder-section`,
  `report-member-list`) ve paylaşımın raporları paylaşmadığını söyler.
- Pano listesinde karo, durumunu metin satırıyla verir ("★ Varsayılan", "Paylaşılan · Sahibi: …", kendi panosunda "Paylaşım: …").
  Karo içinde rozet sınıfı kullanılmadı: `.tile span` kuralı rozet rengini ezer, yeni CSS yazmak yerine metin tercih edildi.

## Ekran (dilim 3)

- Pano görünümünde "Otomatik yenileme" seçicisi (Kapalı, her dakika, 5 dakikada bir, 15 dakikada bir). Seçim yalnızca açık sayfa içindir,
  kaydedilmez; varsayılan kapalıdır. Her turda bileşenler kendi verisini yeniden ister (ayrı istek, bakan kişinin yetkisiyle) ve eski veri
  yanıt gelene kadar ekranda kalır; son yenileme saati yazılır. Düzenleme açıkken tur atlanır. Sunucu tarafı değişmedi.
- İş olayıyla yenileme (kayıt değişince pano güncellensin) sunucudan istemciye itme altyapısı gerektirir; bugün böyle bir kanal yok,
  karar istediği için kodlanmadı.

## Sonuçlar

- Geriye uyumlu migration: yalnızca iki yeni tablo.
- Rapor silmek, o rapora bağlı pano bileşenlerini de siler.
- Dilim 2 migration'ı da yalnızca ekler: `Dashboards.Visibility` sütunu (varsayılan `Private`) ve iki yeni tablo; önceki sürümün kodu yeni
  şemada çalışır.
- Ekip panosu için pano ve raporları ayrı ayrı paylaşılır; rol ya da departmana göre paylaşım dilim 4'ü bekler.

## Açık sorular

- **Firma geneli varsayılan pano gerekli mi?** Bugün varsayılan yalnızca kişiseldir. Yöneticinin firmadaki herkes (ya da bir rol) için
  varsayılan pano belirlemesi istenirse kararlar gerekir: kim belirler (hangi izin), kişisel seçim firma varsayılanını ezer mi, pano
  firmaya açık olmak zorunda mı, hedef kullanıcı raporları göremiyorsa ne gösterilir. Kodlanmadı; pilot geri bildirimiyle karar verilecek.
- Panoyu paylaşırken "bağlı raporları da paylaş" kolaylığı sunulsun mu (bölüm 6'da bilerek yapılmadı)?

## Dilim 4b: departman ve ekip kırılımı (18 Kasım 2026)

Kararlar (kullanıcı, 18 Kasım): **çift sayım yok, tek birincil üyelik**; boyutlar **iş birimi (departman) + ekip**.

- **Türetilmiş sütun.** Veri kümesi `DatasetColumnKind.BusinessUnit` / `Team` sütunu bildirir ve `DerivedFrom` ile aynı kümedeki kullanıcı (ya da kullanıcı
  tutan lookup) sütununa bağlar. Değer kayıtta saklanmaz: `ReportRunner` kaydı okuduktan sonra, hesaptan önce, yalnızca raporun kullandığı türetilmiş sütun için
  `IBusinessUnitDirectory.UnitsOfUsersAsync` ve yeni `ITeamDirectory.PrimaryTeamsOfUsersAsync` ile bellekte doldurur. Gruplama, süzgeç, tablo sütunu, ayrıntı
  (drill), pano bileşeni ve dışa aktarma aynı yoldan çalışır; ayrı pano koduna gerek yoktur.
- **Çift sayım kuralı.** Her kayıt tek kovaya girer: sorumlunun tek departmanı ve **birincil ekibi = en önce katıldığı ekip** (üyelik kimliği Guid v7, zaman sıralı).
  Ekibi/departmanı olmayan sorumlunun kaydı boş kovadadır. Böylece kovaların toplamı kayıt sayısına eşittir. Bugünkü sorumluya göre hesaplanır (tarihsel sahiplik
  yoktur, ADR-0035 ile aynı sınır).
- **Alt birimler.** Kova tam departmandır (etiket "Merkez › Satış" yoludur); "alt birimler dahil" toplamı yapılmadı: üst birim satırı yalnız kendi doğrudan üyelerini sayar.
  Süzgeçte birden çok birim `In` ile seçilir. Birim ağacı toplamı ayrı bir iş olarak kaldı.
- **Yetki.** Kayıtlar yine kayıt boru hattından (izin, kapsam, hassas alan) geçer; kaynak sütun hassas alan nedeniyle görünmüyorsa türetilen de görünmez. Departman ve ekip
  adları erişim yetkisi sayılmaz (`IBusinessUnitDirectory` ile aynı).
- **Sözleşme.** `Core.Contracts/Tenancy/ITeamDirectory` (ekip listesi ve birincil ekip), `TeamDirectory` Infrastructure'da. Şema değişikliği ve migration yoktur.
- **CRM.** Fırsat, müşteri adayı, teklif ve firma veri kümelerine "Sorumlunun departmanı" (`ownerUnit`) ve "Sorumlunun ekibi" (`ownerTeam`); hazır raporlar
  "Departmana göre kazanılan fırsatlar" ve "Ekibe göre kazanılan fırsatlar".
- **Testler.** `DatasetCatalogTests` (türetme doğrulaması), `ReportCenterTests.OrgBreakdown.cs` (kırılım ve çift sayım, doğrulama, tenant izolasyonu, kayıt kapsamı ve izin).
- **Yapılmadı.** Ekip kırılımı için kullanıcının birincil ekibini seçme (şimdilik ilk katıldığı ekip); alt birimler dahil toplam; diğer modüllerin veri kümeleri.
