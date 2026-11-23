# CRM'de eksik süreç ve kurallar: epic listesi (15 Kasım 2026)

Kaynak: bir yerli CRM ürününün 3:24 dakikalık tanıtım videosunun bölüm listesi ve açıklaması
(altyazıya erişilemedi; ayrıntılar yalnızca bölüm başlıklarından ve "öne çıkanlar" maddelerinden çıkarıldı).
Her madde Modulix koduyla karşılaştırıldı; "var" denenler kodda bulunan sınıf ve ekranlara dayanır.
Bu belge tek başına hiçbir şeyi uygulamaz; her epic başlamadan önce kendi ADR'sini alır.

## Videoda olup Modulix'te zaten olanlar

| Videodaki özellik | Modulix karşılığı |
|---|---|
| Logolu çalışma alanı | Firma logosu (`Admin/Settings.razor`) |
| Esnek listeler ve görünümler | Kişisel liste görünümleri (ADR-0036), `ViewTabs`, `ColumnPicker` |
| "Bugün yapacaklarım" | `Crm/Reporting` MyDay, CRM panosu |
| Web formları | Müşteri adayı formları (`LeadForm`, ADR-0014) |
| Segmentler | `Modulix.Modules.Marketing` `SegmentEngine` (ADR-0025) |
| Kod yazmadan alan, form, iş kuralı | Özel alanlar, Form tasarımı (ADR-0021), alan kuralları (`Core/FieldRules`) |
| Rol tabanlı yetki, koyu tema | Rol şablonları (ADR-0005), kapsam/alan yetkisi, açık/koyu tema |
| İzin (rıza) kontrolü | İletişim izni kaydı, gönderimden önce izin süzgeci (ADR-0025) |
| Çoklu dil | Sürüyor (ADR-0034, aşama 4); yeni epic gerekmez |

## Eksikler: epic'ler

### E1. Otomatik müşteri adayı skorlama
- **Videoda:** "Otomatik lead skorlama".
- **Modulix'te:** yok (kodda skor alanı ya da kuralı bulunmuyor).
- **Süreç ve kurallar:** firma puan kuralları tanımlar (alan koşulu → puan: sektör, şehir, çalışan sayısı; davranış → puan:
  web formu, kampanya yanıtı, tamamlanan aktivite, teklif görüntüleme). Puan kayıt boru hattında yeniden hesaplanır,
  her değişiklik tarihçeye yazılır. Eşik aşılınca iş olayı (`crm.lead.score-changed`) yayınlanır; atama, durum değişikliği
  ve bildirim otomasyonla yapılır (kod sabit kural içermez). Zamanla puan azalması (ör. 30 gün hareketsizlik) ayarlanabilir.
- **Dilimler:** 1) kural modeli + hesaplama + puan sütunu/sıralama, 2) davranış puanları (olay abonesi), 3) azalma ve eşik olayı,
  4) puan açıklaması ("neden 72?") kartta.
- **Bağımlılık:** yok; karar beklemeden başlanabilir.

### E2. Canlı panolar ve pano bileşenleri
- **Videoda:** "Canlı satış panoları".
- **Modulix'te:** sabit pano ekranları var (CRM panosu, satış özeti); kullanıcının kurduğu bileşenli pano ve canlı yenileme yok
  (ADR-0035 kalanları).
- **Süreç ve kurallar:** kullanıcı ya da yönetici pano oluşturur; bileşen = rapor veri kümesi + gösterim türü (sayı, çubuk, huni,
  liste) + süzgeç. Her bileşen sorgusu kullanıcının kayıt kapsamıyla çalışır (yetki sunucuda). Pano kişisel ya da paylaşımlı
  (rol/departman) olabilir. Yenileme belirli aralıkla ya da iş olayıyla.
- **Dilimler:** 1) pano + bileşen modeli, sayı/çubuk bileşenleri, 2) paylaşım ve varsayılan pano, 3) canlı yenileme,
  4) ekip/departman kırılımı (ADR-0035 A-11 ile birleşir).
- **Bağımlılık:** rapor veri kümeleri (var).

### E3. Çok kanallı gönderim ve İYS denetimi (e-posta, SMS, WhatsApp)
- **Videoda:** pazarlama modülünde e-posta, SMS, WhatsApp; "İYS ve KVKK izinleri her gönderimden önce kontrol edilir".
- **Modulix'te:** kanal ve izin modeli var; gerçek sağlayıcı yok (yalnızca `fake`), gönderim bloklu, İYS entegrasyonu yok (ADR-0025 K9).
- **Süreç ve kurallar:** her ticari iletiden önce İYS'de alıcı izni sorgulanır, sonuç ve zaman kanıt olarak saklanır; izin yoksa
  ileti açılmaz. İşlem amaçlı ileti İYS'ye takılmaz ama pazarlama içeremez. Sağlayıcı firma başına yapılandırılır (anahtarlar
  gizli depoda). Teslim, okunma, geri dönme durumları webhook ile gelir.
