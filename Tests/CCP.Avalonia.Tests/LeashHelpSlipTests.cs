using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>sync7-leash-b (WPF main 325115b21, Tests/ConditioningControlPanel.Tests/LeashHelpSlipTests.cs): the
/// holder's "?" ink slips. Every catalog entry lays out as a paper card with its stamp, the caption names the
/// other player, every pictogram draws, accents stay readable on paper, and the "?" opens on hover, pins on
/// click without firing anything around it, and folds on a press elsewhere.</summary>
public sealed class LeashHelpSlipTests
{
    private static void Ensure()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    [Fact]
    public void EveryPictogramIsDrawn() => AvaloniaTestDispatcher.Run(() =>
    {
        Ensure();
        foreach (var e in LeashHelpCatalog.All) Assert.Contains(e.Pictogram, LeashHelpCatalog.Pictograms);
        foreach (var p in LeashHelpCatalog.Pictograms)
            Assert.True(LeashHelpSlip.Pictogram(p).Children.Count >= 2, p);
    });

    [Fact]
    public void AccentsFollowTheCardAndStayReadableOnPaper() => AvaloniaTestDispatcher.Run(() =>
    {
        Ensure();
        Assert.Equal(Color.FromRgb(0xFF, 0xCF, 0x6B), LeashHelpSlip.AccentFor("assign").Bright);
        Assert.Equal(LeashHelpSlip.MintAccent, LeashHelpSlip.AccentFor("reward"));
        foreach (var k in Enum.GetValues<PunishKind>()) Assert.Equal(LeashHelpSlip.RedAccent, LeashHelpSlip.AccentFor(LeashHelpCatalog.Id(k)));
        Assert.Equal(LeashHelpSlip.PinkAccent, LeashHelpSlip.AccentFor("praise"));
        Assert.Equal(LeashHelpSlip.LilacAccent, LeashHelpSlip.AccentFor("pardon"));
        Assert.Equal(LeashHelpSlip.MintAccent, LeashHelpSlip.AccentFor("credit"));
        foreach (var e in LeashHelpCatalog.All)
            Assert.True(LeashHelpSlip.Contrast(LeashHelpSlip.AccentFor(e.Id).Deep, LeashHelpSlip.PaperColor) >= 4.5, e.Id);
    });

    [Fact]
    public void SlipNamesTheOtherPlayer() => AvaloniaTestDispatcher.Run(() =>
    {
        Ensure();
        var tug = new LeashHelpSlip(LeashHelpCatalog.Get("tug")!, "Vex");
        Assert.Contains("Vex", tug.CaptionText);
        Assert.DoesNotContain("{0}", tug.CaptionText);
        Assert.Equal(Loc.Get("leash_slip_assign_cap"), new LeashHelpSlip(LeashHelpCatalog.Get("assign")!, "Vex").CaptionText);
    });

    [Fact]
    public void EverySlipLaysOutWithItsWords() => AvaloniaTestDispatcher.Run(() =>
    {
        Ensure();
        foreach (var e in LeashHelpCatalog.All)
        {
            var slip = new LeashHelpSlip(e, "Mira");
            slip.Measure(Size.Infinity);
            slip.Arrange(new Rect(slip.DesiredSize));
            Assert.True(slip.Bounds.Height > 120, e.Id);
            var words = slip.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(Loc.Get(e.TitleKey), words);
            Assert.Contains(LeashHelpCatalog.StampText(e.Stamp, Loc.Get), words);
            Assert.Contains(LeashHelpCatalog.YouLabel(e, Loc.Get), words);
            Assert.Contains(Loc.Get(e.LimitsKey), words);
            Assert.DoesNotContain(words, w => w != null && w.StartsWith("leash_", StringComparison.Ordinal));
        }
        var strict = new LeashHelpSlip(LeashHelpCatalog.ForPunish(PunishKind.Chaster), "Mira");
        Assert.Contains("STRICT", strict.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
    });

    [Fact]
    public void HelpOpensOnHoverPinsOnClickAndFoldsOnAPressElsewhere() => AvaloniaTestDispatcher.Run(() =>
    {
        Ensure();
        var help = LeashHelpButton.Create(LeashHelpCatalog.Get("reward")!, () => "Mira");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20) };
        row.Children.Add(help);
        int around = 0;
        row.AddHandler(Button.ClickEvent, (_, _) => around++, RoutingStrategies.Bubble, handledEventsToo: false);
        var w = new Window { Width = 700, Height = 600, Content = row };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("leash-slip-help:reward", help.Tag);
            Assert.False(help.Focusable);
            var at = help.TranslatePoint(new Point(8, 8), w)!.Value;
            var away = new Point(680, 580);   // below and right of the slip

            w.MouseMove(at, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(LeashHelpButton.IsOpen(help));
            Assert.False(LeashHelpButton.IsPinned(help));
            Assert.Equal("leash-slip:reward", LeashHelpButton.Slip(help)!.Tag);

            Click(w, at);
            Assert.True(LeashHelpButton.IsPinned(help));
            Assert.Equal(0, around);   // the "?" keeps its click: the command next to it never fires

            var slip = LeashHelpButton.Slip(help)!;
            var slipHost = TopLevel.GetTopLevel(slip)!;
            var inSlip = slip.TranslatePoint(new Point(150, 60), slipHost)!.Value;
            Click(slipHost, inSlip);
            Assert.True(LeashHelpButton.IsOpen(help));

            Click(w, away);
            Assert.False(LeashHelpButton.IsOpen(help));
            Assert.False(LeashHelpButton.IsPinned(help));

            Click(w, at);   // hover reopens, the click pins
            Assert.True(LeashHelpButton.IsPinned(help));
            Click(w, at);   // a second click unpins and closes
            Assert.False(LeashHelpButton.IsOpen(help));

            LeashHelpButton.Pin(help);
            LeashHelpButton.Pin(help);   // already pinned stays open
            Assert.True(LeashHelpButton.IsOpen(help));
            help.IsVisible = false;
            Assert.False(LeashHelpButton.IsOpen(help));
        }
        finally { w.Close(); }
    });

    private static void Click(TopLevel w, Point p)
    {
        w.MouseMove(p, RawInputModifiers.None);
        w.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        w.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
}
