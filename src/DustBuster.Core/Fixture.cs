using System.Text.Json;

namespace DustBuster.Core;

/// <summary>
/// Test vakası: bir uygulama kimliği, sanal bir disk + kayıt defteri ağacı ve beklenen sonuç.
/// Uygulamadaki "Test vakası dışa aktar" düğmesi bu biçimde JSON üretir; testler aynı biçimi okur.
/// Yollar "~", "%LOCALAPPDATA%" gibi belirteçlerle yazılır; "\" ile bitenler dizindir ve dolu kabul edilir.
/// </summary>
public sealed class Fixture
{
    public sealed class Expectation
    {
        /// <summary>Güçlü (seçili) bulunması gereken yollar</summary>
        public List<string> Strong { get; set; } = new();
        /// <summary>Zayıf (seçimsiz) bulunması gereken yollar</summary>
        public List<string> Weak { get; set; } = new();
        /// <summary>Hiç listelenmemesi gereken yollar (negatif kontroller)</summary>
        public List<string> Absent { get; set; } = new();
        /// <summary>Eşleşmesi beklenen veritabanı kayıtları (boşsa kontrol edilmez)</summary>
        public List<string> DatabaseIds { get; set; } = new();
    }

    public sealed class FixtureOptions
    {
        public bool IncludeSystemLocations { get; set; }
        public bool IncludeAggressiveLocations { get; set; }
        public bool UseDatabase { get; set; } = true;

        public ScanOptions ToScanOptions() => new()
        {
            IncludeSystemLocations = IncludeSystemLocations,
            IncludeAggressiveLocations = IncludeAggressiveLocations,
            UseDatabase = UseDatabase
        };
    }

