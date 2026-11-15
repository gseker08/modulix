# ADR-0050: Müşteri adayı skorlama

- **Durum:** Dilim 1 (kural modeli, kayıtta hesaplama, liste sütunu ve sıralama, kural ekranı) 15 Kasım 2026'da teslim edilir. Diğer dilimler öneridir.
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

## Sonuçlar

- Kural değişince mevcut adayların puanı, aday bir sonraki kaydedilişinde güncellenir. Toplu yeniden hesaplama dilim 1b'dir; o zamana kadar
  kural ekranı bunu açıkça yazar.
- Puan geçmişi, denetim kaydındaki alan değişikliğidir; ayrı tablo yoktur.
- Migration: `crm.Leads.Score` (`int`, varsayılan 0, `IX_Leads_TenantId_Score`) ve `crm.LeadScoreRules` (RLS'ye eklenir). Geriye uyumludur;
  mevcut adaylar 0 puanla başlar.

## Dilimler

| Dilim | Kapsam | Durum |
|---|---|---|
| 1 | Kural modeli, kayıtta hesaplama, liste sütunu/sıralama/süzgeç, kural ekranı | Teslim |
| 1b | Toplu yeniden hesaplama: kural değişince arka plan işçisi açık adayları sistem adına, kayıt boru hattından yeniden puanlar | Öneri |
| 2 | Davranış puanı: iş olaylarına abone (tamamlanan aktivite, kampanya yanıtı, teklif görüntüleme) | Öneri; olay listesi ADR-0039'dan |
| 3 | Zamanla azalma (hareketsiz gün sayısı) ve eşik olayı `crm.lead.score-changed`; atama/bildirim otomasyonla (ADR-0027) | Öneri |
| 4 | Puan açıklaması: kartta hangi kuralın kaç puan verdiği | Öneri |

## Açık kararlar

- Puan aralığı 0..100 mü kalsın, yoksa kırpmasız toplam mı? (Dilim 1: 0..100.)
- Kural birden çok koşul (VE) taşısın mı? Dilim 1'de tek koşul; birleşik koşul ihtiyacı pilotla ölçülecek.
- Yapay zekâ ile tahmini puan (ADR-0047) ayrı bir puan mı olur, bu puanın yerine mi geçer?
