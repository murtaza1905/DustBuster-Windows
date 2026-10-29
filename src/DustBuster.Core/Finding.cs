using System.Text.Json.Serialization;

namespace DustBuster.Core;

/// <summary>Bulgunun geldiği kaynak.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FindingSource>))]
public enum FindingSource
{
    /// <summary>Kurulum klasörü (kaldırıcıdan sonra kalırsa)</summary>
    installFolder,
    /// <summary>Kimlik veya tam adla birebir bilinen yol (hızlı tarama)</summary>
    exactPath,
    /// <summary>Dizin veya kayıt defteri taramasında kimlik eşleşmesi</summary>
    identifier,
    /// <summary>Dizin veya kayıt defteri taramasında ad eşleşmesi</summary>
    name,
    /// <summary>Gömülü kalıntı veritabanı</summary>
    database,
    /// <summary>Hedefi kurulum klasörünün içinde olan kısayol</summary>
    shortcut,
    /// <summary>Komutu kurulum klasörünün içinde olan başlangıç girdisi (Run)</summary>
    startup,
    /// <summary>Eylemi kurulum klasörünün içinde olan zamanlanmış görev</summary>
    scheduledTask,
    /// <summary>ImagePath'i kurulum klasörünün içinde olan hizmet</summary>
    service,
    /// <summary>Uygulamanın kendi beyan ettiği yardımcı yürütülebilirin kalıntısı</summary>
    helper,
    /// <summary>Uygulama başına elle tanımlı kural (conditions.json)</summary>
    condition
}

/// <summary>Tek bir kalıntı bulgusu.</summary>
public sealed record Finding(string Path, LeftoverKind Kind, FindingSource Source, MatchStrength Strength, string Note)
{
    [JsonIgnore]
    public bool IsSelectedByDefault => Strength == MatchStrength.strong;

    [JsonIgnore]
    public bool IsRegistry => PathUtil.IsRegistryPath(Path);
}

public sealed record RejectedPath(string Path, string Reason);

public sealed class ScanResult
{
    public List<Finding> Findings { get; }
    /// <summary>Derin tarama güvenlik sınırını aştıysa atılan sonuç sayısı</summary>
    public int DroppedDeepCount { get; }
    /// <summary>Eşleşen veritabanı kayıt kimlikleri</summary>
    public List<string> DatabaseIds { get; }
    /// <summary>Kabul edilmeyen yollar (rapor için)</summary>
    public List<RejectedPath> Rejected { get; }
    /// <summary>Uygulanan kural kimlikleri (conditions.json)</summary>
    public List<string> ConditionIds { get; }

    public ScanResult(List<Finding> findings, int droppedDeepCount = 0, List<string>? databaseIds = null,
                      List<RejectedPath>? rejected = null, List<string>? conditionIds = null)
    {
        Findings = findings;
        DroppedDeepCount = droppedDeepCount;
        DatabaseIds = databaseIds ?? new List<string>();
        Rejected = rejected ?? new List<RejectedPath>();
        ConditionIds = conditionIds ?? new List<string>();
    }

    public IEnumerable<Finding> Strong => Findings.Where(f => f.Strength == MatchStrength.strong);
    public IEnumerable<Finding> Weak => Findings.Where(f => f.Strength == MatchStrength.weak);
    public bool Contains(string path) => Findings.Any(f => PathUtil.PathEquals(f.Path, path));
}
