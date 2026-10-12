using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The local sandbox whitelist (AccountSeed.SandboxWhitelistRequested, decision 2026-10-12): a DEBUG build in a
/// CCP_USERDATA_DIR sandbox with CCP_SANDBOX_WHITELIST=1 passes every TierGate; without the flag the gates stay shut;
/// a Release build (the gate and CI run tests in Release) never grants it, flag or not.</summary>
public sealed class SandboxWhitelistTests : IDisposable
{
    private const string Flag = "CCP_SANDBOX_WHITELIST";
    private readonly string? _oldFlag = Environment.GetEnvironmentVariable(Flag);
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;

    public SandboxWhitelistTests()
    {
        CoreSecrets.RetrieveProvider = _ => null;   // no stored account: only the override can entitle
        CoreSecrets.StoreProvider = (_, _) => { };
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Flag, _oldFlag);
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
        AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())); // SandboxWhitelisted back to false
        CoreAccount.IsLoggedInProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = CoreAccount.IsWhitelistedProvider = null;
        CoreAccount.DisplayNameProvider = null;
        CoreAccount.ChangeDisplayNameProvider = null;
        CoreAccount.DeleteAccountProvider = null;
        CoreEntitlement.HasPremiumProvider = CoreEntitlement.HasLabProvider = null;
        CoreProgram.HasPremiumProvider = null;
    }

    private static void SeedWith(string? flag)
    {
        Environment.SetEnvironmentVariable(Flag, flag);
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
    }

    [Fact]
    public void WithTheFlagInASandbox_LabAndPremiumGatesOpenOnlyInDebug()
    {
        Assert.True(SandboxNet.Active);   // TestUserDataProfile: tests always run sandboxed
        SeedWith("1");
#if DEBUG
        const bool expected = true;
#else
        const bool expected = false;      // Release: compiled out, the paywall stays
#endif
        Assert.Equal(expected, AccountSeed.SandboxWhitelisted);
        Assert.Equal(expected, CoreAccount.IsWhitelisted);
        Assert.Equal(expected, TierGate.RequiresLab("Lab feature").Allowed);
        Assert.Equal(expected, TierGate.RequiresPremium("Premium feature").Allowed);
        Assert.False(CoreAccount.IsLoggedIn); // no fake identity: nothing signs in or syncs
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("true")]
    public void WithoutTheFlag_GatesStayDenied(string? flag)
    {
        SeedWith(flag);
        Assert.False(AccountSeed.SandboxWhitelisted);
        Assert.False(CoreAccount.IsWhitelisted);
        Assert.False(TierGate.RequiresLab("Lab feature").Allowed);
        Assert.False(TierGate.RequiresPremium("Premium feature").Allowed);
    }

    /// <summary>Guard for Release: the flag is read only between `#if DEBUG` and `#else`, whose branch returns false.</summary>
    [Fact]
    public void TheFlagIsReadOnlyInsideIfDebug()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Platform", "AccountSeed.cs"));
        var m = Regex.Match(src, @"#if DEBUG\s*\n(?<debug>.*?)#else\s*\n(?<rel>.*?)#endif", RegexOptions.Singleline);
        Assert.True(m.Success, "AccountSeed.SandboxWhitelistRequested lost its #if DEBUG block");
        Assert.Contains("\"" + Flag + "\"", m.Groups["debug"].Value);
        Assert.Contains("SandboxNet.Active", m.Groups["debug"].Value);
        Assert.Equal("return false;", m.Groups["rel"].Value.Trim());
        Assert.Equal(1, Regex.Matches(src, Regex.Escape("\"" + Flag + "\"")).Count);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "CCP.Avalonia"), "*.cs", SearchOption.AllDirectories))
            if (!f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.EndsWith("AccountSeed.cs"))
                Assert.DoesNotContain(Flag, File.ReadAllText(f));
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
