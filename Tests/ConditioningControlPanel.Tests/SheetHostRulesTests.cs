using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The shop frame lane's host rules: the wallet point the page's stars aim at, the header wallet in the
/// DEBUG shop preview, the Disco hues on the wire, the slide's settle, and the sheet as a quiet surface.</summary>
public class SheetHostRulesTests
{
    [Fact]
    public void TheWalletPoint_IsTheStarCentre_InThePagesOwnPixels()
    {
        // The chip sits in the bar above the sheet: the page sees it above its own top edge.
        Assert.Equal((95, -27), SheetHostRules.WalletAt(new Point(100, -12), 5, 15));
        Assert.Equal((0, 0), SheetHostRules.WalletAt(new Point(5.4, 14.6), 5, 15));
        Assert.Null(SheetHostRules.WalletAt(new Point(double.NaN, 0), 5, 15));
        Assert.Null(SheetHostRules.WalletAt(new Point(1e9, 0), 5, 15));
    }

    [Fact]
    public void TheHeaderWallet_ShowsThePreviewWallet_NeverTheRealOne_WhileThePreviewRuns()
    {
        Assert.Equal((true, 410, true), SheetHostRules.HeaderWallet(preview: true, signedIn: false, real: 9000, previewSp: 410));
        Assert.Equal((true, 410, true), SheetHostRules.HeaderWallet(preview: true, signedIn: true, real: 9000, previewSp: 410));
        Assert.Equal((true, 9000, false), SheetHostRules.HeaderWallet(preview: false, signedIn: true, real: 9000, previewSp: 410));
        Assert.False(SheetHostRules.HeaderWallet(preview: false, signedIn: false, real: 9000, previewSp: 410).Show);
        Assert.False(SheetHostRules.HeaderWallet(preview: false, signedIn: true, real: -1, previewSp: 0).Show);
    }

