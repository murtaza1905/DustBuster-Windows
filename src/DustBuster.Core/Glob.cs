namespace DustBuster.Core;

/// <summary>
/// `*` ve `?` destekleyen, büyük/küçük harfe duyarsız basit joker eşleştirici ve
/// <see cref="IScanFileSystem"/> üzerinde çalışan yol genişletici (kural ve veritabanı kalıpları için).
/// </summary>
public static class Glob
{
    public static bool Matches(string pattern, string name)
    {
        var p = pattern.ToLowerInvariant();
        var s = name.ToLowerInvariant();
        int pi = 0, si = 0, starP = -1, starS = -1;
        while (si < s.Length)
        {
            if (pi < p.Length && (p[pi] == '?' || p[pi] == s[si]))
            {
                pi++; si++;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                starP = pi; starS = si;
                pi++;
            }
            else if (starP >= 0)
            {
                pi = starP + 1;
                starS++;
                si = starS;
            }
            else
            {
                return false;
            }
        }
        while (pi < p.Length && p[pi] == '*') pi++;
        return pi == p.Length;
    }

    public static bool HasWildcard(string s) => s.Contains('*') || s.Contains('?');

    /// <summary>"%LOCALAPPDATA%\Google\Chrome*" gibi bir kalıbı var olan yollara açar. Joker içermeyen kalıp, yol diskte varsa kendisi olarak döner.</summary>
    public static List<string> Expand(string pattern, KnownFolders folders, IScanFileSystem fs)
    {
        var pat = folders.Expand(pattern);
        if (pat.Length == 0) return new List<string>();
        var root = PathUtil.Root(pat);
        if (root.Length == 0 || (!PathUtil.IsRegistryPath(root) && !(root.Length == 3 && root[1] == ':'))) return new List<string>();

        var rel = PathUtil.Relative(pat, root);
        var components = rel == null ? Array.Empty<string>() : rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0) return new List<string>();

        // "…\Temp\*" gibi son bileşeni neredeyse salt joker olan kalıplar bir klasörün tamamını sahiplenir; ikinci emniyet
        var last = components[^1];
        if (HasWildcard(last))
        {
            var literal = new string(last.Where(c => c != '*' && c != '?').ToArray());
            if (literal.Length < 3) return new List<string>();
        }

        if (!components.Any(HasWildcard))
        {
            return fs.Exists(pat) ? new List<string> { fs.Canonical(pat) } : new List<string>();
        }

        var current = new List<string> { root };
        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            var isLast = index == components.Length - 1;
            var next = new List<string>();
            foreach (var basePath in current)
            {
                if (HasWildcard(component))
                {
                    var names = fs.Children(basePath);
                    if (names == null) continue;
                    foreach (var name in names)
                    {
                        if (!Matches(component, name)) continue;
                        var candidate = PathUtil.Join(basePath, name);
                        if (isLast || fs.IsDirectory(candidate)) next.Add(candidate);
                    }
                }
                else
                {
                    var candidate = PathUtil.Join(basePath, component);
                    if (isLast ? fs.Exists(candidate) : fs.IsDirectory(candidate)) next.Add(fs.Canonical(candidate));
                }
            }
            current = next;
            if (current.Count == 0) break;
        }
        current.Sort(PathUtil.Comparer);
        return current;
    }
}
