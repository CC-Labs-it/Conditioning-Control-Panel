using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>views-tab-presets: the corner-GIF option panel (WPF MainWindow.Presets.cs:515-636, StartSession :1586)
/// driving the session-scoped corner overlay (WPF SessionEngine's own corner window), the rack rows' hover reveal
/// (WPF SdRackActions) and the selected-card sheen (WPF OnPresetsTabVisibilityChanged / RefreshCardSheen).</summary>
public sealed class PresetsTabCornerGifWireTests
{
    // 4x4, three frames (same GIF as CornerGifOverlayTests).
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    [Fact]
    public Task CornerGifOptionShowsTheSessionOverlay_LiveEditsMoveIt_StandaloneWaits_StopHandsBack_PanicCloses() => Run(async (shell, tab) =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-sessiongif-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        var s = CoreSettings.Current;
        var saved = (s.CornerGifOverlays, s.SessionCornerGifAllowed, s.PanicKeyEnabled, s.PanicKey);
        var handlers = (CoreCornerGif.RefreshHandler, CoreCornerGif.SessionShowHandler, CoreCornerGif.SessionCloseHandler,
            CoreCornerGif.StandaloneActiveProvider, CoreCornerGif.SpiralOverlayActiveProvider);
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            CornerGifOverlay.StopAll();
            CornerGifOverlay.SkipPlatformChecksForTests = true;
            CornerGifOverlay.ManualFramesForTests = true;
            CornerGifOverlay.Seed(() => shell);
            (s.SessionCornerGifAllowed, s.PanicKeyEnabled, s.PanicKey) = (true, true, "F8");
            s.CornerGifOverlays = new List<CornerGifOverlaySetting>();

            // The user path: the Gamer Girl row shows the option, unticked, with its own description.
            var row = tab.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>()
                .First(b => b.Tag is Session { Id: "gamer_girl" });
            row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.CornerGifOptionPanel.IsVisible);
            Assert.Equal(Loc.Get("session_gamer_girl_corner_gif_desc"), tab.TxtCornerGifDesc.Text);
            Assert.False(tab.ChkCornerGifEnabled.IsChecked == true || tab.CornerGifSettings.IsVisible);

            tab.ChkCornerGifEnabled.IsChecked = true;
            Assert.True(tab.CornerGifSettings.IsVisible);
            tab.SetCornerGifPath(gif);   // the picker's result (a StorageProvider dialog cannot open headless)
            Assert.Equal("tiny.gif", ((IconLabel)tab.BtnSelectCornerGif.Content!).Label.Text);
            tab.RbCornerTR.IsChecked = true;
            tab.SliderCornerGifSize.Value = 200;
            tab.SliderCornerGifOpacity.Value = 50;
            Assert.Equal(("200px", "50%"), (tab.TxtCornerGifSize.Text, tab.TxtCornerGifOpacity.Text));

