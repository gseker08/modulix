using System.Text.RegularExpressions;

namespace Modulix.ArchitectureTests;

/// <summary>
/// ADR-0051: bir modül başka bir modülün sözleşme projesine başvuruyorsa bu iki modül arasında bir bağımlılık beyanı
/// (<c>IModule.Dependencies</c>) bulunur. Yön serbesttir: genişleme noktasında başvuran taraf sağlayandır (CRM, Pazarlama'nın
/// sözleşmesini uygular; bağımlı olan Pazarlama'dır). Beyan <c>new ModuleDependency("anahtar", ...)</c> biçiminde yazılır.
/// </summary>
public partial class ModuleDependencyRulesTests
{
    private const string ModulePrefix = "Modulix.Modules.";

    [Fact]
    public void A_reference_to_another_modules_contracts_is_covered_by_a_declared_dependency()
    {
        var modules = RepositoryLayout.SourceProjects()
            .Where(p => RepositoryLayout.ProjectName(p).StartsWith(ModulePrefix, StringComparison.Ordinal)
                && !RepositoryLayout.IsModuleContracts(RepositoryLayout.ProjectName(p)))
            .Select(Read)
            .ToDictionary(m => m.Project, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var module in modules.Values)
        {
            foreach (var reference in RepositoryLayout.ProjectReferences(module.Csproj).Where(RepositoryLayout.IsModuleContracts))
            {
                var ownerProject = reference[..^".Contracts".Length];
                if (ownerProject == module.Project || !modules.TryGetValue(ownerProject, out var owner))
                {
                    continue;
                }

                if (!module.Declared.Contains(owner.Key) && !owner.Declared.Contains(module.Key))
                {
                    problems.Add(
                        $"{module.Project} → {reference}: '{module.Key}' ile '{owner.Key}' arasında IModule.Dependencies beyanı yok (ADR-0051).");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Declared_dependencies_name_existing_modules()
    {
        var modules = RepositoryLayout.SourceProjects()
            .Where(p => RepositoryLayout.ProjectName(p).StartsWith(ModulePrefix, StringComparison.Ordinal)
                && !RepositoryLayout.IsModuleContracts(RepositoryLayout.ProjectName(p)))
            .Select(Read)
            .ToList();
        var keys = modules.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);

        var unknown = modules
            .SelectMany(m => m.Declared.Where(d => !keys.Contains(d)).Select(d => $"{m.Project}: bilinmeyen bağımlılık '{d}'."))
            .ToList();

        Assert.True(unknown.Count == 0, string.Join(Environment.NewLine, unknown));
    }

    private static ModuleSource Read(FileInfo csproj)
    {
        var project = RepositoryLayout.ProjectName(csproj);
        var sources = csproj.Directory!
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(f => File.ReadAllText(f.FullName))
            .ToList();

        var keys = sources.SelectMany(s => ModuleKeyPattern().Matches(s).Select(m => m.Groups["key"].Value)).Distinct().ToList();
        Assert.True(keys.Count == 1, $"{project}: tek bir 'const string ModuleKey = \"...\"' bekleniyordu, bulunan: {keys.Count}.");

        var declared = sources
            .SelectMany(s => DependencyPattern().Matches(s).Select(m => m.Groups["key"].Value))
            .ToHashSet(StringComparer.Ordinal);
        return new ModuleSource(project, csproj, keys[0], declared);
    }

    private sealed record ModuleSource(string Project, FileInfo Csproj, string Key, IReadOnlySet<string> Declared);

    [GeneratedRegex(@"const\s+string\s+ModuleKey\s*=\s*""(?<key>[a-z0-9]+)""")]
    private static partial Regex ModuleKeyPattern();

    [GeneratedRegex(@"new\s+ModuleDependency\(\s*""(?<key>[a-z0-9]+)""")]
    private static partial Regex DependencyPattern();
}
