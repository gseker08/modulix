using System.Globalization;

namespace Modulix.Core.Contracts.Localization;

/// <summary>
/// Sunucunun ürettiği, çevrilebilir bir ileti (ADR-0034 §6): sözlük anahtarı, yer tutucu değerleri ve Türkçe kaynak metin.
/// <see cref="Text"/> her zaman doludur (yer tutucular işlenmiş Türkçe metin, <c>tr.json</c> ile aynı); çevirinin yapılamadığı
/// her yerde (arka plan işi, günlük, içe aktarma satırı) o kullanılır. Anahtarı olmayan ileti henüz sözlüğe taşınmamıştır ve
/// olduğu gibi gösterilir; düz metinden örtük dönüşüm bu yüzden vardır (eski çağıranlar değişmeden derlenir).
/// </summary>
public sealed class ServerMessage
{
    public ServerMessage(string text, string? key = null, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Key = key;
        Args = args;
    }

    /// <summary>Türkçe kaynak metin (yer tutucular işlenmiş).</summary>
    public string Text { get; }

    /// <summary>Sözlük anahtarı (<c>server.*</c>); null ise ileti çevrilmez.</summary>
    public string? Key { get; }

    /// <summary>Anahtarın metnindeki <c>{0}</c>, <c>{1}</c> yer tutucularının değerleri.</summary>
    public IReadOnlyList<object?> Args { get; }

    /// <summary>Anahtar ve Türkçe kalıptan ileti: <paramref name="turkish"/> <c>tr.json</c>'daki kalıptır, değerler değişmez kültürle işlenir.</summary>
    public static ServerMessage Of(string key, string turkish, params object?[] args) =>
        new(args.Length == 0 ? turkish : string.Format(CultureInfo.InvariantCulture, turkish, args), key, args);

    public static implicit operator ServerMessage(string text) => new(text);

    public override string ToString() => Text;
}
