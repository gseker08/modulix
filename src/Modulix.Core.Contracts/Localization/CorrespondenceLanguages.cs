namespace Modulix.Core.Contracts.Localization;

/// <summary>
/// Kişinin ve firmanın yazışma dili (ADR-0034 §6): hesabı olmayan dış alıcıya (müşteri yetkilisi, firma) giden içeriğin ve ona açılan
/// herkese açık sayfaların dili. Kodlar arayüz dilleriyle aynıdır (sunucudaki <c>LanguageOptions</c>; eşitliği birim test denetler).
/// Boş değer "tenant varsayılanı" demektir.
/// </summary>
public static class CorrespondenceLanguages
{
    /// <summary>Sütun uzunluğu (dil kodu).</summary>
    public const int MaxLength = 5;

    public static IReadOnlyList<string> Codes { get; } = ["tr", "en", "az"];

    /// <summary>Desteklenen bir kod ise onu (küçük harfle), değilse <c>null</c>.</summary>
    public static string? Normalize(string? code)
    {
        var trimmed = code?.Trim().ToLowerInvariant();
        return trimmed is not null && Codes.Contains(trimmed) ? trimmed : null;
    }
}
