# ADR-0050: Müşteri adayı skorlama

- **Durum:** Dilim 1 (kural modeli, kayıtta hesaplama, liste sütunu ve sıralama, kural ekranı) 15 Kasım 2026'da teslim edildi (PR #417).
  Dilim 1b (toplu yeniden hesaplama, PR #441), dilim 2 (davranış puanı, PR #449), dilim 3 (zamanla azalma ve eşik, PR #462) ve dilim 4
  (puan açıklaması) 16-17 Kasım 2026'da teslim edildi.
- **Kaynak:** [CRM eksik süreç epic listesi](../crm-eksik-surecler-epicleri.md) E1.

## Bağlam

Müşteri adayları (ADR-0014) web formu, içe aktarma ve elle girişle geliyor. Satış ekibi hangi adayla önce ilgileneceğini bugün öncelik
alanından ve geliş zamanından çıkarıyor. Öncelik elle seçiliyor, aday sayısı artınca sıralama tutarsızlaşıyor. Rakip CRM'ler otomatik puan
veriyor. Modulix'te puan alanı, puan kuralı ya da hesaplama yok.

Sınırlar:

- Ürün ilkesi: kurallar firma başına yapılandırılır, kodda sektöre ya da firmaya özel puan yazılmaz.
- Kayıt işlemleri kayıt boru hattından geçer (izin, kapsam, denetim, outbox); puan da bu yoldan yazılmalı.
- Hassas alan: tahmini bütçe alan yetkisine tabidir ve sıralanamaz (değeri sezdirir). Bütçeye bağlı puan aynı sızıntıyı yaratır.

## Karar

### Kural modeli

`crm.LeadScoreRules` (tenant tablosu, RLS altında). Bir kural tek koşul ve bir puandan oluşur:

| Alan | Anlamı |
|---|---|
| Ad, açıklama, etkin | Yönetim için |
| Alan (`field`) | Adayın puanlanabilir alanlarından biri (aşağıdaki liste) |
| İşleç (`operator`) | `equals`, `notEquals`, `contains`, `isSet`, `isEmpty` |
| Değer (`value`) | `isSet`/`isEmpty` dışında zorunlu |
| Puan (`points`) | -100..100, sıfır olamaz; eksi puan "uygun değil" işaretidir |

Puanlanabilir alanlar sabit bir beyaz listedir (`LeadScoring.Fields`): kaynak, sektör, ülke, şehir, öncelik, unvan, ilgilenilen ürün/hizmet,
hizmet kapsamı, UTM kaynak/ortam/kampanya ve varlık denetimi için e-posta, telefon, web sitesi, firma adı, mevcut firma, kampanya. Liste
koddadır çünkü her alanın karşılaştırma biçimi (kod, metin, kod listesi) bilinmelidir. Liste firmaya özel değildir; hangi alanın kaç puan
alacağı firmanındır.

**Bilerek dışarıda:** tahmini bütçe (hassas alan; puan, bütçeyi göremeyen kullanıcıya bütçe hakkında bilgi verir) ve özel alanlar
(dilim 3'te değerlendirilecek).

### Hesaplama

- Puan = eşleşen etkin kuralların puan toplamı, 0..100 aralığına kırpılır. Kural sırası sonucu değiştirmez.
- Metin karşılaştırması büyük/küçük harf duyarsız ve kırpılmıştır. `contains` metin içinde arar; hizmet kapsamında tam kod eşleşmesi arar.
- Hesap saf bir fonksiyondur (`LeadScoring.Calculate`); birim testle denetlenir.
- Puan, kayıt deposunun `AppliedAsync` adımında hesaplanır: aday oluşturulurken, alanları değişirken, web formundan, içe aktarmadan ve iş akışı
  eylemlerinden geçerken. Böylece puan değişikliği denetim kaydına ve olaya (outbox) kayıtla aynı işlemde girer.
- `score` sistem alanıdır: genel kayıt uçlarından gelen değer yok sayılır, istemci puan yazamaz.

### Yetki

- Kuralları okumak ve yazmak `crm.lead-scoring.manage` ister. Hiçbir hazır role verilmez; firma yöneticisi rol düzenleyicisinden verir.
- Puanı görmek adayı görmekle aynıdır (`crm.leads.read` ve kayıt kapsamı). Puan, adayın görünür alanlarından hesaplandığı için yeni bilgi sızdırmaz.
- Kurallar genel kayıt uçlarından sunulur (`/api/records/crm.lead-score-rule`); yeni uç nokta eklenmez.

### Liste

Müşteri adayları listesine "Puan" sütunu eklenir (`sort=score`/`-score`, eşitlikte en yeni geliş önce) ve `minScore` süzgeci gelir.

### Toplu yeniden hesaplama (dilim 1b)

- Kural eklenince, değişince ya da silinince mevcut adayların puanı eskir. CRM modülünün zamanlanmış işi (ADR-0046) `crm.lead-scoring`
  (varsayılan 5 dakikada bir, `Jobs:crm.lead-scoring`) her turda **açık** adayları etkin kurallarla yeniden hesaplar ve puanı tutmayanları
  kayıt boru hattından boş değerli bir güncellemeyle günceller; puanı `AppliedAsync` her zamanki gibi hesaplar. Puan değişikliği denetim
  kaydına (kullanıcısız, abonelik `crm.lead-scoring`) ve `RecordUpdated` olayına girer.
- Ayrı bir "yeniden hesapla" işareti ya da kuyruk tablosu tutulmaz: puanı tutan aday dokunulmadan geçilir, aynı tur iki kez çalışsa ikinci
  tur bir şey yazmaz. Kural yoksa yalnızca puanı sıfır olmayan adaylar taranır. Tarama maliyeti açık aday sayısıyla orantılıdır; ölçüm
  gerektirirse kural değişikliğinde işaret koyan bir abonelik eklenir.
- Bir turda en çok 200 aday güncellenir (tur tek işlemde kaydedilir); kalanlar sonraki turlara kalır. Güncellenemeyen aday turu durdurmaz,
  özette sayılır.
- Elenmiş ve dönüştürülmüş adayların puanı donar: kapanış anındaki puan tarihî bilgidir.
- İşin sistem kimliği yalnızca `crm.leads.read`, `crm.leads.update` ve `crm.leads.assign` taşır. Atama izni yalnızca sorumlusu boş
  adayları (atama havuzu) görmek içindir; iş sorumlu ya da durum yazmaz.
- Kural ekranı ve silme onayı "birkaç dakika içinde yansır" der.
### Davranış puanı (dilim 2)

- Kural modeline sayaç alanları ve `atLeast` ("en az") işleci eklenir; değer 1..1000 tam sayıdır. Yeni tablo ya da migration yoktur: kural
  aynı satırda (alan, işleç, değer, puan) durur.
- Sayaçlar saklanmaz, aday puanlanırken CRM'in kendi tablolarından türetilir (`LeadBehavior`, `Records/LeadBehaviorReader`):
  - `contacts` (gerçekleşen temas sayısı): adaya bağlı, tamamlanmış ve ilk temas kuralına göre gerçekleşmiş temas sayılan aktiviteler
    (`LeadRules.CountsAsContact`: ulaşılan arama, gönderilen/ulaşılan e-posta, cevapsız olmayan toplantı).
  - `meetings` (yapılan toplantı sayısı): bunların toplantı olanları.
  - `repeatInquiries` (tekrar başvuru sayısı): aynı normalleştirilmiş e-posta ya da telefonla gelen diğer müşteri adayları, durumu ne olursa
    olsun, yalnızca aynı tenant'ta.
  Türetmek, aktivite silinince ya da başka adaya taşınınca sayacın kendiliğinden doğru kalmasını sağlar ve dilim 3'teki zamanla azalmayı
  (son N gün) aynı sorguya bir süzgeç olarak ekletir.
- Gizli aktiviteler sayılmaz: puanı gören ama aktiviteyi göremeyen kullanıcıya varlığı sezdirilmez.
- Sayaçlar yalnızca bir etkin kural sayaç kullanıyorsa okunur; kullanmayan tenant'ta ek sorgu yoktur.
- Tetikleyici: `crm.lead-behavior-score` aboneliği `crm.activity` kayıt olaylarını (oluşturma, güncelleme, silme) dinler; etkilenen adaylar
  olay farkındaki `leadId` eski/yeni değerleri ve aktivitenin bugünkü adayıdır. Puan yalnızca değişecekse kayıt boru hattından boş değerli
  güncellemeyle yazılır (boş güncelleme de denetim satırı ve olay ürettiği için önceden bakılır); tekrar teslim ikinci yazım üretmez.
  Tekrar başvuruda eski adayların puanı toplu işle (dilim 1b) birkaç dakika içinde güncellenir; yeni aday kaydedilirken zaten doğru puanlanır.
- ADR-0039 iş olaylarından şimdilik yararlanılmaz: kampanya yanıtı ve teklif görüntüleme için yayınlanan bir iş olayı yok; teklif adaya değil
  fırsata bağlıdır. Pazarlama modülü yanıt olayı yayınladığında yeni bir sayaç bu dilimin kalıbıyla eklenir.

### Zamanla azalma ve eşik (dilim 3)

- **Azalma bir sayaçtır:** `inactiveDays` (hareketsiz gün sayısı) son gerçekleşen temastan (dilim 2'deki temas tanımı; gizli aktivite
  sayılmaz), temas yoksa adayın oluşturulmasından bu yana geçen tam gündür. Firma azalmayı eksi puanlı "en az N gün" kurallarıyla ifade eder
  (ör. en az 14 gün → -15, en az 30 gün → -15). Ayrı bir azalma ayarı, yarı ömür formülü ya da migration yoktur; kademeler firmanındır.
- Gün geçtikçe değişen tek girdi budur. Olay olmadan değiştiği için toplu yeniden puanlama işi (dilim 1b) her turda yansıtır; gün sınırını
  geçen aday bir sonraki turda güncellenir. Yeni gerçekleşen temas hareketsizliği sıfırlar ve `crm.lead-behavior-score` ile hemen yansır.
  Saat, işte `TimeProvider`dan, kayıtta kayıt deposunun saatinden okunur.
- **Eşik ayrı bir iş olayı değildir:** epic notundaki `crm.lead.score-changed` iş olayı yayınlanmaz. Puan değişikliği zaten `RecordUpdated`
  olayının farkında eski/yeni değerle taşınır; ayrı olay aynı bilgiyi ikinci kez yazardı. Bunun yerine puan kural altyapısına (ADR-0027)
  `crm.lead.score` sayı alanı olarak beyan edilir: "puan değişti" tetikleyicisi + yeni puan ≥ eşik + eski puan < eşik koşulu yalnızca eşiğin
  aşıldığı değişiklikte tetikler. Eşik firmanındır (kuralın koşulu).
- Hazır şablon `crm.lead-score-threshold-follow-up` ("Puanı eşiği aşan müşteri adayı için takip"): eski < 70 ≤ yeni ve aday açıksa
  sorumluya bugüne takip aktivitesi açar. Atama ve bildirim de aynı tetikleyiciyle otomasyonun kendi aksiyonlarıyla kurulur; CRM yeni aksiyon
  eklemez.
- Puan alanının okuma izni yoktur: puan, adayı görebilen herkese görünür (Yetki bölümü).

### Puan açıklaması (dilim 4)

- `GET /api/crm/leads/{id}/score` (`crm.leads.read`) kayıttaki puanı, bugünkü kurallarla hesaplanan puanı ve puana katkı veren etkin
  kuralları (ad, alan, işleç, değer, puan; büyük katkı önce) döner. Aday önce kayıt boru hattından okunur: görünmeyen aday (kapsam dışı ya da
  başka tenant) 404'tür. Açıklama, kaydedilirken yapılan hesabın aynı girdileriyle (kurallar ve davranış sayaçları) üretilir.
- **Kural adı ve koşulu adayı görebilen herkese gösterilir.** Kural yönetimi (`crm.lead-scoring.manage`) yalnızca yazma ve tüm kural listesi
  içindir; satış temsilcisinin "bu aday neden 70?" sorusunu yanıtlamak ürün amacıdır. Kural sayaç ya da adayın görünür alanına baktığı için
  açıklama yeni kişisel veri sızdırmaz; firma kural adına hassas bilgi yazmamalıdır.
- Kayıttaki puan ile hesaplanan farklıysa ekran bunu söyler: açık adayda "birkaç dakika içinde güncellenir" (dilim 1b işi), kapanmış adayda
  "puan donmuştur".
- Ekran: müşteri adayı kartında "Puan: N" bölümü; koşul metni kural ekranıyla aynı biçimdedir (`LeadScoreCatalog.ConditionText`).

## Sonuçlar

- Kural değişikliği açık adaylara birkaç dakika içinde yansır (dilim 1b). Yeniden puanlama `RecordUpdated` olayı ürettiği için aday
  güncellemesine bağlı otomasyon kuralları (ADR-0027) bu güncellemeyle de çalışabilir.
- Puan geçmişi, denetim kaydındaki alan değişikliğidir; ayrı tablo yoktur.
- Migration: `crm.Leads.Score` (`int`, varsayılan 0, `IX_Leads_TenantId_Score`) ve `crm.LeadScoreRules` (RLS'ye eklenir). Geriye uyumludur;
  mevcut adaylar 0 puanla başlar.

## Dilimler

| Dilim | Kapsam | Durum |
|---|---|---|
| 1 | Kural modeli, kayıtta hesaplama, liste sütunu/sıralama/süzgeç, kural ekranı | Teslim |
| 1b | Toplu yeniden hesaplama: kural değişince arka plan işçisi açık adayları sistem adına, kayıt boru hattından yeniden puanlar | Teslim |
| 2 | Davranış puanı: gerçekleşen temas, toplantı ve tekrar başvuru sayaçları ("en az N"); aktivite olayına abone | Teslim; kampanya yanıtı Pazarlama iş olayını bekliyor |
| 3 | Zamanla azalma (hareketsiz gün sayısı) ve eşik: puan otomasyon alanı ve eşik şablonu; ayrı iş olayı yok | Teslim |
| 4 | Puan açıklaması: kartta hangi kuralın kaç puan verdiği | Teslim |

## Açık kararlar

- Puan aralığı 0..100 mü kalsın, yoksa kırpmasız toplam mı? (Dilim 1: 0..100.)
- Kural birden çok koşul (VE) taşısın mı? Dilim 1'de tek koşul; birleşik koşul ihtiyacı pilotla ölçülecek.
- Yapay zekâ ile tahmini puan (ADR-0047) ayrı bir puan mı olur, bu puanın yerine mi geçer?
