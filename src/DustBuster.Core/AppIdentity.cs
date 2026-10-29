using System.Text.Json.Serialization;

namespace DustBuster.Core;

/// <summary>Bir kalıntının uygulamaya ne kadar güvenle bağlandığı.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MatchStrength>))]
public enum MatchStrength
{
    /// <summary>Ad benzerliği → seçimsiz, kullanıcı kontrol eder</summary>
    weak = 0,
    /// <summary>Kimlik / kesin yol / veritabanı → varsayılan seçili</summary>
    strong = 1
}

/// <summary>Uygulamanın kurulum biçimi.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InstallKind>))]
public enum InstallKind
{
    /// <summary>Kayıt defterindeki Uninstall anahtarıyla kurulmuş klasik kurulum (NSIS, Inno, Squirrel…)</summary>
    win32,
    /// <summary>Windows Installer paketi (ProductCode GUID)</summary>
    msi,
    /// <summary>MSIX / AppX paketi (PackageFamilyName)</summary>
    msix,
    /// <summary>Kayıt defterinde kaydı olmayan taşınabilir .exe</summary>
    portable
}

/// <summary>
/// Kaldırılacak uygulamanın tarama için gereken kimliği. Uygulama katmanı bunu kayıt defteri
/// (Uninstall anahtarı), paket yöneticisi (MSIX) ve .exe sürüm bilgisinden doldurur; çekirdek
/// yalnızca bu yapıya bakar.
/// </summary>
public sealed class AppIdentity
{
    /// <summary>Görünen ad (DisplayName / paket adı)</summary>
    public string Name { get; set; } = string.Empty;
    public InstallKind Kind { get; set; } = InstallKind.win32;
    /// <summary>Yayıncı (Publisher / CompanyName)</summary>
    public string? Publisher { get; set; }
    /// <summary>Kurulum klasörü (InstallLocation veya ana .exe'nin klasörü)</summary>
    public string? InstallLocation { get; set; }
    /// <summary>Ana yürütülebilir dosyanın tam yolu</summary>
    public string? ExecutablePath { get; set; }
    /// <summary>.exe sürüm bilgisindeki ProductName</summary>
    public string? ProductName { get; set; }
    /// <summary>.exe sürüm bilgisindeki CompanyName</summary>
    public string? CompanyName { get; set; }
    /// <summary>Uninstall anahtarının tam yolu ("HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Notion")</summary>
    public string? UninstallKeyPath { get; set; }
    public string? UninstallString { get; set; }
    public string? QuietUninstallString { get; set; }
    /// <summary>MSI ProductCode ("{GUID}")</summary>
    public string? ProductCode { get; set; }
    /// <summary>MSIX PackageFamilyName ("Vendor.App_hash")</summary>
    public string? PackageFamilyName { get; set; }
    /// <summary>MSIX PackageFullName</summary>
    public string? PackageFullName { get; set; }
    /// <summary>Uygulamaya ait bilinen kimlikler (AppUserModelId, kayıt defteri ürün anahtarı adı vb.)</summary>
    public List<string> ExtraIdentifiers { get; set; } = new();
    /// <summary>Uygulamanın kendi beyan ettiği ek yürütülebilirler (yardımcı, güncelleyici) — tam yol</summary>
    public List<string> HelperExecutables { get; set; } = new();

    [JsonIgnore]
    public string ExecutableName => ExecutablePath is { Length: > 0 } p ? PathUtil.StripExtension(PathUtil.LastComponent(p)) : string.Empty;

    [JsonIgnore]
    public string InstallFolderName => InstallLocation is { Length: > 0 } p ? PathUtil.LastComponent(p) : string.Empty;

    /// <summary>Microsoft'un kendi uygulaması mı? (Windows bileşenleriyle çakışan adlar için güvenlik)</summary>
    [JsonIgnore]
    public bool IsMicrosoftApp
    {
        get
        {
            var vendor = SearchTerms.Squash(Publisher ?? CompanyName ?? string.Empty);
            return vendor.StartsWith("microsoft", StringComparison.Ordinal);
        }
    }

    /// <summary>Kurulum klasörü Program Files altında mı? (tüm kullanıcılar, yönetici gerekir)</summary>
    public bool IsMachineWide(KnownFolders folders)
    {
        if (InstallLocation is not { Length: > 0 } p) return false;
        return PathUtil.IsUnder(p, folders.ProgramFiles) || PathUtil.IsUnder(p, folders.ProgramFilesX86)
               || PathUtil.IsUnder(p, folders.ProgramData);
    }

    public AppIdentity Clone()
    {
        return new AppIdentity
        {
            Name = Name, Kind = Kind, Publisher = Publisher, InstallLocation = InstallLocation, ExecutablePath = ExecutablePath,
            ProductName = ProductName, CompanyName = CompanyName, UninstallKeyPath = UninstallKeyPath, UninstallString = UninstallString,
            QuietUninstallString = QuietUninstallString, ProductCode = ProductCode, PackageFamilyName = PackageFamilyName,
            PackageFullName = PackageFullName, ExtraIdentifiers = new List<string>(ExtraIdentifiers),
            HelperExecutables = new List<string>(HelperExecutables)
        };
    }
}

/// <summary>Tarama seçenekleri.</summary>
public sealed class ScanOptions
{
    /// <summary>Program Files, ProgramData\Microsoft, HKLM, zamanlanmış görevler ve hizmetler de taransın</summary>
    public bool IncludeSystemLocations { get; set; }
    /// <summary>Daha geniş konumlar (Saved Games, Belgeler\My Games, Temp)</summary>
    public bool IncludeAggressiveLocations { get; set; }
    /// <summary>Gömülü kalıntı veritabanı kullanılsın</summary>
    public bool UseDatabase { get; set; } = true;
    /// <summary>Bu sayının üzerinde sonuç dönerse derin tarama sonuçları atılır.</summary>
    public int MaxReasonableResults { get; set; } = 150;
}
