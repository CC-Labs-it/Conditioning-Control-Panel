using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A spend on another platform corrects this PC's wallet (owner call 2026-10-31, contract C2):
/// only a /v2/user/sync reply whose wallet_net dropped may lower it.
/// </summary>
public class V2WalletNetTests
{
    private const string A = "acct-a";
    private const string B = "acct-b";

    private static AppSettings Signed(string account, int sp, int? net = null)
    {
        var s = new AppSettings { UnifiedId = account, SkillPoints = sp };
        if (net is { } n) s.WalletNetSeen = new() { [account] = n };
        return s;
    }

    [Fact]
    public void NoNetOnTheReply_KeepsRaiseOnlyAndTheStoredValue()
    {
        var down = V2WalletNet.Decide(true, replyNet: null, storedNet: -40, serverSp: 70, localSp: 100);
        Assert.Equal(new V2WalletNet.Step(100, -40, false), down);
        var up = V2WalletNet.Decide(true, null, null, 500, 100);
        Assert.Equal(new V2WalletNet.Step(500, null, false), up);
    }

    [Fact]
    public void FirstSighting_StoresTheNetAndLowersNothing()
    {
        // A lifetime of old spends is not a fresh drop: this PC has nothing to compare with.
        var step = V2WalletNet.Decide(true, replyNet: -300, storedNet: null, serverSp: 70, localSp: 100);
        Assert.Equal(new V2WalletNet.Step(100, -300, false), step);
    }

    [Fact]
    public void ALowerNet_AdoptsTheRepliesBalanceDownIncluded()
    {
        var step = V2WalletNet.Decide(true, replyNet: -130, storedNet: -100, serverSp: 70, localSp: 100);
        Assert.Equal(new V2WalletNet.Step(70, -130, true), step);
    }

    [Fact]
    public void ALowerNet_AdoptsUpwardToo_TheReplyIsTheNumber()
    {
        var step = V2WalletNet.Decide(true, -130, -100, serverSp: 900, localSp: 100);
        Assert.Equal(new V2WalletNet.Step(900, -130, true), step);
    }

    [Theory]
    [InlineData(-100)]   // no movement
    [InlineData(-60)]    // a Back Room or stakes win: the net rose
    public void AnEqualOrHigherNet_NeverLowersTheWallet(int net)
    {
        // The local credit (130) is ahead of the reply, the very case raise-only exists for.
        var step = V2WalletNet.Decide(true, net, storedNet: -100, serverSp: 100, localSp: 130);
        Assert.Equal(new V2WalletNet.Step(130, net, false), step);
    }

    [Fact]
    public void AnEqualNet_StillRaises()
    {
        var step = V2WalletNet.Decide(true, -100, -100, serverSp: 500, localSp: 130);
        Assert.Equal(new V2WalletNet.Step(500, -100, false), step);
    }

    [Fact]
    public void AnAccountThatChangedUnderTheReply_MovesNothingAndStoresNothing()
    {
        var step = V2WalletNet.Decide(sameAccount: false, replyNet: -130, storedNet: -100, serverSp: 70, localSp: 100);
        Assert.Equal(new V2WalletNet.Step(100, -100, false), step);
    }

    [Fact]
    public void ANegativeBalance_IsNotAnAnswer_SoTheDropStaysToBeSeen()
    {
        var step = V2WalletNet.Decide(true, -130, -100, serverSp: -1, localSp: 100);
        Assert.Equal(new V2WalletNet.Step(100, -100, false), step);
    }

    [Fact]
    public void ABalanceOverTheCap_IsClamped()
    {
        Assert.Equal(SparklePoints.Cap, V2WalletNet.Decide(true, -2, -1, SparklePoints.Cap + 5, 10).NextSp);
    }

    [Fact]
    public void ThisPCsOwnBuy_TakesNothingTwice()
    {
        var s = Signed(A, 100, net: -100);
        // The receipt lands first, as it does today.
        Assert.True(V2WalletAdoption.Decide(true, fromBuy: true, serverSp: 70, localSp: s.SkillPoints, out var next));
        s.SkillPoints = next;
        // The next sync sees the same spend as a drop and adopts the same number.
        Assert.True(ProfileSyncService.ApplySyncWallet(s, A, serverSp: 70, replyNet: -130));
        Assert.Equal(70, s.SkillPoints);
        // And the one after sees no drop at all.
        Assert.False(ProfileSyncService.ApplySyncWallet(s, A, 70, -130));
        Assert.Equal(70, s.SkillPoints);
    }

