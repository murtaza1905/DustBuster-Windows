namespace DustBuster.Core;

/// <summary>
/// Uygulama kalıntılarını bulan çekirdek. Dosya sistemine ve kayıt defterine yalnızca
/// <see cref="IScanFileSystem"/> üzerinden dokunur; hiçbir şey silmez, boyut hesaplamaz.
/// Akış: uygulamanın kendi kaldırıcısı çalıştırıldıktan sonra (veya öncesinde ön izleme için) çağrılır.
/// </summary>
public sealed class LeftoverScanner
{
    public KnownFolders Folders { get; }
    public IScanFileSystem Fs { get; }
    public ILeftoverDatabase? Database { get; }
    public AppConditions? Conditions { get; }
    public ScanOptions Options { get; }

    public LeftoverScanner(KnownFolders folders, IScanFileSystem fs, ILeftoverDatabase? database = null,
                           AppConditions? conditions = null, ScanOptions? options = null)
    {
        Folders = folders;
        Fs = fs;
        Database = database;
        Conditions = conditions;
        Options = options ?? new ScanOptions();
    }

    // MARK: Dış API

    /// <summary>Hızlı tarama: yalnızca birebir bilinen yollar + veritabanı + kurallar. Anında sonuç verir.</summary>
    public ScanResult ScanFast(AppIdentity identity)
    {
        var terms = SearchTerms.From(identity);
        var collected = new Collector(Folders, Options);
        AddInstallFolder(identity, collected);
        foreach (var c in FastCandidates(identity, terms)) collected.Add(c);
        var databaseIds = AddDatabaseHits(identity, terms, collected);
        var matched = AddConditionHits(identity, databaseIds, collected);
        ApplyExclusions(matched, collected);
        DowngradeVendorFolders(terms, collected);
        collected.FinalizeEmptyDirectories(IsEmptyDirectory);
        return collected.Result(0, databaseIds, matched.Select(m => m.Id).ToList());
    }

    /// <summary>Tam tarama: kesin kaynaklar + veritabanı + dizin ve kayıt defteri taraması (+ sistem konumları seçiliyse).</summary>
    public ScanResult Scan(AppIdentity identity)
    {
        var terms = SearchTerms.From(identity);
        var collected = new Collector(Folders, Options);
        AddInstallFolder(identity, collected);
        foreach (var c in FastCandidates(identity, terms)) collected.Add(c);
        foreach (var c in ShortcutCandidates(identity)) collected.Add(c);
        foreach (var c in StartupCandidates(identity)) collected.Add(c);
        if (Options.IncludeSystemLocations)
        {
            foreach (var c in ScheduledTaskCandidates(identity)) collected.Add(c);
            foreach (var c in ServiceCandidates(identity)) collected.Add(c);
        }
        var databaseIds = AddDatabaseHits(identity, terms, collected);
        var matchedConditions = AddConditionHits(identity, databaseIds, collected);

        var deep = new List<Finding>();
        deep.AddRange(SearchUserLocations(terms));
        deep.AddRange(SearchRegistry(terms));
        deep.AddRange(SearchStartMenu(terms));
        if (Options.IncludeSystemLocations) deep.AddRange(SearchSystemLocations(terms));

        var dropped = 0;
        if (deep.Count > Options.MaxReasonableResults)
        {
            dropped = deep.Count;
        }
        else
        {
            var matchedSet = new HashSet<string>(databaseIds, StringComparer.OrdinalIgnoreCase);
            foreach (var f in deep)
            {
                // Ad benzerliğiyle bulunan yol başka bir uygulamanın veritabanı kaydında geçiyorsa o uygulamaya aittir
                if (f.Strength == MatchStrength.weak && !collected.Contains(f.Path) && Options.UseDatabase && Database != null)
                {
                    var owners = Database.OtherOwners(f.Path, Folders, matchedSet, identity);
                    if (owners.Count > 0)
                    {
                        collected.Reject(f.Path, "başka uygulamaya ait (veritabanı: " + string.Join(", ", owners) + ")");
                        continue;
                    }
                }
                collected.Add(f);
            }
        }
        ApplyExclusions(matchedConditions, collected);
        DowngradeVendorFolders(terms, collected);
        collected.FinalizeEmptyDirectories(IsEmptyDirectory);
        return collected.Result(dropped, databaseIds, matchedConditions.Select(m => m.Id).ToList());
    }

