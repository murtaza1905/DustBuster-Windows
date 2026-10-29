namespace DustBuster.Core;

/// <summary>Kayıt defteri Run değerleri, hizmet ImagePath'leri ve UninstallString'lerden yürütülebilir yolunu çıkarır.</summary>
public static class CommandLine
{
    /// <summary>"\"C:\Program Files\X\x.exe\" --flag" → "C:\Program Files\X\x.exe"; bulunamazsa null.</summary>
    public static string? ExecutablePath(string? command, KnownFolders? folders = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var cmd = command.Trim();
        if (cmd.StartsWith("\\??\\", StringComparison.Ordinal)) cmd = cmd[4..];
        if (cmd.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase) && folders != null)
        {
            cmd = PathUtil.Join(folders.Windows, cmd[12..]);
        }
        if (cmd.StartsWith("system32\\", StringComparison.OrdinalIgnoreCase) && folders != null)
        {
            cmd = PathUtil.Join(folders.Windows, cmd);
        }

        string candidate;
        if (cmd.StartsWith('"'))
        {
            var close = cmd.IndexOf('"', 1);
            candidate = close > 1 ? cmd[1..close] : cmd[1..];
        }
        else
        {
            var exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0)
            {
                candidate = cmd[..(exe + 4)];
            }
            else
            {
                var space = cmd.IndexOf(' ');
                candidate = space > 0 ? cmd[..space] : cmd;
            }
        }
        candidate = ExpandEnvironment(candidate, folders).Trim();
        if (candidate.Length == 0) return null;
        return PathUtil.Normalize(candidate);
    }

    /// <summary>%VAR% genişletmesi: bilinen klasör belirteçleri ve (gerçek makinede) ortam değişkenleri.</summary>
    public static string ExpandEnvironment(string value, KnownFolders? folders)
    {
        if (!value.Contains('%')) return value;
        var result = value;
        if (folders != null)
        {
            result = ReplaceToken(result, "%SystemRoot%", folders.Windows);
            result = ReplaceToken(result, "%windir%", folders.Windows);
            result = ReplaceToken(result, "%ProgramFiles%", folders.ProgramFiles);
            result = ReplaceToken(result, "%ProgramFiles(x86)%", folders.ProgramFilesX86);
            result = ReplaceToken(result, "%ProgramData%", folders.ProgramData);
            result = ReplaceToken(result, "%LocalAppData%", folders.LocalAppData);
            result = ReplaceToken(result, "%AppData%", folders.RoamingAppData);
            result = ReplaceToken(result, "%UserProfile%", folders.Home);
            result = ReplaceToken(result, "%Temp%", folders.Temp);
            result = ReplaceToken(result, "%Public%", folders.Public);
            result = ReplaceToken(result, "%SystemDrive%", folders.SystemDrive.TrimEnd('\\'));
        }
        if (result.Contains('%') && OperatingSystem.IsWindows())
        {
            result = Environment.ExpandEnvironmentVariables(result);
        }
        return result;
    }

    private static string ReplaceToken(string value, string token, string replacement)
    {
        var idx = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            value = value[..idx] + replacement + value[(idx + token.Length)..];
            idx = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        }
        return value;
    }
}
