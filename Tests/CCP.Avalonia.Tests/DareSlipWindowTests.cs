using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class DareSlipWindowTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont().With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Control? Find(Control root, string tag) => root.GetLogicalDescendants().OfType<Control>().FirstOrDefault(c => Equals(c.Tag, tag));
    private static void Click(Control root, string tag) => Assert.IsType<Button>(Find(root, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task IntroThenComposeAndSendWritingSlip() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var service = new FakeService();
        var marked = 0;
        var friend = new DareFriend("friend", "Friend", null);
        var window = new DareSlipWindow(service, null, new[] { friend }, null, false, () => marked++);
        try
        {
            Assert.NotNull(Find(window, "dares-intro"));
            Click(window, "dares-intro-go");
            Assert.Equal(1, marked);
            Click(window, "dares-who:friend");
            Click(window, "dares-tile:duel");
            Click(window, "dares-tile:bubbles");
            Click(window, "dares-when:today");
            Click(window, "dares-stake:sp");
            var amount = DareGrammar.AmountsFor(DareStakeKind.Sp)[0];
            Click(window, "dares-amount:" + amount);
            Click(window, "dares-stake:sp");
            Click(window, "dares-amount:" + amount);
            Assert.NotNull(Find(window, "dares-send"));
            var result = await window.SendAsync();
            Assert.Equal(DareCreateStatus.Writing, result!.Status);
            Assert.Equal("friend", service.To);
            Assert.Equal(DareKind.Bubbles, service.Slip!.What.Kind);
            Assert.NotNull(Find(window, "dares-write-line"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task IntroDrawsBundledIconsNotAnIconFont() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        var intro = DareIntro.Build(() => { });
        Assert.Equal(new[] { IconKind.Mail, IconKind.ArrowRight, IconKind.ArrowSwap, IconKind.ArrowRight, IconKind.Clock, IconKind.ArrowRight, IconKind.Star },
            intro.GetLogicalDescendants().OfType<IconGlyph>().Select(g => g.Kind));
        Assert.DoesNotContain(intro.GetLogicalDescendants().OfType<TextBlock>(), t => t.FontFamily.Name.Contains("MDL2") || (t.Text ?? "").Any(c => c >= '\uE000' && c <= '\uF8FF'));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ComposerDrawsTheSlipPressedTilesAndShortAmountReasons() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        var window = new DareSlipWindow(new FakeService { Sparkles = 60 }, new DareFriend("friend", "Friend", null), Array.Empty<DareFriend>(), null, true, () => { });
        try
        {
            Assert.Equal(DareSlipLook.SlipPaper, Assert.IsType<SolidColorBrush>(Assert.IsType<Border>(Find(window, "dares-slip")).Background).Color);
            Click(window, "dares-tile:duel");
            Assert.Contains(Find(window, "dares-chip:How")!.GetLogicalDescendants().OfType<TextBlock>(), t => t.FontFamily == DareSlipLook.Hand);
            Click(window, "dares-chip:How");
            Assert.Contains("on", Find(window, "dares-tile:duel")!.Classes);
            Assert.DoesNotContain("on", Find(window, "dares-tile:cant")!.Classes);
            foreach (var tag in new[] { "dares-tile:duel", "dares-tile:bubbles", "dares-when:today", "dares-stake:sp" }) Click(window, tag);
            var shortChip = Assert.IsType<Button>(Find(window, "dares-amount:100:short"));
            var have = Loc.GetF("dares_sp_have", 60);
            Assert.False(shortChip.IsEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(shortChip));
            Assert.Equal(have, ToolTip.GetTip(shortChip));
            Assert.Equal(have, AutomationProperties.GetHelpText(shortChip));
            Assert.True(Assert.IsType<Button>(Find(window, "dares-amount:50")).IsEnabled);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private sealed class FakeService : IDaresService
    {
        public int? Sparkles { get; init; }
        public event Action? Changed { add { } remove { } }
        public DaresSnapshot Snapshot => new(true, new DaresMe(null, true, true, 0), Array.Empty<DareBet>());
        public bool IsPreview => false;
        public string? To { get; private set; }
        public DareSlip? Slip { get; private set; }
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<DareCreateOutcome> CreateAsync(string friendId, DareSlip slip, CancellationToken ct = default)
        { To = friendId; Slip = slip; return Task.FromResult(new DareCreateOutcome(DareCreateStatus.Writing, WriteCode: "ABC")); }
        public Task<DareActResult> ActAsync(string betId, DareOp op, CancellationToken ct = default) => Task.FromResult(DareActResult.Done);
    }
}
