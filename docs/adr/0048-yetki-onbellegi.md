# ADR-0048: İstek başı yetki verisinin kısa süreli önbelleği

- **Durum:** Kabul edildi, 14 Kasım 2026 (30 saniyelik gecikme kullanıcı tarafından onaylandı). İstek içi tekrarın kaldırılması ve istek başına tek bağlantı bu ADR'den bağımsız olarak yapıldı (aşağıda "Önce yapılanlar").
- **Karar:** Her oturumlu istekte okunan üyelik, rol, departman, modül hakkı, erişilebilir şirket ve iki adımlı doğrulama zorunluluğu verisi uygulama içi `IMemoryCache`'te **30 saniye** tutulur. Bu verileri değiştiren her kayıt, aynı süreçte önbelleği **hemen** boşaltır; başka sunucu örneklerinde ve veritabanına doğrudan yazan araçlarda değişiklik en geç 30 saniyede görünür.

> Numara notu: 14 Kasım 2026'da `main`'de en yüksek numara 0047'ydi. Birleştirmeden önce numara yeniden kontrol edilmeli.

## Bağlam

Kullanıcılar ekranların yavaş açıldığını bildirdi. Oturumlu her API isteği, uç noktanın kendi işinden önce şu denetimleri veritabanından yapar:

| Adım | Kaynak | Sorgu |
|---|---|---|
| Oturum kapatılmış mı (Y7a) | `UserSessions.ValidateAsync` | 1 |
| Üyelik ve tenant etkin mi | `MembershipDirectory.FindActiveAsync` | 1 |
| Firma iki adımlı doğrulama istiyor mu (Y7b) | `TenantSettings.GetValueAsync` (+ açıksa kullanıcı) | 1–2 |
| Erişilebilir şirketler (ADR-0008) | `MembershipDirectory.GetAccessibleCompaniesAsync` | 3 |
| Açık modüller | `ModuleEntitlements.GetEnabledAsync` | 1 |
| Roller ve departman (izin, kapsam) | `MembershipPermissions.LoadRolesAsync` | 3 |
| Kayıt kapsamı istisnaları, platform tavanı (ADR-0030) | `MembershipPermissions`, `ScopeLimits` | 1–2 |

Rolü olan (yönetici olmayan) kişide roller ve departman üç kez okunduğu için sayı daha yüksekti. Sorguların her biri 1 ms'nin altında; sorun sayı ve tur. Yük altında (aynı makinede derleme ve paralel testler) her tur gecikmeyi büyütür. Ölçüm: `tests/Modulix.IntegrationTests/RequestQueryCountTests.cs` (`GET /api/crm/companies`).

| Durum (yönetici, ısınmış ikinci istek) | Komut | Bağlantı açılışı (tenant varken her biri ayrıca `sp_set_session_context` turu) |
|---|---|---|
| Önce (`main`, bae66ffe) | 17 (14 denetim + 3 uç nokta) | 17 |
| Sonra (bu ADR + istek içi tekrar + tek bağlantı) | 4 (oturum denetimi + 3 uç nokta) | 2 (oturum denetimi + tenant kesinleşince tutulan bağlantı) |

### Önce yapılanlar (ADR gerektirmeyen, davranışı değiştirmeyen)

1. **İstek içi tekrar.** Roller ve departman aynı istekte izin, kayıt kapsamı ve "kapsam dışı ortak kayıtlar gizli mi" sorularının her biri için ayrı ayrı okunuyordu (3 × 3 sorgu). `MembershipPermissions` artık bunları istek (scope) içinde üyelik başına bir kez okur; aynı bağlamda bir kayıt yazılınca (`SavedChanges`) bellek boşalır, yani rol değiştiren işlemin sonundaki okuma güncel veriyi görür.
2. **İstek başına tek bağlantı.** EF her sorguda bağlantıyı açıp kapatıyordu; RLS interceptor'ı her açılışta ayrı bir `sp_set_session_context` turu ekliyordu. Aktif tenant kesinleşince (`TenantContextMiddleware`, iki adımlı doğrulama denetiminden sonra) bağlantı açılır ve istek sonuna kadar açık kalır. Tenant bu noktadan sonra istekte değişmez (aksi yalnızca ayrı scope açan arka plan yollarında olur); oturum değeri salt okunur olduğu için yanlış tenant'a geçiş zaten imkânsızdır. Bedeli: bir istek bağlantıyı süresi boyunca tutar; dış servis bekleyen uzun istekler havuzdan bir bağlantıyı daha uzun süre kullanır (bugün akış/websocket ucu yok).

## Seçenekler

