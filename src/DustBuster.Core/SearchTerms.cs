namespace DustBuster.Core;

/// <summary>
/// Arama kriterleri. Yalnızca iki şey eşleşir:
///  1) Kimlikler (PackageFamilyName, ProductCode, AppUserModelId ve benzeri) — birebir veya alt bileşen olarak
///  2) Uygulama adı — kelime sınırıyla (alt dize araması yapılmaz)
/// Yayıncı adı gibi aynı satıcının BAŞKA uygulamalarını da yakalayan terimler bilinçli olarak kullanılmaz.
/// </summary>
public sealed class SearchTerms : IEquatable<SearchTerms>
{
    /// <summary>Küçük harfli kimlikler</summary>
    public IReadOnlyList<string> Identifiers { get; }
    /// <summary>Uygulama adı varyantları, küçük harfli ve token'lara ayrılmış</summary>
    public IReadOnlyList<IReadOnlyList<string>> NameTokenSets { get; }
    /// <summary>Satıcı token'ları (satıcı klasörlerini tanımak için; eşleştirmede kullanılmaz)</summary>
    public IReadOnlyList<string> VendorTokens { get; }
    public bool IsMicrosoftApp { get; }

    public const int MinNameLength = 4;
    /// <summary>Önek eşleşmesi (ad + ek) yalnızca bu uzunluktan uzun adlar için yapılır</summary>
    public const int MinPrefixLength = 8;

