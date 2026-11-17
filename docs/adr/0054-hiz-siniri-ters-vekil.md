# ADR-0054: Çok sunuculu dağıtımda hız sınırı (ters vekil + sunucu payı)

- **Durum:** Kabul edildi, 17 Kasım 2026. Kullanıcı "ters vekil" seçeneğini seçti (mimari değerlendirme A4'ün kalan kalemi: hız sınırları düğüm belleğinde).
- **Karar:** Küme genelindeki ortak hız sınırı **ters vekilde** (nginx, bulut ağ geçidi ya da WAF) uygulanır. Uygulama içi sınırlar ikinci hat olarak kalır. Yapılandırmadaki dakikalık değerler **küme toplamıdır**; her sunucu payını `RateLimiting:Nodes` (sunucu sayısı) ile bölerek alır (`RateLimits.PerNode`, `Modulix.Core.Hosting`).

> Numara notu: 17 Kasım 2026'da `main`'de en yüksek numara 0053'tü. Birleştirmeden önce numara yeniden kontrol edilmeli.

## Bağlam

Uygulamada yedi hız sınırı ilkesi vardır:

- Oturumsuz kimlik uçları (IP başına).
- Oturumlu `/api` istekleri (kişi ve firma başına).
- Oturumsuz uçlar: iletişim tercih sayfası, web formu (müşteri adayı), teklif paylaşımı, destek anketi, imza sağlayıcısı webhook'u.

Hepsi ASP.NET Core'un bellek içi sayaçlarını kullanır. Tek sunucuda bu yeterlidir. N sunucuda her sunucu kendi sayacını tutar, gerçek sınır N katına çıkar. Örneğin 30/dk giriş sınırı üç sunucuda 90/dk olur.

Şifre denemesi saldırısına karşı asıl koruma hesap kilitlemedir. Kilitleme veritabanında tutulur ve sunucu sayısından etkilenmez. IP sınırı hesap taramasını ve yükü sınırlar.

## Seçenekler

1. **Ters vekilde ortak sınır + uygulamada sunucu payı (seçildi).** Ters vekil bütün istekleri görür; IP tabanlı ortak sınır orada tek yerde uygulanır. Uygulamaya istek başına ek yük gelmez ve yeni altyapı (Redis vb.) gerekmez; on-prem kurulumda da müşterinin mevcut vekiliyle çalışır.
2. **Veritabanı tabanlı ortak sayaç.** Her sunucu aynı sınırı görür. Ama her istek tek paylaşımlı veritabanına bir yazma ekler ve bu yük bütün firmalara yansır. Reddedildi.
3. **Dağıtık önbellek (Redis).** Doğru ve hızlıdır, ama yeni bir altyapı bileşeni, yedek ve izleme ister; on-prem kurulumu ağırlaştırır. Gerekirse `RateLimits` arkasına sonradan eklenebilir. Ertelendi.

## Ayrıntılar

- `RateLimits.PerNode(configuration, anahtar, varsayılan, minimum)`: `ceil(ayar / RateLimiting:Nodes)`, en az `minimum`. `RateLimiting:Nodes` verilmezse ya da 1'den küçükse 1 sayılır, yani tek sunucuda davranış değişmez.
- Oturumlu genel sınırda (`RateLimiting:UserPerMinute`, `RateLimiting:TenantPerMinute`) 0 ya da negatif değer sınırı kapatmaya devam eder (`minimum: 0`).
- Bölme, yük dengeleyicinin istekleri sunuculara yaklaşık eşit dağıttığını varsayar. Yapışkan oturumda bir kişinin bütün istekleri tek sunucuya düşer ve o kişinin sınırı payla sınırlanır, yani daha sıkı olur. Bu güvenli yöndür; varsayılanlar (kişi başına 600/dk) üç sunucuda bile 200/dk bırakır.
- Kişi ve firma başına sınır yalnızca uygulamadadır. Ters vekil oturum çerezini çözemez, kişiyi ve firmayı bilmez.
- Ters vekil arkasında istemci IP'si `ForwardedHeaders` ile okunur. Aksi halde bütün istekler vekilin IP'sinden sayılır ve IP sınırı bütün kullanıcıları birlikte keser.
- Yeni bir hız sınırı ilkesi izin sayısını `RateLimits.PerNode` ile alır. `RateLimitRulesTests` ayarı doğrudan okuyan ilkeyi yakalar.

## Sonuçlar

- Çok sunuculu dağıtımda işletmen iki şey yapar: `RateLimiting:Nodes`'u sunucu sayısına ayarlar ve ters vekilde ortak IP sınırını kurar (`docs/tenant-isletim.md`, "Hız sınırları ve ters vekil").
- Otomatik ölçeklemede sunucu sayısı değişir. `RateLimiting:Nodes` en büyük sayıya göre ayarlanırsa sınır güvenli yönde kalır.
- Kişi ve firma başına sınır çok sunucuda yaklaşıktır. Kesin sınır gerekirse seçenek 3 eklenir.