    /// <summary>
    /// Filtresiz ham liste: standart konumlarda, ayraçlar atıldığında adında kimliği veya uygulama adını
    /// içeren TÜM yollar. Eşleştiricinin atladıklarını görmek için (rapor ve test vakası).
    /// </summary>
    public (List<string> Needles, List<string> Paths) RawMentions(AppIdentity identity, int limit = 300)
    {
        var terms = SearchTerms.From(identity);
        var needles = new List<string>();
        foreach (var id in terms.Identifiers)
        {
            var squashed = SearchTerms.Squash(id);
            if (squashed.Length >= 4 && !needles.Contains(squashed)) needles.Add(squashed);
        }
        foreach (var set in terms.NameTokenSets)
        {
            var squashed = string.Concat(set);
            if (squashed.Length >= 4 && !needles.Contains(squashed)) needles.Add(squashed);
            foreach (var token in set)
            {
                if (token.Length >= 5 && !SearchTerms.GenericNames.Contains(token) && !needles.Contains(token)) needles.Add(token);
            }
        }
        if (needles.Count == 0) return (needles, new List<string>());

        var locations = new List<(string Root, int Depth)>
        {
            (Folders.RoamingAppData, 3), (Folders.LocalAppData, 3), (Folders.LocalLowAppData, 2), (Folders.ProgramData, 2),
            (Folders.Packages, 1), (Folders.UserPrograms, 1), (Folders.Temp, 1), (Folders.StartMenuPrograms, 2), (Folders.CommonStartMenuPrograms, 2),
            (Folders.Desktop, 1), (Folders.PublicDesktop, 1), (PathClassifier.SoftwareUser, 2), (PathClassifier.SoftwareMachine, 2),
            (PathClassifier.SoftwareMachine32, 2), (PathClassifier.UninstallKeyUser, 1), (PathClassifier.UninstallKeyMachine, 1),
            (PathClassifier.UninstallKeyMachine32, 1), (PathClassifier.RunKeyUser, 1), (PathClassifier.RunKeyMachine, 1)
        };
        var found = new List<string>();
        var seen = new HashSet<string>(PathUtil.Comparer);
        foreach (var (root, depth) in locations)
        {
            CollectMentions(root, root, needles, depth, 0, found, seen, limit);
            if (found.Count >= limit) break;
        }
        return (needles, found);
    }

    // MARK: Toplayıcı

    private sealed class Collector
    {
        private readonly KnownFolders _folders;
        private readonly ScanOptions _options;
        private readonly Dictionary<string, Finding> _byPath = new(PathUtil.Comparer);
        private readonly List<string> _order = new();
        public List<RejectedPath> Rejected { get; } = new();

        public Collector(KnownFolders folders, ScanOptions options)
        {
            _folders = folders;
            _options = options;
        }

        public void Add(Finding finding)
        {
            var key = PathUtil.Normalize(finding.Path);
            if (_byPath.TryGetValue(key, out var existing))
            {
                // Aynı yol iki kaynaktan geldiyse güçlü olanı ve birleşik notu sakla
                if (finding.Strength > existing.Strength || (finding.Source == FindingSource.database && existing.Source != FindingSource.database))
                {
                    var note = existing.Note == finding.Note ? finding.Note : finding.Note + " + " + existing.Note;
                    var strength = (MatchStrength)Math.Max((int)existing.Strength, (int)finding.Strength);
                    _byPath[key] = new Finding(existing.Path, existing.Kind, finding.Source, strength, note);
                }
                return;
            }
            _byPath[key] = finding;
            _order.Add(key);
        }

        public void Reject(string path, string reason) => Rejected.Add(new RejectedPath(path, reason));

        public IReadOnlyList<string> Paths => _order;

        public bool Contains(string path) => _byPath.ContainsKey(PathUtil.Normalize(path));

        public Finding? Get(string path) => _byPath.TryGetValue(PathUtil.Normalize(path), out var f) ? f : null;

        public void Downgrade(string path, string note)
        {
            var key = PathUtil.Normalize(path);
            if (!_byPath.TryGetValue(key, out var f) || f.Strength != MatchStrength.strong) return;
            _byPath[key] = f with { Strength = MatchStrength.weak, Note = f.Note + " — " + note };
        }

        public void Remove(string path, string reason)
        {
            var key = PathUtil.Normalize(path);
            if (!_byPath.Remove(key)) return;
            _order.RemoveAll(p => string.Equals(p, key, PathUtil.Comparison));
            Rejected.Add(new RejectedPath(path, reason));
        }

        /// <summary>
        /// Boş klasörler: uygulamanın kendi kabukları (güçlü) "boş klasör" etiketiyle listelenir; yalnızca zayıf
        /// kanıtla bulunan boş klasörler kazanç getirmediği için elenir.
        /// </summary>
        public void FinalizeEmptyDirectories(Func<string, bool> isEmpty)
        {
            foreach (var key in _order.ToList())
            {
                if (!_byPath.TryGetValue(key, out var f) || !isEmpty(f.Path)) continue;
                if (f.Strength == MatchStrength.weak)
                {
                    Remove(f.Path, "boş klasör, yalnızca zayıf eşleşme");
                }
                else
                {
                    _byPath[key] = f with { Note = f.Note + " — boş klasör" };
                }
            }
        }

