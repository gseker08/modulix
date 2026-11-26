# ADR-0059: Kişi kartında zaman çizelgesi

- **Durum:** Kabul edildi, 26 Kasım 2026; **dilim 1 (CRM kaynakları: aktivite, teklif; uç noktalar, kişi detayında sekme ve özet şeridi) teslim edildi.** **Dilim 2 (Sipariş, Sözleşme, Destek, Pazarlama kaynakları) 26 Kasım 2026'da teslim edildi.** Fırsat ve aday kişi alanı bekler.
- **Kaynak:** [ADR-0053](0053-musteri-zaman-cizelgesi.md) "Yapılmadı / sınırlar" (kişi kartında çizelge yok); `docs/oncelikli-isler.md` R3.
- **Karar (önerilen):** Kaynak sözleşmesi (`ICustomerTimelineSource`) **geriye uyumlu genişletilir**: sorguya isteğe bağlı kişi kimlikleri eklenir, kişiye bağlanabilen kaynaklar kişi sorgusunu uygular, bağlayamayanlar `null`/boş dönmeden önce **kişi kapsamını desteklemediklerini bildirir**. Kişi kartı aynı birleştirici ve aynı arayüz bileşeniyle `GET /api/crm/contacts/{id}/timeline` ucundan beslenir.

## Bağlam

Firma kartında birleşik çizelge var (ADR-0053). Kişi kartında (`crm/kisiler/{id}`) yok; kullanıcı "bu yetkiliyle en son ne konuşuldu" sorusu için firma çizelgesini açıp gözle süzüyor. Kişi, firmanın çok sayıda kaydının yalnızca bir kısmına bağlıdır, bu yüzden firma çizelgesini kişiye göre süzmek yetmez: bağ her kaynakta ayrı bir alandadır.

Kaynakların bugünkü kişi bağları (koddan):

| Kaynak | Kişiye bağ | Not |
|---|---|---|
| `crm.activities` | katılımcı `ContactId` (CRM kişisi) | birden çok katılımcı olabilir |
| `crm.quotes` | `Quote.ContactId` (CRM kişisi) | durum satırları da bu bağdan |
| `crm.opportunities`, `crm.leads` | doğrudan kişi alanı yok | fırsat ve aday firmaya/ana kişiye bağlı; kapsam dışı kalabilir |
| `orders.orders` | `Order.PersonId` (ortak kişi) | CRM kişisi değil, `core.person` |
| `contracts.contracts` | `Contract.PersonId`, taraf `PersonId` | sözleşme ve taraf düzeyinde |
| `support.tickets` | `Ticket.PersonId` (ortak kişi) | |
| `marketing` iletim/yanıt | `PersonId` | kişiye giden ileti zaten kişi düzeyinde (ADR-0053 sınırı) |

İki kimlik türü vardır: CRM kişisi (`crm.Contacts`, `Contact.PersonId` ortak kişiyi gösterir) ve ortak kişi (`core.person`). Modüller CRM'in tablosunu okuyamadığı için sözleşmeye **ortak kişi kimliği** konmalıdır; CRM kaynakları kendi tarafında CRM kişisine çevirir.

## Karar

