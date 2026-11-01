using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeMenuBrushThreadTests
{
    [Fact]
    public async Task MenuRendersAfterAvatarTypeIsInitializedOnAnotherThread()
    {
        await Task.Run(() => RuntimeHelpers.RunClassConstructor(typeof(AvatarTubeWindow).TypeHandle));
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var wasMuted = settings.AvatarMuted;
            settings.AvatarMuted = true; // the muted menu item uses the red shared brush
            var tube = new AvatarTubeWindow(null);
            var menu = tube.FindControl<ContextMenu>("AvatarContextMenu")!;
            try
            {
                tube.Show();
                menu.Open(tube.FindControl<Border>("AvatarBorder") ?? (Control)tube);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Assert.True(menu.IsOpen);
                Assert.Equal(Color.FromRgb(255, 99, 71),
                    ((ISolidColorBrush)tube.FindControl<MenuItem>("MenuItemMute")!.Foreground!).Color);
            }
            finally
            {
                menu.Close();
                tube.Close();
                settings.AvatarMuted = wasMuted;
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
