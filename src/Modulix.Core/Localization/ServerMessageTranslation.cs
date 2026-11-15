using System.Globalization;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;

namespace Modulix.Core.Localization;

/// <summary>
/// Sunucu iletilerinin çevirisi (ADR-0034 §6). Anahtarı olan ileti sözlükten isteğin diline çevrilir; anahtarsız ileti, sözlükte
/// bulunmayan anahtar ya da yer tutucusu uymayan metin Türkçe kaynak metne düşer. Hata kodu (<see cref="RecordErrorCode"/>) ve
/// alan adları değişmez.
/// </summary>
public static class ServerMessageTranslation
{
    public static string Translate(this ITextCatalog catalog, string language, ServerMessage message) =>
        Translate(catalog, language, message.Key, message.Args, message.Text);

    /// <summary>Anahtarın <paramref name="language"/> dilindeki metni; çevrilemezse <paramref name="fallback"/>.</summary>
    public static string Translate(this ITextCatalog catalog, string language, string? key, IReadOnlyList<object?>? args, string fallback)
    {
        if (key is null || !catalog.Bundle(language).TryGetValue(key, out var text))
        {
            return fallback;
        }

        if (args is not { Count: > 0 })
        {
            return text;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, text, [.. args]);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    /// <summary>Hatada ya da alan hatalarında çevrilecek bir ileti var mı? Yoksa dil çözülmeden olduğu gibi döner.</summary>
    public static bool IsTranslatable(RecordError error) =>
        error.MessageKey is not null || error.FieldErrors?.Any(e => e.MessageKey is not null) == true;

    /// <summary>Hata ve alan hatalarının iletileri <paramref name="language"/> dilinde; kod, alan adları ve alan hata kodları aynı kalır.</summary>
    public static RecordError Translate(this ITextCatalog catalog, string language, RecordError error)
    {
        if (!IsTranslatable(error))
        {
            return error;
        }

        return error with
        {
            Message = Translate(catalog, language, error.MessageKey, error.MessageArgs, error.Message),
            FieldErrors = error.FieldErrors?
                .Select(e => e.MessageKey is null ? e : e with { Message = Translate(catalog, language, e.MessageKey, e.MessageArgs, e.Message) })
                .ToList(),
        };
    }
}