            // Start: the option is written into the session, the runner raises the overlay in its corner.
            var session = new Session { Id = "corner_wire", Name = "Corner Wire", DurationMinutes = 5, HasCornerGifOption = true };
            shell.StartSession(session);
            await Settle();
            Assert.True(runner.CornerGifShown);
            Assert.Equal((true, gif, CornerPosition.TopRight, 200, 50), (session.Settings.CornerGifEnabled, session.Settings.CornerGifPath,
                session.Settings.CornerGifPosition, session.Settings.CornerGifSize, session.Settings.CornerGifOpacity));
            var screen = shell.Screens.Primary ?? shell.Screens.All[0];
            double k = screen.Scaling;
            var w = CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot)!;
            Assert.Equal(0.5, w.Spiral.Opacity, 3);
            Assert.Equal((screen.Bounds.Right - (int)Math.Round(200 * k), screen.Bounds.Y), (w.Position.X, w.Position.Y));
            Assert.False(CornerGifOverlay.HasActiveOverlays);                 // the session slot is not a standalone one
            Assert.False(File.Exists(CornerGifOverlay.SentinelPath));         // nor does it arm the standalone sentinel

            // #474 live edits: a corner pick moves it, the debounced opacity re-shows it.
            tab.RbCornerBL.IsChecked = true;
            await Settle();
            w = CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot)!;
            Assert.Equal((screen.Bounds.X, screen.Bounds.Bottom - (int)Math.Round(200 * k)), (w.Position.X, w.Position.Y));
            tab.SliderCornerGifOpacity.Value = 80;
            tab.FlushCornerGifEdit();   // the 400 ms debounce's tick
            await Settle();
            Assert.Equal(0.8, CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot)!.Spiral.Opacity, 3);

            // A standalone slot switched on mid-session stands down while the session owns the corner ...
            s.CornerGifOverlays.Add(new CornerGifOverlaySetting { Enabled = true, GifPath = gif, Size = 100, Opacity = 30 });
            CornerGifOverlay.Refresh(shell);
            await Settle();
            Assert.Equal(new[] { CornerGifOverlay.SessionSlot }, CornerGifOverlay.ShownSlots);

            // ... and gets it back when the session ends (the handback).
            runner.Stop(completed: false);
            await Settle();
            Assert.False(runner.CornerGifShown);
            Assert.Equal(new[] { 0 }, CornerGifOverlay.ShownSlots);

            // Panic takes the session's overlay down too (the 'corner-gif' surface + the session pause).
            s.CornerGifOverlays.Clear();
            CornerGifOverlay.Refresh(shell);
            tab.ChkCornerGifEnabled.IsChecked = true;
            shell.StartSession(new Session { Id = "corner_wire_2", Name = "Corner Wire 2", DurationMinutes = 5, HasCornerGifOption = true });
            await Settle();
            Assert.Equal(new[] { CornerGifOverlay.SessionSlot }, CornerGifOverlay.ShownSlots);
            shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
            await Settle();
            Assert.Empty(CornerGifOverlay.ShownSlots);
            Assert.False(runner.CornerGifShown);
        }
        finally
        {
            runner.Stop();
            AvApp.Sessions = null;
            CornerGifOverlay.StopAll();
            CornerGifOverlay.SkipPlatformChecksForTests = false;
            CornerGifOverlay.ManualFramesForTests = false;
            (CoreCornerGif.RefreshHandler, CoreCornerGif.SessionShowHandler, CoreCornerGif.SessionCloseHandler,
                CoreCornerGif.StandaloneActiveProvider, CoreCornerGif.SpiralOverlayActiveProvider) = handlers;
            (s.CornerGifOverlays, s.SessionCornerGifAllowed, s.PanicKeyEnabled, s.PanicKey) = saved;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public Task RowActionsShowOnlyUnderThePointer_AndTheSheenFollowsTheSelectionWhileTheTabIsShown() => Run((shell, tab) =>
    {
        var motion = CoreSettings.Current.MotionLevel;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            Assert.True(AmbientFxCanvas.Env.AllowAmbientLoops);
            var rows = tab.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().ToList();
            StackPanel Actions(Border r) => ((Grid)r.Child!).Children.OfType<StackPanel>().Single(p => p.Classes.Contains("rackActions"));
            Assert.All(rows, r => Assert.Equal(0, Actions(r).Opacity));
            Assert.All(rows, r => Assert.False(Actions(r).IsHitTestVisible));
            // WPF Hidden leaves the tab order too: Tab from a resting row never lands on its invisible Edit/Delete.
            Visual? TabFrom(Border r)
            {
                Assert.True(r.Focus());
                shell.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                return shell.FocusManager?.GetFocusedElement() as Visual;
            }
            Assert.False(TabFrom(rows[0]) is { } v && Actions(rows[0]).IsVisualAncestorOf(v));

            var hovered = rows[0];
            shell.MouseMove(hovered.TranslatePoint(new Point(hovered.Bounds.Width / 2, hovered.Bounds.Height / 2), shell)!.Value, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Actions(hovered).Opacity);
            Assert.True(Actions(hovered).IsHitTestVisible);
            Assert.True(TabFrom(hovered) is { } hv && Actions(hovered).IsVisualAncestorOf(hv));   // under the pointer they are reachable again
            Assert.Equal(0, Actions(rows[1]).Opacity);   // only the row under the pointer

            // The sheen sits on the selected rack row, then on the picked preset chip.
            Assert.Null(tab.CardSheenHost);
            rows[1].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
            var selectedRow = tab.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>()
                .First(r => ReferenceEquals(r.Tag, rows[1].Tag));
            Assert.Same(selectedRow, tab.CardSheenHost);

            var chip = tab.PresetCardsPanel.Children.OfType<Border>().First(b => b.Tag is string);
            var id = (string)chip.Tag!;
            var at = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), shell)!.Value;
            shell.MouseDown(at, MouseButton.Left);
            shell.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(id, (tab.CardSheenHost as Border)?.Tag);   // the rail rebuilds: the NEW chip wears it

            // Leaving the tab drops it; coming back puts it back; reduced motion never has one.
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(tab.CardSheenHost);
            shell.ShowTab("presets");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(id, (tab.CardSheenHost as Border)?.Tag);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            tab.RefreshCardSheen();
            Assert.Null(tab.CardSheenHost);
        }
        finally { CoreSettings.Current.MotionLevel = motion; }
        return Task.CompletedTask;
    });

    private static async Task Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            while (CornerGifOverlay.LastDecode is { IsCompleted: false } d) await d;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Task Run(Func<MainShellWindow, PresetsTabView, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            shell.ShowTab("presets");
            Dispatcher.UIThread.RunJobs();
            await body(shell, shell.Named<PresetsTabView>("PresetsTab")!);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
        }
    });
}
