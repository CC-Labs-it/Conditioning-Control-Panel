using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Dashboard pointer behavior, WPF MainWindow.UiUpdates.cs:1189-1250.</summary>
public sealed class DashboardLogoClickTests
{
    private static readonly System.Reflection.PropertyInfo AchievementsProp = typeof(AvApp).GetProperty("Achievements",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
    private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    private static readonly Type Egg = typeof(SettingsTabView);

    private sealed class Reset : IDisposable
    {
        public int Due;
        public Action Fire = null!;
        public bool Cancelled;
        public void Dispose() => Cancelled = true;
    }

    [Fact]
    public void OverlappingPulsesKeepBothLegsOfTheLatestClick() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var logo = new Border(); // No text or font dependency.
        var resets = new List<Reset>();
        int now = 0;
        IDisposable Schedule(Action fire, TimeSpan delay)
        {
            var reset = new Reset { Due = now + (int)delay.TotalMilliseconds, Fire = fire };
            resets.Add(reset);
            return reset;
        }
        void At(int time)
        {
            now = time;
            foreach (var reset in resets.Where(r => r.Due <= now).ToArray())
            {
                if (!reset.Cancelled) reset.Fire();
                resets.Remove(reset);
            }
        }
        double Target() => Assert.IsType<TransformOperations>(logo.GetBaseValue(Visual.RenderTransformProperty).Value).Value.M11;
        var first = SettingsTabView.RestartLogoPulse(logo, null, Schedule);
        var outward = logo.Transitions;
        At(60);
        using var latest = SettingsTabView.RestartLogoPulse(logo, first, Schedule);
        Assert.NotSame(outward, logo.Transitions); // Restart outward from 1, even at the same target.
        Assert.Equal(TimeSpan.FromMilliseconds(80), Assert.IsType<global::Avalonia.Animation.TransformOperationsTransition>(Assert.Single(logo.Transitions!)).Duration);
        At(80); Assert.Equal(1.05, Target()); // The first click must not reverse the latest.
        At(139); Assert.Equal(1.05, Target());
        At(140); Assert.Equal(1, Target()); // Latest outward leg ends after its own 80 ms.
        At(220); Assert.Equal(1, Target()); // Its return transition also lasts 80 ms.
    });

    [Fact]
    public Task WordmarkClicksTrackPulseAndReachTheEasterEgg() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (motionWas, achWas, readersWas) = (s.MotionLevel, AchievementsProp.GetValue(null), SettingsTabView.EasterEggReaders);
        var dir = Directory.CreateTempSubdirectory("ccp-logo-click-").FullName;
        int readerAsks = 0;
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        AchievementsProp.SetValue(null, engine);
        SettingsTabView.EasterEggReaders = () => { readerAsks++; return new TaskCompletionSource<int>().Task; };   // the note never opens
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show(); Dispatcher.UIThread.RunJobs();
            var dash = shell.SettingsPage!;
            var frame = dash.FindControl<Border>("LogoBrandFrame")!;
            var logo = dash.FindControl<AnimatedLogoImage>("ImgLogo")!;
            void Click(MouseButton button = MouseButton.Left)
            {
                var pt = frame.TranslatePoint(new Point(frame.Bounds.Width / 2, frame.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(pt, button); shell.MouseUp(pt, button); Dispatcher.UIThread.RunJobs();
            }

            s.MotionLevel = MotionLevel.Off;
            Click(MouseButton.Right);
            Assert.Equal(0, engine.Progress.AvatarClickCount);
            Assert.Equal(0, readerAsks);
            Click();
            Assert.Equal(1, engine.Progress.AvatarClickCount);
            Assert.Null(logo.RenderTransform);   // no pulse with motion off, as WPF's AllowTransitions gate
            s.MotionLevel = MotionLevel.Full;
            Click();   // the pulse: scale(1.05), back to scale(1) 80 ms later (either may be current under CI load)
            var transform = logo.GetBaseValue(Visual.RenderTransformProperty); Assert.True(transform.HasValue); Assert.IsType<TransformOperations>(transform.Value);
            // Clicks through the shell take seconds each on a loaded runner, so the windows are primed rather
            // than clicked out (thresholds: AchievementEngineTests.AvatarClicksUnlockAtWpfThresholds).
            (engine.Progress.AvatarClickStartTime, engine.Progress.AvatarClickCount) = (DateTime.Now, 19);
            Click();
            Assert.True(engine.Progress.IsUnlocked("neon_obsession"));   // the 20th rapid click
            Egg.GetField("_easterEggFirstClick", Flags)!.SetValue(dash, DateTime.Now);
            Egg.GetField("_easterEggClickCount", Flags)!.SetValue(dash, 98);
            Assert.Equal(0, readerAsks);
            Click();   // the 99th: still nothing
            Assert.Equal(0, readerAsks);
            Click();   // the 100th inside 60 s
            Assert.Equal(1, readerAsks);
            Click();
            Assert.Equal(1, readerAsks);   // once per run, as WPF's _easterEggTriggered
            var pulse = Egg.GetField("_logoPulseReset", Flags)!; (pulse.GetValue(dash) as IDisposable)?.Dispose();
            var pending = new Reset(); pulse.SetValue(dash, pending);
            shell.Content = null; Assert.True(pending.Cancelled); Assert.Null(logo.RenderTransform);
        }
        finally
        {
            shell?.Close(); Dispatcher.UIThread.RunJobs();
            s.MotionLevel = motionWas;
            AchievementsProp.SetValue(null, achWas);
            SettingsTabView.EasterEggReaders = readersWas;
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });
}
