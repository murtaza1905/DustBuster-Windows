using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustBuster.Core;

/// <summary>
/// Sistem Temizliği kuralları: taranan kökler, uygulama önbelleği sezgisi, yasaklı kökler, hassas
/// kelimeler, satıcı klasörleri ve tarayıcı tanımları. Kaynak: gömülü <c>rules.json</c>.
/// </summary>
public sealed class CleanupRules
{
    [JsonConverter(typeof(JsonStringEnumConverter<RootMode>))]
    public enum RootMode
    {
        /// <summary>Kökün üst düzey girdileri ayrı ayrı listelenir</summary>
        children,
        /// <summary>Kökün kendisi tek aday olarak listelenir</summary>
        self,
        /// <summary>Kök içinde kalıba uyan dosyalar listelenir</summary>
        files
    }

    public sealed class Root
    {
        public string Path { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public CleanupCategory Category { get; set; } = CleanupCategory.cache;
        public CleanupSection Section { get; set; } = CleanupSection.user;
        public RootMode Mode { get; set; } = RootMode.children;
        public string? Pattern { get; set; }
    }

    public sealed class AppCacheRules
    {
        public List<string> Roots { get; set; } = new();
        public int MaxDepth { get; set; } = 2;
        public List<string> CacheFolders { get; set; } = new();
        public List<string> LogFolders { get; set; } = new();
        public List<string> SkipFolders { get; set; } = new();
    }

    public sealed class SplitRoot
    {
        public string Profiles { get; set; } = string.Empty;
        public string Caches { get; set; } = string.Empty;
    }

    public sealed class BrowserRules
    {
        public List<string> ChromiumProfileCacheFolders { get; set; } = new();
        public List<string> ChromiumRootCacheFolders { get; set; } = new();
        public List<string> ChromiumProfileNames { get; set; } = new();
        public List<string> ChromiumProfilePrefixes { get; set; } = new();
        public List<string> ChromiumUserDataRoots { get; set; } = new();
        public string ChromiumUserDataFolder { get; set; } = "User Data";
        public List<SplitRoot> ChromiumSplitRoots { get; set; } = new();
        public Dictionary<string, string> ChromiumNames { get; set; } = new();
        public List<string> GeckoProfileRoots { get; set; } = new();
        public string GeckoCacheFolder { get; set; } = "cache2";
        public Dictionary<string, string> GeckoNames { get; set; } = new();
    }

    public int Schema { get; set; } = 1;
    public List<Root> Roots { get; set; } = new();
    public AppCacheRules AppCaches { get; set; } = new();
    public List<string> ForbiddenRoots { get; set; } = new();
    public List<string> SensitiveKeywords { get; set; } = new();
    public List<string> VendorFolders { get; set; } = new();
    public BrowserRules Browsers { get; set; } = new();

    public static CleanupRules Load(string json) =>
        JsonSerializer.Deserialize<CleanupRules>(json, Json.Options) ?? throw new InvalidDataException("rules.json okunamadı");

    public static CleanupRules? Bundled()
    {
        var json = Json.ReadEmbedded("rules.json");
        return json == null ? null : Load(json);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<CleanupCategory>))]
public enum CleanupCategory
{
    temp, cache, log, browserCache, appCache, systemCache
}

[JsonConverter(typeof(JsonStringEnumConverter<CleanupSection>))]
public enum CleanupSection
{
    /// <summary>Kullanıcı profili: yönetici gerekmez, varsayılan seçili</summary>
    user,
    /// <summary>Windows sistem alanı: ayrı bölümde, seçimsiz, yönetici gerekir</summary>
    system
}

/// <summary>Temizlik adayı: yol, arayüz etiketi, türü ve bölümü. Boyut ve silinebilirlik uygulama katmanında ölçülür.</summary>
public sealed record CleanupCandidate(string Path, string? Label, CleanupCategory Category, CleanupSection Section)
{
    public bool IsSystem => Section == CleanupSection.system;
}

/// <summary>
/// Kuralları dosya sistemine uygular: tarayıcı önbelleklerini tespit eder, kök klasörlerin girdilerini
/// listeler, uygulama önbellek klasörlerini bulur ve bir yolun silinip silinemeyeceğine karar verir.
/// </summary>
public sealed class CleanupPolicy
{
    public CleanupRules Rules { get; }
    public KnownFolders Folders { get; }
    public IScanFileSystem Fs { get; }

    public sealed record ChromiumRoot(string ProfileRoot, string CacheRoot, string Relative, List<string> Profiles);

