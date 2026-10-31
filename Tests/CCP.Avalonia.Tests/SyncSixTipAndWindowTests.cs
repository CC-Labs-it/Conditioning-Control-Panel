using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.JustDrop;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SyncSixTipAndWindowTests
{
    private static void Platform()
    {
        AmbientBeatLoopsTests.Setup();
        LocalizationManager.Instance.SetLanguage("en");
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
    }

    [Fact]
    public Task XpBeadCenterStaysOnTheTipAndShortFillsHideIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        var track = shell.Named<Border>("XPBarTrack")!;
        ((Panel)track.Parent!).Children.Remove(track);
        var host = new Window { Content = track, Width = 600, Height = 40 };
        try
        {
            host.Show();
            host.UpdateLayout();
            var bar = shell.Named<Border>("XPBar")!;
            var bead = shell.Named<Ellipse>("XPTubeBead")!;
            var meniscus = shell.Named<Border>("XPMeniscus")!;
            var fill = Assert.IsType<Grid>(bar.Child);
            Assert.True(fill.Children.IndexOf(bead) < fill.Children.IndexOf(shell.Named<Border>("XPBarSheen")!),
                "WPF paints the bead below the sheen");
            Assert.Equal(new CornerRadius(5), shell.Named<Border>("XPBarFlashOverlay")!.CornerRadius);
            Assert.Equal(track.Height, meniscus.Height);
            Assert.Equal(10, meniscus.Width);
            var brush = Assert.IsType<RadialGradientBrush>(bead.Fill);
            Assert.Equal(new RelativePoint(0.5, 0.5, RelativeUnit.Relative), brush.Center);
            foreach (var width in new[] { 0d, 4, 5, 30, 300, 498, 500 })
            {
                bar.Width = width;
                host.UpdateLayout();
                Assert.Equal(width >= 5, bead.IsVisible);
                if (width < 5) continue;
                var center = bead.TranslatePoint(new Point(bead.Bounds.Width / 2, 4), bar)!.Value.X;
                Assert.Equal(width, center, 2);
            }
        }
        finally { host.Close(); shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task JustDropPillAsksAndOnlyAnAffirmativeAnswerRequestsTheWindow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var enabled = typeof(JustDropService).GetField("ServerEnabled", BindingFlags.NonPublic | BindingFlags.Static)!;
        var before = enabled.GetValue(null);
        var canOpen = typeof(MainShellWindow).GetMethod("CanOpenNavTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tab = new NavTab("justdrop", "jd_door_title", NavTabKind.Window);
        try
        {
            enabled.SetValue(null, true);
            Assert.True((bool)canOpen.Invoke(null, new object[] { tab })!);
            // Observe the strip's request without opening a web host.
            var isolated = new SectionTabStrip { MotionOverride = MotionLevel.Off };
            var host = new Window { Content = isolated, Width = 1400, Height = 160 };
            try
            {
                host.Show();
                isolated.Show(NavSections.Studio, "presets");
                host.UpdateLayout();
                int requests = 0;
                isolated.TabRequested += _ => requests++;
                void Click() => isolated.PillFor("justdrop")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Click();
                Assert.Equal("justdrop", isolated.ConfirmingKey);
                Assert.Equal(0, requests);
                isolated.AnswerConfirmForTests(false);
                Assert.Null(isolated.ConfirmingKey);
                Assert.Equal(0, requests);
                Click();
                isolated.AnswerConfirmForTests(true);
                Assert.Equal(1, requests);
                Assert.Equal("presets", isolated.ActivePillKey);
                Click();
                isolated.Rebuild();
                Assert.Null(isolated.ConfirmingKey);
                isolated.AnswerConfirmForTests(true);
                Assert.Equal(1, requests);
                Click();
                host.Content = null;
                Assert.Null(isolated.ConfirmingKey);
            }
            finally { host.Close(); }
            enabled.SetValue(null, false);
            Assert.False((bool)canOpen.Invoke(null, new object[] { tab })!);
        }
        finally { enabled.SetValue(null, before); }
        return Task.CompletedTask;
    });
}
