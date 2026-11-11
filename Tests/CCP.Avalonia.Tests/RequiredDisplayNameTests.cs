using System;
using System.Linq;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RequiredDisplayNameTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task Replay_switch_loads_saves_and_follows_session_lock()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            CoreSettings.Current.MandatoryVideoReplayAfterFailedCheck = false;
            var view = new VideoFeatureControl();
            var toggle = view.FindControl<CheckBox>("ChkReplayFailedCheck")!;
            Assert.False(toggle.IsChecked);
            toggle.IsChecked = true;
            Assert.True(CoreSettings.Current.MandatoryVideoReplayAfterFailedCheck);
            var paint = typeof(MainShellWindow).GetMethod("SetToggleLock",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            Assert.True(toggle.IsEnabled);
            foreach (var locked in new[] { true, false })
            {
                foreach (var owned in SessionLock.FindOwnedControls(view))
                    paint.Invoke(null, new object?[] { owned, locked, "Session owns this setting" });
                Assert.Equal(!locked, toggle.IsEnabled);
            }
            var host = new Window { Content = view, Width = 1000, Height = 1100 };
            try
            {
                host.Show(); host.UpdateLayout();
                var label = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == Loc.Get("setting_video_replay_failed_check"));
                Assert.True(label.IsEffectivelyVisible && label.Bounds.Width > 0 && label.Bounds.Height > 0);
                var row = Assert.IsType<Grid>(toggle.Parent);
                Assert.Same(row, label.Parent);
                var size = view.FindControl<Slider>("SliderTargetSize")!;
                var panel = Assert.IsType<StackPanel>(row.Parent);
                Assert.Equal(panel.Children.IndexOf((Control)size.Parent!) + 1, panel.Children.IndexOf(row));
            }
            finally { host.Close(); }
            return Task.CompletedTask;
        });
    }
}
