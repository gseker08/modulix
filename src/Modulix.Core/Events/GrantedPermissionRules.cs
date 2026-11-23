namespace Modulix.Core.Events;

/// <summary>
/// <see cref="Modulix.Core.Contracts.Modules.IGrantedExecution"/> kuralı: verilen izinlerden, işin kimliğinin aboneliğinde bildirilmeyenler yalnızca
/// okuma izni olabilir. Böylece bir modül işi başka modülün yazma iznini kendine veremez.
/// </summary>
public static class GrantedPermissionRules
{
    public const string ReadSuffix = ".read";

    /// <summary>Verilen kümeyi doğrular ve etkin küme olarak döner; kural dışı izin varsa <see cref="ArgumentException"/>.</summary>
    public static IReadOnlySet<string> Validate(IReadOnlyCollection<string> granted, IReadOnlyCollection<string> declared)
    {
        ArgumentNullException.ThrowIfNull(granted);
        ArgumentNullException.ThrowIfNull(declared);
        var declaredSet = declared.ToHashSet(StringComparer.Ordinal);
        var result = new HashSet<string>(declaredSet, StringComparer.Ordinal);
        foreach (var permission in granted)
        {
            if (!declaredSet.Contains(permission) && !permission.EndsWith(ReadSuffix, StringComparison.Ordinal))
            {
                throw new ArgumentException($"'{permission}' izni verilemez: iş kimliğinin dışındaki izinler yalnızca okuma izni olabilir.", nameof(granted));
            }

            result.Add(permission);
        }

        return result;
    }
}
