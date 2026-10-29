namespace DustBuster.Core;

/// <summary>
/// Tarama için gereken bilinen klasörler. Gerçek makinede ortamdan, testlerde sabit bir ev dizininden
/// türetilir. Kurallar ve test vakaları yolları "%LOCALAPPDATA%\…" gibi belirteçlerle yazar; bu sınıf
/// belirteçleri açar ve kapatır.
/// </summary>
public sealed class KnownFolders
{
    public string Home { get; }
    public string RoamingAppData { get; }
    public string LocalAppData { get; }
    public string LocalLowAppData { get; }
    public string ProgramData { get; }
    public string ProgramFiles { get; }
    public string ProgramFilesX86 { get; }
    public string Windows { get; }
    public string Temp { get; }
    public string Public { get; }
    public string SystemDrive { get; }

    public KnownFolders(string home, string roamingAppData, string localAppData, string localLowAppData, string programData,
                        string programFiles, string programFilesX86, string windows, string temp, string publicFolder, string systemDrive)
    {
        Home = PathUtil.Normalize(home);
        RoamingAppData = PathUtil.Normalize(roamingAppData);
        LocalAppData = PathUtil.Normalize(localAppData);
        LocalLowAppData = PathUtil.Normalize(localLowAppData);
        ProgramData = PathUtil.Normalize(programData);
        ProgramFiles = PathUtil.Normalize(programFiles);
        ProgramFilesX86 = PathUtil.Normalize(programFilesX86);
        Windows = PathUtil.Normalize(windows);
        Temp = PathUtil.Normalize(temp);
        Public = PathUtil.Normalize(publicFolder);
        SystemDrive = PathUtil.Normalize(systemDrive);
    }

    /// <summary>Test vakaları için: ev dizininden standart düzen ("C:\Users\test" → AppData\Roaming vb.).</summary>
    public static KnownFolders ForHome(string home)
    {
        var h = PathUtil.Normalize(home);
        var drive = PathUtil.Root(h);
        return new KnownFolders(
            home: h,
            roamingAppData: PathUtil.Join(h, "AppData\\Roaming"),
            localAppData: PathUtil.Join(h, "AppData\\Local"),
            localLowAppData: PathUtil.Join(h, "AppData\\LocalLow"),
            programData: PathUtil.Join(drive, "ProgramData"),
            programFiles: PathUtil.Join(drive, "Program Files"),
            programFilesX86: PathUtil.Join(drive, "Program Files (x86)"),
            windows: PathUtil.Join(drive, "Windows"),
            temp: PathUtil.Join(h, "AppData\\Local\\Temp"),
            publicFolder: PathUtil.Join(PathUtil.Join(drive, "Users"), "Public"),
            systemDrive: drive);
    }

    /// <summary>Gerçek makine: Environment'tan okur.</summary>
    public static KnownFolders FromEnvironment()
    {
        string Get(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var home = Get(Environment.SpecialFolder.UserProfile);
        var local = Get(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Get(Environment.SpecialFolder.ApplicationData);
        var localLow = Environment.GetEnvironmentVariable("LOCALAPPDATALOW")
                       ?? PathUtil.Join(home, "AppData\\LocalLow");
        var programData = Get(Environment.SpecialFolder.CommonApplicationData);
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Get(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? Get(Environment.SpecialFolder.ProgramFilesX86);
        var windows = Get(Environment.SpecialFolder.Windows);
        var temp = Path.GetTempPath();
        var publicFolder = Environment.GetEnvironmentVariable("PUBLIC") ?? PathUtil.Join(PathUtil.Root(home) + "Users", "Public");
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") is { Length: > 0 } sd ? sd + "\\" : PathUtil.Root(windows);
        return new KnownFolders(home, roaming, local, localLow, programData, programFiles, programFilesX86, windows, temp, publicFolder, systemDrive);
    }

    private IEnumerable<(string Token, string Path)> Tokens()
    {
        yield return ("%LOCALAPPDATA%", LocalAppData);
        yield return ("%LOCALLOW%", LocalLowAppData);
        yield return ("%APPDATA%", RoamingAppData);
        yield return ("%PROGRAMDATA%", ProgramData);
        yield return ("%PROGRAMFILES(X86)%", ProgramFilesX86);
        yield return ("%PROGRAMFILES%", ProgramFiles);
        yield return ("%WINDIR%", Windows);
        yield return ("%TEMP%", Temp);
        yield return ("%PUBLIC%", Public);
        yield return ("%USERPROFILE%", Home);
        yield return ("%SYSTEMDRIVE%", SystemDrive);
    }

    /// <summary>"~" ve "%BELİRTEÇ%" biçimlerini gerçek yola açar; diğer yollar yalnızca normalize edilir.</summary>
    public string Expand(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var p = raw.Replace('/', '\\');
        if (p == "~") return Home;
        if (p.StartsWith("~\\", StringComparison.Ordinal)) return PathUtil.Normalize(Home + p[1..]);
        if (p.StartsWith('%'))
        {
            foreach (var (token, path) in Tokens())
            {
                if (p.StartsWith(token, PathUtil.Comparison))
                {
                    var rest = p[token.Length..];
                    if (rest.Length == 0) return path;
                    if (rest.StartsWith('\\')) return PathUtil.Normalize(path + rest);
                }
            }
        }
        return PathUtil.Normalize(p);
    }

    /// <summary>Gerçek yolu en özgül belirtece kapatır (rapor ve test vakası dışa aktarımı için).</summary>
    public string Contract(string path)
    {
        var p = PathUtil.Normalize(path);
        // En uzun kök önce: LocalAppData, Home'un altındadır
        var ordered = Tokens().Where(t => t.Token != "%SYSTEMDRIVE%").OrderByDescending(t => t.Path.Length).ToList();
        foreach (var (token, root) in ordered)
        {
            if (string.Equals(p, root, PathUtil.Comparison)) return token;
            var rel = PathUtil.Relative(p, root);
            if (rel != null) return token + "\\" + rel;
        }
        return p;
    }

    /// <summary>Komut satırı gibi serbest metin içindeki gerçek yolları belirteçlere çevirir.</summary>
    public string ContractEmbedded(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = text;
        var ordered = Tokens().Where(t => t.Token != "%SYSTEMDRIVE%").OrderByDescending(t => t.Path.Length).ToList();
        foreach (var (token, root) in ordered)
        {
            var idx = result.IndexOf(root, PathUtil.Comparison);
            while (idx >= 0)
            {
                result = result[..idx] + token + result[(idx + root.Length)..];
                idx = result.IndexOf(root, PathUtil.Comparison);
            }
        }
        return result;
    }

    public string StartMenuPrograms => PathUtil.Join(RoamingAppData, "Microsoft\\Windows\\Start Menu\\Programs");
    public string CommonStartMenuPrograms => PathUtil.Join(ProgramData, "Microsoft\\Windows\\Start Menu\\Programs");
    public string Desktop => PathUtil.Join(Home, "Desktop");
    public string PublicDesktop => PathUtil.Join(Public, "Desktop");
    public string Packages => PathUtil.Join(LocalAppData, "Packages");
    public string UserPrograms => PathUtil.Join(LocalAppData, "Programs");
    public string ScheduledTasks => PathUtil.Join(Windows, "System32\\Tasks");
    public string SavedGames => PathUtil.Join(Home, "Saved Games");
    public string Documents => PathUtil.Join(Home, "Documents");
}
