using DustBuster.Core;
using Xunit;

namespace DustBuster.Core.Tests;

public class PathUtilTests
{
    [Fact]
    public void NormalizeAndRoots()
    {
        Assert.Equal("C:\\Users\\test", PathUtil.Normalize("C:/Users/test/"));
        Assert.Equal("C:\\", PathUtil.Normalize("C:\\"));
        Assert.Equal("C:\\", PathUtil.Root("C:\\Users\\test"));
        Assert.Equal("HKCU:", PathUtil.Root("HKCU:\\Software\\Notion"));
        Assert.True(PathUtil.IsRoot("C:\\"));
        Assert.True(PathUtil.IsRoot("HKLM:"));
        Assert.False(PathUtil.IsRoot("C:\\Users"));
        Assert.Equal("C:\\", PathUtil.Parent("C:\\Users"));
        Assert.Null(PathUtil.Parent("C:\\"));
        Assert.Equal("HKCU:\\Software", PathUtil.Parent("HKCU:\\Software\\Notion"));
        Assert.Equal("Notion", PathUtil.LastComponent("HKCU:\\Software\\Notion"));
    }

    [Fact]
    public void RelativeAndUnderAreCaseInsensitive()
    {
        Assert.True(PathUtil.IsUnder("c:\\users\\TEST\\AppData", "C:\\Users\\test"));
        Assert.True(PathUtil.IsUnder("C:\\Users\\test", "C:\\Users\\test"));
        Assert.False(PathUtil.IsUnder("C:\\Users\\test2", "C:\\Users\\test"));
        Assert.Equal("AppData\\Local", PathUtil.Relative("C:\\Users\\test\\AppData\\Local", "c:\\users\\test"));
        Assert.Null(PathUtil.Relative("C:\\Users\\test", "C:\\Users\\test"));
        Assert.True(PathUtil.IsRegistryPath("HKLM:\\SYSTEM"));
        Assert.False(PathUtil.IsRegistryPath("C:\\HKLM"));
    }
}

public class KnownFoldersTests
{
    [Fact]
    public void ExpandAndContractRoundTrip()
    {
        var f = KnownFolders.ForHome("C:\\Users\\test");
        Assert.Equal("C:\\Users\\test\\AppData\\Local\\Google", f.Expand("%LOCALAPPDATA%\\Google"));
        Assert.Equal("C:\\Users\\test\\AppData\\Roaming", f.Expand("%APPDATA%"));
        Assert.Equal("C:\\ProgramData\\X", f.Expand("%PROGRAMDATA%/X"));
        Assert.Equal("C:\\Users\\test\\.docker", f.Expand("~\\.docker"));
        Assert.Equal("C:\\Program Files (x86)\\Google", f.Expand("%PROGRAMFILES(X86)%\\Google"));
        Assert.Equal("%LOCALAPPDATA%\\Google", f.Contract("C:\\Users\\test\\AppData\\Local\\Google"));
        Assert.Equal("%TEMP%\\x.tmp", f.Contract("C:\\Users\\test\\AppData\\Local\\Temp\\x.tmp"));
        Assert.Equal("%USERPROFILE%\\Desktop\\a.lnk", f.Contract("C:\\Users\\test\\Desktop\\a.lnk"));
        Assert.Equal("%PROGRAMFILES(X86)%\\Google", f.Contract("C:\\Program Files (x86)\\Google"));
        Assert.Equal("HKCU:\\Software\\X", f.Contract("HKCU:\\Software\\X"));
        Assert.Equal("\"%LOCALAPPDATA%\\Programs\\Notion\\Notion.exe\" --hidden",
            f.ContractEmbedded("\"C:\\Users\\test\\AppData\\Local\\Programs\\Notion\\Notion.exe\" --hidden"));
    }
}

