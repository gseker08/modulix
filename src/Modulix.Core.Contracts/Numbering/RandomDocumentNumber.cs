using System.Security.Cryptography;

namespace Modulix.Core.Contracts.Numbering;

/// <summary>
/// Sıralı seriye geçmemiş kayıt türlerinin numarası (ADR-0049 dilim 3a): <c>ÖNEK-yyyyMMdd-XXXXXX</c>, son ek karışmayan harf ve rakamlardan
/// rastgele seçilir (I, O, 0, 1 yok). Sayaç tutmaz ve tahmin edilemez; benzersizliği tablonun <c>(TenantId, numara)</c> dizini güvenceye alır.
/// Belge niteliğindeki kayıtlar bunu değil <see cref="IDocumentNumbers"/> serisini kullanır.
/// </summary>
public static class RandomDocumentNumber
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int SuffixLength = 6;

    public static string New(string prefix, DateTimeOffset createdAt)
    {
        Span<char> suffix = stackalloc char[SuffixLength];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"{prefix}-{createdAt:yyyyMMdd}-{new string(suffix)}";
    }
}