    public List<ChromiumRoot> ChromiumRoots { get; } = new();
    public List<string> ChromiumCachePaths { get; } = new();

    public CleanupPolicy(CleanupRules rules, KnownFolders folders, IScanFileSystem fs)
    {
        Rules = rules;
        Folders = folders;
        Fs = fs;
    }

    // MARK: Yol yardımcıları

    public List<string> RootPaths => Rules.Roots.Select(r => Folders.Expand(r.Path)).ToList();
    public List<string> GeckoRoots => Rules.Browsers.GeckoProfileRoots.Select(Folders.Expand).ToList();
    public List<string> ForbiddenRoots => Rules.ForbiddenRoots.Select(Folders.Expand).ToList();
    public List<string> AppCacheRoots => Rules.AppCaches.Roots.Select(Folders.Expand).ToList();

    private List<string> VisibleChildren(string dir)
    {
        var names = Fs.Children(dir);
        if (names == null) return new List<string>();
        return names.OrderBy(n => n, PathUtil.Comparer).ToList();
    }

    private bool IsVendorFolder(string name) => Rules.VendorFolders.Any(v => string.Equals(v, name, PathUtil.Comparison));

    // MARK: Tarayıcılar

    private bool IsProfileName(string name) =>
        Rules.Browsers.ChromiumProfileNames.Contains(name, PathUtil.Comparer)
        || Rules.Browsers.ChromiumProfilePrefixes.Any(p => name.StartsWith(p, PathUtil.Comparison));

    /// <summary>
    /// LocalAppData altında (iki seviye) "User Data\Local State" taşıyan Chromium tabanlı tarayıcıları ve
    /// Opera gibi profil/önbellek kökleri ayrı olan tarayıcıları bulur; önbellek klasörlerini toplar.
    /// </summary>
    public List<string> DetectChromiumCaches()
    {
        ChromiumRoots.Clear();
        ChromiumCachePaths.Clear();
        var b = Rules.Browsers;

        foreach (var rootPattern in b.ChromiumUserDataRoots)
        {
            var root = Folders.Expand(rootPattern);
            foreach (var name in VisibleChildren(root))
            {
                var path = PathUtil.Join(root, name);
                if (!Fs.IsRegularDirectory(path)) continue;
                TryChromium(root, name, path);
                foreach (var sub in VisibleChildren(path))
                {
                    var subPath = PathUtil.Join(path, sub);
                    if (Fs.IsRegularDirectory(subPath)) TryChromium(root, name + "\\" + sub, subPath);
                }
            }
        }

        foreach (var split in b.ChromiumSplitRoots)
        {
            var profilesRoot = Folders.Expand(split.Profiles);
            var cachesRoot = Folders.Expand(split.Caches);
            foreach (var name in VisibleChildren(profilesRoot))
            {
                var profilePath = PathUtil.Join(profilesRoot, name);
                if (!Fs.IsRegularDirectory(profilePath) || !Fs.Exists(PathUtil.Join(profilePath, "Local State"))) continue;
                var cachePath = PathUtil.Join(cachesRoot, name);
                var relative = PathUtil.LastComponent(profilesRoot) + "\\" + name;
                var profiles = VisibleChildren(profilePath).Where(IsProfileName).ToList();
                ChromiumRoots.Add(new ChromiumRoot(profilePath, cachePath, relative, profiles));
                foreach (var baseRoot in new[] { profilePath, cachePath })
                {
                    if (profiles.Count > 0)
                    {
                        foreach (var profile in profiles)
                        {
                            foreach (var folder in b.ChromiumProfileCacheFolders) AddIfDirectory(PathUtil.Join(PathUtil.Join(baseRoot, profile), folder));
                        }
                    }
                    foreach (var folder in b.ChromiumProfileCacheFolders.Concat(b.ChromiumRootCacheFolders)) AddIfDirectory(PathUtil.Join(baseRoot, folder));
                }
            }
        }
        return new List<string>(ChromiumCachePaths);
    }

    private void TryChromium(string root, string relative, string path)
    {
        var b = Rules.Browsers;
        var userData = PathUtil.Join(path, b.ChromiumUserDataFolder);
        if (!Fs.IsRegularDirectory(userData) || !Fs.Exists(PathUtil.Join(userData, "Local State"))) return;
        if (ChromiumRoots.Any(r => PathUtil.PathEquals(r.ProfileRoot, userData))) return;
        var profiles = VisibleChildren(userData).Where(IsProfileName).ToList();
        if (profiles.Count == 0) return;
        ChromiumRoots.Add(new ChromiumRoot(userData, userData, relative, profiles));
        foreach (var profile in profiles)
        {
            foreach (var folder in b.ChromiumProfileCacheFolders) AddIfDirectory(PathUtil.Join(PathUtil.Join(userData, profile), folder));
        }
        foreach (var folder in b.ChromiumRootCacheFolders) AddIfDirectory(PathUtil.Join(userData, folder));
    }