    [Fact]
    public void TheDiscoHues_GoOnTheWireAsHex_AtMostSix()
    {
        Assert.Equal(new[] { "#ff3d9e", "#00c229" }, SheetHostRules.DiscoWire(new uint[] { 0xFF3D9E, 0xFF00C229 }));
        Assert.Equal(6, SheetHostRules.DiscoWire(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 })!.Length);
        Assert.Null(SheetHostRules.DiscoWire(new uint[0]));
        Assert.Null(SheetHostRules.DiscoWire(null));
    }

    [Fact]
    public void TheFogColour_IsTheFiltersOwn_UserPickThenModThenHotPink()
    {
        Assert.Equal("#12abef", SheetHostRules.FogColourWire("#12ABEF", ((byte)1, (byte)2, (byte)3)));
        Assert.Equal("#12abef", SheetHostRules.FogColourWire(" 12abef ", null));
        Assert.Equal("#010203", SheetHostRules.FogColourWire("", ((byte)1, (byte)2, (byte)3)));
        Assert.Equal("#010203", SheetHostRules.FogColourWire("#zzzzzz", ((byte)1, (byte)2, (byte)3)));
        Assert.Equal("#ff69b4", SheetHostRules.FogColourWire(null, null));
    }

    [Fact]
    public void BuildInit_SendsTheFogColour()
    {
        string? root = null;
        for (var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory); d != null && root == null; d = d.Parent)
            if (System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj"))) root = d.FullName;
        Assert.NotNull(root);
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(root!, "ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.cs"));
        Assert.Contains("init[\"fogColour\"] = SheetHostRules.FogColourWire(App.Settings?.Current?.PinkFilterColor, App.Mods?.GetFilterColorRgb())", src);
    }

    [Fact]
    public void TheOpeningSlide_OvershootsSoftly_AndSettlesHome()
    {
        double over = CounterSheetRules.OvershootDip(MotionLevel.Full, gentle: false, 600);
        Assert.True(over > 0 && over <= 14);
        Assert.Equal(over / 2, CounterSheetRules.OvershootDip(MotionLevel.Reduced, false, 600), 6);
        Assert.Equal(0, CounterSheetRules.OvershootDip(MotionLevel.Full, gentle: true, 600));
        Assert.Equal(0, CounterSheetRules.OvershootDip(MotionLevel.Off, false, 600));
        Assert.Equal(600, CounterSheetRules.OpenOffset(0, 600, over), 6);       // starts wholly above the bar
        Assert.Equal(-over, CounterSheetRules.OpenOffset(0.7, 600, over), 6);   // the deepest point, past the place
        Assert.Equal(0, CounterSheetRules.OpenOffset(1, 600, over), 6);         // home
        Assert.True(CounterSheetRules.OpenOffset(0.85, 600, over) is < 0 and > -14);
        Assert.Equal(CounterSheetRules.SlideOffset(CounterSheetRules.EaseOut(0.5), 600), CounterSheetRules.OpenOffset(0.5, 600, 0), 6);
    }

    [Fact]
    public void AnOpenSheet_ParksTheStartupLadder_LikeAGame()
    {
        Assert.True(StartupQueueCore.IsQuiet(new QuietInputs { CounterSheetUp = true, NowUtc = System.DateTime.UtcNow }));
        Assert.True(StartupQueueCore.IsParked(new QuietInputs { CounterSheetUp = true }));
        Assert.False(StartupQueueCore.IsQuiet(new QuietInputs { NowUtc = System.DateTime.UtcNow }));
    }

    [Fact]
    public void AWalletPress_UnderAModal_OnlyNudges()
    {
        Assert.Equal(SheetHostRules.DoorPress.Open, SheetHostRules.OnDoorPress(doorEnabled: true, threadModal: false));
        Assert.Equal(SheetHostRules.DoorPress.Nudge, SheetHostRules.OnDoorPress(doorEnabled: false, threadModal: false));   // a message box on the door
        Assert.Equal(SheetHostRules.DoorPress.Nudge, SheetHostRules.OnDoorPress(doorEnabled: true, threadModal: true));    // a ShowDialog anywhere
        Assert.Equal(4, SheetHostRules.NudgeDip(MotionLevel.Full, gentle: false));
        Assert.Equal(2, SheetHostRules.NudgeDip(MotionLevel.Reduced, gentle: false));
        Assert.Equal(0, SheetHostRules.NudgeDip(MotionLevel.Off, gentle: false));
        Assert.Equal(0, SheetHostRules.NudgeDip(MotionLevel.Full, gentle: true));
    }

    [Fact]
    public void OnlyVisibleFloatingCards_HideWhileTheSheetIsDown()
    {
        Assert.True(SheetHostRules.HidesWhileDown(visible: true, floatingCard: true));
        Assert.False(SheetHostRules.HidesWhileDown(visible: false, floatingCard: true));
        Assert.False(SheetHostRules.HidesWhileDown(visible: true, floatingCard: false));   // the avatar tube, overlays: left alone
    }

    [Fact]
    public void ThePreviewMark_FollowsTheHeaderWalletRule()
    {
        Assert.Equal("Preview", SheetHostRules.PreviewMarkText(SheetHostRules.HeaderWallet(true, false, 9000, 410).PreviewMark));
        Assert.Null(SheetHostRules.PreviewMarkText(SheetHostRules.HeaderWallet(false, true, 9000, 410).PreviewMark));
    }

    [Fact]
    public void TheSheet_StaysWarmWhileUp_AndHoldsItsRevealForTheFirstAnswer()
    {
        var view = System.IO.File.ReadAllText(Root("ConditioningControlPanel", "Controls", "CounterSheetView.cs"));
        Assert.Contains("_slot.Visibility = Visibility.Visible;", view);
        Assert.Contains("if (_web == null) _slot.Visibility = Visibility.Collapsed;", view);
        Assert.Contains("HoldReveal(RevealCap)", view);
        Assert.Contains("TimeSpan.FromMilliseconds(150)", view);
        Assert.DoesNotContain("_tongue", view);
    }

    private static string Root(params string[] parts)
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return System.IO.Path.Combine(dir!.FullName, System.IO.Path.Combine(parts));
    }
}