    [Fact]
    public void ASpendElsewhere_LowersThisWallet_AndTheHeaderHearsIt()
    {
        var s = Signed(A, 100, net: -100);
        var heard = 0;
        s.PropertyChanged += (object? _, PropertyChangedEventArgs e) =>
        {
            if (e.PropertyName == nameof(AppSettings.SkillPoints)) heard++;
        };
        Assert.True(ProfileSyncService.ApplySyncWallet(s, A, serverSp: 70, replyNet: -130));
        Assert.Equal(70, s.SkillPoints);
        Assert.Equal(-130, s.WalletNetSeen[A]);
        Assert.Equal(1, heard);   // MainWindow.SparkleWallet repaints on exactly this
    }

    [Fact]
    public void StoredNetsArePerAccount()
    {
        var s = Signed(B, 100);
        s.WalletNetSeen = new() { [A] = -500 };
        // B has no stored net of its own: A's must not read as a drop for B.
        Assert.True(ProfileSyncService.ApplySyncWallet(s, B, serverSp: 70, replyNet: -600));
        Assert.Equal(100, s.SkillPoints);
        Assert.Equal(-600, s.WalletNetSeen[B]);
        Assert.Equal(-500, s.WalletNetSeen[A]);
    }

    [Fact]
    public void AReplyForAnAccountNoLongerSignedIn_TouchesNothing()
    {
        var s = Signed(B, 100, net: -10);
        var before = s.WalletNetSeen;
        Assert.False(ProfileSyncService.ApplySyncWallet(s, A, serverSp: 5000, replyNet: -999));
        Assert.Equal(100, s.SkillPoints);
        Assert.Same(before, s.WalletNetSeen);
    }

    [Fact]
    public void TheStoredDictionaryIsReplaced_NeverMutated_ForASaveOnAnotherThread()
    {
        var s = Signed(A, 100, net: -100);
        var before = s.WalletNetSeen;
        ProfileSyncService.ApplySyncWallet(s, A, 70, -130);
        Assert.NotSame(before, s.WalletNetSeen);
        Assert.Equal(-100, before[A]);
    }

    [Fact]
    public void NoLiveSettings_NoWrite()
    {
        Assert.False(ProfileSyncService.ApplySyncWallet(null, A, 70, -130));
    }

    [Fact]
    public void TheStoredNetIsMachineLocal()
    {
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.WalletNetSeen)));
        var current = Signed(A, 100, net: -100);
        var restored = new AppSettings { WalletNetSeen = new() { [A] = -999 } };
        ProfileSyncService.PreserveLocalOnlyFields(current, restored);
        Assert.Equal(-100, restored.WalletNetSeen[A]);
    }

    [Fact]
    public void TheStoredNetSurvivesTheSettingsFile()
    {
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(Signed(A, 100, net: -100)));
        Assert.Equal(-100, back!.WalletNetSeen[A]);
    }

    [Theory]
    [InlineData("{}", null)]
    [InlineData("{\"wallet_net\":null}", null)]
    [InlineData("{\"wallet_net\":-130}", -130)]
    [InlineData("{\"wallet_net\":0}", 0)]
    [InlineData("{\"wallet_net\":-130.0}", -130)]
    [InlineData("{\"wallet_net\":12.5}", null)]
    [InlineData("{\"wallet_net\":\"-130\"}", null)]
    [InlineData("{\"wallet_net\":99999999999}", null)]
    [InlineData("{\"wallet_net\":{}}", null)]
    public void TheReplyFieldParses_AndAnOddOneCountsAsMissing(string json, int? want)
    {
        // The real reply class, so the field name and shape stay what the merge reads.
        var type = typeof(ProfileSyncService).GetNestedType("V2SyncResponse", BindingFlags.NonPublic)!;
        var reply = JsonConvert.DeserializeObject(json, type);
        var raw = (JToken?)type.GetProperty("WalletNet")!.GetValue(reply);
        Assert.Equal(want, V2WalletNet.ParseNet(raw));
    }

    [Fact]
    public void TheSyncReplyGoesThroughTheRule_AndNoOtherSiteLowersOrMaxesIt()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Settings", "ProfileSyncService.cs"));
        Assert.Contains("ApplySyncWallet(App.Settings?.Current, unifiedId!, v2Result.SkillPoints.Value", src);
        Assert.DoesNotContain("MergeMax(v2Result", src);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
