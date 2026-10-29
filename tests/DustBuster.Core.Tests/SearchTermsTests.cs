using DustBuster.Core;
using Xunit;

namespace DustBuster.Core.Tests;

public class SearchTermsTests
{
    private static AppIdentity Identity(string name, string? publisher = null, string? exe = null, InstallKind kind = InstallKind.win32,
                                        string? pfn = null, string? key = null) => new()
    {
        Name = name,
        Publisher = publisher,
        CompanyName = publisher,
        ExecutablePath = exe,
        Kind = kind,
        PackageFamilyName = pfn,
        UninstallKeyPath = key,
        InstallLocation = exe == null ? null : PathUtil.Parent(exe)
    };

    [Fact]
    public void TokensSplitOnNonAlphanumerics()
    {
        Assert.Equal(new[] { "google", "chrome" }, SearchTerms.Tokens("Google Chrome"));
        Assert.Equal(new[] { "notion", "id" }, SearchTerms.Tokens("notion.id"));
        Assert.Equal(new[] { "7", "zip" }, SearchTerms.Tokens("7-Zip"));
        Assert.Equal("googlechrome", SearchTerms.Squash("Google Chrome"));
    }

    [Fact]
    public void CleanNameDropsVersionArchAndParentheses()
    {
        Assert.Equal(new[] { "7", "zip" }, SearchTerms.CleanNameTokens("7-Zip 23.01 (x64 edition)"));
        Assert.Equal(new[] { "notepad" }, SearchTerms.CleanNameTokens("Notepad++ (64-bit x64)"));
        Assert.Equal(new[] { "microsoft", "visual", "studio", "code" }, SearchTerms.CleanNameTokens("Microsoft Visual Studio Code (User)"));
        Assert.Equal(new[] { "python", "3", "12" }.Take(1), SearchTerms.CleanNameTokens("Python 3.12.1 (64-bit)").Take(1));
        Assert.DoesNotContain("12", SearchTerms.CleanNameTokens("Python 3.12.1 (64-bit)"));
    }

    [Fact]
    public void GenericAndShortNamesProduceNoTokenSets()
    {
        Assert.Empty(SearchTerms.From(Identity("Update")).NameTokenSets);
        Assert.Empty(SearchTerms.From(Identity("App")).NameTokenSets);
        Assert.Empty(SearchTerms.From(Identity("VLC")).NameTokenSets);
        Assert.NotEmpty(SearchTerms.From(Identity("Notion")).NameTokenSets);
    }

    [Fact]
    public void MultiWordNameGetsBothTokenSetAndSquashedVariant()
    {
        var terms = SearchTerms.From(Identity("Google Chrome", "Google LLC"));
        Assert.Contains(terms.NameTokenSets, s => s.SequenceEqual(new[] { "google", "chrome" }));
        Assert.Contains(terms.NameTokenSets, s => s.SequenceEqual(new[] { "googlechrome" }));
        Assert.Equal(MatchStrength.strong, terms.Match("Google\\Chrome", true));
        Assert.Equal(MatchStrength.strong, terms.Match("GoogleChrome", true));
        Assert.Null(terms.Match("Google", true));
        Assert.Null(terms.Match("Google\\Chrome Beta", true));
        Assert.Null(terms.Match("Google\\Update", true));
    }

    [Fact]
    public void WeakMatchOnlyLooksAtOwnName()
    {
        var terms = SearchTerms.From(Identity("Notion", "Notion Labs, Inc."));
        Assert.Equal(MatchStrength.weak, terms.Match("Notion Calendar", true));
        Assert.Equal(MatchStrength.weak, terms.Match("notion-installer.exe", false));
        Assert.Null(terms.Match("Notion Calendar\\Default", true));
        Assert.Null(terms.Match("notionx", true));
    }

    [Fact]
    public void PrefixRuleAppliesOnlyToLongNames()
    {
        var longName = SearchTerms.From(Identity("LogiPluginService"));
        Assert.Equal(MatchStrength.weak, longName.Match("LogiPluginServiceNative", true));
        var shortName = SearchTerms.From(Identity("Discord"));
        Assert.Null(shortName.Match("DiscordPTB", true));
        Assert.Null(shortName.Match("discordcanary", true));
    }

