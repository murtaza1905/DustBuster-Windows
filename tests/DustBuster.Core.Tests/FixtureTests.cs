using DustBuster.Core;
using Xunit;
using Xunit.Abstractions;

namespace DustBuster.Core.Tests;

/// <summary>
/// Fixtures altındaki her vaka sanal disk + kayıt defterinde çalıştırılır ve beklentiyle karşılaştırılır.
/// Yeni bir vaka eklemek için JSON dosyasını klasöre koymak yeterlidir; kod değişmez.
/// </summary>
public class FixtureTests
{
    private readonly ITestOutputHelper _output;

    public FixtureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static IEnumerable<object[]> FixtureFiles()
    {
        var dir = FixturesDirectory;
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            yield return new object[] { Path.GetFileName(file) };
        }
    }

    [Fact]
    public void FixturesDirectoryIsNotEmpty()
    {
        Assert.True(Directory.Exists(FixturesDirectory), "Fixtures klasörü çıktıya kopyalanmamış: " + FixturesDirectory);
        Assert.NotEmpty(Directory.GetFiles(FixturesDirectory, "*.json"));
    }

    [Fact]
    public void BundledRulesAndConditionsLoad()
    {
        var rules = CleanupRules.Bundled();
        Assert.NotNull(rules);
        Assert.NotEmpty(rules!.Roots);
        Assert.NotEmpty(rules.Browsers.ChromiumNames);
        var conditions = AppConditions.Bundled();
        Assert.NotNull(conditions);
        Assert.True(conditions!.Count >= 3);
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void FixturePasses(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(FixturesDirectory, fileName));
        var fixture = Fixture.Load(json);
        var (result, verdict) = fixture.Run(null, AppConditions.Bundled());
        foreach (var f in result.Findings) _output.WriteLine($"{f.Strength,-6} {f.Source,-13} {fixture.Contract(f.Path)}  — {f.Note}");
        foreach (var r in result.Rejected) _output.WriteLine($"reject {fixture.Contract(r.Path)}  — {r.Reason}");
        if (verdict.UnexpectedExtras.Count > 0) _output.WriteLine("ℹ️ " + string.Join("; ", verdict.UnexpectedExtras));
        Assert.True(result.DroppedDeepCount == 0, $"{fileName}: güvenlik sınırı aşıldı ({result.DroppedDeepCount})");
        Assert.True(verdict.Passed, $"{fileName} — {fixture.Name}\n{verdict.Summary}");
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void FixtureRoundTripsThroughJson(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(FixturesDirectory, fileName));
        var fixture = Fixture.Load(json);
        var again = Fixture.Load(fixture.ToJson());
        Assert.Equal(fixture.Name, again.Name);
        Assert.Equal(fixture.Tree, again.Tree);
        Assert.Equal(fixture.Expect.Strong, again.Expect.Strong);
        Assert.Equal(fixture.Expect.Weak, again.Expect.Weak);
        Assert.Equal(fixture.Expect.Absent, again.Expect.Absent);
        Assert.Equal(fixture.Identity.Name, again.Identity.Name);
        Assert.Equal(fixture.Identity.Kind, again.Identity.Kind);
        Assert.Equal(fixture.ShortcutTargets?.Count ?? 0, again.ShortcutTargets?.Count ?? 0);
        Assert.Equal(fixture.RunCommands?.Count ?? 0, again.RunCommands?.Count ?? 0);
    }

    [Fact]
    public void CaptureProducesReplayableFixture()
    {
        var source = Fixture.Load(File.ReadAllText(Path.Combine(FixturesDirectory, "notion.json")));
        var fs = source.MakeFileSystem();
        var scanner = new LeftoverScanner(source.Folders, fs, null, AppConditions.Bundled(), source.Options.ToScanOptions());
        var identity = source.MakeIdentity();
        var result = scanner.Scan(identity);
        var (_, mentions) = scanner.RawMentions(identity);
        var captured = Fixture.Capture("yakalanan", source.Folders, identity, source.Options.ToScanOptions(), result, mentions, fs);

        Assert.Equal("observed", captured.Status);
        Assert.NotEmpty(captured.Tree);
        Assert.Contains(captured.Tree, t => t.StartsWith("%APPDATA%", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(captured.RunCommands);

        var replay = Fixture.Load(captured.ToJson());
        var (_, verdict) = replay.Run(null, AppConditions.Bundled());
        Assert.True(verdict.Passed, verdict.Summary);
    }
}
