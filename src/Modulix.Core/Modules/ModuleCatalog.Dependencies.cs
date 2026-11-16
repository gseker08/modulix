using Modulix.Core.Contracts.Modules;

namespace Modulix.Core.Modules;

/// <summary>Modül bağımlılık beyanlarının açılış doğrulaması (ADR-0051).</summary>
public sealed partial class ModuleCatalog
{
    /// <summary>Modülün beyan ettiği bağımlılıklar; modül katalogda yoksa boş.</summary>
    public IReadOnlyList<ModuleDependency> DependenciesOf(string moduleKey) => Find(moduleKey)?.Dependencies ?? [];

    /// <summary>Bağımlılıklar başka modüllere başvurur; bütün modüller okunduktan sonra denetlenir.</summary>
    private void ValidateDependencies()
    {
        foreach (var module in modules.Values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in module.Dependencies)
            {
                if (dependency.ModuleKey == module.Key)
                {
                    throw new InvalidOperationException($"'{module.Key}' modülü kendisine bağımlı olamaz.");
                }

                if (IsAlwaysEnabled(dependency.ModuleKey))
                {
                    throw new InvalidOperationException(
                        $"'{module.Key}' modülü '{dependency.ModuleKey}' bağımlılığı beyan edemez: her zaman açıktır.");
                }

                // Sağlayan modül bu katalogda olmayabilir (kurulu değil; testler kataloğu alt kümeyle kurar): hata değildir,
                // çağıran "yok" sonucunu ele alır (ADR-0044). Beyanın gerçek bir modülü gösterdiğini mimari test denetler.
                if (!AccessKeys.IsValidModuleKey(dependency.ModuleKey))
                {
                    throw new InvalidOperationException($"'{module.Key}' modülünde geçersiz bağımlılık anahtarı: '{dependency.ModuleKey}'.");
                }

                if (!Enum.IsDefined(dependency.Kind))
                {
                    throw new InvalidOperationException($"'{module.Key}' modülünün '{dependency.ModuleKey}' bağımlılığında geçersiz tür.");
                }

                if (string.IsNullOrWhiteSpace(dependency.Reason))
                {
                    throw new InvalidOperationException($"'{module.Key}' modülünün '{dependency.ModuleKey}' bağımlılığında gerekçe boş.");
                }

                if (!seen.Add(dependency.ModuleKey))
                {
                    throw new InvalidOperationException($"'{module.Key}' modülünde bağımlılık iki kez beyan edilmiş: '{dependency.ModuleKey}'.");
                }
            }
        }

        // Zorunlu bağımlılıklarda döngü olursa döngüdeki modüllerin hiçbiri ilk açılan olamaz.
        var finished = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in modules.Keys)
        {
            RejectRequiredCycle(module, [], finished);
        }
    }

    private void RejectRequiredCycle(string moduleKey, List<string> path, HashSet<string> finished)
    {
        if (finished.Contains(moduleKey))
        {
            return;
        }

        var start = path.IndexOf(moduleKey);
        if (start >= 0)
        {
            throw new InvalidOperationException(
                $"Zorunlu modül bağımlılıklarında döngü: {string.Join(" → ", path.Skip(start).Append(moduleKey))}.");
        }

        path.Add(moduleKey);
        foreach (var dependency in modules[moduleKey].Dependencies
                     .Where(d => d.Kind == ModuleDependencyKind.Required && modules.ContainsKey(d.ModuleKey)))
        {
            RejectRequiredCycle(dependency.ModuleKey, path, finished);
        }

        path.RemoveAt(path.Count - 1);
        finished.Add(moduleKey);
    }
}