public class GlobTests
{
    [Fact]
    public void WildcardMatching()
    {
        Assert.True(Glob.Matches("com.google.*", "com.google.Chrome"));
        Assert.True(Glob.Matches("*cache*", "GPUCache"));
        Assert.True(Glob.Matches("thumbcache_?.db", "thumbcache_1.db"));
        Assert.False(Glob.Matches("thumbcache_?.db", "thumbcache_12.db"));
        Assert.False(Glob.Matches("Notion*", "Nation"));
    }

    [Fact]
    public void ExpandWalksVirtualFileSystem()
    {
        var folders = KnownFolders.ForHome("C:\\Users\\test");
        var fs = new VirtualFileSystem(new[]
        {
            "C:\\Users\\test\\AppData\\Local\\Packages\\A.App_x\\AC\\INetCache\\",
            "C:\\Users\\test\\AppData\\Local\\Packages\\B.App_y\\AC\\INetCache\\",
            "C:\\Users\\test\\AppData\\Local\\Packages\\C.App_z\\AC\\Temp\\",
            "HKCU:\\Software\\Vendor\\AppOne\\",
            "HKCU:\\Software\\Vendor\\AppTwo\\"
        });
        var hits = Glob.Expand("%LOCALAPPDATA%\\Packages\\*\\AC\\INetCache", folders, fs);
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.EndsWith("AC\\INetCache", h));
        Assert.Single(Glob.Expand("HKCU:\\Software\\Vendor\\AppO*", folders, fs));
        Assert.Empty(Glob.Expand("%LOCALAPPDATA%\\Packages\\*", folders, fs));
        Assert.Single(Glob.Expand("HKCU:\\Software\\Vendor\\AppOne", folders, fs));
        Assert.Empty(Glob.Expand("HKCU:\\Software\\Vendor\\Missing", folders, fs));
    }
}

public class VirtualFileSystemTests
{
    [Fact]
    public void DirectoriesFilesAndCanonicalCase()
    {
        var fs = new VirtualFileSystem(new[] { "C:\\Users\\test\\AppData\\Roaming\\Notion\\", "C:\\Users\\test\\Desktop\\Notion.lnk" });
        Assert.True(fs.IsDirectory("c:\\users\\test\\appdata\\roaming\\notion"));
        Assert.True(fs.Exists("C:\\Users\\test\\Desktop\\notion.lnk"));
        Assert.False(fs.IsDirectory("C:\\Users\\test\\Desktop\\Notion.lnk"));
        Assert.Equal("C:\\Users\\test\\AppData\\Roaming\\Notion", fs.Canonical("c:\\users\\test\\appdata\\roaming\\notion"));
        Assert.Equal(new[] { "AppData", "Desktop" }, fs.Children("C:\\Users\\test"));
        Assert.Null(fs.Children("C:\\Users\\nobody"));
        Assert.True(fs.IsEffectivelyEmptyDirectory("C:\\Users\\test\\AppData\\Roaming\\Notion"));
        Assert.False(fs.IsEffectivelyEmptyDirectory("C:\\Users\\test\\Desktop"));
    }

    [Fact]
    public void RegistryKeysBehaveLikeDirectoriesAndValuesLikeFiles()
    {
        var fs = new VirtualFileSystem(new[] { "HKCU:\\Software\\Notion\\" },
            runCommands: new Dictionary<string, string> { ["HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\Notion"] = "C:\\x\\Notion.exe --hidden" });
        Assert.True(fs.IsDirectory("HKCU:\\Software\\Notion"));
        Assert.True(fs.IsDirectory("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run"));
        Assert.False(fs.IsDirectory("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\Notion"));
        Assert.True(fs.Exists("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\Notion"));
        Assert.Equal("C:\\x\\Notion.exe --hidden", fs.RunCommand("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\Notion"));
        Assert.Equal(new[] { "Notion" }, fs.Children("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run"));
    }
}

