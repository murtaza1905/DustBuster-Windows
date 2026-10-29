using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustBuster.Core;

/// <summary>Çekirdeğin tüm JSON okuma/yazma ayarları.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string? ReadEmbedded(string logicalName)
    {
        var assembly = typeof(Json).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Uygulama başına elle tanımlanmış kurallar: sezgisel taramanın bulamadığı kesin konumlar
/// (<c>include</c>) ve hiçbir kaynaktan listeye girmemesi gereken yollar (<c>exclude</c>).
/// Kaynak: gömülü <c>conditions.json</c>. Bir uygulamaya kural eklemek için dosyaya kayıt eklemek yeterlidir.
/// </summary>
public sealed class AppCondition
{
    public sealed class IncludeRule
    {
        public string Path { get; set; } = string.Empty;
        public MatchStrength Strength { get; set; } = MatchStrength.strong;
        public string? Note { get; set; }
    }

    public string Id { get; set; } = string.Empty;
    /// <summary>Bu kimliklerden biri (PackageFamilyName, ProductCode, Uninstall anahtar adı, ExtraIdentifiers) birebir eşleşirse kural uygulanır</summary>
    public List<string> Identifiers { get; set; } = new();
    /// <summary>Görünen ad (DisplayName / ProductName) temizlenmiş haliyle birebir eşleşirse kural uygulanır</summary>
    public List<string> Names { get; set; } = new();
    /// <summary>Taramada bu veritabanı kayıtlarından biri eşleşirse kural uygulanır</summary>
    public List<string> DatabaseIds { get; set; } = new();
    public List<IncludeRule> Include { get; set; } = new();
    /// <summary>Joker destekli "%LOCALAPPDATA%\…" veya "HKCU:\…" kalıpları; eşleşen bulgular kaynağı ne olursa olsun elenir</summary>
    public List<string> Exclude { get; set; } = new();

    public bool Matches(AppIdentity identity, IReadOnlyCollection<string> matchedDatabaseIds)
    {
        var ids = new List<string>();
        if (identity.PackageFamilyName is { Length: > 0 } pfn) ids.Add(pfn);
        if (identity.ProductCode is { Length: > 0 } code) ids.Add(code);
        if (identity.UninstallKeyPath is { Length: > 0 } key) ids.Add(PathUtil.LastComponent(key));
        ids.AddRange(identity.ExtraIdentifiers);
        foreach (var id in Identifiers)
        {
            if (ids.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase))) return true;
        }
        var names = new List<string> { SearchTerms.Squash(string.Concat(SearchTerms.CleanNameTokens(identity.Name))) };
        if (identity.ProductName is { Length: > 0 } product) names.Add(string.Concat(SearchTerms.CleanNameTokens(product)));
        foreach (var name in Names)
        {
            var squashed = string.Concat(SearchTerms.CleanNameTokens(name));
            if (squashed.Length > 0 && names.Contains(squashed)) return true;
        }
        return DatabaseIds.Any(d => matchedDatabaseIds.Contains(d, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class AppConditions
{
    private sealed class FileModel
    {
        public int Schema { get; set; } = 1;
        public List<AppCondition> Conditions { get; set; } = new();
    }

    public IReadOnlyList<AppCondition> Conditions { get; }

    public AppConditions(IEnumerable<AppCondition> conditions)
    {
        Conditions = conditions.ToList();
    }

    public static AppConditions Load(string json)
    {
        var file = JsonSerializer.Deserialize<FileModel>(json, Json.Options) ?? new FileModel();
        return new AppConditions(file.Conditions);
    }

    public static AppConditions? Bundled()
    {
        var json = Json.ReadEmbedded("conditions.json");
        return json == null ? null : Load(json);
    }

    public int Count => Conditions.Count;

    public List<AppCondition> Matching(AppIdentity identity, IReadOnlyCollection<string> databaseIds) =>
        Conditions.Where(c => c.Matches(identity, databaseIds)).ToList();

    public sealed record Hit(string Path, string ConditionId, MatchStrength Strength, string? Note);

    /// <summary>Eşleşen kuralların include kalıplarını diskte var olan yollara açar.</summary>
    public List<Hit> Expand(IEnumerable<AppCondition> matched, KnownFolders folders, IScanFileSystem fs)
    {
        var hits = new List<Hit>();
        var seen = new HashSet<string>(PathUtil.Comparer);
        foreach (var condition in matched)
        {
            foreach (var rule in condition.Include)
            {
                foreach (var path in Glob.Expand(rule.Path, folders, fs))
                {
                    if (seen.Add(path)) hits.Add(new Hit(path, condition.Id, rule.Strength, rule.Note));
                }
            }
        }
        return hits;
    }

    /// <summary>Yol, eşleşen kurallardan birinin exclude kalıbına uyuyorsa o kuralın kimliğini döner.</summary>
    public string? Exclusion(string path, KnownFolders folders, IEnumerable<AppCondition> matched)
    {
        var contracted = folders.Contract(path);
        foreach (var condition in matched)
        {
            foreach (var pattern in condition.Exclude)
            {
                var expanded = folders.Expand(pattern);
                if (Glob.Matches(pattern, contracted) || Glob.Matches(expanded, path)) return condition.Id;
            }
        }
        return null;
    }
}
