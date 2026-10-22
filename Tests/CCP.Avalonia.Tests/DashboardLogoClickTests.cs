using System;
using System.IO;
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

/// <summary>
/// WPF ImgLogo_MouseLeftButtonDown (MainWindow.UiUpdates.cs:1212) through real presses on the
/// Dashboard wordmark: the achievement track (neon_obsession at 20 rapid clicks), the click pulse
/// under motion, and the 100th click asking the server for the reader count before the note opens.
/// </summary>
public sealed class DashboardLogoClickTests
{
    private static readonly System.Reflection.PropertyInfo AchievementsProp = typeof(AvApp).GetProperty("Achievements",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;

    private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    private static readonly Type Egg = typeof(SettingsTabView);

    [Fact]
    public Task WordmarkClicksTrackPulseAndReachTheEasterEgg() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (motionWas, achWas, readersWas) = (s.MotionLevel, AchievementsProp.GetValue(null), SettingsTabView.EasterEggReaders);
        var dir = Directory.CreateTempSubdirectory("ccp-logo-click-").FullName;
        int readerAsks = 0;
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        AchievementsProp.SetValue(null, engine);
        SettingsTabView.EasterEggReaders = () => { readerAsks++; return new TaskCompletionSource<int>().Task; };   // the note never opens
        var shell = new MainShellWindow();
        shell.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var dash = shell.SettingsPage!;
            var frame = dash.FindControl<Border>("LogoBrandFrame")!;
            var logo = dash.FindControl<AnimatedLogoImage>("ImgLogo")!;
            void Click()
            {
                var pt = frame.TranslatePoint(new Point(frame.Bounds.Width / 2, frame.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(pt, MouseButton.Left); shell.MouseUp(pt, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            }

            s.MotionLevel = MotionLevel.Off;
            Click();
            Assert.Equal(1, engine.Progress.AvatarClickCount);
            Assert.Null(logo.RenderTransform);   // no pulse with motion off, as WPF's AllowTransitions gate
            s.MotionLevel = MotionLevel.Full;
            Click();   // the pulse: scale(1.05), back to scale(1) 80 ms later (either may be current under CI load)
            Assert.IsType<TransformOperations>(logo.GetBaseValue(Visual.RenderTransformProperty).Value);
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
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
            s.MotionLevel = motionWas;
            AchievementsProp.SetValue(null, achWas);
            SettingsTabView.EasterEggReaders = readersWas;
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });
}