    private void AddIfDirectory(string path)
    {
        if (!Fs.IsRegularDirectory(path)) return;
        if (ChromiumCachePaths.Any(p => PathUtil.PathEquals(p, path))) return;
        ChromiumCachePaths.Add(Fs.Canonical(path));
    }

    public string ChromiumBrowserName(string relative)
    {
        if (Rules.Browsers.ChromiumNames.TryGetValue(relative, out var known)) return known;
        var match = Rules.Browsers.ChromiumNames.FirstOrDefault(kv => string.Equals(kv.Key, relative, PathUtil.Comparison));
        if (match.Key != null) return match.Value;
        var parts = relative.Split('\\');
        if (Rules.Browsers.ChromiumNames.TryGetValue(parts[0], out var vendorKnown)) return vendorKnown;
        return parts[^1];
    }

    private string? ChromiumLabel(string path)
    {
        foreach (var root in ChromiumRoots)
        {
            foreach (var basePath in new[] { root.ProfileRoot, root.CacheRoot })
            {
                var rest = PathUtil.Relative(path, basePath);
                if (rest == null) continue;
                var parts = rest.Split('\\');
                var folder = parts[^1];
                var profile = parts.Length > 1 ? parts[0] : string.Empty;
                var profileSuffix = Rules.Browsers.ChromiumProfilePrefixes.Any(p => profile.StartsWith(p, PathUtil.Comparison)) ? " (" + profile + ")" : string.Empty;
                var type = folder switch
                {
                    "GPUCache" => "GPU Cache",
                    "ShaderCache" or "GrShaderCache" => "Shader Cache",
                    "GraphiteDawnCache" or "DawnWebGPUCache" or "DawnGraphiteCache" or "DawnCache" => "GPU Shader Cache",
                    _ => folder
                };
                return ChromiumBrowserName(root.Relative) + profileSuffix + " " + type;
            }
        }
        return null;
    }

    /// <summary>Tespit edilen Chromium önbellekleri + Gecko profil önbellekleri.</summary>
    public List<CleanupCandidate> BrowserCandidates()
    {
        var outList = new List<CleanupCandidate>();
        foreach (var path in ChromiumCachePaths)
        {
            outList.Add(new CleanupCandidate(path, ChromiumLabel(path), CleanupCategory.browserCache, CleanupSection.user));
        }
        foreach (var root in GeckoRoots)
        {
            if (!Fs.IsDirectory(root)) continue;
            var family = PathUtil.Parent(root) ?? root;
            var familyName = PathUtil.LastComponent(family);
            var label = Rules.Browsers.GeckoNames.TryGetValue(familyName, out var known) ? known : familyName;
            foreach (var profile in VisibleChildren(root))
            {
                var cache = PathUtil.Join(PathUtil.Join(root, profile), Rules.Browsers.GeckoCacheFolder);
                if (!Fs.IsRegularDirectory(cache)) continue;
                outList.Add(new CleanupCandidate(Fs.Canonical(cache), label + " Cache", CleanupCategory.browserCache, CleanupSection.user));
            }
        }
        return outList;
    }

    /// <summary>Yol tarayıcı bölümünde ele alınıyorsa (tespit edilen Chromium kökü veya altı, Gecko kökü) true.</summary>
    public bool IsBrowserOwned(string path)
    {
        foreach (var root in ChromiumRoots)
        {
            if (PathUtil.IsUnder(path, root.ProfileRoot) || PathUtil.IsUnder(path, root.CacheRoot)) return true;
            var profileParent = PathUtil.Parent(root.ProfileRoot);
            if (profileParent != null && PathUtil.PathEquals(path, profileParent)) return true;
            var cacheParent = PathUtil.Parent(root.CacheRoot);
            if (cacheParent != null && PathUtil.PathEquals(path, cacheParent)) return true;
        }
        foreach (var root in GeckoRoots)
        {
            if (PathUtil.IsUnder(path, root) || PathUtil.IsUnder(root, path)) return true;
        }
        return false;
    }

    // MARK: Kök girdileri