        /// <summary>Güçlü listelenen bir klasörün altındaki bulgular klasörle birlikte gideceği için ayrıca listelenmez.</summary>
        private void CollapseNested()
        {
            var strongDirs = _order.Select(k => _byPath[k]).Where(f => f.Strength == MatchStrength.strong).Select(f => f.Path).ToList();
            foreach (var key in _order.ToList())
            {
                var f = _byPath[key];
                var parent = strongDirs.FirstOrDefault(d => !PathUtil.PathEquals(d, f.Path) && PathUtil.IsUnder(f.Path, d));
                if (parent == null) continue;
                _byPath.Remove(key);
                _order.Remove(key);
                Rejected.Add(new RejectedPath(f.Path, "üst klasörü zaten listede: " + parent));
            }
        }

        public ScanResult Result(int droppedDeep, List<string> databaseIds, List<string> conditionIds)
        {
            CollapseNested();
            var findings = _order.Select(k => _byPath[k])
                .OrderByDescending(f => (int)f.Strength)
                .ThenBy(f => f.Kind.Rank())
                .ThenBy(f => f.Path, PathUtil.Comparer)
                .ToList();
            return new ScanResult(findings, droppedDeep, databaseIds, new List<RejectedPath>(Rejected), conditionIds);
        }
    }

    // MARK: Kaynaklar

    private Finding Make(string path, FindingSource source, MatchStrength strength, string note) =>
        new(Fs.Canonical(path), PathClassifier.Kind(path, Folders), source, strength, note);

    /// <summary>Diskte var ve yeniden ayrıştırma noktası değil.</summary>
    private bool IsUsable(string path) => Fs.Exists(path) && !Fs.IsReparsePoint(path);

    private bool IsEmptyDirectory(string path) => !PathUtil.IsRegistryPath(path) && Fs.IsDirectory(path) && Fs.IsEffectivelyEmptyDirectory(path);

    private bool IsInsideInstall(AppIdentity identity, string? executable)
    {
        if (string.IsNullOrEmpty(executable)) return false;
        if (identity.InstallLocation is { Length: > 0 } install && PathUtil.IsUnder(executable, install) && !PathUtil.PathEquals(executable, install)) return true;
        if (identity.ExecutablePath is { Length: > 0 } exe && PathUtil.PathEquals(executable, exe)) return true;
        return identity.HelperExecutables.Any(h => PathUtil.PathEquals(h, executable));
    }

    private void AddInstallFolder(AppIdentity identity, Collector collector)
    {
        if (identity.InstallLocation is not { Length: > 0 } install) return;
        var admission = PathClassifier.Admit(install, Folders, Options);
        if (!IsUsable(install)) return;
        if (!admission.IsAccepted)
        {
            // Program Files altındaki kurulum klasörü sistem taraması kapalıyken bile uygulamanın kendisidir;
            // WindowsApps (MSIX) klasörleri yalnızca paket yöneticisiyle kaldırılır
            var inWindowsApps = PathUtil.IsUnder(install, PathUtil.Join(Folders.ProgramFiles, "WindowsApps"))
                                || PathUtil.IsUnder(install, PathUtil.Join(Folders.ProgramFilesX86, "WindowsApps"));
            if (!inWindowsApps && (PathUtil.IsUnder(install, Folders.ProgramFiles) || PathUtil.IsUnder(install, Folders.ProgramFilesX86)))
            {
                collector.Add(new Finding(Fs.Canonical(install), LeftoverKind.installFolder, FindingSource.installFolder, MatchStrength.strong,
                    "Kurulum klasörü — yönetici yetkisi gerekir"));
            }
            else
            {
                collector.Reject(install, "kurulum klasörü: " + admission.Reason);
            }
            return;
        }
        collector.Add(new Finding(Fs.Canonical(install), LeftoverKind.installFolder, FindingSource.installFolder, MatchStrength.strong,
            "Kurulum klasörü" + (admission.Note != null ? " — " + admission.Note : string.Empty)));
    }