- **Dilimler:** 1) gerçek e-posta sağlayıcısı + `List-Unsubscribe`, 2) İYS sorgu/izin eşitleme, 3) SMS, 4) WhatsApp (şablon onayı dahil).
- **Bağımlılık:** hukuk kararı (İYS/KVKK metinleri) ve sağlayıcı seçimi. Karar gelmeden 1. dilim dışında başlanmaz.

### E4. Müşteri yolculukları
- **Videoda:** "Segmentler ve müşteri yolculukları".
- **Modulix'te:** segment ve tek seferlik kampanya çalıştırma var; zamanla ilerleyen çok adımlı yolculuk yok.
- **Süreç ve kurallar:** yolculuk bir giriş koşuluyla (segmente girme, form doldurma, aday oluşma) başlar; adımlar: ileti gönder,
  bekle (süre ya da iş günü takvimi), koşula göre dallan (açtı mı, yanıtladı mı, puan eşiği), görev aç, sahibine bildir, çık.
  Bir kişi aynı yolculuğa aynı anda bir kez girer; çıkış koşulu (abonelikten çıkma, fırsata dönüşme) her adımda denetlenir;
  her gönderim E3'teki izin denetiminden geçer.
- **Dilimler:** 1) model + bekle/gönder/çık adımları (`fake` sağlayıcıyla), 2) dallanma, 3) yolculuk raporu (adım dönüşümü).
- **Bağımlılık:** E3 1. dilim gerçek gönderim için; model `fake` ile önceden yapılabilir. Otomasyon motoru (ADR-0027) yeniden kullanılır.

### E5. Kod yazmadan özel tablolar (kayıt türleri)
- **Videoda:** "Kod yazmadan kendi tablo ve alanlarınız".
- **Modulix'te:** özel alan var; firmanın kendi kayıt türünü (tablo) tanımlaması yok.
- **Süreç ve kurallar:** firma yöneticisi kayıt türü tanımlar (ad, alanlar, ilişki alanları, numara serisi). Kayıtlar ortak bir
  tenant tablosunda tutulur (TenantId + RLS); yeni fiziksel tablo açılmaz. Liste, form (ADR-0021), izinler (`custom.<anahtar>.read`),
  kapsam, denetim, içe/dışa aktarma ve otomasyon kayıt boru hattından otomatik gelir. Tür silinmez, arşivlenir.
- **Dilimler:** 1) tür tanımı + kayıt deposu + liste/form, 2) ilişki (lookup) ve CRM kartında ilgili kayıtlar sekmesi,
  3) otomasyon/rapor kaynağı olarak açılması.
- **Bağımlılık:** büyük mimari karar (depolama biçimi, sorgu/sıralama performansı); önce ADR.

### E6. Görsel süreç tasarımcısı
- **Videoda:** "Kodsuz süreç tasarımcısı".
- **Modulix'te:** otomasyon kuralları, dallanma (ADR-0027 4c-5) ve onay süreçleri var; düzenleme form tabanlı, görsel akış tuvali yok.
- **Süreç ve kurallar:** aynı motor; tasarımcı yalnızca bir düzenleme yüzeyidir. Tetikleyici → koşul → aksiyon/onay/bekle düğümleri
  sürükle-bırakla bağlanır; kaydetmeden önce doğrulanır (ulaşılamayan düğüm, döngü, eksik alan). Sürümlenir; çalışan örnekler
  eski sürümle biter. Deneme çalıştırması (`/preview`) yazmaz.
- **Dilimler:** 1) mevcut kuralı salt okunur akış olarak çizme, 2) düzenleme, 3) sürüm ve deneme çalıştırması.
- **Bağımlılık:** E4 aynı akış modelini kullanırsa birlikte tasarlanmalı.

### E7. Yapay zekâ asistanı (sohbet, cümleyle liste ve pano)
- **Videoda:** "Bu ay kaç aday geldi?" sohbeti; cümleyle liste ve pano oluşturma; "veriler dış yapay zekâ servisine gönderilmez".
- **Modulix'te:** `Modulix.AI` boş proje; ADR-0047 ajan kuralları taslak; AI erişimi menüsü var.
- **Süreç ve kurallar:** asistan yalnızca kullanıcının yetkisi ve kapsamıyla modül servislerini (rapor veri kümeleri, liste sorguları)
  araç olarak çağırır; ham SQL üretmez. İlk sürüm salt okur; yazma ADR-0047 tablosuna uyar. Her soru/yanıt/araç çağrısı denetim
  kaydına yazılır. Model sağlayıcısı firma başına yapılandırılır; "veri dışarı çıkmaz" isteyen firma için yerel/on-prem model seçeneği.
  Cümleden liste = süzgeç + sütun önerisi üretir, kullanıcı onaylayınca kişisel görünüm olarak kaydedilir; cümleden pano E2 bileşenine çevrilir.
