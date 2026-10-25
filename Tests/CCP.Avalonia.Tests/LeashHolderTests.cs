using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LeashHolderTests
{
    private sealed class Wire : ILeashApi
    {
        public readonly List<(string Op, JObject Body)> Calls = new();
        public JObject Reply = new() { ["ok"] = true, ["status"] = "sent" };
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        { Calls.Add((op, (JObject)body.DeepClone())); return Task.FromResult<JObject?>(Reply); }
    }
    private static JObject Block(string intensity = "standard", bool quiet = false, int punished = 0, JObject? report = null, JArray? week = null) => new()
    {
        ["me"] = null, ["offers"] = new JArray(), ["events"] = new JArray(),
        ["holding"] = new JArray { new JObject
        {
            ["who"] = new JObject { ["id"] = "u_pet", ["name"] = "Pet" }, ["online"] = true,
            ["intensity"] = intensity, ["since"] = DateTimeOffset.UtcNow.ToString("o"), ["day"] = 3,
            ["dnd_until"] = quiet ? DateTimeOffset.UtcNow.AddHours(1).ToString("o") : null,
            ["report"] = report, ["week"] = week ?? new JArray(), ["pending"] = new JArray(),
            ["assignment"] = null, ["punished_today"] = punished, ["video_max"] = 12,
        } },
    };
    private static void Run(Action<LeashDrawerSection, LeashService, Wire> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var wire = new Wire();
        var service = new LeashService(wire, () => "u_me", cutSafety: () => { });
        service.ApplyBlock(Block());
        var section = new LeashDrawerSection(() => service);
        section.OnShown();
        try { body(section, service, wire); }
        finally { section.Unsubscribe(); LeashHolderCard.CloseSheetsForPanic(); }
    });
    private static Button Button(Control root, string tag) => root.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag as string == tag);
    private static void Click(Control root, string tag) => LeashGateCardTests.Click(Button(root, tag));

    [Fact]
    public void WeekStripDrawsAMarkPerDayWithoutEnumTextAndTheTabLineSitsUnderTheLockFigure() => Run((section, service, _) =>
    {
        var week = new JArray(new[] { "g", "x", "r", "t" }.Select((c, i) => new JObject { ["day"] = $"2026-10-0{i + 1}", ["c"] = c }));
        var report = new JObject { ["day"] = "2026-10-04", ["chaster_linked"] = true, ["lock_left_s"] = 3600, ["tab_s"] = 60 };
        service.ApplyBlock(Block(report: report, week: week));
        var card = Assert.Single(section.HolderCards);
        var cells = card.GetLogicalDescendants().OfType<StackPanel>().Select(p => p.Tag as string).Where(t => t?.StartsWith("leash-week:") == true);
        Assert.Equal(new[] { "leash-week:Did", "leash-week:Idle", "leash-week:Punished", "leash-week:Today" }, cells);
        foreach (var mark in Enum.GetNames<WeekMark>())
            Assert.DoesNotContain(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains(mark) == true);
        var lockFig = card.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Tag as string == "leash-fig-lock");
        Assert.Contains(lockFig.GetLogicalDescendants().OfType<TextBlock>(), t => t.Tag as string == "leash-fig-tab");
    });

    [Fact]
    public void ResultLineClearsOnAThreeSecondTimer() => Run((section, _, _) =>
    {
        var card = Assert.Single(section.HolderCards);
        Click(card, "leash-btn:assign");
        Click(card, "leash-assign:minutes:15");
        Assert.Contains(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Tag as string == "leash-result");
        Assert.Equal(TimeSpan.FromSeconds(3), card.ResultTimer!.Interval);
        Assert.True(card.ResultTimer.IsEnabled);
        card.ClearResult();
        Assert.Null(card.ResultText);
        Assert.False(card.ResultTimer.IsEnabled);
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Tag as string == "leash-result");
    });

    [Fact]
    public void HoldingRendersAndPollKeepsTheOpenSheetUntilTheLeashEnds() => Run((section, service, _) =>
    {
        Assert.True(section.IsVisible);
        var card = Assert.Single(section.HolderCards);
        Click(card, "leash-btn:assign");
        Assert.Equal("assign", card.OpenSheet);
        service.ApplyBlock(Block("strict"));
        Assert.Same(card, Assert.Single(section.HolderCards));
        Assert.Equal("assign", card.OpenSheet);
        service.ApplyBlock(new JObject { ["me"] = null, ["holding"] = new JArray(), ["offers"] = new JArray(), ["events"] = new JArray() });
        Assert.Empty(section.HolderCards);
        Assert.False(section.IsVisible);
    });

    [Fact]
    public void QuietDisablesAssignPunishAndTugButAllowsRewardsAndPanicClosesSheets() => Run((section, service, _) =>
    {
        service.ApplyBlock(Block(quiet: true));
        var card = Assert.Single(section.HolderCards);
        foreach (var id in new[] { "assign", "punish", "tug" }) Assert.False(Button(card, "leash-btn:" + id).IsEnabled);
        Assert.True(Button(card, "leash-btn:reward").IsEnabled);
        Click(card, "leash-btn:reward");
        PanicSurfaces.All.Single(s => s.Id == "leash-holder-sheets").Stop(null);
        Assert.Null(card.OpenSheet);
    });

    [Fact]
    public void PunishSheetHidesForbiddenChoicesAndDailyCapReplacesAllChoices() => Run((section, service, _) =>
    {
        service.ApplyBlock(Block("soft"));
        var card = Assert.Single(section.HolderCards);
        Click(card, "leash-btn:punish");
        var tags = card.GetLogicalDescendants().OfType<Button>().Select(b => b.Tag as string).ToArray();
        Assert.DoesNotContain(tags, t => t?.StartsWith("leash-punish:bubbles:") == true);
        Assert.DoesNotContain(tags, t => t?.StartsWith("leash-punish:chaster:") == true);
        Assert.Contains(tags, t => t?.StartsWith("leash-punish:lines:") == true);
        service.ApplyBlock(Block("strict", punished: 3));
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<Button>(), b => (b.Tag as string)?.StartsWith("leash-punish:") == true);
        Assert.Contains(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("leash_sheet_punish_cap"));
    });

    [Fact]
    public void AssignmentRewardAndPunishmentSendTheSelectedPresetAndOnlySuccessClosesTheSheet() => Run((section, _, wire) =>
    {
        var card = Assert.Single(section.HolderCards);
        Click(card, "leash-btn:assign");
        Click(card, "leash-assign:minutes:15");
        Assert.Equal("assign", wire.Calls.Last().Op);
        Assert.Equal(15, (int?)wire.Calls.Last().Body["size"]);
        Assert.Null(card.OpenSheet);
        Assert.NotNull(card.ResultText);
        Click(card, "leash-btn:reward");
        Click(card, "leash-reward:pardon");
        Assert.Equal("reward", wire.Calls.Last().Op);
        Assert.Equal("pardon", (string?)wire.Calls.Last().Body["kind"]);
        Assert.Null(card.OpenSheet);
        wire.Reply = new JObject { ["ok"] = false, ["reason"] = "failed" };
        Click(card, "leash-btn:punish");
        var first = card.GetLogicalDescendants().OfType<Button>().First(b => (b.Tag as string)?.StartsWith("leash-punish:lines:") == true);
        LeashGateCardTests.Click(first);
        Assert.Equal("punish", card.OpenSheet);
        Assert.NotNull(card.ResultText);
    });

    [Fact]
    public void ReceiptsRepaintTheExistingCardWithoutClosingItsSheet() => Run((section, service, _) =>
    {
        var card = Assert.Single(section.HolderCards);
        Click(card, "leash-btn:assign");
        Assert.DoesNotContain(card.GetLogicalDescendants().OfType<TextBlock>(), t => (t.Tag as string)?.StartsWith("leash-sent-row:") == true);
        service.ApplyReceipts(new[] { new LeashReceipt("0123456789abcdef", LeashItemKind.Assign, "u_pet", "Pet", LeashStep.Seen, DateTimeOffset.UtcNow) });
        Assert.Equal("assign", card.OpenSheet);
        Assert.Same(card, Assert.Single(section.HolderCards));
        Assert.Contains(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Tag as string == "leash-sent-row:assign:seen");
    });

    [Fact]
    public void VideoPickerFiltersNumbersAndRespectsTheirCapAndTugIsLocallyThrottled() => Run((section, _, wire) =>
    {
        var card = Assert.Single(section.HolderCards);
        Click(card, "leash-btn:punish");
        var pick = card.GetLogicalDescendants().OfType<Button>().Single(b => (b.Tag as string)?.StartsWith("leash-punish:video:") == true);
        LeashGateCardTests.Click(pick);
        var cap = card.GetLogicalDescendants().OfType<LeashCapSlider>().Single();
        Assert.Equal(12, cap.Ceiling); Assert.True(cap.Value <= 12);
        var box = card.GetLogicalDescendants().OfType<TextBox>().Single();
        Assert.False(Button(card, "leash-video-send").IsEnabled);
        box.Text = "ab123x";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("123", box.Text);
        Click(card, "leash-video-send");
        Assert.Equal("123", (string?)wire.Calls.Last().Body["watch"]?["id"]);
        Click(card, "leash-btn:tug");
        Click(card, "leash-btn:tug");
        Assert.Equal(1, wire.Calls.Count(c => c.Op == "tug"));
    });
}