    /// <summary>Kimlik ve adla birebir bilinen yollar: AppData kökleri, ProgramData, Packages, Başlat Menüsü, HKCU/HKLM Software.</summary>
    private List<Finding> FastCandidates(AppIdentity identity, SearchTerms terms)
    {
        var outList = new List<Finding>();
        var seen = new HashSet<string>(PathUtil.Comparer);

        void AddIfUsable(string path, string note, MatchStrength strength = MatchStrength.strong)
        {
            if (!seen.Add(path)) return;
            if (!IsUsable(path)) return;
            var admission = PathClassifier.Admit(path, Folders, Options);
            if (!admission.IsAccepted) return;
            var label = note + (admission.Note != null ? " — " + admission.Note : string.Empty);
            outList.Add(Make(path, FindingSource.exactPath, (MatchStrength)Math.Min((int)strength, (int)admission.Strength), label));
        }

        if (identity.PackageFamilyName is { Length: > 0 } pfn)
        {
            AddIfUsable(PathUtil.Join(Folders.Packages, pfn), "Paket verisi (PackageFamilyName)");
        }
        if (identity.UninstallKeyPath is { Length: > 0 } key)
        {
            AddIfUsable(key, "Uninstall kaydı");
        }
        if (identity.ProductCode is { Length: > 0 } code)
        {
            AddIfUsable(PathUtil.Join(PathClassifier.UninstallKeyMachine, code), "Uninstall kaydı (MSI)");
            AddIfUsable(PathUtil.Join(PathClassifier.UninstallKeyMachine32, code), "Uninstall kaydı (MSI, 32 bit)");
            AddIfUsable(PathUtil.Join(PathClassifier.UninstallKeyUser, code), "Uninstall kaydı (MSI, kullanıcı)");
        }

        // Ad varyantları: görünen ad, ürün adı, exe adı, kurulum klasörü adı (yalnızca özgül olanlar)
        var names = new List<string>();
        void AddName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var trimmed = raw.Trim();
            var squashed = string.Concat(SearchTerms.CleanNameTokens(trimmed));
            // Birebir klasör/anahtar adı araması alt dize yapmadığı için 3 karakterlik adlar ("vlc") da güvenlidir
            if (squashed.Length < 3 || SearchTerms.GenericNames.Contains(squashed)) return;
            if (!names.Contains(trimmed, PathUtil.Comparer)) names.Add(trimmed);
            var cleaned = string.Join(" ", SearchTerms.CleanNameTokens(trimmed));
            if (cleaned.Length >= 3 && !names.Contains(cleaned, PathUtil.Comparer)) names.Add(cleaned);
        }
        AddName(identity.Name);
        AddName(identity.ProductName);
        AddName(identity.ExecutableName);
        if (identity.Kind != InstallKind.msix) AddName(identity.InstallFolderName);

