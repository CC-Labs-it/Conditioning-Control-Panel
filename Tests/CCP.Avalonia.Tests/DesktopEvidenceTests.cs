using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DesktopEvidenceTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    // WPF MainWindow.Haptics.cs:685-694: modal help, then re-read wizard settings.
    [Fact]
    public Task HapticsHelpOpensOwnedWizardAndRefreshesOnClose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var cfg = CoreSettings.Current.Haptics;
        var old = cfg.ButtplugUrl;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("haptics"); Settle();
            var page = shell.Named<StudioTabView>("StudioTab")!.HapticsPanel;
            page.FindControl<Button>("BtnHapticsHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            var wizard = Assert.Single(shell.OwnedWindows.OfType<HapticsSetupWindow>());
            Assert.True(wizard.IsVisible);
            Assert.Same(shell, wizard.Owner);
            cfg.ButtplugUrl = "ws://127.0.0.1:12346";
            wizard.Close();
            await Task.Yield(); Settle();
            Assert.Equal(cfg.ButtplugUrl, page.FindControl<TextBox>("TxtHapticIntifaceUrl")!.Text);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            cfg.ButtplugUrl = old;
            shell.RequestExit();
        }
    });

    // WPF MainWindow.ToolTipHygiene.cs:120-145: code navigation without a mouse leave.
    [Fact]
    public Task NavigationClosesOpenTooltipWithoutPointerMovement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Settle();
            var door = shell.FindControl<Button>("DoorHome")!;
            var point = door.TranslatePoint(new Point(door.Bounds.Width / 2, door.Bounds.Height / 2), shell)!.Value;
            shell.MouseMove(point, RawInputModifiers.None); Settle();
            Assert.True(door.IsPointerOver);
            ToolTip.SetTip(door, "navigation probe");
            ToolTip.SetIsOpen(door, true);
            Assert.True(ToolTip.GetIsOpen(door));
            shell.ShowTab("settings");
            Assert.False(ToolTip.GetIsOpen(door));
            shell.ShowTab("dashboard"); // no tooltip is also safe
        }
        finally { ToolTip.SetIsOpen(shell, false); shell.RequestExit(); }
        return Task.CompletedTask;
    });

    // WPF EmiMutePromptWindow.xaml.cs:37-57,94-95: only an explicit choice mutes.
    [Theory]
    [InlineData("BtnMute", EmiMuteChoice.Mute)]
    [InlineData("BtnKeep", EmiMuteChoice.Keep)]
    [InlineData("BtnDontAsk", EmiMuteChoice.DontAsk)]
    [InlineData("Escape", EmiMuteChoice.Keep)]
    [InlineData("Dismiss", EmiMuteChoice.Keep)]
    public Task MutePromptReturnsTheUsersChoice(string action, EmiMuteChoice expected) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var owner = new Window();
        owner.Show();
        try
        {
            var answer = EmiMutePromptWindow.Ask(owner);
            var prompt = Assert.Single(owner.OwnedWindows.OfType<EmiMutePromptWindow>());
            Assert.True(prompt.IsVisible);
            if (action == "Dismiss") prompt.Close();
            else if (action == "Escape") prompt.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            else prompt.FindControl<Button>(action)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(answer.IsCompletedSuccessfully);
            Assert.Equal(expected, await answer);
            Assert.False(prompt.IsVisible);
        }
        finally
        {
            foreach (var w in owner.OwnedWindows.ToArray()) w.Close();
            owner.Close();
        }
    });

    // WPF TakeoverOrb.cs:130-185,327-339: active state and clock lifecycle.
    [Fact]
    public Task OrbParksAndResumesWithVisibilityAndMotion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode);
        var orb = new TakeoverOrb { Width = 200, Height = 200 };
        var panel = new Border { Child = orb };
        var host = new Window { Content = panel };
        try
        {
            (settings.MotionLevel, settings.PerformanceMode) = (MotionLevel.Full, false);
            orb.Restart(); host.Show(); host.Activate(); Settle();
            Assert.False(orb.IsHitTestVisible);
            orb.SetActive(true);
            Assert.True(orb.IsActive);
            Assert.True(orb.IsRunning);
            orb.Pause(); Assert.False(orb.IsRunning);
            orb.Resume(); Assert.True(orb.IsRunning);
            panel.IsVisible = false; Settle(); Assert.False(orb.IsRunning);
            panel.IsVisible = true; Settle(); Assert.True(orb.IsRunning);
            settings.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(orb.IsRunning);
            settings.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(orb.IsRunning);
            orb.SetActive(false); Assert.False(orb.IsActive);
            orb.Stop(); Assert.False(orb.IsRunning);
            host.Content = null; Settle(); Assert.False(orb.IsRunning);
        }
        finally { host.Close(); (settings.MotionLevel, settings.PerformanceMode) = old; }
        return Task.CompletedTask;
    });
}
