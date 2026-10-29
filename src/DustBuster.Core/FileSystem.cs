namespace DustBuster.Core;

/// <summary>
/// Tarayıcının dosya sistemine ve kayıt defterine tek erişim noktası. Gerçek makinede
/// <see cref="RealFileSystem"/>, testlerde yol listesinden kurulan <see cref="VirtualFileSystem"/>
/// kullanılır; tarama mantığı ikisini ayırt etmez. Kayıt defteri anahtarları "HKCU:\…" biçiminde
/// birer "dizin" gibi ele alınır.
/// </summary>
public interface IScanFileSystem
{
    /// <summary>Dizindeki (veya anahtardaki) girdi adları. Dizin yoksa veya okunamıyorsa null.</summary>
    IReadOnlyList<string>? Children(string path);
    bool Exists(string path);
    bool IsDirectory(string path);
    /// <summary>Sembolik bağ, junction veya başka bir yeniden ayrıştırma noktası</summary>
    bool IsReparsePoint(string path);
    /// <summary>Diskteki gerçek yazım ("notion" → "Notion"); bulunamazsa verilen yol</summary>
    string Canonical(string path);
    /// <summary>.lnk kısayolunun hedef yolu</summary>
    string? ShortcutTarget(string path);
    /// <summary>Başlangıç girdisi (Run anahtarındaki değer) → komut satırı; anahtar altındaki değer adları Children ile gelir</summary>
    string? RunCommand(string valuePath);
    /// <summary>Zamanlanmış görev dosyasının çalıştırdığı yürütülebilir</summary>
    string? TaskExecutable(string taskPath);
    /// <summary>Hizmet anahtarının ImagePath değerindeki yürütülebilir</summary>
    string? ServiceExecutable(string serviceKeyPath);
}

public static class ScanFileSystemExtensions
{
    public static bool IsRegularDirectory(this IScanFileSystem fs, string path) => fs.IsDirectory(path) && !fs.IsReparsePoint(path);

    /// <summary>
    /// Dizin, içinde (sınırlı derinlikte) hiç dosya barındırmıyorsa "boş" sayılır. Yalnızca boş alt
    /// klasörlerden oluşan bir ağaç da boştur.
    /// </summary>
    public static bool IsEffectivelyEmptyDirectory(this IScanFileSystem fs, string path, int maxDepth = 6, int maxEntries = 400)
    {
        if (!fs.IsDirectory(path)) return false;
        var visited = 0;
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((path, 0));
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            var names = fs.Children(dir);
            if (names == null) continue;
            foreach (var name in names)
            {
                visited++;
                if (visited > maxEntries) return false;
                var child = PathUtil.Join(dir, name);
                if (fs.IsReparsePoint(child)) continue;
                if (fs.IsDirectory(child))
                {
                    if (depth + 1 < maxDepth) stack.Push((child, depth + 1));
                }
                else
                {
                    return false;
                }
            }
        }
        return true;
    }
}

/// <summary>
/// Yol listesinden kurulan sanal disk ve kayıt defteri. Sonu "\" ile biten veya altında çocuğu olan
/// girdiler dizin, diğerleri dosya sayılır. Ara dizinler otomatik oluşturulur. Karşılaştırmalar
/// büyük/küçük harfe duyarsızdır; ilk eklenen yazım korunur.
/// </summary>
public sealed class VirtualFileSystem : IScanFileSystem
{
    private readonly HashSet<string> _directories = new(PathUtil.Comparer);
    private readonly HashSet<string> _files = new(PathUtil.Comparer);
    private readonly HashSet<string> _reparsePoints = new(PathUtil.Comparer);
    private readonly Dictionary<string, Dictionary<string, string>> _children = new(PathUtil.Comparer);
    private readonly Dictionary<string, string> _canonical = new(PathUtil.Comparer);
    private readonly Dictionary<string, string> _shortcutTargets = new(PathUtil.Comparer);
    private readonly Dictionary<string, string> _runCommands = new(PathUtil.Comparer);
    private readonly Dictionary<string, string> _taskExecutables = new(PathUtil.Comparer);
    private readonly Dictionary<string, string> _serviceExecutables = new(PathUtil.Comparer);

