# ADR-0049: Ortak iş ana verisi ve belge numaralama

- **Durum:** Dilim 1 (belge numaralama) 15 Kasım 2026'da teslim edildi (PR #395). Dilim 6a (firmanın tatil listesi ve takvim hesabı Core'da) 15 Kasım 2026'da teslim edildi (PR #397). Dilim 6b (destek SLA takvimi firmanın listesini uygular) ve 6c (firmanın haftalık mesai saatleri, firma iş takvimi) 15 Kasım 2026'da teslim edilir. Diğer dilimler öneridir; "Açık kararlar" yanıtlanmadan kodlanmaz.
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
- **Destek talebi (3b, sonra):** gelen e-posta konudaki `[TK-…]` numarasıyla talebe bağlanıyor; rastgele sonek bunu tahmin edilemez kılıyor ve ayrıştırıcı öneki sabit arıyor. Geçişten önce gönderen denetimi incelenecek ve ayrıştırıcı firmanın önekini tanıyacak.
- **Fırsat ve müşteri adayı:** belge değil, iç takip kaydı; toplu içe aktarmayla çok sayıda açılıyor. Şimdilik rastgele kalıyor.
- **Geçmeyenler:** üç üretici tek ortak yardımcıya indi (`Core.Contracts.Numbering.RandomDocumentNumber`); davranışları değişmedi.

#### Dilim 3a: beş belge ortak seride (teslim edildi)

- **Seriler:** `crm.quote` (TK), `orders.order` (SP), `orders.delivery` (TS), `orders.job` (IS), `contracts.contract` (SZ); modüller `IModule.NumberSeries` ile beyan eder.
- **Eski kayıtlar** numarasını korur (`TK-20261101-AB12CD` gibi); veri taşıma ve migration yok. Eski numaralar rastgele olduğundan tohum verilmez, her seri 1'den başlar. Eski ve yeni biçim çakışamaz (yeni biçimde yıl 4, eskide tarih 8 hanedir).
- **Numara ne zaman alınır:** doğrulamadan sonra, kayıt eklenmeden hemen önce (`CreatedAsync`); reddedilen istek sıra harcamaz. CRM depo tabanına bunun için beklemeli `CreatedAsync` kancası eklendi.
- **Teklif revizyonu** ailenin numarasını taşır, seriden sıra almaz; çoğaltma yeni ailedir ve yeni numara alır.
- **Yönetim listesi:** firmada açık olmayan modülün serisi listelenmez ve yapılandırılamaz (404).

Aşağıdaki özgün not tarihçe için duruyor:

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
- **Kalan:** lojistik teslim süresi, platformun hazır tatil kataloğu (açık karar 3), şube başına takvim, `hr.Holidays` tablosunun kaldırılması.

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