public class CommandLineTests
{
    [Fact]
    public void ExtractsExecutableFromCommands()
    {
        var f = KnownFolders.ForHome("C:\\Users\\test");
        Assert.Equal("C:\\Program Files\\X\\x.exe", CommandLine.ExecutablePath("\"C:\\Program Files\\X\\x.exe\" --flag", f));
        Assert.Equal("C:\\Users\\test\\AppData\\Local\\Discord\\Update.exe", CommandLine.ExecutablePath("%LOCALAPPDATA%\\Discord\\Update.exe --processStart Discord.exe", f));
        Assert.Equal("C:\\Windows\\System32\\svchost.exe", CommandLine.ExecutablePath("%SystemRoot%\\System32\\svchost.exe -k netsvcs", f));
        Assert.Equal("C:\\Windows\\System32\\drivers\\x.sys", CommandLine.ExecutablePath("\\SystemRoot\\System32\\drivers\\x.sys", f));
        Assert.Equal("C:\\Windows\\System32\\drivers\\y.sys", CommandLine.ExecutablePath("system32\\drivers\\y.sys", f));
        Assert.Null(CommandLine.ExecutablePath("   ", f));
    }
}

public class PathClassifierTests
{
    private static readonly KnownFolders F = KnownFolders.ForHome("C:\\Users\\test");
    private static readonly ScanOptions UserOnly = new();
    private static readonly ScanOptions System = new() { IncludeSystemLocations = true };

    [Fact]
    public void KindsFollowKnownFolders()
    {
        Assert.Equal(LeftoverKind.roamingAppData, PathClassifier.Kind(F.Expand("%APPDATA%\\Notion"), F));
        Assert.Equal(LeftoverKind.localAppData, PathClassifier.Kind(F.Expand("%LOCALAPPDATA%\\Notion"), F));
        Assert.Equal(LeftoverKind.package, PathClassifier.Kind(F.Expand("%LOCALAPPDATA%\\Packages\\A_b"), F));
        Assert.Equal(LeftoverKind.temp, PathClassifier.Kind(F.Expand("%TEMP%\\x"), F));
        Assert.Equal(LeftoverKind.installFolder, PathClassifier.Kind(F.Expand("%LOCALAPPDATA%\\Programs\\Notion"), F));
        Assert.Equal(LeftoverKind.startMenu, PathClassifier.Kind(F.Expand("%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\Notion.lnk"), F));
        Assert.Equal(LeftoverKind.startMenu, PathClassifier.Kind(F.Expand("%PROGRAMDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\X"), F));
        Assert.Equal(LeftoverKind.shortcut, PathClassifier.Kind(F.Expand("%USERPROFILE%\\Desktop\\Notion.lnk"), F));
        Assert.Equal(LeftoverKind.registryUser, PathClassifier.Kind("HKCU:\\Software\\Notion", F));
        Assert.Equal(LeftoverKind.registryMachine, PathClassifier.Kind("HKLM:\\Software\\Notion", F));
        Assert.Equal(LeftoverKind.startupEntry, PathClassifier.Kind(PathClassifier.RunKeyUser + "\\Notion", F));
        Assert.Equal(LeftoverKind.service, PathClassifier.Kind(PathClassifier.ServicesKey + "\\svc", F));
        Assert.Equal(LeftoverKind.scheduledTask, PathClassifier.Kind(F.Expand("%WINDIR%\\System32\\Tasks\\X"), F));
        Assert.Equal(LeftoverKind.programFiles, PathClassifier.Kind(F.Expand("%PROGRAMFILES%\\X"), F));
        Assert.Equal(LeftoverKind.homeDotfile, PathClassifier.Kind(F.Expand("%USERPROFILE%\\.docker"), F));
        Assert.Equal(LeftoverKind.programData, PathClassifier.Kind(F.Expand("%PROGRAMDATA%\\X"), F));
    }

