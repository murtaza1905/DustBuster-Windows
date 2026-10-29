using System.Text.Json.Serialization;

namespace DustBuster.Core;

/// <summary>Kalıntının bulunduğu konum türü.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LeftoverKind>))]
public enum LeftoverKind
{
    installFolder,
    roamingAppData,
    localAppData,
    localLowAppData,
    programData,
    package,
    temp,
    startMenu,
    shortcut,
    registryUser,
    registryMachine,
    startupEntry,
    scheduledTask,
    service,
    programFiles,
    savedGames,
    documents,
    homeDotfile,
    other
}

public static class LeftoverKindExtensions
{
    /// <summary>Arayüz etiketi.</summary>
    public static string Label(this LeftoverKind kind) => kind switch
    {
        LeftoverKind.installFolder => "Kurulum Klasörü",
        LeftoverKind.roamingAppData => "AppData\\Roaming",
        LeftoverKind.localAppData => "AppData\\Local",
        LeftoverKind.localLowAppData => "AppData\\LocalLow",
        LeftoverKind.programData => "ProgramData",
        LeftoverKind.package => "Paket Verisi (MSIX)",
        LeftoverKind.temp => "Geçici Dosyalar",
        LeftoverKind.startMenu => "Başlat Menüsü",
        LeftoverKind.shortcut => "Kısayol",
        LeftoverKind.registryUser => "Kayıt Defteri (Kullanıcı)",
        LeftoverKind.registryMachine => "Kayıt Defteri (Makine)",
        LeftoverKind.startupEntry => "Başlangıç Girdisi",
        LeftoverKind.scheduledTask => "Zamanlanmış Görev",
        LeftoverKind.service => "Hizmet",
        LeftoverKind.programFiles => "Program Files",
        LeftoverKind.savedGames => "Saved Games",
        LeftoverKind.documents => "Belgeler",
        LeftoverKind.homeDotfile => "Ev Dizini (gizli)",
        _ => "Diğer"
    };

    public static int Rank(this LeftoverKind kind) => kind switch
    {
        LeftoverKind.installFolder => 0,
        LeftoverKind.roamingAppData => 1,
        LeftoverKind.localAppData => 2,
        LeftoverKind.localLowAppData => 3,
        LeftoverKind.package => 4,
        LeftoverKind.programData => 5,
        LeftoverKind.registryUser => 6,
        LeftoverKind.registryMachine => 7,
        LeftoverKind.startupEntry => 8,
        LeftoverKind.startMenu => 9,
        LeftoverKind.shortcut => 10,
        LeftoverKind.scheduledTask => 11,
        LeftoverKind.service => 12,
        LeftoverKind.temp => 13,
        _ => 50
    };
}

/// <summary>Kaldırma işleminde bir yolun kabul edilip edilmeyeceğine ve nasıl etiketleneceğine karar verir.</summary>
public static class PathClassifier
{
    public const string RunKeyUser = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    public const string RunOnceKeyUser = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce";
    public const string RunKeyMachine = "HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    public const string RunKeyMachine32 = "HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Run";
    public const string UninstallKeyUser = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
    public const string UninstallKeyMachine = "HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
    public const string UninstallKeyMachine32 = "HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
    public const string AppPathsUser = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths";
    public const string AppPathsMachine = "HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths";
    public const string ServicesKey = "HKLM:\\SYSTEM\\CurrentControlSet\\Services";
    public const string SoftwareUser = "HKCU:\\Software";
    public const string SoftwareMachine = "HKLM:\\Software";
    public const string SoftwareMachine32 = "HKLM:\\Software\\WOW6432Node";

    /// <summary>Windows'un kendi anahtarları altında yine de kalıntı barındıran, kabul edilen alt anahtarlar.</summary>
    public static readonly string[] AllowedMicrosoftRegistryRoots =
    {
        RunKeyUser, RunOnceKeyUser, RunKeyMachine, RunKeyMachine32, UninstallKeyUser, UninstallKeyMachine, UninstallKeyMachine32,
        AppPathsUser, AppPathsMachine
    };

    /// <summary>Kullanıcı verisi taşıyan, kaldırma listesine ASLA girmeyecek AppData alt kökleri.</summary>
    public static readonly string[] ProtectedAppDataRoots =
    {
        "Microsoft\\Credentials", "Microsoft\\Crypto", "Microsoft\\Protect", "Microsoft\\Vault", "Microsoft\\SystemCertificates",
        "Microsoft\\TokenBroker", "Microsoft\\Internet Explorer"
    };

