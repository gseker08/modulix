# ADR-0049: Ortak iş ana verisi ve belge numaralama

- **Durum:** Dilim 1 (belge numaralama) 15 Kasım 2026'da teslim edildi (PR #395). Dilim 6a (firmanın tatil listesi ve takvim hesabı Core'da) 15 Kasım 2026'da teslim edilir. Diğer dilimler öneridir; "Açık kararlar" yanıtlanmadan kodlanmaz.
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

### Dilim 2: seriyi firma yapılandırır (öneri)

Önek, basamak sayısı ve sıfırlama firma ayarından değiştirilebilir (dinamik tasarım ilkesi); şirket başına ayrı seri (ADR-0008, `ICompanyScoped` belgeler için). Yönetim ekranı: seriler, son verilen numara, sonraki numarayı ileri alma (denetim kaydıyla).

### Dilim 3: mevcut üreticilerin geçişi (ürün kararı ister)

Teklif, sipariş, operasyon işi ve destek kaydı numaraları bugün rastgele soneklidir. Sıralı seriye geçiş kullanıcının gördüğü numara biçimini değiştirir; eski kayıtlar numarasını korur. Geçilmeyecekse dört kopya üretici en azından tek ortak yardımcıya indirilir.

### Dilim 4: taslak ve kesin numara, iptal izi (Ç8.1'in kalanı)

Kesintisiz seri isteyen belgeler (fatura, irsaliye) için: numara taslakta değil kesinleştirmede verilir; iptal edilen numara iz bırakır. İlk ihtiyaç duyan belgeyle birlikte.

### Dilim 5: para birimi, kur, vergi oranı, birim

Kur tablosu ve tanımları `crm` şemasından `core` şemasına taşınır; CRM, Orders ve Lojistik masraf Core sözleşmesinden okur. Taşıma iki adımlıdır (önce Core tablosu + çift okuma, sonra CRM tablosunun kaldırılması). Saf hesap çekirdeği (`Core.Contracts.Pricing`) yerinde kalır.

### Dilim 6: çalışma takvimi

Tek Core takvimi (mesai saatleri + tatil günleri, tenant başına; ileride şirket/şube başına). Destek SLA'sı ve İK izin hesabı buna geçer; onay eskalasyonu ve lojistik teslim süresi yeni kullanıcılardır.

#### Dilim 6a: firmanın tatil listesi ve takvim hesabı (teslim edildi)

- **Tatil listesi Core'da:** `core.Holidays` (tenant tablosu, RLS altında, `ICompanyShared`; `UNIQUE(TenantId, Date)`), kayıt türü `core.holiday` (kayıt boru hattı, denetim kaydı, içe aktarma). Okuma her üyeye açık, yazma `core.holidays.manage`.
- **Sözleşme:** `Modulix.Core.Contracts.Calendar.IHolidayCalendar` (aralıktaki tatil günleri). Saf hesaplayıcı `WorkCalendar` (+ `CalendarSpec`, `CalendarSpecParser`) destek modülünden aynı ad alanına taşındı; SLA'ya özgü `SlaClock` destekte kaldı.
- **İK geçişi:** izin gün sayısı `IHolidayCalendar`'dan okunur. `hr.holiday` kayıt türü, içe aktarma tanımı ve denetim tanımı kaldırıldı; ekran Yönetim'e taşındı (eski adres açılır).
- **Veri:** migration `hr.Holidays` satırlarını kimlik, sürüm ve zamanlarıyla `core.Holidays`'e kopyalar (RLS kopya süresince kapatılır) ve `core.holidays.manage` iznini `hr.leaves.manage` taşıyan rollere ekler. `hr.Holidays` tablosu kesintisiz dağıtım için bir sürüm daha durur, sonraki migration'da kaldırılır. Dağıtım sırasında eski sürümün yazdığı tatil (kopyadan sonra) yeni listeye geçmez; dağıtım penceresinde tatil düzenlenmemelidir.
- **Kalan (dilim 6b):** destek SLA takviminin firmanın tatil listesini de uygulaması (takvim başına seçenek; anlık görüntüye girer), onay eskalasyonu ve teslim süresinin takvime geçmesi, platformun hazır tatil kataloğu (açık karar 3), firmanın haftalık mesai saatleri (bugün İK hafta sonunu sabit cumartesi–pazar sayar).

## Açık kararlar

1. **Dilim 3:** teklif, sipariş ve destek numaraları sıralı seriye geçsin mi? Öneri: evet, yalnızca yeni kayıtlar için ve firma ayarıyla (dilim 2'den sonra).
2. **Dilim 5:** kur kaynağı (elle giriş mi, TCMB'den günlük çekim mi) ve kurun şirket bazında mı tenant bazında mı tutulacağı.
3. **Dilim 6:** resmî tatiller platform tarafından mı sağlanır (yıllık güncellenen katalog), firma mı girer? Öneri: platform kataloğu + firma eklemeleri.
4. Seri başına boşluk raporu (hangi numara neden atlandı) hangi belgeler için zorunlu? Mevzuat doğrulaması ister.

## Sonuçlar

- Sıralı numara isteyen yeni kayıt türü tek satır beyan ve tek çağrıyla numara alır; eşzamanlılık bir kez çözülmüştür.
- Sefer açılışında "aynı numara" çakışması kalmaz.
- Sayaç satırı işlem sonuna dek kilitli kaldığı için aynı tenant'ta aynı seriden numara alan işlemler sıraya girer. Uzun işlemlerde numara en sonda alınmalıdır (sefer deposu doğrulamadan sonra alır).
- Açık işlem yokken alınan numara, kayıt başarısız olursa harcanmış olur; kesintisiz seri isteyen belgeler numarayı işlem içinde almalıdır (dilim 4).
- Yeni tenant tablosu: `TenantIsolationSql.AddTable("core", "NumberCounters")`.
