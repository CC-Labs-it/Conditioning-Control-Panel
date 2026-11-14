using System;
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
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF BrainDrainFeatureControl.xaml:269-290, .xaml.cs:88-110,207-218,269-282.
public sealed class BrainDrainVolumeRowTests
{
    private static Task Run(Action<BrainDrainFeatureControl, SettingsService, Window> test) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont()
                    .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                    .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.BrainDrainVolume = 100;
            var card = new BrainDrainFeatureControl();
            var host = new Window { Content = card };
            try
            {
                host.Show();
                Dispatcher.UIThread.RunJobs();
                test(card, service, host);
            }
            finally { host.Close(); }
            return Task.CompletedTask;
        });

    [Fact]
    public Task VolumeDefaultsToFullAndFoldedThenWritesSilentAndFullLimits() => Run((card, service, _) =>
    {
        var slider = card.FindControl<Slider>("SliderVolume")!;
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(100, slider.Maximum);
        Assert.Equal(100, slider.Value);
        Assert.Equal("100%", card.FindControl<TextBlock>("TxtVolume")!.Text);
        var fold = card.FindControl<MoreFold>("FoldMore")!;
        Assert.False(fold.IsOpen);
        fold.IsOpen = true;
        service.SaveImmediate();
        slider.Value = 0;
        Assert.Equal(0, service.Current.BrainDrainVolume);
        Assert.Equal("0%", card.FindControl<TextBlock>("TxtVolume")!.Text);
        Assert.True(service.HasPendingSave);
        slider.Value = 100;
        Assert.Equal(100, service.Current.BrainDrainVolume);
        Dispatcher.UIThread.RunJobs();
        Assert.True(fold.IsOpen);
    });

    [Fact]
    public Task ExternalVolumeEditRevealsTheChangedSetting() => Run((card, service, _) =>
    {
        service.Current.BrainDrainVolume = 27;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(27, card.FindControl<Slider>("SliderVolume")!.Value);
        Assert.Equal("27%", card.FindControl<TextBlock>("TxtVolume")!.Text);
        Assert.True(card.FindControl<MoreFold>("FoldMore")!.IsOpen);
    });

    [Fact]
    public Task RestoreFollowsTheNewInstanceAndDetachDropsTheOldHook() => Run((card, service, host) =>
    {
        var old = service.Current;
        service.RestoreFrom(new AppSettings { BrainDrainVolume = 36 });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(36, card.FindControl<Slider>("SliderVolume")!.Value);
        Assert.True(card.FindControl<MoreFold>("FoldMore")!.IsOpen);
        card.FindControl<Slider>("SliderVolume")!.Value = 45;
        Assert.Equal(45, service.Current.BrainDrainVolume);
        Assert.Equal(100, old.BrainDrainVolume);
        host.Content = null;
        Dispatcher.UIThread.RunJobs(); // Content removal detaches during layout.
        Assert.Null(TopLevel.GetTopLevel(card));
        service.Current.BrainDrainVolume = 80;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(45, card.FindControl<Slider>("SliderVolume")!.Value);
        host.Content = card;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(80, card.FindControl<Slider>("SliderVolume")!.Value);
    });
}