    public static LeftoverKind Kind(string path, KnownFolders folders)
    {
        var p = PathUtil.Normalize(path);
        if (PathUtil.IsRegistryPath(p))
        {
            if (PathUtil.IsUnder(p, RunKeyUser) || PathUtil.IsUnder(p, RunOnceKeyUser) || PathUtil.IsUnder(p, RunKeyMachine) || PathUtil.IsUnder(p, RunKeyMachine32))
            {
                return LeftoverKind.startupEntry;
            }
            if (PathUtil.IsUnder(p, ServicesKey)) return LeftoverKind.service;
            return p.StartsWith("HKCU:", PathUtil.Comparison) ? LeftoverKind.registryUser : LeftoverKind.registryMachine;
        }

        if (PathUtil.IsUnder(p, folders.StartMenuPrograms) || PathUtil.IsUnder(p, folders.CommonStartMenuPrograms)) return LeftoverKind.startMenu;
        if (PathUtil.IsUnder(p, folders.ScheduledTasks)) return LeftoverKind.scheduledTask;
        if (PathUtil.IsUnder(p, folders.Packages)) return LeftoverKind.package;
        if (PathUtil.IsUnder(p, folders.Temp)) return LeftoverKind.temp;
        if (PathUtil.IsUnder(p, folders.UserPrograms)) return LeftoverKind.installFolder;
        if (PathUtil.IsUnder(p, folders.LocalLowAppData)) return LeftoverKind.localLowAppData;
        if (PathUtil.IsUnder(p, folders.LocalAppData)) return LeftoverKind.localAppData;
        if (PathUtil.IsUnder(p, folders.RoamingAppData)) return LeftoverKind.roamingAppData;
        if (PathUtil.IsUnder(p, folders.ProgramData)) return LeftoverKind.programData;
        if (PathUtil.IsUnder(p, folders.ProgramFiles) || PathUtil.IsUnder(p, folders.ProgramFilesX86)) return LeftoverKind.programFiles;
        if (PathUtil.HasExtension(p, ".lnk")) return LeftoverKind.shortcut;
        if (PathUtil.IsUnder(p, folders.SavedGames)) return LeftoverKind.savedGames;
        if (PathUtil.IsUnder(p, folders.Documents)) return LeftoverKind.documents;
        var rel = PathUtil.Relative(p, folders.Home);
        if (rel != null && rel.StartsWith('.')) return LeftoverKind.homeDotfile;
        return LeftoverKind.other;
    }

    public enum Decision { accepted, rejected }

    public readonly record struct Admission(Decision Decision, MatchStrength Strength, string? Note, string? Reason)
    {
        public static Admission Accept(MatchStrength strength, string? note = null) => new(Decision.accepted, strength, note, null);
        public static Admission Reject(string reason) => new(Decision.rejected, MatchStrength.weak, null, reason);
        public bool IsAccepted => Decision == Decision.accepted;
    }

    private static bool UnderAnyAppDataRoot(string p, KnownFolders f, out string? root)
    {
        foreach (var r in new[] { f.RoamingAppData, f.LocalAppData, f.LocalLowAppData })
        {
            if (PathUtil.IsUnder(p, r)) { root = r; return true; }
        }
        root = null;
        return false;
    }

