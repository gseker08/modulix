using System.Text.RegularExpressions;

namespace Modulix.ArchitectureTests;

/// <summary>
/// ADR-0046: modül projesindeki her uç nokta izin kontrolüyle biter. Oturum ve modül lisans kontrolünü host uygular
/// (<c>MapModuleEndpoints</c>); izin (<c>RequirePermission</c> / <c>RequireAnyPermission</c>) modüldedir: uç noktada ya da uç noktanın grubunda. İzin serviste
/// denetleniyorsa bu <c>PermissionCheckedInService()</c> ile açıkça yazılır. Hiçbiri yoksa bu test kırılır. Yetkilendirme yalnızca arayüzde gizlemek değildir; sunucuda uygulanır.
/// </summary>
/// <remarks>
/// Denetim kaynak metni üzerindedir: <c>.MapGet(...)</c> gibi bir çağrıdan o ifadenin sonundaki <c>;</c> işaretine kadar olan
/// zincirde izin çağrısı aranır. İzni grup düzeyinde veren ya da gerçekten izinsiz olması gereken uç nokta
/// <see cref="WithoutPermission"/> listesine gerekçesiyle yazılır.
/// </remarks>
public partial class ModuleEndpointRulesTests
{
    /// <summary>
    /// İzin çağrısı aranmayan uç noktalar: "proje/dosya: yol" → gerekçe. Yalnızca oturumsuz uçlar (<c>IModulePublicEndpoints</c>, ADR-0046): tenant ve lisans
    /// <c>IPublicTenantScopes</c> ile, kayıt erişimi bağlantı belirteci ve sistem kimliğinin izinleriyle denetlenir.
    /// </summary>
    private static readonly Dictionary<string, string> WithoutPermission = new()
    {
        ["Modulix.Modules.Support/Endpoints/SupportChannelEndpoints.cs: /view"] =
            "Herkese açık memnuniyet anketi: bağlantı belirteciyle, hız sınırı ve gövde sınırıyla; sistem kimliği support.survey yalnızca talebi okur.",
        ["Modulix.Modules.Support/Endpoints/SupportChannelEndpoints.cs: /answer"] =
            "Herkese açık memnuniyet anketi yanıtı: bağlantı belirteciyle, hız sınırıyla; salt okunur lisansta yazılamaz (IPublicTenantScopes write).",
    };

    [Fact]
    public void Module_endpoints_require_a_permission()
    {
        var unprotected = MappedEndpoints()
            .Where(endpoint => !endpoint.HasPermission && !WithoutPermission.ContainsKey(endpoint.Id))
            .Select(endpoint => endpoint.Id)
            .Order()
            .ToList();

        Assert.True(
            unprotected.Count == 0,
            $"İzin kontrolü olmayan modül uç noktası:{Environment.NewLine}{string.Join(Environment.NewLine, unprotected)}"
            + $"{Environment.NewLine}Uç noktayı .RequirePermission(...) ile bitirin (ADR-0046); "
            + "izinsiz olması gerekiyorsa WithoutPermission listesine gerekçesiyle ekleyin.");
    }

    [Fact]
    public void Permission_exceptions_are_still_needed()
    {
        var endpoints = MappedEndpoints();
        var stale = WithoutPermission.Keys
            .Where(id => !endpoints.Any(endpoint => endpoint.Id == id && !endpoint.HasPermission))
            .Order()
            .ToList();

        Assert.True(stale.Count == 0, $"WithoutPermission listesinde artık gerekmeyen satır: {string.Join(", ", stale)}.");
    }

