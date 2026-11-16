using Modulix.Core.Contracts.Modules;

namespace Modulix.Core.Modules;

/// <summary>Açık modül kümesinde karşılanmayan bağımlılık: <see cref="ModuleKey"/> açık, <see cref="DependencyKey"/> kapalı.</summary>
public sealed record MissingDependency(string ModuleKey, string DependencyKey, ModuleDependencyKind Kind, string Reason);

/// <summary>
/// Modül bağımlılık kuralı (ADR-0051): bir açık modül kümesinin beyanlara uyup uymadığını söyler. Modül hakkı yazan her yol
/// (platformdan modül açma/kapama, firma oluşturma, plan tanımı ve atama, sektör profili) bunu kullanır; kural başka yerde yazılmaz.
/// Katalogda bulunmayan (kurulu olmayan) sağlayıcıya bağımlılık sayılmaz. Çalışma zamanını etkilemez (ADR-0044).
/// </summary>
public static class ModuleDependencyRules
{
    /// <summary>Kümede karşılanmayan bağımlılıklar, katalog sırasıyla. <paramref name="kind"/> verilmezse iki tür de döner.</summary>
    public static IReadOnlyList<MissingDependency> Missing(
        ModuleCatalog catalog, IEnumerable<string> enabledModules, ModuleDependencyKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(enabledModules);
        var enabled = enabledModules.ToHashSet(StringComparer.Ordinal);

        return [.. catalog.Modules
            .Where(m => enabled.Contains(m.Key))
            .SelectMany(m => m.Dependencies
                .Where(d => (kind is null || d.Kind == kind) && catalog.ContainsModule(d.ModuleKey) && !enabled.Contains(d.ModuleKey))
                .Select(d => new MissingDependency(m.Key, d.ModuleKey, d.Kind, d.Reason)))];
    }

    /// <summary>
    /// Kümede karşılanmayan zorunlu bağımlılık varsa <see cref="ArgumentException"/> fırlatır (ParamName <paramref name="paramName"/>).
    /// <paramref name="ignore"/>: değişiklikten önce de var olan ihlaller; bunlar değişikliği engellemez (mevcut firmalar, ADR-0051 karar 4).
    /// </summary>
    public static void EnsureRequiredSatisfied(
        ModuleCatalog catalog, IEnumerable<string> enabledModules, string paramName, IEnumerable<string>? ignore = null)
    {
        var known = ignore is null
            ? []
            : Missing(catalog, ignore, ModuleDependencyKind.Required).Select(m => (m.ModuleKey, m.DependencyKey)).ToHashSet();
        var missing = Missing(catalog, enabledModules, ModuleDependencyKind.Required)
            .Where(m => !known.Contains((m.ModuleKey, m.DependencyKey)))
            .ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(Describe(catalog, missing), paramName);
        }
    }

    /// <summary>Kullanıcıya gösterilen ileti (ör. "'Pazarlama' modülü 'CRM' açık olmadan kullanılamaz.").</summary>
    public static string Describe(ModuleCatalog catalog, IReadOnlyList<MissingDependency> missing)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(missing);
        return string.Join(" ", missing.Select(m =>
            $"'{Name(catalog, m.ModuleKey)}' modülü '{Name(catalog, m.DependencyKey)}' açık olmadan kullanılamaz."));
    }

    private static string Name(ModuleCatalog catalog, string moduleKey) => catalog.Find(moduleKey)?.DisplayName ?? moduleKey;
}
