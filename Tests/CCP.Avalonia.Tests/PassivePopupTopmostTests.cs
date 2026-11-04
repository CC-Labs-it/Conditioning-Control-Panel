using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class PassivePopupTopmostTests
{
    [Fact]
    public Task LoadedPopupReassertsPlacement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new Window { Width = 200, Height = 100, ShowActivated = false };
        var calls = 0;
        AchievementPopup.PlacePassive(window, 20, w => { Assert.Same(window, w); calls++; });
        Assert.Equal(0, calls);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, calls);
        window.Close();
        return Task.CompletedTask;
    });
}