    [Fact]
    public void Scanner_finds_the_module_endpoints()
    {
        // Tarayıcı sessizce hiçbir şey bulmazsa kural boşa geçer; taşınan ilk uç nokta (Antrepo panosu) görülmeli.
        Assert.Contains(MappedEndpoints(), endpoint => endpoint.Id.EndsWith(": /dashboard", StringComparison.Ordinal)
            && endpoint.Id.StartsWith("Modulix.Modules.Warehouse/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("group.MapGet(\"/a\", () => 1).RequirePermission(\"x\");", true)]
    [InlineData("group.MapPost(\"/a\", async (int id) => { await Task.Yield(); return id; })\n    .RequireAnyPermission(\"x\", \"y\");", true)]
    [InlineData("group.MapGet(\"/a\", () => 1);", false)]
    [InlineData("var group = routes.MapGroup(\"\").PermissionCheckedInService();\ngroup.MapGet(\"/a\", () => 1);", true)]
    [InlineData("var group = routes.MapGroup(\"\");\ngroup.MapGet(\"/a\", () => 1).PermissionCheckedInService();", true)]
    [InlineData("var other = routes.MapGroup(\"\").RequirePermission(\"x\");\ngroup.MapGet(\"/a\", () => 1);", false)]
    [InlineData("group.MapGet(\"/a\", () => 1).WithName(\"a\"); other.RequirePermission(\"x\");", false)]
    [InlineData("group.MapDelete(\"/a;b\", () => \";\").RequirePermission(\"x\");", true)]
    [InlineData("group.MapGet(\"/a\", () =>\n{\n    // host'ta; kalır\n    return 1;\n});\nother.RequirePermission(\"x\");", false)]
    public void Scanner_reads_the_statement_of_a_map_call(string source, bool expected)
    {
        var endpoint = Assert.Single(Scan("Test/File.cs", source));

        Assert.Equal(expected, endpoint.HasPermission);
    }

    private static List<(string Id, bool HasPermission)> MappedEndpoints()
    {
        var result = new List<(string Id, bool HasPermission)>();
        var modules = RepositoryLayout.SourceProjects()
            .Where(project => RepositoryLayout.ProjectName(project).Split('.') is ["Modulix", "Modules", _]);
        foreach (var project in modules)
        {
            var directory = project.Directory!;
            foreach (var file in directory.EnumerateFiles("*.cs", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
            {
                string relative = Path.GetRelativePath(directory.FullName, file.FullName).Replace('\\', '/');
                result.AddRange(Scan($"{RepositoryLayout.ProjectName(project)}/{relative}", File.ReadAllText(file.FullName)));
            }
        }

        return result;
    }

    private static List<(string Id, bool HasPermission)> Scan(string file, string source)
    {
        var result = new List<(string Id, bool HasPermission)>();
        foreach (Match match in MapCall().Matches(source))
        {
            string statement = StatementFrom(source, match.Index);
            bool onEndpoint = PermissionCall().IsMatch(statement);
            result.Add(($"{file}: {match.Groups["route"].Value}", onEndpoint || GroupHasPermission(source, match.Groups["group"].Value)));
        }

        return result;
    }

    /// <summary>
    /// İzin grup düzeyinde verilmiş mi: uç noktanın eşlendiği değişken (<c>group.MapGet</c> → <c>group</c>) aynı dosyada
    /// <c>var group = ...;</c> ile tanımlanmış ve o ifade izin çağrısı içeriyorsa gruptaki bütün uç noktalar onu taşır.
    /// </summary>
    private static bool GroupHasPermission(string source, string group)
    {
        var declaration = Regex.Match(source, $@"\bvar\s+{Regex.Escape(group)}\s*=");
        return declaration.Success && PermissionCall().IsMatch(StatementFrom(source, declaration.Index));
    }

    /// <summary>
    /// <paramref name="start"/> konumundan, parantez ve süslü parantez derinliği sıfırken görülen ilk <c>;</c> işaretine kadar
    /// olan metin. Dize ve karakter sabitlerinin içi atlanır (lambda gövdesindeki ve dizedeki <c>;</c> ifadeyi bitirmez).
    /// </summary>
    private static string StatementFrom(string source, int start)
    {
        int depth = 0;
        for (int i = start; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                // Satır yorumu: içindeki kesme işareti ("host'ta") karakter sabiti sanılmasın.
                int end = source.IndexOf('\n', i);
                i = end < 0 ? source.Length : end;
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 1;
            }
            else if (c is '"' or '\'')
            {
                i = SkipLiteral(source, i);
            }
            else if (c is '(' or '{' or '[')
            {
                depth++;
            }
            else if (c is ')' or '}' or ']')
            {
                depth--;
            }
            else if (c == ';' && depth <= 0)
            {
                return source[start..i];
            }
        }

        return source[start..];
    }

    private static int SkipLiteral(string source, int open)
    {
        char quote = source[open];
        for (int i = open + 1; i < source.Length; i++)
        {
            if (source[i] == '\\')
            {
                i++;
            }
            else if (source[i] == quote)
            {
                return i;
            }
        }

        return source.Length;
    }

    private static bool IsBuildOutput(FileInfo file) =>
        file.FullName.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    [GeneratedRegex(@"(?<group>[A-Za-z_]\w*)\s*\.Map(Get|Post|Put|Delete|Patch)\s*\(\s*""(?<route>[^""]*)""")]
    private static partial Regex MapCall();

    [GeneratedRegex(@"\.(Require(Any)?Permission|PermissionCheckedInService)\s*\(")]
    private static partial Regex PermissionCall();
}