    public static Admission Admit(string path, KnownFolders folders, ScanOptions options)
    {
        var p = PathUtil.Normalize(path);

        if (PathUtil.IsRegistryPath(p)) return AdmitRegistry(p, options);

        if (p.Equals(folders.Home, PathUtil.Comparison) || PathUtil.IsRoot(p)) return Admission.Reject("ev veya kök dizin");

        if (UnderAnyAppDataRoot(p, folders, out var appDataRoot))
        {
            var rel = PathUtil.Relative(p, appDataRoot!);
            if (rel == null) return Admission.Reject("AppData kökünün kendisi");
            if (PathUtil.IsUnder(p, folders.StartMenuPrograms))
            {
                return PathUtil.Relative(p, folders.StartMenuPrograms) == null
                    ? Admission.Reject("Başlat Menüsü kökünün kendisi")
                    : Admission.Accept(MatchStrength.strong);
            }
            if (PathUtil.IsUnder(p, folders.Packages))
            {
                var inside = PathUtil.Relative(p, folders.Packages);
                return inside == null ? Admission.Reject("Packages kökünün kendisi") : Admission.Accept(MatchStrength.strong);
            }
            if (PathUtil.IsUnder(p, folders.Temp))
            {
                return PathUtil.Relative(p, folders.Temp) == null
                    ? Admission.Reject("Temp kökünün kendisi")
                    : Admission.Accept(MatchStrength.strong, "geçici klasör");
            }
            if (!rel.Contains('\\') && (rel.Equals("Programs", PathUtil.Comparison) || rel.Equals("Microsoft", PathUtil.Comparison)
                                        || rel.Equals("Packages", PathUtil.Comparison) || rel.Equals("Temp", PathUtil.Comparison)))
            {
                return Admission.Reject("AppData'nın ortak kökü: " + rel);
            }
            foreach (var protectedRoot in ProtectedAppDataRoots)
            {
                if (PathUtil.IsUnder(p, PathUtil.Join(appDataRoot!, protectedRoot)))
                {
                    return Admission.Reject("korunan Windows kullanıcı verisi: " + protectedRoot);
                }
            }
            var top = rel.Split('\\')[0];
            if (top.Equals("Microsoft", PathUtil.Comparison) && !PathUtil.IsUnder(p, PathUtil.Join(appDataRoot!, "Microsoft\\Windows\\Start Menu")))
            {
                var second = rel.Split('\\').Length > 1 ? rel.Split('\\')[1] : string.Empty;
                if (second.Equals("Windows", PathUtil.Comparison)) return Admission.Reject("Windows'un kendi klasörü: Microsoft\\Windows");
            }
            return Admission.Accept(MatchStrength.strong);
        }

        if (PathUtil.IsUnder(p, folders.ProgramData))
        {
            var rel = PathUtil.Relative(p, folders.ProgramData);
            if (rel == null) return Admission.Reject("ProgramData kökünün kendisi");
            if (PathUtil.IsUnder(p, folders.CommonStartMenuPrograms))
            {
                return PathUtil.Relative(p, folders.CommonStartMenuPrograms) == null
                    ? Admission.Reject("Başlat Menüsü kökünün kendisi")
                    : Admission.Accept(MatchStrength.strong, "tüm kullanıcılar için ortak");
            }
            var top = rel.Split('\\')[0];
            if (top.Equals("Microsoft", PathUtil.Comparison)) return Admission.Reject("Windows'un kendi alanı: ProgramData\\Microsoft");
            if (top.Equals("Package Cache", PathUtil.Comparison) || top.Equals("Packages", PathUtil.Comparison))
            {
                return Admission.Reject("Windows Installer paket önbelleği");
            }
            return Admission.Accept(MatchStrength.strong, "tüm kullanıcılar için ortak — yönetici gerekebilir");
        }

        if (PathUtil.IsUnder(p, folders.ProgramFiles) || PathUtil.IsUnder(p, folders.ProgramFilesX86))
        {
            var root = PathUtil.IsUnder(p, folders.ProgramFiles) ? folders.ProgramFiles : folders.ProgramFilesX86;
            var rel = PathUtil.Relative(p, root);
            if (rel == null) return Admission.Reject("Program Files kökünün kendisi");
            var top = rel.Split('\\')[0];
            if (top.Equals("Common Files", PathUtil.Comparison) || top.Equals("WindowsApps", PathUtil.Comparison)
                || top.Equals("Windows Defender", PathUtil.Comparison) || top.Equals("Microsoft", PathUtil.Comparison)
                || top.Equals("Windows NT", PathUtil.Comparison) || top.Equals("Internet Explorer", PathUtil.Comparison)
                || top.Equals("Windows Mail", PathUtil.Comparison) || top.Equals("Windows Media Player", PathUtil.Comparison)
                || top.Equals("Windows Photo Viewer", PathUtil.Comparison) || top.Equals("ModifiableWindowsApps", PathUtil.Comparison)
                || top.Equals("dotnet", PathUtil.Comparison) || top.Equals("PowerShell", PathUtil.Comparison))
            {
                return Admission.Reject("Windows bileşeni veya ortak alan: " + top);
            }
            if (!options.IncludeSystemLocations) return Admission.Reject("sistem konumu (Program Files) — 'Sistem konumlarını da tara' kapalı");
            return Admission.Accept(MatchStrength.strong, "yönetici yetkisi gerekir");
        }

        if (PathUtil.IsUnder(p, folders.Windows))
        {
            if (PathUtil.IsUnder(p, folders.ScheduledTasks))
            {
                if (!options.IncludeSystemLocations) return Admission.Reject("zamanlanmış görev — 'Sistem konumlarını da tara' kapalı");
                return Admission.Accept(MatchStrength.strong, "zamanlanmış görev, yönetici yetkisi gerekir");
            }
            return Admission.Reject("Windows sistem klasörü");
        }

        if (PathUtil.IsUnder(p, folders.Desktop) || PathUtil.IsUnder(p, folders.PublicDesktop))
        {
            if (PathUtil.HasExtension(p, ".lnk")) return Admission.Accept(MatchStrength.strong, "masaüstü kısayolu");
            return Admission.Reject("masaüstündeki kullanıcı dosyası");
        }

        if (PathUtil.IsUnder(p, folders.SavedGames))
        {
            return PathUtil.Relative(p, folders.SavedGames) == null
                ? Admission.Reject("Saved Games kökünün kendisi")
                : Admission.Accept(MatchStrength.weak, "kayıtlı oyun verisi — kontrol et");
        }

        if (PathUtil.IsUnder(p, folders.Documents))
        {
            var rel = PathUtil.Relative(p, folders.Documents);
            if (rel != null && rel.StartsWith("My Games\\", PathUtil.Comparison) && rel.Split('\\').Length >= 2)
            {
                return Admission.Accept(MatchStrength.weak, "Belgeler\\My Games altında — kontrol et");
            }
            return Admission.Reject("kullanıcı verisi (Belgeler)");
        }

        if (PathUtil.IsUnder(p, folders.Home))
        {
            var rel = PathUtil.Relative(p, folders.Home);
            if (rel != null && rel.StartsWith('.') && !rel.Contains('\\'))
            {
                return Admission.Accept(MatchStrength.weak, "ev dizininde gizli klasör — kontrol et");
            }
            return Admission.Reject("kullanıcı verisi (Belgeler, Masaüstü vb.)");
        }

        if (PathUtil.IsUnder(p, folders.Public))
        {
            return Admission.Reject("tüm kullanıcılara ortak kullanıcı verisi");
        }

        return Admission.Reject("kapsam dışı kök");
    }

