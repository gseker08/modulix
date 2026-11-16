namespace Modulix.Shared.Numbering;

// Belge numarası serileri (ADR-0049 dilim 2a): /api/numbering/series. Hepsi core.numbering.manage ister.

/// <summary>
/// Bir belge serisi. <c>Default*</c> modülün beyan ettiği varsayılan, diğerleri firmada uygulanan değerdir. <see cref="Reset"/>: "Never" ya da "Yearly".
/// <see cref="LastValue"/>: bu dönemde verilen son sıra (henüz numara verilmediyse 0); <see cref="NextNumber"/>: sıradaki numaranın ön izlemesi.
/// <see cref="Version"/> 0 ise firma seriye henüz dokunmamıştır; yazarken geri gönderilir, arada başkası değiştirdiyse 409.
/// </summary>
public sealed record NumberSeriesDto(
    string Key,
    string ModuleKey,
    string DisplayName,
    string Prefix,
    int Digits,
    string Reset,
    string DefaultPrefix,
    int DefaultDigits,
    string DefaultReset,
    bool IsCustomized,
    int Year,
    long LastValue,
    string NextNumber,
    long Version);

/// <summary>Önek 1-10 büyük harf ya da rakam (küçük harf büyüğe çevrilir), basamak 3-10, sıfırlama "Never" ya da "Yearly".</summary>
public sealed record ConfigureNumberSeriesRequest(string? Prefix, int Digits, string Reset, long Version);

/// <summary><see cref="NextValue"/>: bu dönemde verilecek sıradaki sıra numarası; yalnızca ileri alınabilir.</summary>
public sealed record AdvanceNumberSeriesRequest(long NextValue, long Version);
