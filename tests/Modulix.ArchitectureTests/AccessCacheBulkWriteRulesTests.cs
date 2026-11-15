using System.Text.RegularExpressions;

namespace Modulix.ArchitectureTests;

/// <summary>
/// Yetki önbelleği (ADR-0048) yalnızca EF ile kaydedilen değişiklikte boşalır (<c>AccessCacheInvalidator</c>). Önbelleğin
/// okuduğu tablolara <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> ya da ham SQL ile yazan kod bu yolu atlar ve yetki değişikliği
/// 30 saniye geç görünür. Bu test yeni bir toplu yazmayı yakalar: yazan kod <c>AccessCache.Invalidate</c> çağırıp gerekçesiyle
/// <see cref="Allowed"/> listesine eklenir ya da kayıt EF üzerinden yapılır. İzlenen tablolar <c>WatchedTypes</c>'tan okunur;
/// listeye yeni tür eklenince bu test kendiliğinden kapsar.
/// </summary>
public class AccessCacheBulkWriteRulesTests
{
    private const string InvalidatorPath = "src/Modulix.Infrastructure/Authorization/AccessCacheInvalidator.cs";
    private const string DbContextPath = "src/Modulix.Infrastructure/Persistence/ModulixDbContext.cs";

    /// <summary>
    /// İzin verilen toplu yazmalar: "dosya|DbSet" ve gerekçe. Liste yalnızca küçülür; kaldırılan yazmanın satırı da silinir
    /// (<see cref="Allowed_bulk_writes_still_exist"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Allowed = new Dictionary<string, string>
    {
        ["src/Modulix.Infrastructure/Tenancy/MembershipDirectory.cs|Memberships"] =
            "Yalnızca son giriş zamanı (LastSignInAt) yazılır; önbellekteki üyelik özeti bu alanı taşımaz.",
    };

    private static readonly Regex WatchedTypeExpression = new(@"typeof\((\w+)\)", RegexOptions.Compiled);
    private static readonly Regex DbSetExpression = new(@"DbSet<(\w+)>\s+(\w+)\s*=>", RegexOptions.Compiled);
    private static readonly Regex BulkWriteExpression = new(@"\.Execute(Update|Delete)(Async)?\b", RegexOptions.Compiled);
    private static readonly Regex RawSqlExpression = new(@"\.(ExecuteSql\w*|SqlQuery\w*|FromSql\w*)\b", RegexOptions.Compiled);
    private static readonly Regex SqlWriteVerb = new(@"\b(UPDATE|DELETE|INSERT|MERGE)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void Watched_types_map_to_db_sets()
    {
        // Testin kendisi bozulmasın: izlenen her tür bağlamda bir DbSet'e karşılık gelir; ayrıştırma boş liste döndürmez.
        var types = WatchedTypes();
        var sets = DbSetsByType();

        Assert.True(types.Count >= 10, $"WatchedTypes ayrıştırılamadı ({types.Count} tür).");
        Assert.Empty(types.Where(t => !sets.ContainsKey(t)));
    }

    [Fact]
    public void Cached_authorization_tables_are_not_written_in_bulk()
    {
        var sets = DbSetsByType();
        var watched = WatchedTypes().Select(t => sets[t]).ToHashSet(StringComparer.Ordinal);
        var problems = BulkWrites(watched)
            .Where(write => !Allowed.ContainsKey(write.File + "|" + write.Set))
            .Select(write => $"{write.File}:{write.Line} '{write.Set}' toplu yazılıyor ({write.Kind}). Yetki önbelleği (ADR-0048) boşalmaz: " +
                             "kaydı EF ile yapın ya da AccessCache.Invalidate çağırıp AccessCacheBulkWriteRulesTests.Allowed listesine gerekçesiyle ekleyin.")
            .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Allowed_bulk_writes_still_exist()
    {
        var sets = DbSetsByType();
        var watched = WatchedTypes().Select(t => sets[t]).ToHashSet(StringComparer.Ordinal);
        var found = BulkWrites(watched).Select(write => write.File + "|" + write.Set).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(Allowed.Keys.Where(key => !found.Contains(key)));
    }

    private static IReadOnlyList<string> WatchedTypes()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryLayout.Root.FullName, InvalidatorPath));
        var start = text.IndexOf("WatchedTypes", StringComparison.Ordinal);
        var end = text.IndexOf("};", start, StringComparison.Ordinal);
        return WatchedTypeExpression.Matches(text[start..end]).Select(m => m.Groups[1].Value).ToList();
    }

    private static IReadOnlyDictionary<string, string> DbSetsByType()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryLayout.Root.FullName, DbContextPath));
        return DbSetExpression.Matches(text)
            .GroupBy(m => m.Groups[1].Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Groups[2].Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Kaynak metindeki toplu yazmalar. <c>ExecuteUpdate/Delete</c> için ifadenin başından çağrıya kadarki metinde <c>.DbSet</c>,
    /// ham SQL için çağrıdan ifade sonuna kadarki metinde yazma fiili ve tablo adı aranır (tablo adı DbSet adıyla aynıdır).
    /// </summary>
    private static IEnumerable<(string File, int Line, string Set, string Kind)> BulkWrites(IReadOnlySet<string> watched)
    {
        var root = RepositoryLayout.Root.FullName;
        foreach (var file in new DirectoryInfo(Path.Combine(root, "src")).EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            var parts = file.FullName.Split(Path.DirectorySeparatorChar);
            if (parts.Any(part => part is "bin" or "obj" or "Migrations"))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
            var lines = File.ReadAllLines(file.FullName);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (BulkWriteExpression.IsMatch(lines[i]))
                {
                    var statement = StatementBefore(lines, i);
                    foreach (var set in watched.Where(set => Regex.IsMatch(statement, $@"\.{set}\b")))
                    {
                        yield return (relative, i + 1, set, "ExecuteUpdate/ExecuteDelete");
                    }
                }

                if (RawSqlExpression.IsMatch(lines[i]))
                {
                    var statement = StatementAfter(lines, i);
                    if (SqlWriteVerb.IsMatch(statement))
                    {
                        foreach (var set in watched.Where(set => Regex.IsMatch(statement, $@"[\[.]{set}\b")))
                        {
                            yield return (relative, i + 1, set, "ham SQL");
                        }
                    }
                }
            }
        }
    }

    private static string StatementBefore(string[] lines, int index)
    {
        var start = index;
        while (start > 0 && !EndsStatement(lines[start - 1]))
        {
            start--;
        }

        return string.Join('\n', lines[start..(index + 1)]);
    }

    private static string StatementAfter(string[] lines, int index)
    {
        var end = index;
        while (end < lines.Length - 1 && !lines[end].TrimEnd().EndsWith(");", StringComparison.Ordinal))
        {
            end++;
        }

        return string.Join('\n', lines[index..(end + 1)]);
    }

    private static bool EndsStatement(string line)
    {
        var trimmed = line.TrimEnd();
        return trimmed.EndsWith(';') || trimmed.EndsWith('{') || trimmed.EndsWith('}');
    }
}