    public List<CleanupCandidate> RootCandidates()
    {
        var outList = new List<CleanupCandidate>();
        foreach (var root in Rules.Roots)
        {
            var expandedRoots = Glob.HasWildcard(root.Path)
                ? Glob.Expand(root.Path, Folders, Fs)
                : new List<string> { Folders.Expand(root.Path) };
            foreach (var rootPath in expandedRoots)
            {
                if (!Fs.IsRegularDirectory(rootPath)) continue;
                switch (root.Mode)
                {
                    case CleanupRules.RootMode.self:
                        outList.Add(new CleanupCandidate(Fs.Canonical(rootPath), LabelFor(root, rootPath), root.Category, root.Section));
                        break;
                    case CleanupRules.RootMode.files:
                        foreach (var name in VisibleChildren(rootPath))
                        {
                            var filePath = PathUtil.Join(rootPath, name);
                            if (Fs.IsDirectory(filePath)) continue;
                            if (root.Pattern != null && !Glob.Matches(root.Pattern, name)) continue;
                            outList.Add(new CleanupCandidate(filePath, root.Label + " › " + name, root.Category, root.Section));
                        }
                        break;
                    default:
                        foreach (var name in VisibleChildren(rootPath))
                        {
                            var childPath = PathUtil.Join(rootPath, name);
                            if (IsBrowserOwned(childPath)) continue;
                            if (Fs.IsReparsePoint(childPath)) continue;
                            outList.Add(new CleanupCandidate(childPath, root.Label + " › " + name, root.Category, root.Section));
                        }
                        break;
                }
            }
        }
        return outList;
    }

    private string LabelFor(CleanupRules.Root root, string expandedPath)
    {
        if (!Glob.HasWildcard(root.Path)) return root.Label;
        // "%LOCALAPPDATA%\Packages\*\AC\INetCache" → paket adı etikete eklenir
        var rel = PathUtil.Relative(expandedPath, Folders.Packages);
        if (rel != null)
        {
            var package = rel.Split('\\')[0];
            var underscore = package.IndexOf('_');
            var shortName = underscore > 0 ? package[..underscore] : package;
            return root.Label + " › " + shortName;
        }
        return root.Label + " › " + PathUtil.LastComponent(expandedPath);
    }

    // MARK: Uygulama önbellekleri

    /// <summary>
    /// AppData kökleri altında (sınırlı derinlikte) "Cache", "Code Cache", "GPUCache", "logs" gibi adlı klasörler:
    /// Electron ve benzeri uygulamaların önbellekleri. Tarayıcı kökleri ve Windows'un kendi klasörleri atlanır.
    /// </summary>
    public List<CleanupCandidate> AppCacheCandidates()
    {
        var outList = new List<CleanupCandidate>();
        var seen = new HashSet<string>(PathUtil.Comparer);
        var rules = Rules.AppCaches;
        foreach (var root in AppCacheRoots)
        {
            if (!Fs.IsDirectory(root)) continue;
            var skip = rules.SkipFolders.Select(s => PathUtil.Join(root, s)).ToList();
            WalkAppCaches(root, root, 0, rules.MaxDepth, skip, outList, seen);
        }
        return outList;
    }

    private void WalkAppCaches(string root, string dir, int depth, int maxDepth, List<string> skip, List<CleanupCandidate> outList, HashSet<string> seen)
    {
        if (depth > maxDepth) return;
        foreach (var name in VisibleChildren(dir))
        {
            var path = PathUtil.Join(dir, name);
            if (!Fs.IsRegularDirectory(path)) continue;
            if (skip.Any(s => PathUtil.IsUnder(path, s))) continue;
            if (IsBrowserOwned(path)) continue;
            if (RootPaths.Any(r => !Glob.HasWildcard(r) && PathUtil.IsUnder(path, r))) continue;

            var rel = PathUtil.Relative(path, root) ?? name;
            var matchedCache = Rules.AppCaches.CacheFolders.FirstOrDefault(f => rel.EndsWith("\\" + f, PathUtil.Comparison) || string.Equals(rel, f, PathUtil.Comparison));
            var matchedLog = matchedCache == null
                ? Rules.AppCaches.LogFolders.FirstOrDefault(f => rel.EndsWith("\\" + f, PathUtil.Comparison) || string.Equals(rel, f, PathUtil.Comparison))
                : null;
            if (matchedCache != null || matchedLog != null)
            {
                if (depth == 0) continue;
                if (!seen.Add(path)) continue;
                var label = rel.Replace("\\", " › ");
                outList.Add(new CleanupCandidate(path, label, matchedCache != null ? CleanupCategory.appCache : CleanupCategory.log, CleanupSection.user));
                continue;
            }
            if (depth + 1 <= maxDepth) WalkAppCaches(root, path, depth + 1, maxDepth, skip, outList, seen);
        }
    }

