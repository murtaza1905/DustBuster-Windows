using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DustBuster.Core;

/// <summary>
/// Gerçek disk ve kayıt defteri. Kayıt defteri yolları "HKCU:\…" / "HKLM:\…" biçimindedir; anahtarlar
/// dizin, değerler dosya gibi davranır. Windows dışında kayıt defteri her zaman boş görünür.
/// </summary>
public sealed class RealFileSystem : IScanFileSystem
{
    private readonly KnownFolders _folders;

    public RealFileSystem(KnownFolders folders)
    {
        _folders = folders;
    }

    // MARK: Dosya sistemi

    public IReadOnlyList<string>? Children(string path)
    {
        if (PathUtil.IsRegistryPath(path)) return RegistryChildren(path);
        try
        {
            if (!Directory.Exists(path)) return null;
            var names = new List<string>();
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                names.Add(entry.Name);
            }
            names.Sort(PathUtil.Comparer);
            return names;
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    public bool Exists(string path)
    {
        if (PathUtil.IsRegistryPath(path)) return RegistryExists(path);
        try { return Directory.Exists(path) || File.Exists(path); }
        catch (IOException) { return false; }
    }

    public bool IsDirectory(string path)
    {
        if (PathUtil.IsRegistryPath(path)) return RegistryKeyExists(path);
        try { return Directory.Exists(path); }
        catch (IOException) { return false; }
    }

    public bool IsReparsePoint(string path)
    {
        if (PathUtil.IsRegistryPath(path)) return false;
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    public string Canonical(string path)
    {
        if (PathUtil.IsRegistryPath(path)) return PathUtil.Normalize(path);
        try
        {
            var normalized = PathUtil.Normalize(path);
            var parent = PathUtil.Parent(normalized);
            if (parent == null || !Directory.Exists(parent)) return normalized;
            var name = PathUtil.LastComponent(normalized);
            foreach (var entry in new DirectoryInfo(parent).EnumerateFileSystemInfos(name, SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(entry.Name, name, PathUtil.Comparison)) return PathUtil.Join(Canonical(parent), entry.Name);
            }
            return normalized;
        }
        catch (IOException) { return PathUtil.Normalize(path); }
        catch (UnauthorizedAccessException) { return PathUtil.Normalize(path); }
        catch (ArgumentException) { return PathUtil.Normalize(path); }
    }

    public string? ShortcutTarget(string path)
    {
        if (!PathUtil.HasExtension(path, ".lnk")) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            var target = ShellLink.LocalPath(bytes);
            return target == null ? null : PathUtil.Normalize(CommandLine.ExpandEnvironment(target, _folders));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public string? TaskExecutable(string taskPath)
    {
        try
        {
            if (!File.Exists(taskPath)) return null;
            var doc = XDocument.Load(taskPath);
            var command = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
            return CommandLine.ExecutablePath(command, _folders);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Xml.XmlException) { return null; }
    }

    // MARK: Kayıt defteri

    public string? RunCommand(string valuePath)
    {
        var parent = PathUtil.Parent(valuePath);
        if (parent == null) return null;
        return RegistryValue(parent, PathUtil.LastComponent(valuePath));
    }

    public string? ServiceExecutable(string serviceKeyPath)
    {
        var image = RegistryValue(serviceKeyPath, "ImagePath");
        return CommandLine.ExecutablePath(image, _folders);
    }

    private static bool RegistryExists(string path) => RegistryKeyExists(path) || RegistryValueExists(path);

    [SupportedOSPlatform("windows")]
    private static RegistryKey? OpenKey(string path)
    {
        var normalized = PathUtil.Normalize(path);
        var root = PathUtil.Root(normalized);
        var rel = PathUtil.Relative(normalized, root);
        RegistryKey hive = root.ToUpperInvariant() switch
        {
            "HKCU:" => Registry.CurrentUser,
            "HKLM:" => Registry.LocalMachine,
            "HKU:" => Registry.Users,
            "HKCR:" => Registry.ClassesRoot,
            _ => throw new ArgumentException("bilinmeyen kök: " + root)
        };
        if (rel == null) return hive;
        try { return hive.OpenSubKey(rel, writable: false); }
        catch (System.Security.SecurityException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static IReadOnlyList<string>? RegistryChildren(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = OpenKey(path);
        if (key == null) return null;
        var names = new List<string>();
        try
        {
            names.AddRange(key.GetSubKeyNames());
            foreach (var value in key.GetValueNames())
            {
                if (value.Length > 0) names.Add(value);
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        names.Sort(PathUtil.Comparer);
        return names;
    }

    private static bool RegistryKeyExists(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = OpenKey(path);
            return key != null;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool RegistryValueExists(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var parent = PathUtil.Parent(path);
        if (parent == null) return false;
        try
        {
            using var key = OpenKey(parent);
            if (key == null) return false;
            var name = PathUtil.LastComponent(path);
            return key.GetValueNames().Any(v => string.Equals(v, name, PathUtil.Comparison));
        }
        catch (ArgumentException) { return false; }
        catch (IOException) { return false; }
    }

    private static string? RegistryValue(string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = OpenKey(keyPath);
            var value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value switch
            {
                string s => s,
                string[] lines => string.Join(" ", lines),
                _ => value?.ToString()
            };
        }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

/// <summary>.lnk (Shell Link Binary File Format) içinden yerel hedef yolu okur; COM gerektirmez.</summary>
public static class ShellLink
{
    private const uint HasLinkTargetIdList = 0x1;
    private const uint HasLinkInfo = 0x2;
    private const uint HasName = 0x4;
    private const uint HasRelativePath = 0x8;
    private const uint HasWorkingDir = 0x10;
    private const uint HasArguments = 0x20;
    private const uint HasIconLocation = 0x40;
    private const uint IsUnicode = 0x80;

    public static string? LocalPath(byte[] data)
    {
        if (data.Length < 76 || BitConverter.ToUInt32(data, 0) != 0x4C) return null;
        var flags = BitConverter.ToUInt32(data, 20);
        var pos = 76;
        if ((flags & HasLinkTargetIdList) != 0)
        {
            if (pos + 2 > data.Length) return null;
            var size = BitConverter.ToUInt16(data, pos);
            pos += 2 + size;
        }
        string? result = null;
        if ((flags & HasLinkInfo) != 0)
        {
            if (pos + 28 > data.Length) return null;
            var linkInfoSize = (int)BitConverter.ToUInt32(data, pos);
            var headerSize = (int)BitConverter.ToUInt32(data, pos + 4);
            var infoFlags = BitConverter.ToUInt32(data, pos + 8);
            var localBasePathOffset = (int)BitConverter.ToUInt32(data, pos + 16);
            var commonPathSuffixOffset = (int)BitConverter.ToUInt32(data, pos + 24);
            if ((infoFlags & 0x1) != 0)
            {
                string? basePath = null;
                string? suffix = null;
                if (headerSize >= 0x24)
                {
                    var unicodeBase = (int)BitConverter.ToUInt32(data, pos + 28);
                    if (unicodeBase > 0 && pos + unicodeBase < data.Length) basePath = ReadUnicodeZ(data, pos + unicodeBase);
                    if (headerSize >= 0x28)
                    {
                        var unicodeSuffix = (int)BitConverter.ToUInt32(data, pos + 32);
                        if (unicodeSuffix > 0 && pos + unicodeSuffix < data.Length) suffix = ReadUnicodeZ(data, pos + unicodeSuffix);
                    }
                }
                basePath ??= localBasePathOffset > 0 && pos + localBasePathOffset < data.Length ? ReadAnsiZ(data, pos + localBasePathOffset) : null;
                suffix ??= commonPathSuffixOffset > 0 && pos + commonPathSuffixOffset < data.Length ? ReadAnsiZ(data, pos + commonPathSuffixOffset) : null;
                if (basePath != null)
                {
                    result = basePath;
                    if (!string.IsNullOrEmpty(suffix)) result = PathUtil.Join(basePath, suffix);
                }
            }
            pos += linkInfoSize;
        }
        if (result != null) return result;

        // LinkInfo yoksa (ör. ortam değişkenli hedef) RelativePath veya ExtraData'daki EnvironmentVariableDataBlock
        var unicode = (flags & IsUnicode) != 0;
        string? relativePath = null;
        foreach (var flag in new[] { HasName, HasRelativePath, HasWorkingDir, HasArguments, HasIconLocation })
        {
            if ((flags & flag) == 0) continue;
            if (pos + 2 > data.Length) return null;
            var count = BitConverter.ToUInt16(data, pos);
            pos += 2;
            var byteLength = unicode ? count * 2 : count;
            if (pos + byteLength > data.Length) return null;
            var text = unicode ? Encoding.Unicode.GetString(data, pos, byteLength) : Encoding.Default.GetString(data, pos, byteLength);
            if (flag == HasRelativePath) relativePath = text;
            pos += byteLength;
        }
        while (pos + 8 <= data.Length)
        {
            var blockSize = (int)BitConverter.ToUInt32(data, pos);
            if (blockSize < 4) break;
            var signature = BitConverter.ToUInt32(data, pos + 4);
            if (signature == 0xA0000001 && pos + 8 + 260 <= data.Length)
            {
                var ansi = ReadAnsiZ(data, pos + 8);
                var unicodeTarget = pos + 8 + 260 + 520 <= data.Length ? ReadUnicodeZ(data, pos + 8 + 260) : null;
                return string.IsNullOrEmpty(unicodeTarget) ? ansi : unicodeTarget;
            }
            pos += blockSize;
        }
        return relativePath;
    }

    private static string ReadAnsiZ(byte[] data, int offset)
    {
        var end = offset;
        while (end < data.Length && data[end] != 0) end++;
        return Encoding.Default.GetString(data, offset, end - offset);
    }

    private static string ReadUnicodeZ(byte[] data, int offset)
    {
        var end = offset;
        while (end + 1 < data.Length && !(data[end] == 0 && data[end + 1] == 0)) end += 2;
        return Encoding.Unicode.GetString(data, offset, end - offset);
    }
}