    [Fact]
    public void ChannelWordsSeparateApps()
    {
        var terms = SearchTerms.From(Identity("Brave"));
        Assert.Equal(MatchStrength.strong, terms.Match("BraveSoftware\\Brave", true));
        Assert.Null(terms.Match("BraveSoftware\\Brave-Browser-Nightly", true));
        var beta = SearchTerms.From(Identity("Brave Beta"));
        Assert.Equal(MatchStrength.strong, beta.Match("Brave Beta", true));
    }

    [Fact]
    public void IdentifiersMatchExactlyOrWithDotUnderscoreSuffix()
    {
        var terms = SearchTerms.From(Identity("WhatsApp", "WhatsApp Inc.", kind: InstallKind.msix, pfn: "5319275A.WhatsAppDesktop_cv1g1gvanyjgm"));
        Assert.True(terms.MatchesIdentifier("5319275A.WhatsAppDesktop_cv1g1gvanyjgm"));
        Assert.True(terms.MatchesIdentifier("5319275a.whatsappdesktop_cv1g1gvanyjgm.backup"));
        Assert.False(terms.MatchesIdentifier("Microsoft.WindowsStore_8wekyb3d8bbwe"));
        Assert.Equal(MatchStrength.strong, terms.Match("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", true));
        Assert.Equal(MatchStrength.weak, terms.Match("WhatsApp Desktop", true));
    }

    [Fact]
    public void PlainUninstallKeyNamesAreNotIdentifiers()
    {
        var terms = SearchTerms.From(Identity("Google Chrome", "Google LLC", key: "HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Google Chrome"));
        Assert.Empty(terms.Identifiers);
        var guid = SearchTerms.From(Identity("Something", key: "HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{1234-ABCD}"));
        Assert.Contains("{1234-abcd}", guid.Identifiers);
        Assert.True(SearchTerms.LooksLikeIdentifier("notion.id"));
        Assert.True(SearchTerms.LooksLikeIdentifier("Discord_x64"));
        Assert.False(SearchTerms.LooksLikeIdentifier("Google Chrome"));
    }

    [Fact]
    public void MicrosoftFoldersNeverMatchForeignApps()
    {
        var terms = SearchTerms.From(Identity("Microsoft Teams Lookalike", "Acme"));
        Assert.Null(terms.Match("Microsoft", true));
        Assert.Null(terms.Match("Microsoft.WindowsStore_8wekyb3d8bbwe", true));
        var ms = SearchTerms.From(Identity("Microsoft Teams", "Microsoft Corporation"));
        Assert.True(ms.IsMicrosoftApp);
        Assert.Equal(MatchStrength.strong, ms.Match("Microsoft\\Teams", true));
    }

    [Fact]
    public void VendorTokensAndNameEqualsVendor()
    {
        var chrome = SearchTerms.From(Identity("Google Chrome", "Google LLC"));
        Assert.Contains("google", chrome.VendorTokens);
        Assert.True(chrome.IsVendorName("Google"));
        Assert.False(chrome.NameEqualsVendor);

        var dropbox = SearchTerms.From(Identity("Dropbox", "Dropbox, Inc."));
        Assert.True(dropbox.NameEqualsVendor);

        var opera = SearchTerms.From(Identity("Opera GX", "Opera Software ASA"));
        Assert.True(opera.IsVendorName("Opera Software"));
    }

    [Fact]
    public void ExecutableNameIsAnExtraNameVariant()
    {
        var terms = SearchTerms.From(Identity("Visual Studio Code", "Microsoft Corporation", exe: "C:\\Users\\test\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe"));
        Assert.Contains(terms.NameTokenSets, s => s.SequenceEqual(new[] { "visual", "studio", "code" }));
        Assert.Contains(terms.NameTokenSets, s => s.SequenceEqual(new[] { "microsoft", "vs", "code" }));
        Assert.DoesNotContain(terms.NameTokenSets, s => s.SequenceEqual(new[] { "code" }));
        Assert.Equal(MatchStrength.strong, terms.Match("Microsoft VS Code", true));
    }
}
