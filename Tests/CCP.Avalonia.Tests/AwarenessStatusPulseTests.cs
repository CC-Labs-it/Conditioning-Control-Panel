using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessStatusPulseTests
{
    // WPF MainWindow.Awareness.cs:52,78: arrival reflects the already-enabled master.
    [Fact]
    public void EnabledMasterPulsesOnAwarenessArrival() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.KeywordTriggersEnabled, s.MotionLevel, s.PerformanceMode);
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? host = null;
        try
        {
            s.KeywordTriggersEnabled = true;
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            host = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            host.Show(); host.Activate();
            host.ShowTab("awareness"); Dispatcher.UIThread.RunJobs();
            var view = host.FindControl<AwarenessTabView>("AwarenessTab")!;
            var glow = view.FindControl<Border>("AwarenessStatusGlow")!;
            Assert.True(view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked);
            Assert.Equal("Live", view.FindControl<TextBlock>("TxtAwarenessStatus")!.Text);
            Assert.True(glow.IsVisible);
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            host.Close(); Dispatcher.UIThread.RunJobs();
            Assert.False(glow.IsVisible);
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
        }
        finally
        {
            host?.Close(); Dispatcher.UIThread.RunJobs();
            (s.KeywordTriggersEnabled, s.MotionLevel, s.PerformanceMode) = old;
        }
    });

    [Fact]
    public void LiveStatusBreathesBehindTheSteadyDotAndParksWithItsHost() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.KeywordTriggersEnabled, s.MotionLevel, s.PerformanceMode);
        var time = new SteppedTime();
        var oldTime = BeatLoop.Time;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        CoreEntitlement.HasPremiumProvider = () => true;
        BeatLoop.Time = time;
        var resources = Application.Current!.Resources;
        var hadLocalTint = resources.Keys.Contains("FxGlowColor");
        var oldTint = resources["FxGlowColor"];
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? host = null;
        try
        {
            s.KeywordTriggersEnabled = false;
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            // The shell's one pulse registry (SetAwarenessStatusPulse), reached the way a user does.
            host = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            host.Show(); host.Activate();
            host.ShowTab("awareness"); Dispatcher.UIThread.RunJobs();
            var view = host.FindControl<AwarenessTabView>("AwarenessTab")!;
            Assert.True(view.IsEffectivelyVisible);
            var dot = view.FindControl<Ellipse>("AwarenessStatusDot")!;
            var glow = view.FindControl<Border>("AwarenessStatusGlow");
            Assert.NotNull(glow);
            Assert.False(glow.IsVisible);
            view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked = true;
            Assert.True(s.KeywordTriggersEnabled); // real consent-gated master editor
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Live", view.FindControl<TextBlock>("TxtAwarenessStatus")!.Text);
            Assert.Null(dot.Effect);
            Assert.Equal(1, dot.Opacity);
            Assert.Same(dot.Parent, glow.Parent); // the moving glow is a sibling, never an Effect ancestor
            Assert.Equal(0.22, glow.Opacity, 3);
            Assert.Equal(10, glow.BoxShadow[0].Blur);
            var loop = Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            Assert.True(loop.OnFrameBeat);
            resources["FxGlowColor"] = Colors.Cyan;
            CoreMods.RaiseModChanged(null, new ModPackage(BuiltInMods.CCPDefault, null, isBuiltIn: true));
            Assert.Equal(Colors.Cyan, glow.BoxShadow[0].Color);
            Assert.Same(loop, Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow)));
            time.Advance(TimeSpan.FromSeconds(2.8)); BeatLoop.StepAll();
            Assert.Equal(0.95, glow.Opacity, 3);
            if (Environment.GetEnvironmentVariable("CCP_TEST_EVIDENCE_DIR") is { Length: > 0 } evidence)
            {
                Directory.CreateDirectory(evidence);
                using var frame = host.CaptureRenderedFrame();
                frame?.Save(System.IO.Path.Combine(evidence, "Awareness-live-status.png"));
            }
            time.Advance(TimeSpan.FromSeconds(2.8)); BeatLoop.StepAll();
            Assert.Equal(0.22, glow.Opacity, 3);
            Assert.Equal(1, dot.Opacity);

            host.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
            Assert.False(loop.IsRunning);
            Assert.False(glow.IsVisible);
            host.ShowTab("awareness"); Dispatcher.UIThread.RunJobs();
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            host.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            host.WindowState = WindowState.Normal; host.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));

            // Headless never deactivates a window when another activates; drive the platform callback.
            typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null); Dispatcher.UIThread.RunJobs();
            Assert.False(host.IsActive);
            Assert.False(glow.IsVisible);
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null); Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsActive);
            s.MotionLevel = MotionLevel.Reduced; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            s.MotionLevel = MotionLevel.Full; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            s.PerformanceMode = true; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(glow.IsVisible);
            s.PerformanceMode = false; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked = false; Dispatcher.UIThread.RunJobs();
            Assert.Equal("Off", view.FindControl<TextBlock>("TxtAwarenessStatus")!.Text);
            Assert.False(glow.IsVisible);
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); // an Off dot must not restart on a gate change
            Assert.False(glow.IsVisible);
        }
        finally
        {
            host?.Close(); Dispatcher.UIThread.RunJobs();
            CoreEntitlement.HasPremiumProvider = oldPremium;
            if (hadLocalTint) resources["FxGlowColor"] = oldTint;
            else resources.Remove("FxGlowColor");
            (s.KeywordTriggersEnabled, s.MotionLevel, s.PerformanceMode) = old;
            BeatLoop.Time = oldTime;
        }
    });
}