    public int Schema { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    /// <summary>"observed" (uygulamadan dışa aktarıldı, henüz doğrulanmadı) veya "curated" (beklenti elle onaylandı)</summary>
    public string Status { get; set; } = "curated";
    public string Notes { get; set; } = string.Empty;
    public string Home { get; set; } = "C:\\Users\\test";
    public AppIdentity Identity { get; set; } = new();
    public FixtureOptions Options { get; set; } = new();
    /// <summary>Sanal disk ve kayıt defteri yolları</summary>
    public List<string> Tree { get; set; } = new();
    /// <summary>Boş olması gereken dizinler</summary>
    public List<string> EmptyDirs { get; set; } = new();
    /// <summary>Sembolik bağ / junction olan yollar</summary>
    public List<string> ReparsePoints { get; set; } = new();
    /// <summary>.lnk yolu → hedef yolu</summary>
    public Dictionary<string, string>? ShortcutTargets { get; set; }
    /// <summary>Run değeri yolu → komut satırı</summary>
    public Dictionary<string, string>? RunCommands { get; set; }
    /// <summary>Görev dosyası yolu → yürütülebilir</summary>
    public Dictionary<string, string>? TaskExecutables { get; set; }
    /// <summary>Hizmet anahtarı yolu → yürütülebilir</summary>
    public Dictionary<string, string>? ServiceExecutables { get; set; }
    public Expectation Expect { get; set; } = new();

    public KnownFolders Folders => KnownFolders.ForHome(Home);

    public string Expand(string path) => Folders.Expand(path);
    public string Contract(string path) => Folders.Contract(path);

    /// <summary>Ağaçtan sanal disk kurar. Dolu dizinlere görünmez bir çocuk eklenir ki "boş klasör" sayılmasınlar.</summary>
    public VirtualFileSystem MakeFileSystem()
    {
        var paths = new List<string>();
        foreach (var raw in Tree)
        {
            var isDir = raw.EndsWith('\\') || raw.EndsWith('/');
            var p = Expand(raw);
            if (isDir)
            {
                paths.Add(p + "\\");
                if (!PathUtil.IsRegistryPath(p)) paths.Add(p + "\\_");
            }
            else
            {
                paths.Add(p);
            }
        }
        foreach (var raw in EmptyDirs) paths.Add(Expand(raw) + "\\");

        var identity = MakeIdentity();
        if (identity.InstallLocation is { Length: > 0 } install)
        {
            paths.Add(install + "\\");
            if (identity.ExecutablePath is { Length: > 0 } exe) paths.Add(exe);
            else paths.Add(PathUtil.Join(install, "_"));
        }
        if (identity.UninstallKeyPath is { Length: > 0 } key) paths.Add(key + "\\");

        Dictionary<string, string> ExpandMap(Dictionary<string, string>? map, bool expandValues)
        {
            var result = new Dictionary<string, string>(PathUtil.Comparer);
            if (map == null) return result;
            foreach (var (k, v) in map) result[Expand(k)] = expandValues ? Expand(v) : CommandLine.ExpandEnvironment(v, Folders);
            return result;
        }

        return new VirtualFileSystem(
            paths,
            ReparsePoints.Select(Expand),
            ExpandMap(ShortcutTargets, true),
            ExpandMap(RunCommands, false),
            ExpandMap(TaskExecutables, true),
            ExpandMap(ServiceExecutables, true));
    }

    public AppIdentity MakeIdentity()
    {
        var id = Identity.Clone();
        if (id.InstallLocation != null) id.InstallLocation = Expand(id.InstallLocation);
        if (id.ExecutablePath != null) id.ExecutablePath = Expand(id.ExecutablePath);
        if (id.UninstallKeyPath != null) id.UninstallKeyPath = PathUtil.Normalize(id.UninstallKeyPath);
        id.HelperExecutables = id.HelperExecutables.Select(Expand).ToList();
        return id;
    }

    // MARK: Çalıştırma ve karşılaştırma

    public sealed class Verdict
    {
        public List<string> MissingStrong { get; } = new();
        public List<string> MissingWeak { get; } = new();
        public List<string> WrongStrength { get; } = new();
        public List<string> UnexpectedPresent { get; } = new();
        public List<string> MissingDatabaseIds { get; } = new();
        public List<string> UnexpectedExtras { get; } = new();

        public bool Passed => MissingStrong.Count == 0 && MissingWeak.Count == 0 && WrongStrength.Count == 0
                              && UnexpectedPresent.Count == 0 && MissingDatabaseIds.Count == 0;

        public string Summary
        {
            get
            {
                var lines = new List<string>();
                if (MissingStrong.Count > 0) lines.Add("GÜÇLÜ beklenen ama bulunmayan: " + string.Join(", ", MissingStrong));
                if (MissingWeak.Count > 0) lines.Add("ZAYIF beklenen ama bulunmayan: " + string.Join(", ", MissingWeak));
                if (WrongStrength.Count > 0) lines.Add("Yanlış güç: " + string.Join(", ", WrongStrength));
                if (UnexpectedPresent.Count > 0) lines.Add("Listelenmemesi gerekirken listelenen: " + string.Join(", ", UnexpectedPresent));
                if (MissingDatabaseIds.Count > 0) lines.Add("Eşleşmeyen veritabanı kaydı: " + string.Join(", ", MissingDatabaseIds));
                if (UnexpectedExtras.Count > 0) lines.Add("Beklentide olmayan ek bulgular (bilgi): " + string.Join(", ", UnexpectedExtras));
                return string.Join("\n", lines);
            }
        }
    }

    public (ScanResult Result, Verdict Verdict) Run(ILeftoverDatabase? database, AppConditions? conditions)
    {
        var fs = MakeFileSystem();
        var scanner = new LeftoverScanner(Folders, fs, database, conditions, Options.ToScanOptions());
        var result = scanner.Scan(MakeIdentity());
        return (result, VerdictFor(result));
    }

    public Verdict VerdictFor(ScanResult result)
    {
        var v = new Verdict();
        var strengthByPath = new Dictionary<string, MatchStrength>(PathUtil.Comparer);
        foreach (var f in result.Findings) strengthByPath[PathUtil.Normalize(f.Path)] = f.Strength;

        foreach (var raw in Expect.Strong)
        {
            var p = Expand(raw);
            if (!strengthByPath.TryGetValue(p, out var s)) v.MissingStrong.Add(raw);
            else if (s == MatchStrength.weak) v.WrongStrength.Add(raw + " (zayıf, güçlü bekleniyordu)");
        }
        foreach (var raw in Expect.Weak)
        {
            var p = Expand(raw);
            if (!strengthByPath.TryGetValue(p, out var s)) v.MissingWeak.Add(raw);
            else if (s == MatchStrength.strong) v.WrongStrength.Add(raw + " (güçlü, zayıf bekleniyordu)");
        }
        foreach (var raw in Expect.Absent)
        {
            if (strengthByPath.ContainsKey(Expand(raw))) v.UnexpectedPresent.Add(raw);
        }
        foreach (var id in Expect.DatabaseIds)
        {
            if (!result.DatabaseIds.Contains(id, StringComparer.OrdinalIgnoreCase)) v.MissingDatabaseIds.Add(id);
        }
        var expected = new HashSet<string>(Expect.Strong.Concat(Expect.Weak).Select(Expand), PathUtil.Comparer);
        var install = Identity.InstallLocation is { Length: > 0 } i ? Expand(i) : null;
        foreach (var f in result.Findings)
        {
            if (expected.Contains(f.Path)) continue;
            if (install != null && PathUtil.PathEquals(f.Path, install)) continue;
            v.UnexpectedExtras.Add(Contract(f.Path) + " [" + f.Strength + ", " + f.Source + "]");
        }
        return v;
    }

    // MARK: Uygulamadan dışa aktarma

    /// <summary>Gerçek bir taramanın sonucundan "observed" durumunda bir vaka üretir; ev dizini anonimleştirilir.</summary>
    public static Fixture Capture(string name, KnownFolders real, AppIdentity identity, ScanOptions options, ScanResult result,
                                  IReadOnlyList<string> rawMentions, IScanFileSystem fs)
    {
        var fixture = new Fixture
        {
            Name = name,
            Status = "observed",
            Notes = "Uygulamadan dışa aktarıldı; beklenti gözlemle aynı, elle doğrulanmalı.",
            Home = "C:\\Users\\test",
            Options = new FixtureOptions
            {
                IncludeSystemLocations = options.IncludeSystemLocations,
                IncludeAggressiveLocations = options.IncludeAggressiveLocations,
                UseDatabase = options.UseDatabase
            }
        };
        string Anonymize(string path) => real.Contract(path);

        var id = identity.Clone();
        if (id.InstallLocation != null) id.InstallLocation = Anonymize(id.InstallLocation);
        if (id.ExecutablePath != null) id.ExecutablePath = Anonymize(id.ExecutablePath);
        id.HelperExecutables = id.HelperExecutables.Select(Anonymize).ToList();
        fixture.Identity = id;

        var tree = new SortedSet<string>(PathUtil.Comparer);
        var empty = new SortedSet<string>(PathUtil.Comparer);
        var shortcuts = new Dictionary<string, string>(PathUtil.Comparer);
        var runs = new Dictionary<string, string>(PathUtil.Comparer);
        var tasks = new Dictionary<string, string>(PathUtil.Comparer);
        var services = new Dictionary<string, string>(PathUtil.Comparer);

        void Place(string path)
        {
            if (fs.IsDirectory(path))
            {
                if (!PathUtil.IsRegistryPath(path) && fs.IsEffectivelyEmptyDirectory(path)) empty.Add(Anonymize(path) + "\\");
                else tree.Add(Anonymize(path) + "\\");
            }
            else
            {
                tree.Add(Anonymize(path));
            }
        }

        foreach (var f in result.Findings)
        {
            if (f.Source == FindingSource.installFolder) continue;
            Place(f.Path);
            switch (f.Source)
            {
                case FindingSource.shortcut:
                    if (fs.IsDirectory(f.Path))
                    {
                        foreach (var child in fs.Children(f.Path) ?? Array.Empty<string>())
                        {
                            var childPath = PathUtil.Join(f.Path, child);
                            var childTarget = fs.ShortcutTarget(childPath);
                            if (childTarget != null) { shortcuts[Anonymize(childPath)] = Anonymize(childTarget); tree.Add(Anonymize(childPath)); }
                        }
                    }
                    else if (fs.ShortcutTarget(f.Path) is { } target) shortcuts[Anonymize(f.Path)] = Anonymize(target);
                    break;
                case FindingSource.startup:
                    if (fs.RunCommand(f.Path) is { } command) runs[Anonymize(f.Path)] = real.ContractEmbedded(command);
                    break;
                case FindingSource.scheduledTask:
                    if (fs.TaskExecutable(f.Path) is { } exe) tasks[Anonymize(f.Path)] = Anonymize(exe);
                    break;
                case FindingSource.service:
                    if (fs.ServiceExecutable(f.Path) is { } image) services[Anonymize(f.Path)] = Anonymize(image);
                    break;
            }
        }
        foreach (var p in rawMentions) Place(p);
        foreach (var r in result.Rejected) Place(r.Path);

        fixture.Tree = tree.ToList();
        fixture.EmptyDirs = empty.ToList();
        fixture.ShortcutTargets = shortcuts.Count == 0 ? null : shortcuts;
        fixture.RunCommands = runs.Count == 0 ? null : runs;
        fixture.TaskExecutables = tasks.Count == 0 ? null : tasks;
        fixture.ServiceExecutables = services.Count == 0 ? null : services;
        fixture.Expect = new Expectation
        {
            Strong = result.Findings.Where(f => f.Strength == MatchStrength.strong && f.Source != FindingSource.installFolder).Select(f => Anonymize(f.Path)).ToList(),
            Weak = result.Findings.Where(f => f.Strength == MatchStrength.weak).Select(f => Anonymize(f.Path)).ToList(),
            Absent = new List<string>(),
            DatabaseIds = new List<string>(result.DatabaseIds)
        };
        return fixture;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json.Options);

    public static Fixture Load(string json) =>
        JsonSerializer.Deserialize<Fixture>(json, Json.Options) ?? throw new InvalidDataException("test vakası okunamadı");
}
