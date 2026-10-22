using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The launcher's "Media and sound" dialog (WPF Windows/Launcher/LauncherMediaDialog.xaml.cs):
/// the WPF copy and controls, and what each change writes. No network: the consent ask is faked.</summary>
public sealed class LauncherMediaDialogTests
{
    private static async Task WithDialog(Func<LauncherMediaDialog, AppSettings, Task> body)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        LauncherMediaDialog? dlg = null;
        try
        {
            var s = CoreSettings.Current;
            s.MediaSource = "local";
            s.RemoteMediaRatio = 30;
            s.RemoteMediaConsented = false;
            s.FypOnlineConsented = false;
            s.FypOnlineNiches = new() { FypOnlineCoordinator.Catalog[0].Id };
            s.BackRoomMediaSource = "bundled";
            s.BackRoomMediaSubs = new() { "bimbo" };
            s.MasterVolume = 70;
            dlg = new LauncherMediaDialog();
            dlg.Show();
            Dispatcher.UIThread.RunJobs();
            await body(dlg, s);
        }
        finally
        {
            dlg?.Close();
            CoreSettings.ServiceProvider = oldProvider;
        }
    }

    private static ToggleButton Chip(LauncherMediaDialog d, string key) => d.SourceChips.Single(c => (string?)c.Tag == key);

    [Fact]
    public Task OpensWithTheWpfCopyAndTheLiveSettings() => AvaloniaTestDispatcher.RunAsync(() => WithDialog((d, s) =>
    {
        Assert.Equal("Media and sound", d.Title);
        Assert.Equal((560d, 790d), (d.Width, d.Height));
        Assert.Equal(new[] { "My library", "Online", "Mixed" }, d.SourceChips.Select(c => (c.Content as TextBlock)?.Text));
        Assert.Equal(FypOnlineCoordinator.Catalog.Length, d.NicheChips.Count);
        Assert.True(Chip(d, "local").IsChecked);
        Assert.True(d.NicheChips[0].IsChecked);
        Assert.False(d.RatioRowVisible);
        Assert.Equal(70, d.MasterSlider.Value);

        var texts = d.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        foreach (var copy in new[] { "Source", "Niches", "Library", "Asset preset", "Sound", "Master volume",
                                     "The Back Room follows this too.", "Your CCP media library" })
            Assert.Contains(copy, texts);

        // Done only closes: applies-on-change means there is nothing left to write.
        var done = d.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as TextBlock)?.Text == "Done");
        done.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(d.IsVisible);
        Assert.Equal("local", s.MediaSource);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task SourceChipWritesOnlyAfterConsent_AndPointsTheBackRoomAtTheApp() => AvaloniaTestDispatcher.RunAsync(() => WithDialog(async (d, s) =>
    {
        d.ConsentOverride = (_, _) => Task.FromResult(false);
        Chip(d, "online").IsChecked = true;
        await d.LastChange;
        Assert.Equal("local", s.MediaSource);
        Assert.False(s.RemoteMediaConsented);
        Assert.Equal("bundled", s.BackRoomMediaSource);
        Assert.True(Chip(d, "local").IsChecked);
        Assert.False(Chip(d, "online").IsChecked);

        d.ConsentOverride = (_, _) => Task.FromResult(true);
        Chip(d, "mixed").IsChecked = true;
        await d.LastChange;
        Assert.Equal("mixed", s.MediaSource);
        Assert.True(s.RemoteMediaConsented);
        Assert.Equal("auto", s.BackRoomMediaSource);
        Assert.Empty(s.BackRoomMediaSubs);
        Assert.True(d.RatioRowVisible);

        // Un-clicking the live chip leaves the source alone.
        Chip(d, "mixed").IsChecked = false;
        await d.LastChange;
        Assert.Equal("mixed", s.MediaSource);
        Assert.True(Chip(d, "mixed").IsChecked);

        d.RatioSlider.Value = 60;
        Assert.Equal(60, s.RemoteMediaRatio);
    }));

    [Fact]
    public Task LastNicheStaysOn_AndMasterSliderWritesTheVolume() => AvaloniaTestDispatcher.RunAsync(() => WithDialog((d, s) =>
    {
        d.NicheChips[0].IsChecked = false;
        Assert.True(d.NicheChips[0].IsChecked);
        Assert.Equal(new[] { FypOnlineCoordinator.Catalog[0].Id }, s.FypOnlineNiches);

        d.NicheChips[1].IsChecked = true;
        Assert.Equal(new[] { FypOnlineCoordinator.Catalog[0].Id, FypOnlineCoordinator.Catalog[1].Id }, s.FypOnlineNiches);

        d.MasterSlider.Value = 40;
        Assert.Equal(40, s.MasterVolume);
        Assert.Contains("40%", d.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        return Task.CompletedTask;
    }));

    [Fact]
    public Task DoneAndTheLinkKeepTheirPaintUnderThePointer() => AvaloniaTestDispatcher.RunAsync(() => WithDialog((d, s) =>
    {
        var buttons = d.GetVisualDescendants().OfType<Button>().ToList();
        var done = buttons.Single(b => (b.Content as TextBlock)?.Text == "Done");
        var link = buttons.Single(b => (b.Content as TextBlock)?.Text == "Open asset browser");
        foreach (var b in new[] { done, link }) ((IPseudoClasses)b.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        static global::Avalonia.Controls.Presenters.ContentPresenter Part(Button b) =>
            b.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        Application.Current!.TryFindResource("AccentGradientBrush", Application.Current.ActualThemeVariant, out var accent);
        Assert.Same(accent, Part(done).Background);
        Assert.Equal(global::Avalonia.Media.Colors.White, ((global::Avalonia.Media.ISolidColorBrush)Part(done).Foreground!).Color);
        Assert.Equal(0.92, done.Opacity);
        Assert.Equal(global::Avalonia.Media.Colors.Transparent, ((global::Avalonia.Media.ISolidColorBrush)Part(link).Background!).Color);
        Assert.Equal(global::Avalonia.Media.TextDecorations.Underline, ((TextBlock)link.Content!).TextDecorations);
        // WPF MediaChip: the live chip wears the cyan wash and rim, not the theme's accent fill.
        Assert.Equal(global::Avalonia.Media.Color.FromArgb(0x33, 0x5E, 0xC8, 0xF2),
            ((global::Avalonia.Media.ISolidColorBrush)Part(Chip(d, "local")).Background!).Color);
        return Task.CompletedTask;
    }));
}
