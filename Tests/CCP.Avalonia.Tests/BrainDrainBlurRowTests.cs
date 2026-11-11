using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainBlurRowTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        // Another filtered test may already have initialized the shared app without Inter.
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(AppFonts.Inter),
                FontStyle.Normal, FontWeight.Bold), out _))
            FontManager.Current.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection());
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
    }

    private static bool HasHandler(object source, string eventName, object target)
    {
        var field = source.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (field.GetValue(source) as Delegate)?.GetInvocationList()
            .Any(handler => ReferenceEquals(handler.Target, target)) == true;
    }

    // WPF BrainDrainFeatureControl.xaml.cs:102,291-301.
    [Fact]
    public Task SliderSavesToDiskIncludingZero() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var saved = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var previousProvider = CoreSettings.ServiceProvider;
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.BrainDrainBlurStrength = 17;
            service.SaveImmediate();
            var card = new BrainDrainFeatureControl();
            var slider = card.FindControl<Slider>("SliderBlurStrength")!;
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(17, slider.Value);
            foreach (var value in new[] { 63, 0 })
            {
                slider.Value = value;
                Assert.True(service.HasPendingSave);
                var timeout = Stopwatch.StartNew();
                while (new SettingsService().Current.BrainDrainBlurStrength != value
                    && timeout.Elapsed < TimeSpan.FromSeconds(10))
                    await Task.Delay(50);
                Assert.Equal(value, new SettingsService().Current.BrainDrainBlurStrength);
            }
            Assert.Equal("0%", card.FindControl<TextBlock>("TxtBlurStrength")!.Text);
        }
        finally
        {
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = previousProvider;
            if (saved is null) File.Delete(path); else File.WriteAllBytes(path, saved);
        }
    });

    // WPF BrainDrainFeatureControl.xaml.cs:51-54,68-72,201-218.
    [Fact]
    public Task ExternalChangesRebindAndDetach() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var saved = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var previousProvider = CoreSettings.ServiceProvider;
        Window? host = null;
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var old = service.Current;
            old.BrainDrainBlurStrength = 30;
            var card = new BrainDrainFeatureControl();
            var slider = card.FindControl<Slider>("SliderBlurStrength")!;
            host = new Window { Content = card };
            host.Show();
            Assert.True(HasHandler(old, "PropertyChanged", card));
            Assert.True(HasHandler(service, "CurrentReplaced", card));
            await Task.Run(() => old.BrainDrainBlurStrength = 44);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(44, slider.Value);
            Assert.Equal("44%", card.FindControl<TextBlock>("TxtBlurStrength")!.Text);
            Assert.False(service.HasPendingSave);
            var replacement = new AppSettings { BrainDrainBlurStrength = 71 };
            service.RestoreFrom(replacement);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(71, slider.Value);
            Assert.False(HasHandler(old, "PropertyChanged", card));
            Assert.True(HasHandler(replacement, "PropertyChanged", card));
            old.BrainDrainBlurStrength = 8;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(71, slider.Value);
            replacement.BrainDrainBlurStrength = 23;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(23, slider.Value);
            host.Content = null;
            Assert.False(HasHandler(replacement, "PropertyChanged", card));
            Assert.False(HasHandler(service, "CurrentReplaced", card));
            replacement.BrainDrainBlurStrength = 42;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(23, slider.Value);
            host.Content = card;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(42, slider.Value);
            Assert.True(HasHandler(replacement, "PropertyChanged", card));
            Assert.True(HasHandler(service, "CurrentReplaced", card));
            replacement.BrainDrainBlurStrength = 55;
            host.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(42, slider.Value);
        }
        finally
        {
            host?.Close();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = previousProvider;
            if (saved is null) File.Delete(path); else File.WriteAllBytes(path, saved);
        }
    });

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