1. **Sorgu genişlemesi:** `CustomerTimelineQuery`'ye isteğe bağlı `Guid? PersonId` (ortak kişi) eklenir; ikisi birlikte verilmez: ya firma (`CompanyId`) ya kişi. Mevcut alanlar ve firma yolu değişmez; ikili kayıt (`record`) varsayılan parametreyle genişler, eski kaynaklar derlenmeye devam eder.
2. **Kapsam bildirimi:** `ICustomerTimelineSource`'a varsayılan uygulamalı `bool SupportsPerson => false` eklenir. `true` diyen kaynak `PersonId` ile sorguyu uygular; `false` diyen kaynak kişi çizelgesinde **hiç çağrılmaz ve yanıtta kaynak listesine girmez** (böylece "okunabilir, boş" ile "kişiye bağlanamaz" karışmaz).
3. **Kaynaklar:** ilk dilimde `crm.activities`, `crm.quotes` (+ durum satırları), `orders.orders`, `contracts.contracts`, `support.tickets` ve pazarlama iletim/yanıt kaynağı kişiyi destekler. `crm.opportunities` ve `crm.leads` desteklemez (kişi alanı yok); bunlar için alan eklemek ayrı karardır.
4. **Uç nokta:** `GET /api/crm/contacts/{id}/timeline` firma ucuyla aynı parametreleri (`before`, `take`, `sources`) ve aynı yanıt biçimini kullanır. Kişi kaydı kayıt boru hattından okunur; görünmeyen kişi 404. Yetki firma ucuyla aynıdır: her kaynak çağıranın izni, veri kapsamı ve lisansıyla kendi satırını üretir, birleştirici hiçbir yetki vermez. Kişi firmasızsa (`CompanyId` boş) yine çalışır.
5. **Arayüz:** `CustomerTimeline.razor` firma yerine kişi ile de kullanılabilir (`Source` parametresi), kişi detayında "Zaman çizelgesi" sekmesi (ADR-0053'teki firma sekmesiyle aynı kalıp). Özet şeridi kişi için yalnızca desteklenen kaynakları sayar.
6. **Migration yok.** Mevcut tablo ve dizinler kullanılır; kişi süzgeci her kaynakta zaten var olan `personId`/`contactId` alanıdır (gerekirse dizin ayrı küçük adımda eklenir).

## Dilimler

1. Sözleşme genişlemesi (`PersonId`, `SupportsPerson`), uç nokta ve birleştirici; yalnızca CRM etkinlik ve teklif kaynakları.
2. Sipariş, Sözleşme, Destek kaynakları kişiyi destekler (her biri kendi modülünde, kendi testiyle).
3. Pazarlama iletim ve yanıt kaynağı.
4. Kişi detayında sekme, özet şeridi, görsel doğrulama (açık/koyu tema, telefon/tablet/masaüstü).

## Testler (her dilimde dört test)

- **Mutlu yol:** kişiye bağlı kayıtlar kişi çizelgesinde en yeniden eskiye çıkar; başka kişinin kaydı çıkmaz.
- **Doğrulama:** olmayan kişi 404; geçersiz `sources`/`take` 422.
- **Tenant izolasyonu:** A tenant'ının kişisi için B tenant'ının kaydı görünmez; başka tenant'ın kişi kimliği 404.
- **Yetki:** kaynak okuma izni olmayan kullanıcı o kaynağın satırını görmez; veri kapsamı "kendi" olan başkasının kaydını görmez; kapalı modülün kaynağı yanıtta yoktur.
- Sözleşme birim testi: `SupportsPerson` varsayılanı `false`; kişi sorgusunda desteklemeyen kaynak çağrılmaz.

## Alternatifler

- **Firma çizelgesini istemcide kişiye göre süzmek:** reddedildi; sayfalama ve `Take` sınırı bozulur, kişiye bağlı olmayan satırlar gereksiz okunur, yetki dışı veri istemciye taşınır.
- **Kişi için ayrı sözleşme (`IPersonTimelineSource`):** reddedildi; sekiz kaynağın hepsi iki arayüz uygulamak zorunda kalır, birleştirici ikiye bölünür. Tek arayüz, isteğe bağlı kapsamla yeterli.
- **Yalnızca CRM kişisi kimliği taşımak:** reddedildi; Sipariş/Sözleşme/Destek CRM'e başvuramaz (mimari kural), ortak kişi kimliği ortak dildir.

## Yapılmadı / sınırlar (öneri kabul edilirse de)

- Fırsat ve müşteri adayı kişi çizelgesinde yoktur (kişi alanı yok); firmanın ana kişisi gibi dolaylı bir bağ kurulmaz.
- Aktivitede kişi katılımcıysa satır çıkar; kişinin firmasına bağlı ama kişiyi anmayan aktivite çıkmaz (firma çizelgesindedir).
- Kişi firma değiştirirse eski firmayla kurulmuş kayıtlar kayıtların bağına göre görünür; geçmiş yeniden yazılmaz.
- Kişiler arası birleştirme (`ContactMerge`) sonrası kimlik eşlemesi çizelgeyi izler; ayrıca bir geçiş yapılmaz.

## Verilen kararlar (26 Kasım 2026)

1. **Kapsam:** ilk dilim yalnızca CRM kaynakları (aktivite, teklif); Sipariş, Sözleşme, Destek ve Pazarlama sonraki dilimlerde.
2. **Fırsat ve aday:** kişi alanı **eklenecek** (migration gerektirir; ayrı küçük adım, "migration'da dur ve bildir" kuralı). Alan eklenip kaynak `ContactScope` bildirince kişi çizelgesine kendiliğinden girer; birleştirici ve arayüz değişmez.
3. **Özet şeridi:** kişi kartında **olacak**; kaynakların kendi son hareket ve açık iş bildirimlerinden gelir.

**Dinamiklik ilkesi:** hangi kaynakların kişi çizelgesinde olduğu ve hangi alanın kişiye bağ sayıldığı hiçbir yerde sabit listeyle yazılmaz. Kaynak kayıtlıysa ve `SupportsPerson` diyorsa girer; arayüz sekmeleri, süzgeç çipleri ve özet kartları API'nin döndürdüğü `sources` ve `open` listelerinden çizilir; yeni bir modül yalnızca arayüzü uygulayıp kaydolarak (ve kişi bağını bildirerek) kişi çizelgesine girer. Fırsat/aday kişi alanı da form metadata'sında (ADR-0021) tanımlanır, firma özelleştirebilir.

## Dilim 1'de uygulanan ayrıntı (öneriden sapma)

- Sorgu hem `ContactId` (CRM kişisi; CRM kaynakları) hem `PersonId` (ortak kişi; CRM dışı kaynaklar, sonraki dilimler) taşır; CRM kişisi `crm.Contacts` kaydıdır ve CRM dışı modüller CRM'e başvuramaz. `CompanyId` kişinin ana firması, firmasızsa `Guid.Empty`'dir; `IsPerson` kişi sorgusunu ayırt eder.
- Uç ve birleştirici ortaktır (`TimelineComposer`); firma ve kişi uçları ince katmandır. Kişi sorgusunda desteklemeyen kaynak çağrılmaz ve `sources` süzgecinde tanınmaz (400).
- CRM kaynağı kişiyi `ContactScope` ile bildirir: aktivite `related=crm.contact:{id}` (kişi, dönüşen aday ve bağlantılar), teklif `contactId`; teklif durum satırları (kabul, red, süre dolumu, iptal) kişinin tekliflerinden üretilir.

## Dilim 2'de uygulanan ayrıntı

- Sipariş, Sözleşme ve Destek kaynakları kayıtlarını zaten ortak kişiye (`personId`) bağlıyordu; üçü de `SupportsPerson => true` bildirir. Süzgeç alanı sorgudan gelir: `CustomerTimelineQuery.SharedRecordScope` firma sorgusunda `partyId`, kişi sorgusunda `personId` verir; ortak kaydı olmayan firma ya da kişide kaynak "okunabilir, boş"tur. Açık iş sayıları da aynı bağdan sayılır.
- Pazarlama kaynağı geçmişi ortak kişi kimliğiyle sorar (`ICampaignReports.HistoryAsync`, iletim `PersonId`'si ve yanıt `personId`'si zaten eşleşir); rapor, alıcı ve yanıt izinleri aynen uygulanır.
- Sözleşmede yalnızca sözleşmenin kendi `personId`'si sayılır; sözleşme tarafı (`ContractParty.PersonId`) olarak anılan kişi çizelgeye girmez (sınır; istenirse ayrı karar).
- Hiçbir kaynak adı birleştiriciye ya da arayüze yazılmadı: bildirim kaynağın kendisindedir.