- **Dilimler:** 1) sağlayıcı soyutlaması + araç kataloğu + denetim, 2) sohbet ekranı (salt okuma soruları), 3) cümleden liste görünümü,
  4) cümleden pano (E2 sonrası).
- **Bağımlılık:** ADR-0047'nin kabulü, model/sağlayıcı ve maliyet kararı.

### E8. Çalışma alanı rengi ve menü düzeni
- **Videoda:** "Size özel, logolu ve renkli çalışma alanı"; "Formlar, menü ve iş kuralları".
- **Modulix'te:** logo var; firma vurgu rengi (dilim 1) ve rol bazlı menü düzeni (dilim 2, öğe düzeyinde sıra ve gizleme; dilim 3, bölüm sırası; dilim 4, uygulama paneli grubu içinde uygulama sırası) var.
- **Süreç ve kurallar:** firma yalnızca izin verilen tokenları (`--mx-brand` vb.) değiştirir; kaydetmeden önce açık ve koyu temada
  AA kontrastı (4.5:1) sunucuda doğrulanır, geçmeyen renk reddedilir. Menüde modül/öğe sırası ve gizleme rol bazında yapılır;
  gizlemek yetki değildir (uç noktalar yine izinle korunur).
- **Dilimler:** 1) vurgu rengi + kontrast doğrulama, 2) rol bazlı menü düzeni.
- **Not:** tasarım kurallarına (renk yalnızca tokenlardan) aykırı değil, ama tasarım sisteminde "firma tarafından değiştirilebilir
  token" listesi tanımlanmalı.
- **Durum (22 Kasım 2026):** dilim 0 (tasarım sisteminde firmanın değiştirebileceği tokenlar, PR #509), dilim 1 (vurgu rengi, PR #513) ve dilim 2
  (rol menüsü: sunucu PR #525, menüye uygulama ve düzenleme ekranı PR #528, kayıtta satır koruma PR #533) teslim edildi; karar kaydı
  [ADR-0055](adr/0055-calisma-alani-rengi-ve-rol-menusu.md). Kalan: uygulama paneli gruplarının sırası ile bölüm ve uygulama gizleme (ADR'de "Yapılmadı").

### E9. Birleşik 360° müşteri zaman çizelgesi
- **Videoda:** "360° müşteri profili: bütün geçmiş tek sayfada".
- **Modulix'te:** kısmi. Firma kartında ilişkili kayıt sekmeleri var; aktivite, teklif, sipariş, sözleşme, destek ve kampanya yanıtlarını
  tek zaman çizelgesinde birleştiren modüller arası görünüm bulunamadı (doğrulanmalı).
- **Süreç ve kurallar:** her modül kendi sözleşme projesinden bir zaman çizelgesi kaynağı sunar (ADR-0044); kart bunları birleştirir,
  modüller birbirinin tablosunu okumaz. Her satır kullanıcının o modüldeki kapsamıyla süzülür; lisansı olmayan modülün satırı gelmez.
- **Dilimler:** 1) sözleşme + CRM kaynakları, 2) Sipariş/Sözleşme/Destek/Pazarlama kaynakları, 3) süzgeç ve özet şeridi.
- **Durum (17 Kasım 2026):** dilim 1 teslim edildi; tasarım [ADR-0053](adr/0053-musteri-zaman-cizelgesi.md)'te.

## Videoda olmayan ama karşılaştırmada görülen açıklar

Atama kuralları/bölge (round robin, territory), komisyon, fatura/e-Fatura ve tahsilat, saha ziyareti, telefon entegrasyonu bu videoda
geçmiyor; ADR-0014, 0015, 0037 ve `envanter.md`'de zaten açık iş olarak duruyor.

## Önerilen sıra

1. E1 skorlama (küçük, karar istemez, E4 ve E7'yi besler)
2. E2 panolar (rapor altyapısı hazır; A-11 ile birleşir)
3. E9 zaman çizelgesi (ADR-0044 sözleşmeleriyle doğal adım)
4. ~~E8 renk/menü (küçük)~~ (yapıldı, 22 Kasım 2026: [ADR-0055](adr/0055-calisma-alani-rengi-ve-rol-menusu.md))
5. E4 yolculuk (önce `fake` sağlayıcıyla) ve E6 tasarımcı (aynı akış modeli)
6. E5 özel tablolar (önce ADR)
7. E3 gönderim/İYS ve E7 yapay zekâ (hukuk ve sağlayıcı kararı bekliyor)
