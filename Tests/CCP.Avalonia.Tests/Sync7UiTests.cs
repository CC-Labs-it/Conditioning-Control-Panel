using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class Sync7UiTests
{
    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        body();
    });

    [Fact]
    public void FineTuneBorrowsRemainingHeightAndLocalSourceRestoresBrowser() => Run(() =>
    {
        var tab = new AssetsTabView();
        tab.RemoteMediaDetails.IsVisible = true;
        tab.ApplyFineTuneLayout(true, true);
        tab.Measure(new Size(1000, 700));
        tab.Arrange(new Rect(0, 0, 1000, 700));
        Assert.True(tab.RemoteFineTuneScroll.IsVisible);
        Assert.False(tab.AssetBrowserSection.IsVisible);
        Assert.True(tab.RowMediaSource.Height.IsStar);
        Assert.Equal(0, tab.RowAssetBrowser.MinHeight);
        Assert.True(double.IsPositiveInfinity(tab.RemoteFineTuneScroll.MaxHeight));
        Assert.True(tab.RemoteFineTuneScroll.Bounds.Bottom <= tab.Bounds.Height);
        tab.ApplyFineTuneLayout(true, false);
        Assert.False(tab.RemoteFineTuneScroll.IsVisible);
        Assert.True(tab.AssetBrowserSection.IsVisible);
        Assert.True(tab.RowMediaSource.Height.IsAuto);
        Assert.True(tab.RowAssetBrowser.Height.IsStar);
        Assert.Equal(260, tab.RowAssetBrowser.MinHeight);
    });

    [Fact]
    public void EditorShowsPersonaWhileOffAndPreservesStoredCustomText() => Run(() =>
    {
        var s = CoreSettings.Current.CompanionPrompt;
        s.UseCustomPrompt = false;
        s.Personality = "old custom prompt";
        s.ContextReactions = "old reactions";
        var dialog = new CompanionPromptEditorDialog();
        try
        {
            Assert.NotEqual("old custom prompt", dialog.FindControl<TextBox>("TxtPersonality")!.Text);
            Assert.NotEqual("old reactions", dialog.FindControl<TextBox>("TxtContextReactions")!.Text);
            typeof(CompanionPromptEditorDialog).GetMethod("SaveSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null);
            Assert.Equal("old custom prompt", s.Personality);
            Assert.Equal("old reactions", s.ContextReactions);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void SuccessfulTestExplainsMasterOffAfterActivityLine() => Run(() =>
    {
        var settings = CoreSettings.Current.Haptics;
        settings.EnsureV2Migrated();
        settings.Enabled = false;
        using var service = new HapticService(settings);
        CoreHaptics.Service = service;
        var tab = new HapticsTabView();
        tab.NoteTestWhileHapticsOff();
        tab.TxtHapticActivity.Text = "test activity";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get("haptics_test_while_off"), tab.TxtHapticActivity.Text);
        settings.Enabled = true;
        tab.TxtHapticActivity.Text = "normal activity";
        tab.NoteTestWhileHapticsOff();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("normal activity", tab.TxtHapticActivity.Text);
        CoreHaptics.Service = null;
    });
}
