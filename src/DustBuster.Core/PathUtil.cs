namespace DustBuster.Core;

/// <summary>
/// Yol yardımcıları. Çekirdek yalnızca "\" ayraçlı, sonunda ayraç olmayan, büyük/küçük harfe duyarsız
/// karşılaştırılan yollarla çalışır. Kayıt defteri anahtarları da aynı biçimde temsil edilir:
/// "HKCU:\Software\Satıcı\Uygulama".
/// </summary>
public static class PathUtil
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    public const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    public static readonly string[] RegistryHives = { "HKCU:", "HKLM:", "HKU:", "HKCR:" };

    /// <summary>"/" → "\", ardışık ayraçlar tek, sondaki ayraç atılır (sürücü kökü hariç).</summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var p = raw.Replace('/', '\\');
        while (p.Contains("\\\\", StringComparison.Ordinal)) p = p.Replace("\\\\", "\\", StringComparison.Ordinal);
        if (p.Length > 3 && p.EndsWith('\\')) p = p.TrimEnd('\\');
        if (p.Length == 2 && p[1] == ':') p += "\\";
        return p;
    }

    public static bool IsRegistryPath(string path)
    {
        foreach (var hive in RegistryHives)
        {
            if (path.StartsWith(hive, Comparison)) return true;
        }
        return false;
    }

    /// <summary>"C:\" veya "HKCU:" gibi kök bileşen.</summary>
    public static string Root(string path)
    {
        var p = Normalize(path);
        var idx = p.IndexOf('\\');
        var head = idx < 0 ? p : p[..idx];
        if (head.Length == 2 && head[1] == ':') return head + "\\";
        return head;
    }

    public static bool IsRoot(string path)
    {
        var p = Normalize(path);
        if (p.Length == 3 && p[1] == ':' && p[2] == '\\') return true;
        return IsRegistryPath(p) && !p.Contains('\\');
    }

    public static string Join(string basePath, string name)
    {
        var b = Normalize(basePath);
        if (b.EndsWith('\\')) return b + name;
        return b + "\\" + name;
    }

    public static string LastComponent(string path)
    {
        var p = Normalize(path);
        if (p.EndsWith('\\')) p = p.TrimEnd('\\');
        var idx = p.LastIndexOf('\\');
        return idx < 0 ? p : p[(idx + 1)..];
    }

    /// <summary>Üst dizin; kökte null.</summary>
    public static string? Parent(string path)
    {
        var p = Normalize(path);
        if (IsRoot(p)) return null;
        var idx = p.LastIndexOf('\\');
        if (idx < 0) return null;
        var parent = p[..idx];
        if (parent.Length == 2 && parent[1] == ':') parent += "\\";
        if (parent.Length == 0) return null;
        return parent;
    }

    public static string StripExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    public static string Extension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? string.Empty : name[dot..];
    }

    public static bool PathEquals(string a, string b) => string.Equals(Normalize(a), Normalize(b), Comparison);

    /// <summary>path == root veya path root'un altında.</summary>
    public static bool IsUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (string.Equals(p, r, Comparison)) return true;
        var prefix = r.EndsWith('\\') ? r : r + "\\";
        return p.StartsWith(prefix, Comparison);
    }

    /// <summary>path root'un altındaysa (eşit değilse) root'a göre bağıl yol; değilse null.</summary>
    public static string? Relative(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        var prefix = r.EndsWith('\\') ? r : r + "\\";
        if (!p.StartsWith(prefix, Comparison)) return null;
        return p[prefix.Length..];
    }

    public static string[] Components(string path)
    {
        var p = Normalize(path);
        return p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Arama kökünden itibaren bağıl yol ("Google\Chrome"); kök dışındaysa yalnızca son bileşen.</summary>
    public static string RelativeOrLast(string path, string root)
    {
        return Relative(path, root) ?? LastComponent(path);
    }

    public static bool HasExtension(string path, string extension) =>
        path.EndsWith(extension, Comparison);
}
