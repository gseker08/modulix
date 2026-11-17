# ADR-0054: Kullanıcı panoları ve pano bileşenleri

- **Durum:** Kabul edildi, 17 Kasım 2026. Dilim 1a (model, servis, uç noktalar) teslim edildi; ekran dilim 1b'de.
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

Bileşen kendi sorgusunu taşımaz, **kayıtlı bir rapora** bağlanır. Süzgeç, gruplama ve ölçü raporun tanımındadır; kullanıcı raporu
oluşturucuda bir kez kurar, panoya koyar. Böylece doğrulama, hassas alan ve kapsam kuralları tek yerde (rapor çalıştırıcı) kalır.

### 2. Yetki

- Pano **veri erişimi vermez.** Bileşen verisi `IReportCenter.RunAsync` ile, bakan kişinin kayıt izni, veri kapsamı ve hassas alan
  kurallarıyla üretilir. Bakan kişi raporu artık göremiyorsa (paylaşım geri alındı) bileşen veri vermez; pano kalır.
- Yeni izin yok: panoyu açmak ve bileşen verisini okumak `core.reports.read`, pano oluşturmak, düzenlemek ve silmek
  `core.reports.manage` ister.
- Dilim 1'de pano yalnızca sahibine görünür. Başkasının ya da başka firmanın panosu "bulunamadı" döner (iki durum ayırt edilmez).

### 3. Gösterim türleri

- **Sayı:** özet raporda genel toplam satırındaki ilk ölçü (para birimi başına ayrı), tablo raporunda eşleşen kayıt sayısı.
- **Çubuk:** özet raporun grupları ve ilk ölçüsü. Gruplanmamış rapora bağlanamaz (kayıtta reddedilir).

Huni ve liste türleri sonraki dilimlerdedir.

### 4. Sınırlar

Kullanıcı başına en fazla 20 pano, panoda en fazla 12 bileşen (her bileşen bir rapor çalıştırır; rapor başına okuma sınırı ADR-0035'teki
gibidir). Bileşen verisi bileşen başına ayrı istekle gelir; ekran bileşenleri birbirinden bağımsız yükler.

### 5. Denetim

Pano oluşturma, güncelleme ve silme `core.dashboard` kaydı olarak denetime yazılır (ad ve bileşenlerin okunabilir özeti).

## Uç noktalar

| Uç | İzin | Not |
|---|---|---|
| `GET /api/dashboards` | `core.reports.read` | Çağıranın panoları |
| `GET /api/dashboards/{id}` | `core.reports.read` | Bileşenler sırasıyla; görünmeyen raporun adı boş |
| `POST /api/dashboards` | `core.reports.manage` | Ad + bileşen listesi |
| `PUT /api/dashboards/{id}` | `core.reports.manage` | Adı ve bileşen listesini bütün olarak değiştirir; `expectedVersion` uyuşmazsa 409 |
| `DELETE /api/dashboards/{id}` | `core.reports.manage` | |
| `GET /api/dashboards/{id}/widgets/{widgetId}/data` | `core.reports.read` | Raporun çalıştırma sonucu; rapor görünmüyorsa 404 |

Doğrulama hatası 422 ve alan hatasıyla döner (`name`, `widgets`).

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
| 2 | Paylaşım (kullanıcı, rol/departman) ve varsayılan pano |
| 3 | Canlı yenileme (aralıkla ya da iş olayıyla) |
| 4 | Ekip/departman kırılımı (ADR-0035 A-11 ile) |

## Sonuçlar

- Geriye uyumlu migration: yalnızca iki yeni tablo.
- Rapor silmek, o rapora bağlı pano bileşenlerini de siler.
- Paylaşım gelene kadar pano kişiseldir; ekip panosu dilim 2'yi bekler.