1. **Önbellek yok (bugün).** Her değişiklik bir sonraki istekte geçerli. Sorgu sayısı yalnızca yukarıdaki iki adım kadar azalır.
2. **Yalnızca TTL'li önbellek (geçersiz kılma yok).** Basit; ama rol kaldırma, üyelik kapatma, modül kapatma 30 saniye boyunca etkisiz kalır. Yönetici "kaldırdım ama hâlâ görüyor" der; reddedildi.
3. **TTL + yazmada geçersiz kılma (seçildi).** Aynı süreçte değişiklik hemen geçerli, başka süreçlerde en geç TTL.
4. **Dağıtık önbellek (Redis) + yayınla/abone ol.** Çok örnekli kurulumda da anında; ama yeni altyapı bileşeni, on-prem kurulumu ağırlaştırır. Bugün tek örnek çalışıyoruz; gerekirse 3'ün üstüne eklenebilir (geçersiz kılma tek noktadan yapıldığı için).

## Karar ayrıntısı (seçenek 3)

- **Ne önbelleğe girer:** etkin üyelik özeti (kullanıcı + tenant; `MembershipDirectory.FindActiveAsync`), firmanın iki adımlı doğrulama ayarı (`TenantContextMiddleware`), erişilebilir şirketler (üyelik), açık modüller (tenant; `ModuleEntitlements.GetEnabledAsync`), roller ve departman (üyelik; `MembershipPermissions`), rol kapsam istisnaları ve platform tavanları (tenant). Uygulama: `Modulix.Infrastructure/Authorization/AccessCache.cs`.
- **Ne girmez:** oturumun kapatılıp kapatılmadığı (Y7a; "oturumu kapat" anında geçerli olmalı, 1 sorgu); destek üyeliği (süresi dolunca hemen düşmeli; her istekte sorulur); kişinin kendi iki adımlı doğrulama durumu (yalnızca firma ayarı açıkken sorulur); modül lisans süresi (`IModuleLicense`); kayıt verisi ve takım üyelikleri.
- **Anahtar her zaman tenant kimliğini içerir.** Önbellek süreç genelidir, RLS'nin dışındadır; tenant'a ait girdi yalnızca isteğin aktif tenant'ının kimliğiyle okunur ve yazılır, değer o tenant'ın oturumuyla (RLS altında) okunmuştur. Aktif tenant'ı olmayan çağrı (platform işlemi) önbelleği kullanmaz. Açık veritabanı işlemi içinde okunan değer saklanmaz (kaydedilmemiş değişiklik görebilir).
- **Geçersiz kılma tek noktadan:** `AccessCacheInvalidator` (EF `SaveChanges` ve işlem interceptor'ı), kaydedilen değişiklikler arasında yetkiyi etkileyen türleri (tenant, üyelik, destek erişimi, modül hakkı, tenant ayarı, platform tavanı, rol, üyelik rolü, rol kapsam istisnası, şirket, üyelik şirketi, departman, departman üyeliği, departman rolü) görürse o tenant'ın sürüm numarasını artırır; girdiler sürümle anahtarlandığı için eski girdiler bir daha okunmaz. Açık işlemdeki kayıt, işlem sonlanınca bir kez daha geçersiz kılar. Uç noktaları tek tek listelemek gerekmez; yeni bir yazma ucu kendiliğinden kapsanır. Bu tablolara `ExecuteUpdate`/`ExecuteDelete` ya da ham SQL ile yazan kod bu yolu atlar (değişiklik en geç 30 saniyede görünür); bugün bu tablolarda tek toplu yazma `Memberships.LastSignInAt`'tir ve önbellekteki alanları etkilemez. Yeni toplu yazma eklenirse `AccessCache.Invalidate` açıkça çağrılır.
- **Süre:** 30 saniye mutlak. Başka sunucu örneği, `tenant-delete` gibi komut satırı araçları, doğrudan veritabanı müdahalesi ve zamana bağlı geçerlilik (geçici vekâletin başlangıç/bitişi, ADR-0008 adım 5) için gecikme üst sınırı budur.
- **Güvenlik etkisi:** Tek örnekte yazma ile değişiklik anında geçerli. Çok örnekte üyeliği kapatılan ya da rolü daraltılan kişi diğer örneklerde en geç 30 saniye eski yetkiyle çalışabilir. Oturum kapatma (Y7a) önbelleğe girmediği için "bütün oturumları kapat" anında etkilidir; acil erişim kesme bu yolla yapılır.
- **Testler:** `AccessCacheTests` — mutlu yol (önbellekten tekrarlanan istekler), hata (rol değişince bir sonraki istek 403), yetki (üyelik ya da modül kapatılınca bir sonraki istek 403), tenant izolasyonu (A'daki önbellek girdisi aynı kişinin B'deki isteğinde kullanılmaz); `RequestQueryCountTests` sorgu bütçesini denetler.

## Sonuçlar

- Olumlu: önbellek sıcakken oturumlu istek başına yetki için yalnızca oturum denetimi kalır; yük altında gecikme belirgin düşer.
- Olumsuz: çok örnekli kurulumda yetki değişikliği 30 saniyeye kadar gecikebilir; önbelleğin bellek kullanımı (etkin üyelik başına küçük girdiler, 30 saniyede düşer).
- İzlenecek: çok örnekli kuruluma geçilirse seçenek 4 yeniden değerlendirilir.