    // MARK: Güvenlik kararı

    /// <summary>null ise kurallara göre silinebilir; değilse neden silinemeyeceği.</summary>
    public string? Verdict(string path)
    {
        var p = PathUtil.Normalize(path);
        if (PathUtil.IsRegistryPath(p)) return "kayıt defteri temizlik kapsamı dışında";
        var underRoot = RootPaths.Any(r => Glob.HasWildcard(r) ? Glob.Matches(r + "*", p) : PathUtil.IsUnder(p, r));
        var underChromium = ChromiumCachePaths.Any(c => PathUtil.IsUnder(p, c));
        var underGecko = GeckoRoots.Any(g => PathUtil.IsUnder(p, g));
        var underAppCache = AppCacheRoots.Any(a => PathUtil.IsUnder(p, a)) && IsAppCacheFolderPath(p);
        if (!(underRoot || underChromium || underGecko || underAppCache)) return "izin verilen kökler dışında";

        foreach (var forbidden in ForbiddenRoots)
        {
            if (PathUtil.IsUnder(p, forbidden)) return "yasaklı kök: " + forbidden;
        }
        if (PathUtil.PathEquals(p, Folders.Home) || PathUtil.IsRoot(p)) return "ev veya kök dizin";
        if (PathUtil.PathEquals(p, Folders.LocalAppData) || PathUtil.PathEquals(p, Folders.RoamingAppData) || PathUtil.PathEquals(p, Folders.LocalLowAppData))
        {
            return "AppData kökünün kendisi";
        }
        if (PathUtil.PathEquals(p, Folders.Windows) || PathUtil.PathEquals(p, Folders.ProgramData)) return "sistem kökünün kendisi";

        var lower = p.ToLowerInvariant();
        var lastLower = PathUtil.LastComponent(p).ToLowerInvariant();
        foreach (var keyword in Rules.SensitiveKeywords)
        {
            var k = keyword.ToLowerInvariant();
            if (lastLower.Contains(k) || (!underChromium && !underGecko && lower.Contains(k))) return "hassas kelime: " + keyword;
        }
        return null;
    }

    private bool IsAppCacheFolderPath(string p)
    {
        var all = Rules.AppCaches.CacheFolders.Concat(Rules.AppCaches.LogFolders);
        foreach (var folder in all)
        {
            var needle = "\\" + folder + "\\";
            if (p.EndsWith("\\" + folder, PathUtil.Comparison) || p.Contains(needle, PathUtil.Comparison)) return true;
        }
        return false;
    }

    // MARK: Rapor

    public List<string> ScopeLines()
    {
        var lines = new List<string>();
        foreach (var root in Rules.Roots)
        {
            var path = Folders.Expand(root.Path);
            var exists = Glob.HasWildcard(root.Path) ? Glob.Expand(root.Path, Folders, Fs).Count > 0 : Fs.IsDirectory(path);
            lines.Add("- [" + root.Section + "/" + root.Mode + "] " + path + (exists ? string.Empty : " (yok)"));
        }
        lines.Add("- Chromium tarayıcıları, tespit edilen (" + ChromiumRoots.Count + "):");
        foreach (var root in ChromiumRoots)
        {
            lines.Add("    " + root.Relative + " — profiller: " + (root.Profiles.Count == 0 ? "tek profil" : string.Join(", ", root.Profiles)));
        }
        lines.Add("- Chromium cache klasörleri (" + ChromiumCachePaths.Count + "):");
        foreach (var path in ChromiumCachePaths) lines.Add("    " + path);
        var gecko = GeckoRoots.Where(Fs.IsDirectory).ToList();
        lines.Add("- Gecko profil kökleri, mevcut (" + gecko.Count + "):");
        foreach (var path in gecko) lines.Add("    " + path);
        lines.Add("- Uygulama önbelleği kökleri: " + string.Join(", ", AppCacheRoots) + " (derinlik " + Rules.AppCaches.MaxDepth + ")");
        lines.Add("- Uygulama önbelleği klasör adları: " + string.Join(", ", Rules.AppCaches.CacheFolders));
        lines.Add("- Uygulama günlük klasör adları: " + string.Join(", ", Rules.AppCaches.LogFolders));
        lines.Add("- Hassas anahtar kelimeler: " + string.Join(", ", Rules.SensitiveKeywords));
        lines.Add("- Yasaklı kökler: " + string.Join(", ", ForbiddenRoots));
        return lines;
    }
}