    public VirtualFileSystem(IEnumerable<string> paths,
                             IEnumerable<string>? reparsePoints = null,
                             IDictionary<string, string>? shortcutTargets = null,
                             IDictionary<string, string>? runCommands = null,
                             IDictionary<string, string>? taskExecutables = null,
                             IDictionary<string, string>? serviceExecutables = null)
    {
        foreach (var raw in paths) Add(raw);
        if (reparsePoints != null)
        {
            foreach (var raw in reparsePoints)
            {
                Add(raw);
                _reparsePoints.Add(PathUtil.Normalize(raw));
            }
        }
        if (shortcutTargets != null)
        {
            foreach (var (path, target) in shortcutTargets) { Add(path); _shortcutTargets[PathUtil.Normalize(path)] = PathUtil.Normalize(target); }
        }
        if (runCommands != null)
        {
            foreach (var (path, command) in runCommands) { Add(path); _runCommands[PathUtil.Normalize(path)] = command; }
        }
        if (taskExecutables != null)
        {
            foreach (var (path, exe) in taskExecutables) { Add(path); _taskExecutables[PathUtil.Normalize(path)] = PathUtil.Normalize(exe); }
        }
        if (serviceExecutables != null)
        {
            foreach (var (path, exe) in serviceExecutables) { Add(path + "\\"); _serviceExecutables[PathUtil.Normalize(path)] = PathUtil.Normalize(exe); }
        }
    }

    private void Add(string raw)
    {
        var isDir = raw.EndsWith('/') || raw.EndsWith('\\');
        var path = PathUtil.Normalize(raw);
        if (path.Length == 0) return;
        var root = PathUtil.Root(path);
        if (root.Length == 0) return;

        var rel = PathUtil.Relative(path, root);
        var components = rel == null ? Array.Empty<string>() : rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        _directories.Add(root);
        _canonical.TryAdd(root, root);
        if (components.Length == 0)
        {
            return;
        }

        var current = root;
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            var isLast = i == components.Length - 1;
            var childPath = PathUtil.Join(current, component);
            if (!_children.TryGetValue(current, out var map))
            {
                map = new Dictionary<string, string>(PathUtil.Comparer);
                _children[current] = map;
            }
            map.TryAdd(component, component);
            _canonical.TryAdd(childPath, PathUtil.Join(_canonical.TryGetValue(current, out var canonParent) ? canonParent : current, map[component]));

            if (isLast && !isDir)
            {
                if (!_directories.Contains(childPath)) _files.Add(childPath);
            }
            else
            {
                _directories.Add(childPath);
                _files.Remove(childPath);
            }
            current = childPath;
        }
    }

    public IReadOnlyList<string>? Children(string path)
    {
        var p = PathUtil.Normalize(path);
        if (!_directories.Contains(p)) return null;
        if (!_children.TryGetValue(p, out var map)) return Array.Empty<string>();
        return map.Values.OrderBy(n => n, PathUtil.Comparer).ToList();
    }

    public bool Exists(string path)
    {
        var p = PathUtil.Normalize(path);
        return _directories.Contains(p) || _files.Contains(p);
    }

    public bool IsDirectory(string path) => _directories.Contains(PathUtil.Normalize(path));

    public bool IsReparsePoint(string path) => _reparsePoints.Contains(PathUtil.Normalize(path));

    public string Canonical(string path)
    {
        var p = PathUtil.Normalize(path);
        return _canonical.TryGetValue(p, out var c) ? c : p;
    }

    public string? ShortcutTarget(string path) => _shortcutTargets.TryGetValue(PathUtil.Normalize(path), out var t) ? t : null;
    public string? RunCommand(string valuePath) => _runCommands.TryGetValue(PathUtil.Normalize(valuePath), out var c) ? c : null;
    public string? TaskExecutable(string taskPath) => _taskExecutables.TryGetValue(PathUtil.Normalize(taskPath), out var e) ? e : null;
    public string? ServiceExecutable(string serviceKeyPath) => _serviceExecutables.TryGetValue(PathUtil.Normalize(serviceKeyPath), out var e) ? e : null;

    /// <summary>Testler için: sanal diskteki tüm yollar.</summary>
    public List<string> AllPaths => _directories.Union(_files, PathUtil.Comparer).Select(Canonical).OrderBy(p => p, PathUtil.Comparer).ToList();
}