    /// <summary>Tek başına bir uygulamayı tanımlamayan, çok sayıda yanlış eşleşme üreten adlar.</summary>
    public static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "app", "apps", "application", "applications", "helper", "helpers", "launcher", "agent", "daemon",
        "update", "updater", "updates", "installer", "install", "uninstaller", "uninstall", "setup",
        "client", "server", "service", "services", "windows", "win", "win32", "win64", "x64", "x86", "microsoft",
        "system", "system32", "library", "tool", "tools", "utility", "utilities", "manager", "program", "programs",
        "player", "viewer", "reader", "editor", "browser", "chat", "mail", "music", "photos", "photo", "video",
        "notes", "explorer", "preview", "calendar", "terminal", "console", "settings", "preferences", "support",
        "cache", "caches", "logs", "log", "data", "default", "test", "demo", "beta", "alpha", "pro", "lite",
        "free", "plus", "studio", "office", "home", "cloud", "sync", "drive", "backup", "menu", "bar", "widget",
        "launch", "login", "core", "base", "main", "new", "old", "temp", "tmp", "user", "users", "shared", "common",
        "google", "adobe", "mozilla", "chrome", "firefox", "edge", "opera", "runtime", "redistributable",
        "package", "packages", "local", "roaming", "appdata", "bin", "lib", "config", "games", "game", "software", "code"
    };

    /// <summary>Aynı uygulamanın ayrı dağıtım kanalları (Chrome Canary, X Beta…) ayrı uygulama sayılır.</summary>
    public static readonly HashSet<string> ChannelWords = new(StringComparer.Ordinal)
    {
        "canary", "beta", "dev", "nightly", "alpha", "insiders", "insider", "preview", "rc", "ptb"
    };

    /// <summary>Ad temizlenirken atılan mimari/sürüm sözcükleri.</summary>
    public static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "x64", "x86", "amd64", "arm64", "64", "32", "bit", "64bit", "32bit", "win64", "win32", "version", "edition",
        "current", "user", "machine", "installer", "setup"
    };

    public SearchTerms(IEnumerable<string> identifiers, IEnumerable<IReadOnlyList<string>> nameTokenSets,
                       IEnumerable<string> vendorTokens, bool isMicrosoftApp)
    {
        Identifiers = identifiers.Select(i => i.ToLowerInvariant()).Where(i => i.Length > 0).Distinct().ToList();
        NameTokenSets = nameTokenSets.ToList();
        VendorTokens = vendorTokens.Distinct().ToList();
        IsMicrosoftApp = isMicrosoftApp;
    }

    public static SearchTerms From(AppIdentity identity)
    {
        var ids = new List<string>();
        if (identity.PackageFamilyName is { Length: > 0 } pfn) ids.Add(pfn);
        if (identity.PackageFullName is { Length: > 0 } pfull) ids.Add(pfull);
        if (identity.ProductCode is { Length: > 0 } code) ids.Add(code);
        if (identity.UninstallKeyPath is { Length: > 0 } key)
        {
            // Anahtar adı yalnızca kimlik gibi görünüyorsa ("{GUID}", "notion.id", "Discord_x64") kimlik sayılır;
            // "Google Chrome" gibi düz adlar ad eşleştirmesine bırakılır
            var keyName = PathUtil.LastComponent(key);
            if (LooksLikeIdentifier(keyName)) ids.Add(keyName);
        }
        ids.AddRange(identity.ExtraIdentifiers.Where(LooksLikeIdentifier));

        var extraNames = new List<string>();
        if (identity.ProductName is { Length: > 0 } product && product != identity.Name) extraNames.Add(product);
        var exe = identity.ExecutableName;
        if (exe.Length > 0 && exe != identity.Name) extraNames.Add(exe);
        var folder = identity.InstallFolderName;
        if (folder.Length > 0 && folder != identity.Name && identity.Kind != InstallKind.msix) extraNames.Add(folder);
        if (identity.Kind == InstallKind.msix && identity.PackageFamilyName is { Length: > 0 } fam)
        {
            // "Vendor.App_hash" → "App"
            var underscore = fam.IndexOf('_');
            var head = underscore > 0 ? fam[..underscore] : fam;
            var dot = head.LastIndexOf('.');
            if (dot >= 0 && dot < head.Length - 1) extraNames.Add(head[(dot + 1)..]);
        }

        return new SearchTerms(
            ids,
            MakeNameTokenSets(identity.Name, extraNames),
            MakeVendorTokens(identity.Publisher, identity.CompanyName),
            identity.IsMicrosoftApp);
    }

    // MARK: Yardımcılar

    public static List<string> Tokens(string text)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) current.Append(ch);
            else if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    public static string Squash(string text) => string.Concat(Tokens(text));

    /// <summary>"7-Zip 23.01 (x64 edition)" → ["7", "zip"]: parantez içi, sürüm numarası ve mimari sözcükleri atılır.</summary>
    public static List<string> CleanNameTokens(string raw)
    {
        var text = raw;
        var open = text.IndexOf('(');
        while (open >= 0)
        {
            var close = text.IndexOf(')', open);
            text = close > open ? text.Remove(open, close - open + 1) : text[..open];
            open = text.IndexOf('(');
        }
        var tokens = Tokens(text);
        var cleaned = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (NoiseWords.Contains(t)) continue;
            // Sayısal token'lar sürüm/derleme numarasıdır; yalnızca adın ilk token'ı sayısal olabilir ("7-Zip")
            if (IsVersionLike(t, i)) continue;
            cleaned.Add(t);
        }
        // "v2" gibi tek harf + sayı
        cleaned.RemoveAll(t => t.Length >= 2 && t[0] == 'v' && t[1..].All(char.IsDigit));
        return cleaned;
    }

    private static bool IsVersionLike(string token, int index)
    {
        if (!token.All(char.IsDigit)) return false;
        return index > 0 || token.Length >= 2;
    }

    /// <summary>Uygulama adı ve varyantlarından token kümeleri üretir; genel/kısa adları eler.</summary>
    public static List<IReadOnlyList<string>> MakeNameTokenSets(string appName, IEnumerable<string> extraNames)
    {
        var sets = new List<IReadOnlyList<string>>();
        var all = new List<string> { appName };
        all.AddRange(extraNames);
        foreach (var raw in all)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length < MinNameLength) continue;
            var toks = CleanNameTokens(trimmed);
            if (toks.Count == 0) continue;
            var squashed = string.Concat(toks);
            if (squashed.Length < MinNameLength) continue;
            if (GenericNames.Contains(squashed)) continue;
            if (!squashed.Any(char.IsLetter)) continue;
            if (!sets.Any(s => s.SequenceEqual(toks))) sets.Add(toks);
            if (toks.Count > 1 && !sets.Any(s => s.Count == 1 && s[0] == squashed)) sets.Add(new List<string> { squashed });
        }
        return sets;
    }

    private static readonly HashSet<string> VendorSuffixes = new(StringComparer.Ordinal)
    {
        "inc", "llc", "ltd", "limited", "corp", "corporation", "co", "gmbh", "ag", "sa", "srl", "bv", "oy", "ab", "as",
        "plc", "pty", "kg", "ug", "sas", "spa", "asa", "the"
    };

    /// <summary>Yayıncı adından satıcı token'ları: "Opera Software ASA" → ["operasoftwareasa", "operasoftware", "opera"].</summary>
    public static List<string> MakeVendorTokens(string? publisher, string? companyName)
    {
        var tokens = new List<string>();
        foreach (var raw in new[] { publisher, companyName })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var toks = Tokens(raw);
            if (toks.Count == 0) continue;
            var squashed = string.Concat(toks);
            if (squashed.Length >= 3) tokens.Add(squashed);
            var trimmed = toks.Where(t => !VendorSuffixes.Contains(t)).ToList();
            // "Opera Software ASA" → "operasoftware", "opera": öndeki her parça da satıcı adı olabilir
            for (var i = trimmed.Count; i >= 1; i--)
            {
                var part = string.Concat(trimmed.Take(i));
                if (part.Length < 3) continue;
                tokens.Add(part);
            }
        }
        return tokens.Distinct().ToList();
    }

    public static bool ContainsContiguous(IReadOnlyList<string> needle, IReadOnlyList<string> haystack)
    {
        if (needle.Count == 0 || needle.Count > haystack.Count) return false;
        var last = haystack.Count - needle.Count;
        for (var start = 0; start <= last; start++)
        {
            var ok = true;
            for (var i = 0; i < needle.Count; i++)
            {
                if (haystack[start + i] != needle[i]) { ok = false; break; }
            }
            if (ok) return true;
        }
        return false;
    }

    /// <summary>`name` içinde `id` + ayraçtan hemen sonra gelen bileşen; yoksa "".</summary>
    public static string ComponentAfter(string id, string name)
    {
        var idx = name.IndexOf(id, StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        var rest = name[(idx + id.Length)..];
        if (rest.Length == 0 || !IsSeparator(rest[0])) return string.Empty;
        rest = rest[1..];
        var end = 0;
        while (end < rest.Length && !IsSeparator(rest[end])) end++;
        return rest[..end];
    }

    private static bool IsSeparator(char c) => c == '.' || c == '_';

    /// <summary>"{GUID}", "notion.id", "Vendor.App_hash" kimliktir; boşluk içeren düz adlar değildir.</summary>
    public static bool LooksLikeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains(' ')) return false;
        if (value.StartsWith('{') && value.EndsWith('}')) return true;
        return value.Contains('.') || value.Contains('_');
    }

    // MARK: Eşleştirme

    /// <summary>Girdi adı bir kimliğe eşit mi veya kimlik + ayraç + kanal dışı bir ek mi?</summary>
    public bool MatchesIdentifier(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var id in Identifiers)
        {
            if (id.Length < 3) continue;
            var isMatch = n == id
                || (n.StartsWith(id, StringComparison.Ordinal) && n.Length > id.Length && IsSeparator(n[id.Length]))
                || (n.EndsWith(id, StringComparison.Ordinal) && n.Length > id.Length && IsSeparator(n[n.Length - id.Length - 1]));
            if (!isMatch) continue;
            var next = ComponentAfter(id, n);
            if (ChannelWords.Contains(next)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Verilen yolun (arama köküne göre bağıl, ör. "Google\Chrome") uygulamaya ait olup olmadığını ve
    /// eşleşme gücünü döner. Tek bileşenli adlar da aynı yoldan geçer.
    /// </summary>
    public MatchStrength? Match(string relativePath, bool isDirectory = false)
    {
        var lower = relativePath.Replace('/', '\\').ToLowerInvariant();
        var parts = lower.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var lastComponent = parts.Length > 0 ? parts[^1] : lower;

        // Windows'un kendi klasörleri: kaldırılan uygulama Microsoft'un değilse asla eşleşmez
        if (!IsMicrosoftApp && (lastComponent == "microsoft" || lastComponent == "windows" || lastComponent.StartsWith("microsoft.", StringComparison.Ordinal)))
        {
            return null;
        }

        if (MatchesIdentifier(lastComponent)) return MatchStrength.strong;

        if (NameTokenSets.Count == 0) return null;

        var pathTokens = Tokens(lower);
        if (pathTokens.Count == 0) return null;

        var lastTokens = Tokens(lastComponent);
        if (!isDirectory)
        {
            var dot = lastComponent.LastIndexOf('.');
            if (dot > 0) lastTokens = Tokens(lastComponent[..dot]);
        }

        // "Chrome Canary", "X Beta" gibi kanal türevleri ayrı uygulamadır (yolun herhangi bir yerinde)
        foreach (var token in pathTokens)
        {
            if (!ChannelWords.Contains(token)) continue;
            var termHasIt = NameTokenSets.Any(set => set.Contains(token));
            if (!termHasIt) return null;
        }

        foreach (var termTokens in NameTokenSets)
        {
            if (lastTokens.SequenceEqual(termTokens) || pathTokens.SequenceEqual(termTokens)) return MatchStrength.strong;
        }

        // Zayıf eşleşme yalnızca girdinin KENDİ adında aranır; üst klasör adları kredilendirilmez
        foreach (var termTokens in NameTokenSets)
        {
            if (ContainsContiguous(termTokens, lastTokens)) return MatchStrength.weak;
        }

        // Uzun ve özgül bir ad (≥ 8 karakter) daha uzun bir token'ın başlangıcıysa zayıf eşleşir
        foreach (var termTokens in NameTokenSets)
        {
            if (termTokens.Count != 1) continue;
            var squashed = termTokens[0];
            if (squashed.Length < MinPrefixLength) continue;
            if (lastTokens.Any(t => t.Length > squashed.Length && t.StartsWith(squashed, StringComparison.Ordinal))) return MatchStrength.weak;
        }
        return null;
    }

    /// <summary>Girdi adı satıcı adının kendisi mi? ("Google", "Opera Software")</summary>
    public bool IsVendorName(string name)
    {
        var squashed = Squash(name);
        return squashed.Length >= 3 && VendorTokens.Contains(squashed);
    }

    /// <summary>Uygulama adının kendisi satıcı adıyla aynı mı? ("Steam" / Valve değil; "Dropbox" / Dropbox evet)</summary>
    public bool NameEqualsVendor => NameTokenSets.Any(set => VendorTokens.Contains(string.Concat(set)));

    /// <summary>Rapor için okunabilir açıklama.</summary>
    public List<string> Describe()
    {
        var lines = new List<string>
        {
            "Kimlikler: " + (Identifiers.Count == 0 ? "-" : string.Join(", ", Identifiers))
        };
        if (NameTokenSets.Count == 0)
        {
            lines.Add("Ad eşleşmesi: kullanılmadı (ad genel veya 4 karakterden kısa)");
        }
        else
        {
            lines.Add("Ad token'ları: " + string.Join(", ", NameTokenSets.Select(s => "[" + string.Join(" ", s) + "]")));
        }
        lines.Add("Satıcı token'ları: " + (VendorTokens.Count == 0 ? "-" : string.Join(", ", VendorTokens)));
        lines.Add("Microsoft uygulaması: " + (IsMicrosoftApp ? "evet" : "hayır"));
        return lines;
    }

    public bool Equals(SearchTerms? other)
    {
        if (other is null) return false;
        return Identifiers.SequenceEqual(other.Identifiers)
               && NameTokenSets.Count == other.NameTokenSets.Count
               && NameTokenSets.Zip(other.NameTokenSets).All(p => p.First.SequenceEqual(p.Second))
               && VendorTokens.SequenceEqual(other.VendorTokens)
               && IsMicrosoftApp == other.IsMicrosoftApp;
    }

    public override bool Equals(object? obj) => Equals(obj as SearchTerms);
    public override int GetHashCode() => HashCode.Combine(Identifiers.Count, NameTokenSets.Count, IsMicrosoftApp);
}