        var vendors = new List<string>();
        foreach (var raw in new[] { identity.Publisher, identity.CompanyName })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var trimmed = raw.Trim();
            if (!vendors.Contains(trimmed, PathUtil.Comparer)) vendors.Add(trimmed);
            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // "Opera Software ASA" → "Opera Software", "Opera": öndeki her parça klasör adı olabilir
            for (var i = words.Length - 1; i >= 1; i--)
            {
                var part = string.Join(" ", words.Take(i)).TrimEnd(',', '.');
                if (part.Length >= 3 && !vendors.Contains(part, PathUtil.Comparer)) vendors.Add(part);
            }
            var noPunctuation = trimmed.TrimEnd('.', ',');
            if (noPunctuation.Length >= 3 && !vendors.Contains(noPunctuation, PathUtil.Comparer)) vendors.Add(noPunctuation);
        }

        var dataRoots = new List<(string Root, string Label)>
        {
            (Folders.RoamingAppData, "AppData\\Roaming"), (Folders.LocalAppData, "AppData\\Local"),
            (Folders.LocalLowAppData, "AppData\\LocalLow"), (Folders.ProgramData, "ProgramData")
        };
        var registryRoots = new List<(string Root, string Label)>
        {
            (PathClassifier.SoftwareUser, "HKCU\\Software"), (PathClassifier.SoftwareMachine, "HKLM\\Software"),
            (PathClassifier.SoftwareMachine32, "HKLM\\Software\\WOW6432Node")
        };

        foreach (var (root, label) in dataRoots.Concat(registryRoots))
        {
            foreach (var name in names)
            {
                AddIfUsable(PathUtil.Join(root, name), label + " (ad)");
                foreach (var vendor in vendors)
                {
                    AddIfUsable(PathUtil.Join(PathUtil.Join(root, vendor), name), label + " (satıcı\\ad)");
                }
            }
            foreach (var id in terms.Identifiers)
            {
                AddIfUsable(PathUtil.Join(root, id), label + " (kimlik)");
            }
        }

        foreach (var startMenu in new[] { Folders.StartMenuPrograms, Folders.CommonStartMenuPrograms })
        {
            foreach (var name in names)
            {
                AddIfUsable(PathUtil.Join(startMenu, name), "Başlat Menüsü klasörü");
                AddIfUsable(PathUtil.Join(startMenu, name + ".lnk"), "Başlat Menüsü kısayolu");
                foreach (var vendor in vendors)
                {
                    AddIfUsable(PathUtil.Join(PathUtil.Join(startMenu, vendor), name), "Başlat Menüsü klasörü (satıcı)");
                    AddIfUsable(PathUtil.Join(PathUtil.Join(startMenu, vendor), name + ".lnk"), "Başlat Menüsü kısayolu (satıcı)");
                }
            }
        }
        foreach (var desktop in new[] { Folders.Desktop, Folders.PublicDesktop })
        {
            foreach (var name in names) AddIfUsable(PathUtil.Join(desktop, name + ".lnk"), "Masaüstü kısayolu");
        }
        return outList;
    }

    /// <summary>Hedefi kurulum klasörünün içinde olan kısayollar (Başlat Menüsü, Masaüstü) — adı ne olursa olsun.</summary>
    private List<Finding> ShortcutCandidates(AppIdentity identity)
    {
        var outList = new List<Finding>();
        if (string.IsNullOrEmpty(identity.InstallLocation) && string.IsNullOrEmpty(identity.ExecutablePath)) return outList;
        var dirs = new List<(string Dir, int Depth)>
        {
            (Folders.StartMenuPrograms, 2), (Folders.CommonStartMenuPrograms, 2), (Folders.Desktop, 1), (Folders.PublicDesktop, 1),
            (PathUtil.Join(Folders.RoamingAppData, "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar"), 1),
            (PathUtil.Join(Folders.RoamingAppData, "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\StartMenu"), 1),
            (PathUtil.Join(Folders.RoamingAppData, "Microsoft\\Windows\\Start Menu\\Programs\\Startup"), 1),
            (PathUtil.Join(Folders.ProgramData, "Microsoft\\Windows\\Start Menu\\Programs\\StartUp"), 1)
        };
        foreach (var (dir, depth) in dirs) WalkShortcuts(identity, dir, depth, 0, outList);
        return outList;
    }

    private void WalkShortcuts(AppIdentity identity, string dir, int maxDepth, int depth, List<Finding> outList)
    {
        if (depth >= maxDepth) return;
        var names = Fs.Children(dir);
        if (names == null) return;
        foreach (var name in names)
        {
            var path = PathUtil.Join(dir, name);
            if (Fs.IsReparsePoint(path)) continue;
            if (Fs.IsDirectory(path))
            {
                WalkShortcuts(identity, path, maxDepth, depth + 1, outList);
                continue;
            }
            if (!PathUtil.HasExtension(name, ".lnk")) continue;
            var target = Fs.ShortcutTarget(path);
            if (!IsInsideInstall(identity, target)) continue;
            var note = "Kısayol hedefi kurulum klasöründe: " + PathUtil.LastComponent(target!);
            if (PathUtil.IsUnder(path, Folders.StartMenuPrograms) || PathUtil.IsUnder(path, Folders.CommonStartMenuPrograms))
            {
                // Kısayolun klasörü yalnızca bu uygulamaya aitse klasörün tamamı listelenir
                var parent = PathUtil.Parent(path)!;
                if (!PathUtil.PathEquals(parent, Folders.StartMenuPrograms) && !PathUtil.PathEquals(parent, Folders.CommonStartMenuPrograms)
                    && FolderOnlyPointsInto(identity, parent))
                {
                    outList.Add(Make(parent, FindingSource.shortcut, MatchStrength.strong, "Başlat Menüsü klasörü, tüm kısayolları kurulum klasörüne gidiyor"));
                    continue;
                }
            }
            var admission = PathClassifier.Admit(path, Folders, Options);
            if (!admission.IsAccepted) continue;
            outList.Add(Make(path, FindingSource.shortcut, MatchStrength.strong, note));
        }
    }

    private bool FolderOnlyPointsInto(AppIdentity identity, string folder)
    {
        var names = Fs.Children(folder);
        if (names == null || names.Count == 0) return false;
        foreach (var name in names)
        {
            var path = PathUtil.Join(folder, name);
            if (Fs.IsDirectory(path)) return false;
            if (!PathUtil.HasExtension(name, ".lnk")) continue;
            var target = Fs.ShortcutTarget(path);
            if (target == null) return false;
            if (!IsInsideInstall(identity, target) && !PathUtil.HasExtension(target, ".url") && !PathUtil.HasExtension(target, ".html")) return false;
        }
        return true;
    }

    /// <summary>Komutu kurulum klasörünün içinde olan Run / RunOnce değerleri.</summary>
    private List<Finding> StartupCandidates(AppIdentity identity)
    {
        var outList = new List<Finding>();
        var keys = new List<string> { PathClassifier.RunKeyUser, PathClassifier.RunOnceKeyUser };
        if (Options.IncludeSystemLocations) keys.AddRange(new[] { PathClassifier.RunKeyMachine, PathClassifier.RunKeyMachine32 });
        foreach (var key in keys)
        {
            var names = Fs.Children(key);
            if (names == null) continue;
            foreach (var name in names)
            {
                var valuePath = PathUtil.Join(key, name);
                if (Fs.IsDirectory(valuePath)) continue;
                var command = Fs.RunCommand(valuePath);
                var exe = CommandLine.ExecutablePath(command, Folders);
                if (!IsInsideInstall(identity, exe)) continue;
                var admission = PathClassifier.Admit(valuePath, Folders, Options);
                if (!admission.IsAccepted) continue;
                outList.Add(Make(valuePath, FindingSource.startup, MatchStrength.strong, "Başlangıç komutu kurulum klasöründe: " + PathUtil.LastComponent(exe!)));
            }
        }
        return outList;
    }

    /// <summary>Eylemi kurulum klasörünün içinde olan zamanlanmış görevler (yalnızca sistem taraması açıkken).</summary>
    private List<Finding> ScheduledTaskCandidates(AppIdentity identity)
    {
        var outList = new List<Finding>();
        WalkTasks(identity, Folders.ScheduledTasks, 3, 0, outList);
        return outList;
    }

    private void WalkTasks(AppIdentity identity, string dir, int maxDepth, int depth, List<Finding> outList)
    {
        if (depth >= maxDepth) return;
        var names = Fs.Children(dir);
        if (names == null) return;
        foreach (var name in names)
        {
            var path = PathUtil.Join(dir, name);
            if (Fs.IsDirectory(path)) { WalkTasks(identity, path, maxDepth, depth + 1, outList); continue; }
            var exe = Fs.TaskExecutable(path);
            if (!IsInsideInstall(identity, exe)) continue;
            outList.Add(Make(path, FindingSource.scheduledTask, MatchStrength.strong, "Görev eylemi kurulum klasöründe: " + PathUtil.LastComponent(exe!)));
        }
    }

    /// <summary>ImagePath'i kurulum klasörünün içinde olan hizmetler (yalnızca sistem taraması açıkken).</summary>
    private List<Finding> ServiceCandidates(AppIdentity identity)
    {
        var outList = new List<Finding>();
        var names = Fs.Children(PathClassifier.ServicesKey);
        if (names == null) return outList;
        foreach (var name in names)
        {
            var key = PathUtil.Join(PathClassifier.ServicesKey, name);
            if (!Fs.IsDirectory(key)) continue;
            var exe = Fs.ServiceExecutable(key);
            if (!IsInsideInstall(identity, exe)) continue;
            outList.Add(Make(key, FindingSource.service, MatchStrength.strong, "Hizmet yürütülebiliri kurulum klasöründe: " + PathUtil.LastComponent(exe!)));
        }
        return outList;
    }

    private List<string> AddDatabaseHits(AppIdentity identity, SearchTerms terms, Collector collector)
    {
        if (!Options.UseDatabase || Database == null) return new List<string>();
        var entries = Database.Lookup(identity, terms);
        if (entries.Count == 0) return new List<string>();
        var matchedIds = new HashSet<string>(entries.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var hit in Database.Expand(entries, Folders, Fs))
        {
            if (identity.InstallLocation is { Length: > 0 } install && PathUtil.IsUnder(hit.Path, install)) continue;
            var admission = PathClassifier.Admit(hit.Path, Folders, Options);
            if (!admission.IsAccepted)
            {
                collector.Reject(hit.Path, "Veritabanı " + hit.EntryId + ": " + admission.Reason);
                continue;
            }
            if (!IsUsable(hit.Path)) { collector.Reject(hit.Path, "yeniden ayrıştırma noktası veya yok"); continue; }
            var strength = (MatchStrength)Math.Min((int)admission.Strength, (int)hit.Strength);
            var label = "Veritabanı: " + hit.EntryId + (hit.Note != null ? " — " + hit.Note : string.Empty) + (admission.Note != null ? " — " + admission.Note : string.Empty);
            var others = Database.OtherOwners(hit.Path, Folders, matchedIds, identity);
            if (others.Count > 0)
            {
                strength = MatchStrength.weak;
                label += " — paylaşılan (" + string.Join(", ", others.Take(3)) + "), kontrol et";
            }
            collector.Add(Make(hit.Path, FindingSource.database, strength, label));
        }
        return entries.Select(e => e.Id).ToList();
    }

    private List<AppCondition> AddConditionHits(AppIdentity identity, List<string> databaseIds, Collector collector)
    {
        if (Conditions == null) return new List<AppCondition>();
        var matched = Conditions.Matching(identity, databaseIds);
        if (matched.Count == 0) return matched;
        foreach (var hit in Conditions.Expand(matched, Folders, Fs))
        {
            if (identity.InstallLocation is { Length: > 0 } install && PathUtil.IsUnder(hit.Path, install) && !PathUtil.PathEquals(hit.Path, install)) continue;
            var admission = PathClassifier.Admit(hit.Path, Folders, Options);
            if (!admission.IsAccepted)
            {
                collector.Reject(hit.Path, "Kural " + hit.ConditionId + ": " + admission.Reason);
                continue;
            }
            if (!IsUsable(hit.Path)) { collector.Reject(hit.Path, "yeniden ayrıştırma noktası veya yok"); continue; }
            var label = "Kural: " + hit.ConditionId;
            if (hit.Note != null) label += " — " + hit.Note;
            if (admission.Note != null) label += " — " + admission.Note;
            var strength = (MatchStrength)Math.Min((int)admission.Strength, (int)hit.Strength);
            collector.Add(Make(hit.Path, FindingSource.condition, strength, label));
        }
        return matched;
    }

    /// <summary>
    /// "AppData\Roaming\Microsoft", "HKCU\Software\Google" gibi satıcı adını taşıyan klasör ve anahtarlar aynı satıcının
    /// diğer ürünleriyle paylaşılır; uygulamanın adı satıcı adının kendisi değilse seçimsiz bırakılır.
    /// </summary>
    private void DowngradeVendorFolders(SearchTerms terms, Collector collector)
    {
        if (terms.VendorTokens.Count == 0 || terms.NameEqualsVendor) return;
        var roots = new[]
        {
            Folders.RoamingAppData, Folders.LocalAppData, Folders.LocalLowAppData, Folders.ProgramData,
            Folders.StartMenuPrograms, Folders.CommonStartMenuPrograms,
            PathClassifier.SoftwareUser, PathClassifier.SoftwareMachine, PathClassifier.SoftwareMachine32
        };
        foreach (var path in collector.Paths.ToList())
        {
            foreach (var root in roots)
            {
                var rel = PathUtil.Relative(path, root);
                if (rel == null || rel.Contains('\\')) continue;
                if (!terms.IsVendorName(rel)) continue;
                // Tüm kısayolları bu uygulamaya giden Başlat Menüsü klasörü satıcı adını taşısa da bu uygulamanındır
                if (collector.Get(path)?.Source == FindingSource.shortcut) continue;
                collector.Downgrade(path, "satıcı klasörü, aynı satıcının diğer ürünleri de kullanıyor olabilir — kontrol et");
            }
        }
    }

    private void ApplyExclusions(List<AppCondition> matched, Collector collector)
    {
        if (Conditions == null || matched.Count == 0) return;
        foreach (var path in collector.Paths.ToList())
        {
            var id = Conditions.Exclusion(path, Folders, matched);
            if (id != null) collector.Remove(path, "Kural " + id + " hariç tutuyor");
        }
    }

    // MARK: Dizin ve kayıt defteri taraması

    private readonly record struct Location(string Root, int MaxDepth, bool System);

    private List<Finding> SearchUserLocations(SearchTerms terms)
    {
        var locations = new List<Location>
        {
            new(Folders.RoamingAppData, 3, false),
            new(Folders.LocalAppData, 3, false),
            new(Folders.LocalLowAppData, 3, false),
            new(Folders.ProgramData, 2, false),
            new(Folders.Packages, 1, false),
            new(Folders.UserPrograms, 1, false),
            new(Folders.Temp, 1, false)
        };
        if (Options.IncludeAggressiveLocations)
        {
            locations.Add(new Location(Folders.SavedGames, 1, false));
            locations.Add(new Location(PathUtil.Join(Folders.Documents, "My Games"), 1, false));
            locations.Add(new Location(Folders.Home, 1, false));
        }
        var outList = new List<Finding>();
        foreach (var loc in locations)
        {
            if (!Fs.IsDirectory(loc.Root)) continue;
            outList.AddRange(SearchDirectory(loc.Root, loc.Root, terms, loc.MaxDepth, 0, loc.System));
        }
        return outList;
    }

    private List<Finding> SearchRegistry(SearchTerms terms)
    {
        var locations = new List<Location>
        {
            new(PathClassifier.SoftwareUser, 2, false),
            new(PathClassifier.UninstallKeyUser, 1, false),
            new(PathClassifier.AppPathsUser, 1, false)
        };
        if (Options.IncludeSystemLocations)
        {
            locations.Add(new Location(PathClassifier.SoftwareMachine, 2, true));
            locations.Add(new Location(PathClassifier.SoftwareMachine32, 2, true));
            locations.Add(new Location(PathClassifier.UninstallKeyMachine, 1, true));
            locations.Add(new Location(PathClassifier.UninstallKeyMachine32, 1, true));
            locations.Add(new Location(PathClassifier.AppPathsMachine, 1, true));
        }
        var outList = new List<Finding>();
        foreach (var loc in locations)
        {
            if (!Fs.IsDirectory(loc.Root)) continue;
            outList.AddRange(SearchDirectory(loc.Root, loc.Root, terms, loc.MaxDepth, 0, loc.System));
        }
        return outList;
    }

    private List<Finding> SearchStartMenu(SearchTerms terms)
    {
        var outList = new List<Finding>();
        foreach (var root in new[] { Folders.StartMenuPrograms, Folders.CommonStartMenuPrograms, Folders.Desktop, Folders.PublicDesktop })
        {
            if (!Fs.IsDirectory(root)) continue;
            outList.AddRange(SearchDirectory(root, root, terms, 2, 0, false));
        }
        return outList;
    }

    private List<Finding> SearchSystemLocations(SearchTerms terms)
    {
        var outList = new List<Finding>();
        foreach (var root in new[] { Folders.ProgramFiles, Folders.ProgramFilesX86 })
        {
            if (!Fs.IsDirectory(root)) continue;
            outList.AddRange(SearchDirectory(root, root, terms, 2, 0, true));
        }
        return outList;
    }

    private static readonly string[] NoDescendForeign = { "Microsoft", "Windows", "WindowsApps", "Common Files" };

    private List<Finding> SearchDirectory(string root, string dir, SearchTerms terms, int maxDepth, int depth, bool system)
    {
        var outList = new List<Finding>();
        if (depth >= maxDepth) return outList;
        var names = Fs.Children(dir);
        if (names == null) return outList;
        foreach (var name in names)
        {
            var path = PathUtil.Join(dir, name);
            if (Fs.IsReparsePoint(path)) continue;
            var isDir = Fs.IsDirectory(path);
            var relative = PathUtil.RelativeOrLast(path, root);

            var strength = terms.Match(relative, isDir);
            if (strength != null)
            {
                if (system && strength != MatchStrength.strong) continue;
                var admission = PathClassifier.Admit(path, Folders, Options);
                if (!admission.IsAccepted) continue;
                if (!IsUsable(path)) continue;
                // Yalnızca ad benzerliğiyle bulunan boş klasörler gürültüdür
                if (strength == MatchStrength.weak && IsEmptyDirectory(path)) continue;
                var source = terms.MatchesIdentifier(name) ? FindingSource.identifier : FindingSource.name;
                var effective = (MatchStrength)Math.Min((int)strength.Value, (int)admission.Strength);
                var note = PathUtil.LastComponent(dir) + (admission.Note != null ? " — " + admission.Note : string.Empty);
                outList.Add(Make(path, source, effective, note));
            }
            else if (isDir)
            {
                // Windows'un ve Microsoft'un klasörlerinin içine inme (uygulama Microsoft'un değilse)
                if (!terms.IsMicrosoftApp && depth == 0 && NoDescendForeign.Any(n => string.Equals(n, name, PathUtil.Comparison))) continue;
                if (depth == 0 && (string.Equals(name, "Packages", PathUtil.Comparison) || string.Equals(name, "Temp", PathUtil.Comparison))
                    && PathUtil.PathEquals(dir, Folders.LocalAppData)) continue;
                outList.AddRange(SearchDirectory(root, path, terms, maxDepth, depth + 1, system));
            }
        }
        return outList;
    }

    private void CollectMentions(string root, string dir, List<string> needles, int maxDepth, int depth,
                                 List<string> found, HashSet<string> seen, int limit)
    {
        if (depth >= maxDepth || found.Count >= limit) return;
        var names = Fs.Children(dir);
        if (names == null) return;
        foreach (var name in names)
        {
            if (found.Count >= limit) return;
            var path = PathUtil.Join(dir, name);
            if (Fs.IsReparsePoint(path)) continue;
            var squashed = SearchTerms.Squash(PathUtil.RelativeOrLast(path, root));
            if (needles.Any(n => squashed.Contains(n, StringComparison.Ordinal)))
            {
                if (seen.Add(path)) found.Add(Fs.Canonical(path));
                continue;
            }
            if (Fs.IsDirectory(path))
            {
                CollectMentions(root, path, needles, maxDepth, depth + 1, found, seen, limit);
            }
        }
    }
}

/// <summary>Gömülü kalıntı veritabanı (Tur W-B'de doldurulur). Çekirdek yalnızca bu arayüze bakar.</summary>
public interface ILeftoverDatabase
{
    sealed record Entry(string Id, string DisplayName);
    sealed record Hit(string Path, string EntryId, MatchStrength Strength, string? Note);

    int Count { get; }
    /// <summary>Kimliğe uyan kayıtlar.</summary>
    List<Entry> Lookup(AppIdentity identity, SearchTerms terms);
    /// <summary>Kayıtların yol kalıplarını diskte var olan yollara açar.</summary>
    List<Hit> Expand(IReadOnlyList<Entry> entries, KnownFolders folders, IScanFileSystem fs);
    /// <summary>Yolu sahiplenen, eşleşmeyen diğer kayıtların kimlikleri (aynı uygulamanın sürümleri hariç).</summary>
    List<string> OtherOwners(string path, KnownFolders folders, IReadOnlySet<string> matchedIds, AppIdentity identity);
}
