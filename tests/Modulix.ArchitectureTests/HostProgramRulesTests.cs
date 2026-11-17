using System.Text.RegularExpressions;

namespace Modulix.ArchitectureTests;

/// <summary>
/// ADR-0046 Dilim 8 (kapanış): host modülü tanımaz. <c>Modulix.Api/Program.cs</c> modül projesine ya da modül
/// sözleşme projesine başvurmaz; modül listesi <c>ModulixModules.cs</c>'te, host'un sağladığı sözleşme
/// uygulamaları aynı dosyadaki <c>ModulixModuleBridges</c>'tadır. Yeni modül için <c>Program.cs</c> değişmez.
/// </summary>
public partial class HostProgramRulesTests
{
    [Fact]
    public void Program_has_no_module_specific_lines()
    {
        string path = Path.Combine(RepositoryLayout.Root.FullName, "src", "Modulix.Api", "Program.cs");
        var violations = File.ReadAllLines(path)
            .Select((line, index) => (line, number: index + 1))
            .Where(x => ModuleReference().IsMatch(x.line))
            .Select(x => $"Program.cs:{x.number}: {x.line.Trim()}")
            .ToList();

        Assert.True(violations.Count == 0,
            "Program.cs modüle özel satır içeremez (ADR-0046 Dilim 8); modül listesi ve köprüler ModulixModules.cs'e yazılır:\n"
            + string.Join('\n', violations));
    }

    [GeneratedRegex(@"\bModulix\.Modules\.")]
    private static partial Regex ModuleReference();
}
