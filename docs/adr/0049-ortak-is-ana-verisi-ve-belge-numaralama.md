# ADR-0049: Ortak iş ana verisi ve belge numaralama

- **Durum:** Dilim 1 (belge numaralama) 15 Kasım 2026'da teslim edildi (PR #395). Dilim 6a (firmanın tatil listesi ve takvim hesabı Core'da) 15 Kasım 2026'da teslim edildi (PR #397). Dilim 6b (destek SLA takvimi firmanın listesini uygular) ve 6c (firmanın haftalık mesai saatleri, firma iş takvimi) 15 Kasım 2026'da teslim edilir. Dilim 5'in kararları 22 Kasım 2026'da verildi; dilim 5a (kur Core'da) 22 Kasım 2026'da kodlandı (aşağıda "Dilim 5a uygulaması"). Diğer dilimler öneridir; "Açık kararlar" yanıtlanmadan kodlanmaz.
- **Kaynak:** [Core mimari değerlendirmesi](../core-mimari-degerlendirme-2026-11.md) H4, H5, H6; yol haritası Ç8.1 (seri yönetimi).

> Numara notu: 15 Kasım 2026'da `main`'de en yüksek numara 0047'ydi, 0048 açık PR #394'te kullanılıyordu. Birleştirmeden önce numara yeniden kontrol edilmeli.

## Bağlam

Ürün ilkesi: ortak ana veri Core'a aittir, bir kez girilir, bütün modüller kullanır; müşteri herhangi bir modülle başlayabilir. Firma, kişi ve adres için bu sağlanıyor (ADR-0006, ADR-0012). Üç ortak kavram için sağlanmıyor:

| Kavram | Bugün | Sorun |
|---|---|---|
| Belge numarası | Dört kopya rastgele sonekli üretici (`QuoteRevisionRules.NewNumber`, `OrderRules.NewNumber`, `JobHistory.NewNumber`, `TicketNumbers.New`: `TK-yyyyMMdd-XXXXXX`); sefer kendi "en büyük + 1" sorgusunu yazmıştı | Sıralı seri isteyen her modül eşzamanlılığı yeniden çözüyor. Seferde aynı anda açılan iki kayıttan biri benzersizlik dizinine çarpıp hata alıyordu. Fatura ve irsaliye gibi kesintisiz seri isteyen belgeler gelmeden ortak servis gerekli. |
| Döviz kuru, vergi oranı, birim | Kur CRM'de (`crm` şeması); Orders kuru tekliften kopyalıyor | CRM'siz Lojistik ya da İK müşterisinin kuru yok; masraf, bordro ve faturalanabilir kalem (ADR-0037) de kur isteyecek. |
| Çalışma takvimi | Destek'te `WorkCalendar` (mesai), İK'da `LeaveCalendar` (tatil) | Aynı firmanın tatili iki yerde tanımlanıyor; onay eskalasyonu ve teslim süresi üçüncü kopyayı isteyecek. |

## Karar

Bu üç kavram Core'a alınır; modüller sözleşme üzerinden kullanır. Dilimlerle ilerlenir, her dilim geriye uyumlu migration ile gelir.

### Dilim 1: belge numaralama (teslim edildi)

- **Beyan:** modül serilerini `IModule.NumberSeries` ile bildirir (`NumberSeriesDefinition`: anahtar, ad, önek, basamak sayısı, sıfırlama). Anahtar modül anahtarıyla başlar ve değişmez. Katalog açılışta doğrular (başka modülün anahtarı, yinelenen seri, geçersiz önek reddedilir).
- **Sözleşme:** `Modulix.Core.Contracts.Numbering.IDocumentNumbers.NextAsync(seriesKey, seedLastValue?)`. Beyan edilmemiş seriden ve tenant bağlamı olmadan numara verilmez.
- **Sayaç:** `core.NumberCounters` (tenant tablosu, RLS altında, `ICompanyShared`); seri ve dönem (yıl; sıfırlanmayan seride 0) başına tek satır, `UNIQUE(TenantId, SeriesKey, Period)`. Tenant dışa aktarımına ve silmesine girer.
- **Eşzamanlılık:** sayaç tek SQL yığınıyla artar. Satır yoksa aralık kilidi altında açılır, sonra `UPDATE ... OUTPUT` ile artırılıp okunur; güncelleme satırı işlem sonuna dek kilitler. Çağrı kayıt boru hattının açık işlemi içinde yapıldığı için numara kayıtla birlikte kesinleşir ya da geri alınır. Ham SQL tenant süzgecini açıkça taşır.
- **Yıl:** tenant saat dilimine göre (`ITenantClock`); yılbaşı gecesi açılan belge firmanın yılını alır.
- **Geçiş (tohum):** numarasını kendisi üreten kayıt türü ortak seriye geçerken `seedLastValue` ile o ana dek verilmiş en büyük sırayı bildirir; yalnızca sayaç açılırken sorulur. Migration'da veri taşıma gerekmez.
- **İlk kullanıcı:** sefer (`logistics.trip`, `SF-yıl-sıra`). Numara biçimi değişmedi; `Trips.Number` benzersizlik dizini ikinci güvence olarak duruyor.

### Dilim 2: seriyi firma yapılandırır

Önek, basamak sayısı ve sıfırlama firma ayarından değiştirilebilir (dinamik tasarım ilkesi); şirket başına ayrı seri (ADR-0008, `ICompanyScoped` belgeler için). Yönetim ekranı: seriler, son verilen numara, sonraki numarayı ileri alma (denetim kaydıyla). Üç adımda gelir.

#### Dilim 2a: yapılandırma ve sayacı ileri alma, API (teslim edildi)

- **Tablo:** `core.NumberSeriesSettings` (tenant verisi, RLS altında, `ICompanyShared`; `UNIQUE(TenantId, SeriesKey)`). Önek, basamak ve sıfırlama boş olabilir: boş alan modülün beyanını kullanır, varsayılana eşit değer boş saklanır. Numaralama servisi her numarada bu satırı okur.
- **Çakışmazlık:** sayaç seri ve dönem başına tutulduğu için önek ya da basamak değişikliği sayacı sıfırlamaz; sıfırlama değişince dönem değişir (yıllık seride yıl, sıfırlanmayan seride 0). Önek yalnızca büyük harf ve rakam olduğu için iki biçim aynı metni üretemez.
- **İleri alma:** yalnızca ileri ve yalnızca bu dönemin sayacı açıldıktan sonra. Sayaç yokken ileri almak, modülün geçişte eski numaralardan devam etmek için kullandığı tohumu atlayıp çakışan numara verebilirdi. Güncelleme tek ifadede `LastValue < hedef` koşuluyla yapılır; aynı anda verilen numarayla yarışmaz.
- **İzin ve denetim:** `core.numbering.manage` (migration'da `core.settings.manage` taşıyan rollere verildi); yapılandırma ve ileri alma `core.number-series` denetim kaydına yazılır. Ayar satırının sürümü iki işlemi de sıraya sokar (409).
- **Dilim 2b (teslim edildi):** Yönetim > Belge numaraları ekranı (`/yonetim/belge-numaralari`, `core.numbering.manage`). Seri listesi (modül, sıradaki numara, sıfırlama, firmaya özel mi), çekmecede önek/basamak/sıfırlama ve canlı ön izleme, "Varsayılana dön", bu dönemde numara verildiyse onaylı "Sayacı ileri al". Kurallar 2a'daki sunucu uçlarındadır; ekran yalnızca onları çağırır, çakışmada (409) güncel hâli yükler.
- **Dilim 2c (ertelendi, 16.11.2026):** şirket başına seri. Bugün kullanan belge yok: hiçbir kayıt türü `ICompanyScoped` değil, tek seri olan sefer şirketler arası ortak (`ICompanyShared`). Kullanılmayan altyapı ve migration yazılmaz; ilk şirkete ait belge (fatura, şirket bazlı teklif vb.) geldiğinde o belgeyle birlikte tasarlanır. O gün için beklenen biçim: seri beyanında "şirket başına" seçeneği, sayaç ve ayar satırında isteğe bağlı `CompanyId` (şirketi belgenin `CompanyId`'si belirler, oturumun aktif şirketi değil), yönetim ekranında şirket seçimi.

### Dilim 3: mevcut üreticilerin geçişi

Kodda dört değil sekiz kopya üretici vardı (teklif `TK`, sipariş `SP`, teslimat `TS`, operasyon işi `OP`, sözleşme `SZ`, destek talebi `TK`, fırsat `OP`, müşteri adayı `LD`). 16.11.2026 kararları:

- **Sıralı seriye geçenler (3a):** teklif, sipariş, teslimat, operasyon işi, sözleşme. Varsayılan biçim seferle aynı: `önek-yıl-sıra`, 5 basamak, yıllık sıfırlama; firma Yönetim > Belge numaraları'ndan değiştirir.
- **Önek çakışmaları:** operasyon işi `IS` oldu (eski `OP` fırsatla karışıyordu). Destek talebi geçtiğinde `DT` olacak (bugün teklifle aynı `TK`).
- **Destek talebi (3b, aşağıda teslim edildi):** gelen e-posta konudaki `[TK-…]` numarasıyla talebe bağlanıyor; rastgele sonek bunu tahmin edilemez kılıyor ve ayrıştırıcı öneki sabit arıyor. Geçişten önce gönderen denetimi incelenecek ve ayrıştırıcı firmanın önekini tanıyacak.
- **Fırsat ve müşteri adayı:** belge değil, iç takip kaydı; toplu içe aktarmayla çok sayıda açılıyor. Şimdilik rastgele kalıyor.
- **Geçmeyenler:** üç üretici tek ortak yardımcıya indi (`Core.Contracts.Numbering.RandomDocumentNumber`); davranışları değişmedi.

#### Dilim 3a: beş belge ortak seride (teslim edildi)

- **Seriler:** `crm.quote` (TK), `orders.order` (SP), `orders.delivery` (TS), `orders.job` (IS), `contracts.contract` (SZ); modüller `IModule.NumberSeries` ile beyan eder.
- **Eski kayıtlar** numarasını korur (`TK-20261101-AB12CD` gibi); veri taşıma ve migration yok. Eski numaralar rastgele olduğundan tohum verilmez, her seri 1'den başlar. Eski ve yeni biçim çakışamaz (yeni biçimde yıl 4, eskide tarih 8 hanedir).
- **Numara ne zaman alınır:** doğrulamadan sonra, kayıt eklenmeden hemen önce (`CreatedAsync`); reddedilen istek sıra harcamaz. CRM depo tabanına bunun için beklemeli `CreatedAsync` kancası eklendi.
- **Teklif revizyonu** ailenin numarasını taşır, seriden sıra almaz; çoğaltma yeni ailedir ve yeni numara alır.
- **Yönetim listesi:** firmada açık olmayan modülün serisi listelenmez ve yapılandırılamaz (404).

#### Dilim 3b: destek talebi ortak seride (teslim edildi)

İnceleme (16.11.2026): gelen mesaj, konudaki numarayı bulsa bile ancak gönderen talebin başvuran ya da alıcı adresiyse talebe eklenir; kapsam da ayrıca denetlenir. Numarayı tahmin etmek tek başına talebe mesaj sokmaz, bu yüzden sıralı numara eşleştirmede açık açmaz. Herkese açık anket numarayla değil kişiye özel bağlantıyla çalışır.

- **Seri:** `support.ticket`, önek `DT` (`DT-2026-00001`); `TK` teklifte kaldı. Eski talepler `TK-20261101-AB12CD` numarasını korur; veri taşıma ve migration yok, seri 1'den başlar.
- **Numara ne zaman alınır:** doğrulamadan ve tekrar korumasından sonra (`CreatedAsync`); reddedilen ya da tekrar sayılan istek sıra harcamaz.
- **Eşleştirme önekten bağımsız:** firma öneki değiştirebildiği ve eski talepler `TK-…` taşıdığı için konuda kalıp aranmaz. Köşeli parantezli etiketler (en çok 5; boş, boşluklu ya da 30 karakterden uzun olanlar hariç) gerçek talep numaralarıyla karşılaştırılır, sırayla ilk bulunan alınır (`InboundRules.TicketNumberCandidates`). `[acil]` gibi etiketler kayıt bulmaz.
- **Konu temizliği:** yeni talep açılırken konudan yalnızca gerçek bir talebe ait numara etiketi atılır; başka köşeli etiket konuda kalır. Hiçbir talep bulunmazsa eski `[TK-…]` kalıbı yine atılır.
- **Kabul edilen risk:** rastgele sonek, başvuranın adresini taklit eden gönderen için ek bir engeldi; sıralı numarada bu engel yok. Gönderen adresinin doğrulanması (SPF/DKIM/DMARC) e-posta alıcısının işidir ve alıcı yazılırken zorunlu tutulmalıdır (bugün alıcı kurulu değil, giriş yetkili bir entegrasyon kullanıcısının çağırdığı adaptör sözleşmesidir). Sıralı numara müşteriye firmanın talep hacmini de gösterir; istemeyen firma sayacı ileri alarak başlangıcı değiştirebilir.
- **Kalan:** fırsat ve müşteri adayı rastgele sonekli (karar yukarıda).

Aşağıdaki özgün not tarihçe için duruyor:

Teklif, sipariş, operasyon işi ve destek kaydı numaraları bugün rastgele soneklidir. Sıralı seriye geçiş kullanıcının gördüğü numara biçimini değiştirir; eski kayıtlar numarasını korur. Geçilmeyecekse dört kopya üretici en azından tek ortak yardımcıya indirilir.

### Dilim 4: taslak ve kesin numara, iptal izi (Ç8.1'in kalanı)

Kesintisiz seri isteyen belgeler (fatura, irsaliye) için: numara taslakta değil kesinleştirmede verilir; iptal edilen numara iz bırakır. İlk ihtiyaç duyan belgeyle birlikte.

### Dilim 5: para birimi, kur, vergi oranı, birim (kararlar verildi, 22 Kasım 2026)

> **Kararlar (22 Kasım 2026, ürün sahibi):** K5.1–K5.5'in hepsi aşağıdaki önerilen cevaplarla kabul edildi. Kod 5a (kur) ile başlar; 5b, 5c ve 5d kendi dilimleriyle gelir. Bu bölümdeki "Karar önerisi" ve "Tasarım" metinleri kabul edilmiş karardır.

**Bugün (kodda doğrulandı):** `ExchangeRate`, `TaxRate` ve `MeasureUnit` `Modulix.Modules.Crm` içinde (`crm` şeması, `CrmEntity` = `ITenantEntity` + `ICompanyShared`); kayıt türleri `crm.exchange-rate`, `crm.tax-rate`, `crm.measure-unit`, hepsi `crm.prices.read` / `crm.products.read` izinlerine bağlı ve ekranı CRM menüsündeki "Vergi ve kur". Okuyan tek yer `PriceCalculator` (CRM). Orders kuru tekliften kopyalar (`OrderFromQuote`: `ExchangeRate`, `ExchangeRateDate`), kendi kur tablosu okumaz. Saf hesap çekirdeği `Core.Contracts.Pricing` zaten Core'da. Para birimi listesi (`RegionalOptions.Currencies`) ve tenant varsayılan para birimi (`DefaultCurrency` ayarı) zaten Core'da. Sonuç: CRM lisansı olmayan firma kur giremez; Lojistik masrafı (ADR-0038 dilim 8) ve İK için kur okuyacak ortak yer yok.

**Karar önerisi:**

1. **Kapsam:** üç tablo üç alt dilimde taşınır: 5a kur, 5b vergi oranı, 5c ölçü birimi. **Ürün/hizmet kataloğu (ürün, kategori, fiyat listesi) bu dilimin dışındadır:** ADR-0018'in sahibi CRM'dir ve CRM'siz katalog ihtiyacı (Lojistik/İK) doğunca ayrı ADR ile ele alınır. Vergi oranı ve birim Core'a geçse de ürünün `TaxCategory` (kod eşleşmesi) ve `BaseUnitId` bağı olduğu gibi kalır.
2. **Kaynak: elle giriş.** `Source` alanı bugün zaten var (`Manual`); dilim 5 yalnızca elle girişi taşır. Günlük otomatik çekim (TCMB) **5d**'dir ve ayrı karar ister (dış ağ erişimi izni, sağlayıcı sözleşmesi, hata ve gecikme davranışı). Uzak çekim bile tablodaki aynı `Source` alanıyla yazar; okuyanlar değişmez.
3. **Baz: tenant.** Kur, vergi ve birim tenant başına tutulur (`ICompanyShared`, bugünkü gibi). Şirket başına kur isteyen müşteri yok (dilim 2c ile aynı gerekçe: kullanan olmadan altyapı yazılmaz); ihtiyaç doğarsa `ICompanyScoped`'a geçiş ayrı migration ile eklenir, okuma sözleşmesi şirket parametresini zaten alabilir.
4. **Okuma kuralı aynen taşınır:** `core.ExchangeRates` için "o güne kadarki en yeni doğrudan kur, yoksa ters kur, hiçbiri yoksa `exchange-rate-not-found`; kur uydurulmaz, çapraz kur yoktur" (ADR-0018). Yeni sözleşme bu kuralı tek yerde uygular; `PriceCalculator` kendi sorgusunu bırakıp sözleşmeyi çağırır.

**Tasarım (5a, kur):**

- **Tablo:** `core.ExchangeRates` (tenant tablosu, RLS altında, `ICompanyShared`; `UNIQUE(TenantId, FromCurrency, ToCurrency, ValidOn)`); alanlar bugünkü `crm.ExchangeRates` ile aynı (çift, kur, geçerlilik günü, `Source`, `Note`), sürüm ve denetim alanları dahil. Yeni tablo için migration'da `TenantIsolationSql.AddTable("core", "ExchangeRates")`.
- **Sözleşme:** `Modulix.Core.Contracts.Currency` (yeni ad alanı; `CoreContractsNamespaceRulesTests` listesine eklenir). `IExchangeRateReader.FindAsync(from, to, day, ct)` → bulunan kur (kur, kaynak günü, ters mi) ya da yok. Okuma yetkisi çağıranın modülüne bağlıdır; sözleşme izin denetlemez, yalnızca tenant bağlamında çalışır.
- **Kayıt türü ve izin:** `crm.exchange-rate` → `core.exchange-rate` (kayıt boru hattı, denetim, içe aktarma). İzin: okuma her üyeye açık (Core ana verisi), yazma yeni `core.exchange-rates.manage`; migration bu izni `crm.prices.manage` taşıyan rollere ekler (dilim 6a'daki `core.holidays.manage` örneği).
- **Ekran:** "Kurlar" Yönetim altına taşınır (CRM modülü kapalıyken de açılır; eski CRM adresi yönlendirir). CRM "Vergi ve kur" ekranında kur sekmesi kalkar, vergi 5b'ye kadar yerinde kalır.
- **Okuyanlar:** `PriceCalculator` (CRM) sözleşmeye geçer; Lojistik masraf (dilim 8) ve İK ilk kullanıcılar olarak sözleşmeyi hazır bulur. Orders ve teklif belge kurunu kopyalamaya devam eder (belgeye uygulanan kur belgede saklanır, sonradan eklenen kur eski belgeyi değiştirmez; bu kural değişmez).
- **Veri ve geriye uyum (iki adım, dilim 6a'daki desen):** (1) migration `crm.ExchangeRates` satırlarını kimlik, sürüm ve zamanlarıyla `core.ExchangeRates`'e kopyalar (RLS kopya süresince kapatılır); CRM entity'si modelden çıkar ve `crm.ExchangeRates` tablosu bir sürüm daha durur; (2) sonraki migration'da RLS politikasından çıkarılıp düşürülür (düşürmeden önce Core'da olmayan satırlar bir kez daha kopyalanır). Dağıtım penceresinde eski sürümün yazdığı kur yeni tabloya geçmez: pencerede kur düzenlenmemelidir (`migration-compat.yml` önceki sürümün testlerini yeni şemaya karşı koşar; kur testleri bu pencereye göre yazılır).
- **Eski kayıt türü anahtarı:** `crm.exchange-rate` için kayıtlı denetim satırları olduğu gibi kalır (geçmiş yeniden yazılmaz); içe aktarma tanımı, rapor veri kümesi ve otomasyon referansı varsa `core.exchange-rate`'e taşınır. Taşıma öncesi kodda bu referanslar taranır.

**5b (vergi) ve 5c (birim):** aynı desen (`core.TaxRates`, `core.MeasureUnits`; sözleşmeler `ITaxRateReader`, `IMeasureUnitReader`). Vergi oranının "aynı kod için geçerlilik aralıkları çakışamaz" kuralı ve birimin "kullanılan birimin grubu/katsayısı değişmez" kuralı Core tarafında korunur; ikincisi ürünü bilmeyeceği için **kullanım sorgusu sözleşmeyle** (ör. `IMeasureUnitUsage`; CRM katalogu sağlar) yapılır, Core CRM tablosuna bakmaz. Bu, 5c'nin 5a/5b'den daha zor tarafıdır; ürün kataloğu CRM'de kaldığı sürece birim taşınması ertelenip yalnızca sözleşme açılabilir.

**Kabul ölçütleri (dört test + iki):**
1. Mutlu yol: kur Yönetim'den girilir, CRM fiyat hesabı ve (yeni) bir Core okuyucu çağrısı aynı kuru bulur; ters kur ve "kur yok" sonucu mevcut testlerle aynı davranır.
2. Hata/doğrulama: aynı çift ve gün ikinci kez, sıfır/negatif kur, aynı para birimi çifti (`TRY→TRY`), geçersiz ISO kodu reddedilir.
3. Tenant izolasyonu: A'nın kuru B'nin okuyucusunda görünmez; RLS ham SQL denemesi `TenantIsolationTests` kapsamındadır.
4. Yetki: `core.exchange-rates.manage` olmayan üye kur yazamaz; migration ile izin CRM fiyat yöneticisi rollerine geçer.
5. CRM lisansı olmayan firma Yönetim'den kur girer ve Core okuyucudan okur.
6. Veri taşıma: migration sonrası kur sayısı ve değerleri eski tabloyla aynı; dağıtım penceresi sınırı belgelenmiş.

#### Dilim 5a uygulaması (kur, 22 Kasım 2026)

- **Tablo ve varlık:** `core.ExchangeRates` (`Modulix.Core.Currency.ExchangeRate`, `ITenantEntity` + `ICompanyShared`); `UNIQUE(TenantId, FromCurrency, ToCurrency, ValidOn)`, kur > 0 ve çift farklı denetimleri, kur duyarlılığı `decimal(28,10)` (CRM tablosuyla aynı). `TenantIsolationSql.AddTable` migration'dadır.
- **Sözleşme:** `Modulix.Core.Contracts.Currency` (`IExchangeRateReader.FindAsync` / `ListAsync`, `ExchangeRateQuote`, `ExchangeRatePoint`, `ExchangeRateSources`); ad alanı `CoreContractsNamespaceRulesTests` listesine eklendi. Okuma kuralı ADR-0018'dekinin aynısıdır ve tek yerde (`ExchangeRateReader`) uygulanır.
- **Kayıt türü ve izin:** `core.exchange-rate` (kayıt boru hattı, denetim kaydı); okuma her üyeye açık, yazma `core.exchange-rates.manage`. Doğrulama iletileri `ServerMessages.ExchangeRate` ile üç dilde gelir. `/api/crm/exchange-rates` ve `crm.exchange-rate` kaldırıldı; CRM `PriceCalculator` ve satış raporları (`RateBook.LoadAsync`) okuyucuyu kullanır.
- **Ekran:** Yönetim > Döviz kurları (`/yonetim/doviz-kurlari`). CRM "Vergi ve kur" ekranı "Vergi oranları" oldu (kur sekmesi kalktı; çeviri anahtarları `crm.taxAndRates.*` → `admin.exchangeRates.*`).
- **Veri ve geriye uyum (adım 1/2):** `CoreExchangeRates` migration'ı tabloyu açar, `crm.ExchangeRates` satırlarını kimlik, sürüm ve zamanlarıyla kopyalar (RLS kopya süresince kapatılır) ve `core.exchange-rates.manage` iznini `crm.prices.write` taşıyan rollere verir. `RetireCrmExchangeRatesEntity` CRM varlığını modelden çıkarır; **`crm.ExchangeRates` tablosu veritabanında ve RLS politikasında kalır** (önceki sürüm hâlâ okur) ve dağıtım penceresinde yazılmış kurlar için kopyayı bir kez daha çalıştırır. Adım 2: bu sürüm her yerde yayına girdikten sonraki migration'da tablo düşürülür (elle: `TenantIsolationSql.RemoveTable` + `DROP TABLE`). **Dağıtım penceresinde kur düzenlenmemelidir** (K5.5).
- **Testler:** `ExchangeRateTests` (mutlu yol ve sözleşmeden doğrudan/ters kur okuma, CRM hakkı olmayan firma, doğrulama, tenant izolasyonu, yetki), `PricingTests` ve satış testleri yeni uçla çalışır. Veri kopyası gerçek SQL Server'da elle sınandı: satır tek kez kopyalanır (tekrar güvenli), duyarlılık ve sürüm korunur.
- **Kalan (5a sonrası):** 5b vergi oranı, 5c ölçü birimi (ürün kullanım sorgusu sözleşmeyle), 5d merkez bankası çekimi (ayrı karar); `crm.ExchangeRates` tablosunu düşüren adım 2.

**Dilim 5 kararları (cevaplandı, 22 Kasım 2026; hepsi öneriyle):**
- **K5.1 Kapsam: kabul edildi.** Sıra kur → vergi → birim; ürün/hizmet kataloğu dilimin dışındadır, CRM'siz katalog ihtiyacı doğunca ayrı ADR.
- **K5.2 Kaynak: kabul edildi.** Dilim 5 yalnızca elle girişi taşır; TCMB günlük çekimi ayrı karar isteyen 5d dilimidir.
- **K5.3 Baz: kabul edildi.** Tenant bazında (`ICompanyShared`); şirket bazı ihtiyaç doğunca ayrı migration ile eklenir.
- **K5.4 Okuma izni: kabul edildi.** Kur okuma her üyeye açıktır; yazma `core.exchange-rates.manage`. CRM ekranı fiyat/maliyet alanları için kendi iznini korur.
- **K5.5 Dağıtım penceresi: kabul edildi.** Pencerede kur düzenlemesi yasaktır, çift yazma yapılmaz.

### Dilim 6: çalışma takvimi

Tek Core takvimi (mesai saatleri + tatil günleri, tenant başına; ileride şirket/şube başına). Destek SLA'sı ve İK izin hesabı buna geçer; onay eskalasyonu ve lojistik teslim süresi yeni kullanıcılardır.

#### Dilim 6a: firmanın tatil listesi ve takvim hesabı (teslim edildi)

- **Tatil listesi Core'da:** `core.Holidays` (tenant tablosu, RLS altında, `ICompanyShared`; `UNIQUE(TenantId, Date)`), kayıt türü `core.holiday` (kayıt boru hattı, denetim kaydı, içe aktarma). Okuma her üyeye açık, yazma `core.holidays.manage`.
- **Sözleşme:** `Modulix.Core.Contracts.Calendar.IHolidayCalendar` (aralıktaki tatil günleri). Saf hesaplayıcı `WorkCalendar` (+ `CalendarSpec`, `CalendarSpecParser`) destek modülünden aynı ad alanına taşındı; SLA'ya özgü `SlaClock` destekte kaldı.
- **İK geçişi:** izin gün sayısı `IHolidayCalendar`'dan okunur. `hr.holiday` kayıt türü, içe aktarma tanımı ve denetim tanımı kaldırıldı; ekran Yönetim'e taşındı (eski adres açılır).
- **Veri:** migration `hr.Holidays` satırlarını kimlik, sürüm ve zamanlarıyla `core.Holidays`'e kopyalar (RLS kopya süresince kapatılır) ve `core.holidays.manage` iznini `hr.leaves.manage` taşıyan rollere ekler. `hr.Holidays` tablosu kesintisiz dağıtım için bir sürüm daha durur, sonraki migration'da kaldırılır. Dağıtım sırasında eski sürümün yazdığı tatil (kopyadan sonra) yeni listeye geçmez; dağıtım penceresinde tatil düzenlenmemelidir. **Kaldırma iki adımdır (16.11.2026):** (1) `RetireHrHolidaysEntity`: entity modelden çıkar, migration Core'da olmayan günleri bir kez daha kopyalar (eski sürümün sonradan yazdıkları da geçer); tablo veritabanında ve RLS politikasında kalır, çünkü bir önceki sürümün modelinde hâlâ vardır ve aynı adımda düşürmek geriye uyumsuzdur (`migration-compat` bunu yakaladı). Tenant dışa aktarma ve silme tabloları veritabanından okuduğu için modelde olmayan tabloyu da kapsar. (2) Sonraki sürümde elle yazılan migration tabloyu politikadan çıkarır (`TenantIsolationSql.RemoveTable`) ve düşürür.

#### Dilim 6b: destek SLA takvimi firmanın listesini uygular (teslim edildi)

- **Seçenek:** SLA takviminde `UseTenantHolidays` (`support.SlaCalendars`). Yeni takvimde açık; seçenek gelmeden önce açılmış takvimler kapalı başlar (migration varsayılanı), böylece mevcut hedefler değişmez.
- **Birleştirme:** `TenantHolidays.Apply` (`Core.Contracts.Calendar`, saf hesap). Takvimin kendi tatili aynı günde önceliklidir; firma listesindeki tam gün günü kapatır, yarım gün o günün ilk çalışma penceresinin başından 13:00'e kadar açık bırakır (o gün pencere yoksa kapalı); 7/24 takvime uygulanmaz. 13:00 bugün sabittir (Türkiye'de arife öğleden sonra tatil); firma ayarı olması gerekirse ayrı karar.
- **Anlık görüntü:** liste, SLA örneği açılırken (ve yeniden seçimde) bugünden bir ay geri ile yaklaşık üç yıl ileri arasındaki günlerle görüntüye girer; liste sonradan değişse de açık örnek kendi görüntüsüyle yaşar.

#### Dilim 6c: firmanın haftalık mesai saatleri (teslim edildi)

- **Ayarlar (Core, "Çalışma takvimi" grubu):** `core.calendar.work-days` (Pazartesi–Cuma varsayılan; Pazartesi–Cuma + Cumartesi yarım gün; Pazartesi–Cumartesi; Pazar–Perşembe) ve `core.calendar.work-hours` (hazır aralıklar, varsayılan 09:00–18:00). Seçenekli ayar oldukları için mevcut ayar ekranında görünür; serbest saat girişi yoktur (çapraz doğrulama gerektirir, ihtiyaç doğarsa ayrı dilim). Seçenekler ve çözümleme `WorkWeek` (`Core.Contracts.Calendar`); bozuk değer varsayılana düşer.
- **Sözleşme:** `ITenantWorkCalendar`: `GetWeekAsync` (saat dilimi + haftalık mesai) ve `GetAsync(from, to)` (tatil listesi uygulanmış). Uygulama ayar okuyucusunu çağrı anında çözer (kayıt depolarına verildiği için kurucuda alınırsa servis döngüsü oluşur).
- **İK:** izin gün sayısı firmanın çalışma günlerine göre (`WorkWeek.DayWorth`: penceresi olmayan gün 0, haftanın en uzun gününün %60'ından kısa gün yarım, diğerleri 1). Varsayılan ayarla sonuç önceki sabit cumartesi–pazar kuralıyla aynıdır; açılmış taleplerin gün sayısı değişmez.

#### Dilim 6d: onay adımı süresi iş saatiyle (teslim edildi)

- **Seçenek süreç başınadır:** `ApprovalDefinition.DueInWorkingHours` (varsayılan kapalı; mevcut süreçler takvim saatiyle sürer). Açıkken adımın süresi `ITenantWorkCalendar`'ın takviminde (mesai, çalışma günleri, tatil listesi) `WorkCalendar.AddWorkingTime` ile sayılır; bitiş anı (`DueAt`) adım açılırken bir kez hesaplanır, eskalasyon işi değişmeden bu anı kullanır.
- **Anlık görüntü:** seçenek talep açılırken `ApprovalRequest.DueInWorkingHours`'a kopyalanır; süreç sonradan değişse de açık talep kendi ayarıyla sürer. Seçeneği değiştirmek süreç sürümünü (`Revision`) artırır. Tatil listesi adım açılırken bugünden 400 gün ileriye kadar okunur; bu yüzden iş saatiyle adım süresi en çok 2000 saattir (yaklaşık bir yıllık mesai). Sonradan eklenen tatil açılmış adımın bitişini değiştirmez.
- **Kapsam dışı:** sözleşme onayı (`contracts`) kendi süresini kullanır; adım başına ayrı seçenek yoktur.
- **Kalan:** lojistik teslim süresi, platformun hazır tatil kataloğu (açık karar 3), şube başına takvim. Eski `hr.Holidays` tablosu iki adımda kaldırıldı: entity 16.11.2026'da modelden çıktı (`RetireHrHolidaysEntity`), tablo sonraki migration'da düşürüldü (`DropHrHolidaysTable`; önce RLS politikasından çıkarılır, düşürmeden önce Core'da olmayan günler bir kez daha kopyalanır).

## Açık kararlar

1. **Dilim 3:** teklif, sipariş ve destek numaraları sıralı seriye geçsin mi? Öneri: evet, yalnızca yeni kayıtlar için ve firma ayarıyla (dilim 2'den sonra).
2. **Dilim 5:** cevaplandı (22 Kasım 2026): elle giriş, tenant bazı, kapsam kur → vergi → birim (K5.1–K5.5, dilim 5 bölümü).
3. **Dilim 6:** resmî tatiller platform tarafından mı sağlanır (yıllık güncellenen katalog), firma mı girer? Öneri: platform kataloğu + firma eklemeleri.
4. Seri başına boşluk raporu (hangi numara neden atlandı) hangi belgeler için zorunlu? Mevzuat doğrulaması ister.

## Sonuçlar

- Sıralı numara isteyen yeni kayıt türü tek satır beyan ve tek çağrıyla numara alır; eşzamanlılık bir kez çözülmüştür.
- Sefer açılışında "aynı numara" çakışması kalmaz.
- Sayaç satırı işlem sonuna dek kilitli kaldığı için aynı tenant'ta aynı seriden numara alan işlemler sıraya girer. Uzun işlemlerde numara en sonda alınmalıdır (sefer deposu doğrulamadan sonra alır).
- Açık işlem yokken alınan numara, kayıt başarısız olursa harcanmış olur; kesintisiz seri isteyen belgeler numarayı işlem içinde almalıdır (dilim 4).
- Yeni tenant tablosu: `TenantIsolationSql.AddTable("core", "NumberCounters")`.

- **5d uygulandı (26 Kasım 2026, ürün sahibi önerileri kabul etti):** TCMB günlük bülteni (`today.xml`) **döviz satış kuru**, `Unit` çarpanı uygulanarak TRY karşılığı olarak yazılır. Çekim `ICentralBankFeed` sınırındadır (Core arayüz, Infrastructure `TcmbRateFeed`); varsayılan **kapalı** (`ExchangeRates:CentralBank:Enabled`), dış ağ izni ve adres yapılandırmayla verilir. Zamanlanmış iş `core.exchange-rates` (ADR-0046; altı saatte bir, `core.exchange-rates.manage` izinli sistem kimliği) para birimlerini `Jobs:core.exchange-rates:Currencies` ayarından alır (varsayılan USD, EUR, GBP) ve kayıt boru hattından `CentralBank` kaynağıyla yazar. Kurallar: elle (ya da içe aktarmayla) girilmiş aynı gün kuru ezilmez; aynı kur tekrar yazılmaz; hata ve ulaşılamama eski kuru bırakır, kur uydurulmaz. Fatura kuru kuralı ve teklifte TL gösterge ayrı dilimlerdir.
- **Fatura kuru kuralı (26 Kasım 2026):** firma ayarı (`core.exchange-rates.invoice-rule`, sektöre özel değil): `previous-day` varsayılan (önceki iş günü yayımlanan merkez bankası kuru; "bir önceki günün en yeni kuru" olarak uygulanır, bu yüzden resmî tatil listesi gerekmez ve hafta sonu/tatil kendiliğinden son iş gününe düşer) ya da `same-day`. Okuyucuya `FindForInvoiceAsync` eklendi. Fatura varlığı henüz olmadığından kuralın tüketicisi yoktur; ilk tüketici fatura kesen modüldür ve uygulanan kuru belgede saklar (sonradan gelen kur kesilmiş faturayı değiştirmez).
- **Teklifte gösterge kuru (26 Kasım 2026):** teklifteki mevcut `reportingCurrency`/`exchangeRate`/`exchangeRateDate` alanları artık formda ve otomatiktir: gösterge para birimi verilip kur verilmemişse kur `IExchangeRateReader.FindAsync(teklif para birimi → gösterge, teklif tarihi)` ile alınır; bulunamazsa 422 (kur uydurulmaz). Açık kur elle girilmiş sayılır. Çift değişince (istemci eski kuru geri gönderdiyse) kur yenilenir. Kur yalnızca gösterge ve rapordur, teklif toplamını değiştirmez. Müşteri belgesinde (PDF, herkese açık sayfa) gösterim ayrı dilimdir.