    [Fact]
    public void AppDataIsAcceptedExceptProtectedWindowsAreas()
    {
        Assert.True(PathClassifier.Admit(F.Expand("%APPDATA%\\Notion"), F, UserOnly).IsAccepted);
        Assert.True(PathClassifier.Admit(F.Expand("%LOCALAPPDATA%\\Packages\\A_b"), F, UserOnly).IsAccepted);
        Assert.True(PathClassifier.Admit(F.Expand("%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\X.lnk"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%APPDATA%\\Microsoft\\Windows\\Recent\\x.lnk"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%APPDATA%\\Microsoft\\Credentials\\x"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%LOCALAPPDATA%"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%LOCALAPPDATA%\\Packages"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%LOCALAPPDATA%\\Programs"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%TEMP%"), F, UserOnly).IsAccepted);
        Assert.True(PathClassifier.Admit(F.Expand("%TEMP%\\setup.exe"), F, UserOnly).IsAccepted);
    }

    [Fact]
    public void SystemLocationsRequireTheOption()
    {
        Assert.False(PathClassifier.Admit(F.Expand("%PROGRAMFILES%\\X"), F, UserOnly).IsAccepted);
        var pf = PathClassifier.Admit(F.Expand("%PROGRAMFILES%\\X"), F, System);
        Assert.True(pf.IsAccepted);
        Assert.NotNull(pf.Note);
        Assert.False(PathClassifier.Admit(F.Expand("%PROGRAMFILES%\\WindowsApps\\X"), F, System).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%PROGRAMFILES%\\Common Files\\X"), F, System).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%WINDIR%\\System32\\x.dll"), F, System).IsAccepted);
        Assert.True(PathClassifier.Admit(F.Expand("%WINDIR%\\System32\\Tasks\\X"), F, System).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%WINDIR%\\System32\\Tasks\\X"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%PROGRAMDATA%\\Microsoft\\X"), F, System).IsAccepted);
        Assert.True(PathClassifier.Admit(F.Expand("%PROGRAMDATA%\\Vendor\\X"), F, UserOnly).IsAccepted);
    }

    [Fact]
    public void UserDataIsNeverAccepted()
    {
        Assert.False(PathClassifier.Admit(F.Expand("%USERPROFILE%\\Documents\\Notion"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(F.Expand("%USERPROFILE%\\Desktop\\Notion"), F, UserOnly).IsAccepted);
        Assert.True(PathClassifier.Admit(F.Expand("%USERPROFILE%\\Desktop\\Notion.lnk"), F, UserOnly).IsAccepted);
        Assert.Equal(MatchStrength.weak, PathClassifier.Admit(F.Expand("%USERPROFILE%\\.docker"), F, UserOnly).Strength);
        Assert.Equal(MatchStrength.weak, PathClassifier.Admit(F.Expand("%USERPROFILE%\\Saved Games\\Game"), F, UserOnly).Strength);
        Assert.Equal(MatchStrength.weak, PathClassifier.Admit(F.Expand("%USERPROFILE%\\Documents\\My Games\\Game"), F, UserOnly).Strength);
        Assert.False(PathClassifier.Admit(F.Expand("%USERPROFILE%"), F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit("D:\\Games\\X", F, UserOnly).IsAccepted);
    }

    [Fact]
    public void RegistryRules()
    {
        Assert.True(PathClassifier.Admit("HKCU:\\Software\\Notion", F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit("HKCU:\\Software", F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer", F, UserOnly).IsAccepted);
        Assert.True(PathClassifier.Admit(PathClassifier.RunKeyUser + "\\Notion", F, UserOnly).IsAccepted);
        Assert.True(PathClassifier.Admit(PathClassifier.UninstallKeyUser + "\\Notion", F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit(PathClassifier.UninstallKeyUser, F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit("HKCU:\\Software\\Classes\\notion", F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit("HKLM:\\Software\\Notion", F, UserOnly).IsAccepted);
        Assert.True(PathClassifier.Admit("HKLM:\\Software\\Notion", F, System).IsAccepted);
        Assert.True(PathClassifier.Admit("HKLM:\\Software\\WOW6432Node\\Notion", F, System).IsAccepted);
        Assert.False(PathClassifier.Admit("HKLM:\\Software\\WOW6432Node", F, System).IsAccepted);
        Assert.True(PathClassifier.Admit(PathClassifier.ServicesKey + "\\svc", F, System).IsAccepted);
        Assert.False(PathClassifier.Admit(PathClassifier.ServicesKey + "\\svc", F, UserOnly).IsAccepted);
        Assert.False(PathClassifier.Admit("HKCR:\\.txt", F, System).IsAccepted);
    }
}

public class CleanupPolicyTests
{
    private static readonly KnownFolders F = KnownFolders.ForHome("C:\\Users\\test");

    private static CleanupPolicy MakePolicy(params string[] extraPaths)
    {
        var paths = new List<string>
        {
            "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Local State",
            "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Cache\\",
            "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Code Cache\\",
            "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\History",
            "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Profile 1\\Cache\\",
            "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\ShaderCache\\",
            "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Local State",
            "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Cache\\",
            "%APPDATA%\\Opera Software\\Opera Stable\\Local State",
            "%LOCALAPPDATA%\\Opera Software\\Opera Stable\\Cache\\",
            "%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles\\abcd.default-release\\cache2\\",
            "%APPDATA%\\discord\\Cache\\",
            "%APPDATA%\\discord\\Code Cache\\",
            "%APPDATA%\\discord\\Local Storage\\",
            "%APPDATA%\\Slack\\logs\\",
            "%LOCALAPPDATA%\\Microsoft\\Teams\\Cache\\",
            "%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache\\x.dat",
            "%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer\\thumbcache_32.db",
            "%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer\\iconcache_16.db",
            "%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer\\other.txt",
            "%TEMP%\\setup123\\",
            "%TEMP%\\log.txt",
            "%LOCALAPPDATA%\\CrashDumps\\app.dmp",
            "%LOCALAPPDATA%\\Packages\\A.App_x\\AC\\INetCache\\",
            "%LOCALAPPDATA%\\NVIDIA\\DXCache\\x.bin",
            "%WINDIR%\\Temp\\a.tmp",
            "%WINDIR%\\SoftwareDistribution\\Download\\x\\"
        };
        paths.AddRange(extraPaths);
        var fs = new VirtualFileSystem(paths.Select(p => F.Expand(p) + (p.EndsWith('\\') ? "\\" : string.Empty)));
        return new CleanupPolicy(CleanupRules.Bundled()!, F, fs);
    }

    [Fact]
    public void DetectsChromiumBrowsersIncludingSplitProfileRoots()
    {
        var policy = MakePolicy();
        var caches = policy.DetectChromiumCaches();
        Assert.Contains(caches, c => c.EndsWith("Google\\Chrome\\User Data\\Default\\Cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(caches, c => c.EndsWith("Google\\Chrome\\User Data\\Default\\Code Cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(caches, c => c.EndsWith("Google\\Chrome\\User Data\\Profile 1\\Cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(caches, c => c.EndsWith("Google\\Chrome\\User Data\\ShaderCache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(caches, c => c.EndsWith("Microsoft\\Edge\\User Data\\Default\\Cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(caches, c => c.EndsWith("Opera Software\\Opera Stable\\Cache", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(caches, c => c.EndsWith("History", StringComparison.OrdinalIgnoreCase));
        var labels = policy.BrowserCandidates().Select(c => c.Label).ToList();
        Assert.Contains("Google Chrome Cache", labels);
        Assert.Contains("Google Chrome (Profile 1) Cache", labels);
        Assert.Contains("Microsoft Edge Cache", labels);
        Assert.Contains("Opera Cache", labels);
        Assert.Contains("Firefox Cache", labels);
    }

    [Fact]
    public void RootCandidatesFollowModes()
    {
        var policy = MakePolicy();
        policy.DetectChromiumCaches();
        var roots = policy.RootCandidates();
        Assert.Contains(roots, c => c.Path.EndsWith("Temp\\setup123", StringComparison.OrdinalIgnoreCase) && c.Category == CleanupCategory.temp && !c.IsSystem);
        Assert.Contains(roots, c => c.Path.EndsWith("Temp\\log.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(roots, c => c.Path.EndsWith("\\CrashDumps", StringComparison.OrdinalIgnoreCase) && c.Category == CleanupCategory.log);
        Assert.Contains(roots, c => c.Path.EndsWith("Explorer\\thumbcache_32.db", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(roots, c => c.Path.EndsWith("Explorer\\iconcache_16.db", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(roots, c => c.Path.EndsWith("Explorer\\other.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(roots, c => c.Path.EndsWith("A.App_x\\AC\\INetCache", StringComparison.OrdinalIgnoreCase) && c.Label!.Contains("A.App"));
        Assert.Contains(roots, c => c.Path.EndsWith("NVIDIA\\DXCache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(roots, c => c.Path.EndsWith("Windows\\Temp\\a.tmp", StringComparison.OrdinalIgnoreCase) && c.IsSystem);
        Assert.Contains(roots, c => c.Path.EndsWith("SoftwareDistribution\\Download", StringComparison.OrdinalIgnoreCase) && c.IsSystem && c.Category == CleanupCategory.systemCache);
        Assert.DoesNotContain(roots, c => c.Path.EndsWith("\\Explorer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppCachesAreFoundButBrowsersAndUserDataAreNot()
    {
        var policy = MakePolicy();
        policy.DetectChromiumCaches();
        var caches = policy.AppCacheCandidates();
        Assert.Contains(caches, c => c.Path.EndsWith("discord\\Cache", StringComparison.OrdinalIgnoreCase) && c.Label == "discord › Cache" && c.Category == CleanupCategory.appCache);
        Assert.Contains(caches, c => c.Path.EndsWith("discord\\Code Cache", StringComparison.OrdinalIgnoreCase) && c.Label == "discord › Code Cache");
        Assert.Contains(caches, c => c.Path.EndsWith("Slack\\logs", StringComparison.OrdinalIgnoreCase) && c.Category == CleanupCategory.log);
        Assert.Contains(caches, c => c.Path.EndsWith("Microsoft\\Teams\\Cache", StringComparison.OrdinalIgnoreCase) && c.Label == "Microsoft › Teams › Cache");
        Assert.DoesNotContain(caches, c => c.Path.Contains("Local Storage", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(caches, c => c.Path.Contains("User Data", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(caches, c => c.Path.Contains("Opera Stable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(caches, c => c.Path.Contains("INetCache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerdictBlocksSensitiveAndForbiddenPaths()
    {
        var policy = MakePolicy();
        policy.DetectChromiumCaches();
        Assert.Null(policy.Verdict(F.Expand("%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Cache")));
        Assert.Null(policy.Verdict(F.Expand("%TEMP%\\setup123")));
        Assert.Null(policy.Verdict(F.Expand("%APPDATA%\\discord\\Cache")));
        Assert.NotNull(policy.Verdict(F.Expand("%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\History")));
        Assert.NotNull(policy.Verdict(F.Expand("%APPDATA%\\discord\\Local Storage")));
        Assert.NotNull(policy.Verdict(F.Expand("%USERPROFILE%\\Documents\\x")));
        Assert.NotNull(policy.Verdict(F.Expand("%WINDIR%\\System32\\x.dll")));
        Assert.NotNull(policy.Verdict(F.Expand("%LOCALAPPDATA%")));
        Assert.NotNull(policy.Verdict("HKCU:\\Software\\X"));
        Assert.NotNull(policy.Verdict(F.Expand("%APPDATA%\\Slack\\cookies")));
    }
}