    private static Admission AdmitRegistry(string p, ScanOptions options)
    {
        var isUser = p.StartsWith("HKCU:", PathUtil.Comparison);
        var isMachine = p.StartsWith("HKLM:", PathUtil.Comparison);
        if (!isUser && !isMachine) return Admission.Reject("desteklenmeyen kayıt defteri kökü");

        foreach (var allowed in AllowedMicrosoftRegistryRoots)
        {
            if (PathUtil.IsUnder(p, allowed))
            {
                if (PathUtil.PathEquals(p, allowed)) return Admission.Reject("Windows anahtarının kendisi");
                if (isMachine && !options.IncludeSystemLocations) return Admission.Reject("HKLM — 'Sistem konumlarını da tara' kapalı");
                return Admission.Accept(MatchStrength.strong, isMachine ? "yönetici yetkisi gerekir" : null);
            }
        }

        if (PathUtil.IsUnder(p, ServicesKey))
        {
            if (PathUtil.PathEquals(p, ServicesKey)) return Admission.Reject("Services anahtarının kendisi");
            if (!options.IncludeSystemLocations) return Admission.Reject("hizmet — 'Sistem konumlarını da tara' kapalı");
            return Admission.Accept(MatchStrength.strong, "hizmet kaydı, yönetici yetkisi gerekir");
        }

        var softwareRoots = isUser ? new[] { SoftwareUser } : new[] { SoftwareMachine32, SoftwareMachine };
        foreach (var root in softwareRoots)
        {
            if (!PathUtil.IsUnder(p, root)) continue;
            var rel = PathUtil.Relative(p, root);
            if (rel == null) return Admission.Reject("Software kökünün kendisi");
            var top = rel.Split('\\')[0];
            if (top.Equals("Microsoft", PathUtil.Comparison)) return Admission.Reject("Windows'un kendi anahtarı: Software\\Microsoft");
            if (top.Equals("Classes", PathUtil.Comparison)) return Admission.Reject("dosya ilişkilendirmeleri (Classes)");
            if (top.Equals("Policies", PathUtil.Comparison)) return Admission.Reject("grup ilkesi anahtarı");
            if (top.Equals("WOW6432Node", PathUtil.Comparison) && rel.Split('\\').Length == 1) return Admission.Reject("WOW6432Node kökünün kendisi");
            if (isMachine && !options.IncludeSystemLocations) return Admission.Reject("HKLM — 'Sistem konumlarını da tara' kapalı");
            return Admission.Accept(MatchStrength.strong, isMachine ? "yönetici yetkisi gerekir" : null);
        }
        return Admission.Reject("kapsam dışı kayıt defteri anahtarı");
    }
}
