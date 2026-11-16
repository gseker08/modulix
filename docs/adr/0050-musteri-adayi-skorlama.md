# ADR-0050: Müşteri adayı skorlama

- **Durum:** Dilim 1 (kural modeli, kayıtta hesaplama, liste sütunu ve sıralama, kural ekranı) 15 Kasım 2026'da teslim edildi (PR #417).
  Dilim 1b (toplu yeniden hesaplama, PR #441) ve dilim 2 (davranış puanı) 16 Kasım 2026'da teslim edilir. Diğer dilimler öneridir.
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
| 3 | Zamanla azalma (hareketsiz gün sayısı) ve eşik olayı `crm.lead.score-changed`; atama/bildirim otomasyonla (ADR-0027) | Öneri |
| 4 | Puan açıklaması: kartta hangi kuralın kaç puan verdiği | Öneri |

## Açık kararlar

- Puan aralığı 0..100 mü kalsın, yoksa kırpmasız toplam mı? (Dilim 1: 0..100.)
- Kural birden çok koşul (VE) taşısın mı? Dilim 1'de tek koşul; birleşik koşul ihtiyacı pilotla ölçülecek.
- Yapay zekâ ile tahmini puan (ADR-0047) ayrı bir puan mı olur, bu puanın yerine mi geçer?
