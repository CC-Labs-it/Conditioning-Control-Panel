using System;
using System.IO;
using ConditioningControlPanel.Services.Glassroad;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.PrizeCounter;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Fix wave F1: the counter's Wear is never a dead button. Each family routes to its own picker write
/// (desk, chess, profile in place; Rain to the dressing room; a kart back to the garage that opened the
/// counter, else the page reads "Wear it in the garage"); the header wallet reads the counter's state once
/// the profile lands; the chess pick keeps a push-owed flag so a shared updatedAt never overwrites it.
/// </summary>
[Collection(GameSurfacesCollection.Name)]
public class CounterWearRouteTests
{
    [Theory]
    [InlineData("rain.dress.rose-flare", "rain")]
    [InlineData("desk.hat.party", "desk")]
    [InlineData("desk.toy.snow-globe", "desk")]
    [InlineData("chess.set.royal-capes", "chess")]
    [InlineData("chess.insignia.heart", "chess")]
    [InlineData("prime.banner.arena", "chess")]
    [InlineData("profile.frame.lace", "profile")]
    [InlineData("profile.avatar.emi-pixel", "profile")]
    [InlineData("prime.frame.room-host", "profile")]
    [InlineData("kart.outfit.ringmaster", "kart")]
    [InlineData("kart.umbrella.lace", "kart")]
    [InlineData("prime.paint.host-gold", "kart")]
    [InlineData("prime.trail.podium", "kart")]
    [InlineData("jackpot_remix", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void EveryShopFamily_HasItsWearRoute(string? id, string? family)
        => Assert.Equal(family, PrizeCounterHostService.WearFamily(id));

    [Fact]
    public void OnlyTheGarage_ListsKarts()
    {
        Assert.Contains("kart", PrizeCounterHostService.WearsFor("glassroad"));
        Assert.DoesNotContain("kart", PrizeCounterHostService.WearsFor("wallet"));
        Assert.DoesNotContain("kart", PrizeCounterHostService.WearsFor("piecebypiece"));
        // Rain V2 runs under the gate on (RainV2Gate; off in 7.2.0, RainV2GateTests pins that side).
        foreach (var fam in new[] { "rain", "desk", "chess", "profile" })
            Assert.Contains(fam, PrizeCounterHostService.WearsFor("wallet", rainOn: true));
        foreach (var fam in new[] { "desk", "chess", "profile" })
            Assert.Contains(fam, PrizeCounterHostService.WearsFor("wallet"));
    }

    [Fact]
    public void InPlaceFamilies_CallTheirOwnPicker_AndAKartAwayFromTheGarageOpensNothing()
    {
        var (desk, chess, profile) = (PrizeCounterHostService.WearDesk, PrizeCounterHostService.WearChess, PrizeCounterHostService.WearProfile);
        var seen = new System.Collections.Generic.List<string>();
        try
        {
            PrizeCounterHostService.WearDesk = id => { seen.Add("desk:" + id); return true; };
            PrizeCounterHostService.WearChess = id => { seen.Add("chess:" + id); return true; };
            PrizeCounterHostService.WearProfile = id => { seen.Add("profile:" + id); return false; };
            Assert.Equal("desk", PrizeCounterHostService.RouteWear("desk.hat.party", "wallet"));
            Assert.Equal("chess", PrizeCounterHostService.RouteWear("chess.banner.crest", "wallet"));
            Assert.Equal("refused", PrizeCounterHostService.RouteWear("profile.frame.gilt", "wallet"));
            Assert.Equal("garage", PrizeCounterHostService.RouteWear("kart.outfit.pit-crew", "wallet"));
            Assert.Equal("refused", PrizeCounterHostService.RouteWear("jackpot_remix", "wallet"));
            Assert.Equal(new[] { "desk:desk.hat.party", "chess:chess.banner.crest", "profile:profile.frame.gilt" }, seen);
            Assert.Null(PrizeCounterHostService.WornItem);
        }
        finally
        {
            PrizeCounterHostService.WearDesk = desk;
            PrizeCounterHostService.WearChess = chess;
            PrizeCounterHostService.WearProfile = profile;
        }
    }

    [Fact]
    public void AKartWornFromTheGarage_ClosesBackToIt_WithTheItem()
    {
        Assert.Equal("kart", PrizeCounterHostService.RouteWear("kart.outfit.ballerina", "glassroad"));
        Assert.Equal("kart.outfit.ballerina", PrizeCounterHostService.WornItem);
        Assert.Equal((true, true), GlassroadRules.CounterReturn(PrizeCounterHostService.CloseWear, kartWorn: true));
        Assert.Equal((true, false), GlassroadRules.CounterReturn(PrizeCounterHostService.CloseWear));
        var frame = GlassroadRules.OwnedFrame(new[] { "cos.kart.outfit.ballerina" }, "kart.outfit.ballerina");
        Assert.Equal("kart.outfit.ballerina", (string?)frame["wear"]);
        Assert.Null(GlassroadRules.OwnedFrame(null)["wear"]);
    }

    [Fact]
    public void TheWalletRead_IsOncePerAccount_AndAgainAfterTenMinutes()
    {
        var t = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(PrizeCounterHostService.WalletReadDue("u_a", null, default, t));
        Assert.False(PrizeCounterHostService.WalletReadDue("u_a", "u_a", t, t.AddMinutes(9)));
        Assert.True(PrizeCounterHostService.WalletReadDue("u_a", "u_a", t, t.AddMinutes(10)));
        Assert.True(PrizeCounterHostService.WalletReadDue("u_b", "u_a", t, t.AddSeconds(1)));
        Assert.False(PrizeCounterHostService.WalletReadDue(null, null, default, t));
    }

    [Fact]
    public void TheHeaderWallet_ReadsTheCounterWhenTheProfileLands()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.SparkleWallet.cs"));
        Assert.Contains("App.ProfileSync.ProfileLoaded += OnSparkleWalletProfileLoaded", src);
        Assert.Contains("App.ProfileSync.ProfileLoaded -= OnSparkleWalletProfileLoaded", src);
        Assert.Contains("PrizeCounterHostService.RefreshWalletInBackground()", src);
    }

    [Fact]
    public void ChessWear_PutsTheItemOnItsSlot()
    {
        Assert.Equal("set", PbpLookRules.SlotOf("chess.set.velvet-court"));
        Assert.Equal("banner", PbpLookRules.SlotOf("prime.banner.arena"));
        Assert.Null(PbpLookRules.SlotOf("desk.hat.party"));
        var look = PbpLookRules.With(new ChessLook("chess.set.royal-capes", null, "chess.insignia.star"), "chess.insignia.heart");
        Assert.Equal(new ChessLook("chess.set.royal-capes", null, "chess.insignia.heart"), look);
        Assert.Equal(look, PbpLookRules.With(look, "kart.outfit.ringmaster"));
    }

    [Fact]
    public void AnOwedChessPush_WinsAtLaunch_AndClearsOnlyByRewrite()
    {
        var local = new ChessLook("chess.set.royal-capes", null, null);
        var server = new ChessLook(null, "chess.banner.crest", null);
        // A newer shared updatedAt (a Rain or desk write) would adopt the server copy...
        Assert.Equal((server, true, false), PbpLookRules.Reconcile(local, 100, server, 200, owed: false));
        // ...but an owed local pick is pushed again instead.
        Assert.Equal((local, false, true), PbpLookRules.Reconcile(local, 100, server, 200, owed: true));
        var owed = PbpLookRules.Write(local, 100, owed: true);
        Assert.True(PbpLookRules.IsOwed(owed));
        Assert.Equal((local, 100L), PbpLookRules.Read(owed));
        Assert.False(PbpLookRules.IsOwed(PbpLookRules.Write(local, 100)));
        Assert.False(PbpLookRules.IsOwed(""));
        Assert.False(PbpLookRules.IsOwed("{not json"));
    }

    [Fact]
    public void TheChessHost_HearsTheCounterClose_NoPoll()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "PieceByPiece", "PieceByPieceHostService.Wardrobe.cs"));
        Assert.DoesNotContain("DispatcherTimer", src);
        Assert.Contains("PrizeCounterHostService.Launch(PbpLookRules.CounterFrom, tab, hook)", src);
        Assert.True(PieceByPieceHostService.CounterHandsFocusBack(PrizeCounterHostService.CloseBack));
        Assert.False(PieceByPieceHostService.CounterHandsFocusBack(PrizeCounterHostService.ClosePanic));
        Assert.False(PieceByPieceHostService.CounterHandsFocusBack(PrizeCounterHostService.CloseAppExit));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
