using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Declutter wave 1 (2026-11-15, twin of WPF HudDepthTests / HeaderBannerFxTests): START is the
/// one filled button on the bottom bar, Save and Exit are outline planks sized to their label, and the
/// header banner shows on Home only.</summary>
public sealed class DeclutterBarTests
{
    private static void Run(Action<MainShellWindow> check) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.OfflineMode = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();
            check(shell);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private static bool Filled(Button b) => b.Background switch
    {
        null => false,
        ISolidColorBrush solid => solid.Color.A != 0 && solid.Opacity > 0,
        _ => true,
    };

    [Fact]
    public void StartIsTheOnlyFilledButtonAndSaveAndExitAreOutlinePlanks() => Run(shell =>
    {
        var start = shell.Named<Button>("BtnStart")!;
        var save = shell.Named<Button>("BtnSaveAll")!;
        var exit = shell.Named<Button>("BtnExit")!;
        Assert.True(Filled(start));
        Assert.False(Filled(save));
        Assert.False(Filled(exit));

        // START flexes alone; Save and Exit size to their labels.
        var grid = Assert.IsType<Grid>(start.Parent);
        Assert.Single(grid.ColumnDefinitions, c => c.Width.IsStar);
        Assert.True(grid.ColumnDefinitions[Grid.GetColumn(start)].Width.IsStar);
        Assert.True(grid.ColumnDefinitions[Grid.GetColumn(save)].Width.IsAuto);
        Assert.True(grid.ColumnDefinitions[Grid.GetColumn(exit)].Width.IsAuto);
        Assert.True(start.Bounds.Width > save.Bounds.Width + exit.Bounds.Width);

        foreach (var b in new[] { save, exit })
        {
            Assert.Equal(84, b.MinWidth);
            Assert.Equal(14, b.FontSize);
            Assert.Equal(50, b.Height);
            Assert.True(b.Bounds.Width >= 84, b.Name);
            // the outline: the plate's border is the button's own BorderBrush, the face is clear at rest
            var plate = b.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "b");
            Assert.Same(b.BorderBrush, plate.BorderBrush);
            Assert.Equal(new Thickness(1), plate.BorderThickness);
            Assert.Equal(Colors.Transparent, ((ISolidColorBrush)plate.Background!).Color);
            // the parts HudPlank moves
            Assert.Contains(b.GetVisualDescendants().OfType<Border>(), x => x.Name == "DepthDrop");
            Assert.Contains(b.GetVisualDescendants().OfType<Grid>(), x => x.Name == "DepthFace");
        }

        // Save is neutral; Exit carries the danger colour on text and border only.
        var danger = Application.Current!.FindResource("DangerBrush");
        Assert.Same(shell.FindResource("TextSecondaryBrush"), save.Foreground);
        Assert.Same(shell.FindResource("TextMutedBrush"), save.BorderBrush);
        Assert.Same(danger, exit.Foreground);
        Assert.Same(danger, exit.BorderBrush);
    });

    [Fact]
    public void TheHeaderBannerShowsOnHomeOnly() => Run(shell =>
    {
        var host = shell.Named<Border>("HeaderBannerHost")!;
        var glow = shell.Named<Border>("HeaderBannerGlowHost")!;
        Assert.True(host.IsVisible);   // the shell lands on the dashboard
        Assert.True(glow.IsVisible);

        foreach (var (tab, shows) in new[]
        {
            ("studio", false), ("appsettings", false), ("settings", true), ("companion", false),
            ("Settings", true), ("progression", true),
        })
        {
            shell.ShowTab(tab);
            Dispatcher.UIThread.RunJobs();
            if (!string.Equals(shell.CurrentTab, tab, StringComparison.OrdinalIgnoreCase)) continue; // not a tab on this head
            Assert.True(shows == host.IsVisible, tab);
            Assert.True(shows == glow.IsVisible, tab);   // the halo never outlives the pill
            if (!shows) Assert.False(shell.BannerBreathing, tab);   // a collapsed banner runs no ambient loop
        }

        // An unknown key keeps the current tab and the banner as it was.
        shell.ShowTab("studio");
        shell.ShowTab("no-such-tab");
        Assert.Equal("studio", shell.CurrentTab);
        Assert.False(host.IsVisible);
    });
}
