using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MultiMonitorResultTests
{
    private static Screen MakeScreen(double scaling, PixelRect bounds, PixelRect workingArea, bool primary)
    {
        // Avalonia's reference assembly hides the runtime platform-screen constructor.
        // These synthetic handles never reach a native backend or the real desktop.
        // Pinned to Avalonia 12.1 internals (PlatformScreen ctor, Screen setters): an Avalonia
        // upgrade that renames them throws here; fix this factory, not the assertions.
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(bounds.X), "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, scaling);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, workingArea);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, primary);
        return screen;
    }

    private static void Setup()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Theory]
    [InlineData(true, 2, true)]
    [InlineData(false, 1, true)]
    [InlineData(true, 2, false)]
    [InlineData(false, 1, false)]
    public void ResultsUseTheReportedPrimaryAndMirrorsEchoInput(bool dual, int count, bool markedPrimary) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldQuery = ScreenList.QueryOverride;
        var oldDual = CoreSettings.Current.DualMonitorEnabled;
        var secondary = MakeScreen(1.5, new PixelRect(-1920, 40, 1920, 1080), new PixelRect(-1920, 40, 1920, 1040), false);
        var primary = MakeScreen(2, new PixelRect(320, 40, 2560, 1440), new PixelRect(320, 40, 2560, 1400), markedPrimary);
        var typistScreen = markedPrimary ? primary : secondary;
        ScreenList.QueryOverride = _ => new[] { secondary, primary };
        CoreSettings.Current.DualMonitorEnabled = dual;
        int completions = 0;
        try
        {
            BubbleCountResultWindow.ShowOnAllMonitors(42, strictMode: true, _ => completions++);
            Dispatcher.UIThread.RunJobs();
            var windows = BubbleCountResultWindow.OpenWindows.ToArray();
            Assert.Equal(count, windows.Length);
            var typist = windows.Single(w => !w.FindControl<TextBox>("TxtAnswer")!.IsReadOnly);
            Assert.Equal(typistScreen.Bounds.Position, typist.Position);
            typist.FindControl<TextBox>("TxtAnswer")!.Text = "12";
            Dispatcher.UIThread.RunJobs();
            foreach (var mirror in windows.Where(w => w != typist))
            {
                var mirrorScreen = markedPrimary ? secondary : primary;
                Assert.Equal(mirrorScreen.Bounds.Position, mirror.Position);
                var input = mirror.FindControl<TextBox>("TxtAnswer")!;
                Assert.True(input.IsReadOnly);
                Assert.False(input.Focusable);
                Assert.Equal("12", input.Text);
                Assert.False(mirror.FindControl<Button>("BtnSubmit")!.IsEnabled);
            }
            int closed = 0;
            foreach (var window in windows) window.Closed += (_, _) => closed++;
            BubbleCountResultWindow.ForceCloseAll();
            Assert.Empty(BubbleCountResultWindow.OpenWindows);
            Assert.Equal(0, completions);
            Assert.Equal(count, closed);
        }
        finally
        {
            BubbleCountResultWindow.ForceCloseAll();
            ScreenList.QueryOverride = oldQuery;
            CoreSettings.Current.DualMonitorEnabled = oldDual;
        }
    });

    [Fact]
    public void AnUnavailableTopologyKeepsOneResultAndNoCompletion() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldQuery = ScreenList.QueryOverride;
        var oldDual = CoreSettings.Current.DualMonitorEnabled;
        ScreenList.QueryOverride = _ => Array.Empty<Screen>();
        CoreSettings.Current.DualMonitorEnabled = true;
        int completed = 0;
        try
        {
            BubbleCountResultWindow.ShowOnAllMonitors(42, false, _ => completed++);
            Assert.Single(BubbleCountResultWindow.OpenWindows);
            Assert.False(BubbleCountResultWindow.OpenWindows[0].FindControl<TextBox>("TxtAnswer")!.IsReadOnly);
            BubbleCountResultWindow.ForceCloseAll();
            Assert.Equal(0, completed);
        }
        finally
        {
            BubbleCountResultWindow.ForceCloseAll();
            ScreenList.QueryOverride = oldQuery;
            CoreSettings.Current.DualMonitorEnabled = oldDual;
        }
    });
}
