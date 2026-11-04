using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainBlurRowTests
{
    // WPF BrainDrainFeatureControl.xaml.cs:102,291-301.
    [Fact]
    public Task SliderLoadsAndSavesZeroAndOtherStrengths() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var saved = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.BrainDrainBlurStrength = 0;
            var card = new BrainDrainFeatureControl();
            var slider = card.FindControl<Slider>("SliderBlurStrength")!;
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(0, slider.Value);
            slider.Value = 63;
            Assert.Equal(63, service.Current.BrainDrainBlurStrength);
            slider.Value = 0;
            Assert.Equal(0, service.Current.BrainDrainBlurStrength);
            Assert.Equal("0%", card.FindControl<TextBlock>("TxtBlurStrength")!.Text);
            var reopened = new BrainDrainFeatureControl();
            Assert.Equal(0, reopened.FindControl<Slider>("SliderBlurStrength")!.Value);
        }
        finally
        {
            CoreSettings.ServiceProvider = null;
            if (saved is null) File.Delete(path); else File.WriteAllBytes(path, saved);
        }
        return Task.CompletedTask;
    });
}
