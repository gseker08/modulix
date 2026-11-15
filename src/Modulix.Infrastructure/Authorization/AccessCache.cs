using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Modulix.Infrastructure.Authorization;

/// <summary>
/// Her oturumlu istekte okunan yetki verisinin (üyelik, roller, departman, açık modüller, erişilebilir şirketler, kapsam istisnaları
/// ve tavanları, iki adımlı doğrulama ayarı) kısa süreli önbelleği (ADR-0048). Girdi en çok <see cref="Ttl"/> yaşar; bu süreçte bu
/// verileri değiştiren her kayıt <see cref="AccessCacheInvalidator"/> üzerinden tenant'ın girdilerini hemen geçersiz kılar
/// (tenant sürümü artar, eski sürümle yazılmış girdiler bir daha okunmaz).
/// Önbellek süreç genelidir ve RLS'nin dışındadır: anahtar her zaman tenant kimliğini içerir, değer o tenant'ın oturumuyla okunur.
/// </summary>
public sealed class AccessCache(IMemoryCache cache)
{
    /// <summary>Başka sunucu örneğinde ya da veritabanına doğrudan yazılan değişikliğin görünmesi için üst sınır.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, long> generations = new();
    private long globalGeneration;

    /// <summary>
    /// Önbellekte varsa değeri döner; yoksa okur ve saklar. Açık bir veritabanı işlemi içinde okunan değer saklanmaz: işlem henüz
    /// kaydedilmemiş (geri alınabilecek) değişiklik görebilir. <paramref name="store"/> verilirse yalnızca onun kabul ettiği değer saklanır.
    /// </summary>
    public async Task<T> GetOrLoadAsync<T>(string kind, Guid tenantId, Guid id, DbContext db, Func<Task<T>> load, Func<T, bool>? store = null)
    {
        // Sürüm okumadan ÖNCE alınır: okuma sürerken gelen geçersiz kılma, bu değeri artık okunmayan eski sürüme yazdırır.
        var key = new Key(kind, tenantId, id, generations.GetValueOrDefault(tenantId), Interlocked.Read(ref globalGeneration));
        if (cache.TryGetValue(key, out T? hit))
        {
            return hit!;
        }

        var value = await load();
        if (db.Database.CurrentTransaction is null && (store is null || store(value)))
        {
            cache.Set(key, value, Ttl);
        }

        return value;
    }

    /// <summary>Tenant'ın bütün girdileri bir sonraki okumada veritabanından gelir.</summary>
    public void Invalidate(Guid tenantId) => generations.AddOrUpdate(tenantId, 1, (_, current) => current + 1);

    /// <summary>Bütün tenant'ların girdileri geçersiz olur (tenant'ı belirlenemeyen değişiklik).</summary>
    public void InvalidateAll() => Interlocked.Increment(ref globalGeneration);

    private sealed record Key(string Kind, Guid TenantId, Guid Id, long Generation, long GlobalGeneration);
}
