using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NavigationEntryParityTests
{
    private static Task Run(Action<MainShellWindow> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var offline = settings.OfflineMode;
        var seen = settings.SeenFeatureIntros.ToArray();
        var lastTabs = settings.NavLastTabBySection;
        var oldBark = CoreBark.TabNavigated;
        settings.OfflineMode = true;
        foreach (var key in FeatureIntros.All.Keys)
            if (!settings.SeenFeatureIntros.Contains(key)) settings.SeenFeatureIntros.Add(key);
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            check(shell);
        }
        finally
        {
            shell?.Close();
            CoreBark.TabNavigated = oldBark;
            settings.OfflineMode = offline;
            settings.SeenFeatureIntros.Clear();
            settings.SeenFeatureIntros.AddRange(seen);
            settings.NavLastTabBySection = lastTabs;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnknownKeysStaySilentAndKeepTheCurrentPageWhileAliasesKeepTheirBarkKey() => Run(shell =>
    {
        shell.ShowTab("settings");
        var keys = new List<string?>();
        CoreBark.TabNavigated = key => keys.Add(key);
        shell.ShowTab("not-a-real-tab");
        shell.ShowTab(null);
        shell.ShowTab("");
        Assert.Equal("settings", shell.CurrentTab);
        Assert.True(shell.Named<Control>("SettingsTab")!.IsVisible);
        Assert.Empty(keys);

        shell.ShowTab("PrOgReSsIoN");
        Assert.Equal(new[] { "progression" }, keys);
        Assert.True(shell.Named<Control>("SettingsTab")!.IsVisible);
    });

    [Fact]
    public Task EveryBlinkTrainerNavigationRefreshesTheSavedDurationAndOpacity() => Run(shell =>
    {
        var settings = CoreSettings.Current;
        var duration = settings.BlinkTrainerDurationMinutes;
        var opacity = settings.BlinkTrainerOpacity;
        try
        {
            settings.BlinkTrainerDurationMinutes = 12;
            settings.BlinkTrainerOpacity = 26;
            shell.ShowTab("blinktrainer");
            var view = shell.Named<BlinkTrainerTabView>("BlinkTrainerTab")!;
            Assert.Equal(12, view.FindControl<Slider>("SliderBlinkTrainerDurationNew")!.Value);
            Assert.Equal("26%", view.FindControl<TextBlock>("TxtBlinkTrainerOpacityValue")!.Text);

            settings.BlinkTrainerDurationMinutes = 37;
            settings.BlinkTrainerOpacity = 42;
            shell.ShowTab("BLINKTRAINER"); // remains visible: IsVisibleChanged cannot refresh it
            Assert.Equal(37, view.FindControl<Slider>("SliderBlinkTrainerDurationNew")!.Value);
            Assert.Equal("37 min", view.FindControl<TextBlock>("TxtBlinkTrainerDurationValue")!.Text);
            Assert.Equal(42, view.FindControl<Slider>("SliderBlinkTrainerOpacityNew")!.Value);
            Assert.Equal("42%", view.FindControl<TextBlock>("TxtBlinkTrainerOpacityValue")!.Text);
        }
        finally
        {
            settings.BlinkTrainerDurationMinutes = duration;
            settings.BlinkTrainerOpacity = opacity;
        }
    });
}
