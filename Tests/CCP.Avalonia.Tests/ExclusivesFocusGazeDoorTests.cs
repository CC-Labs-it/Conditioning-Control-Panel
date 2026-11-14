using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>#3929 slice (WPF MainWindow.Exclusives.cs:242, :257): the Focus Gaze Premium card shows the Play
/// wall and scrolls its switch into view; navigating never turns the switch on.</summary>
public sealed class ExclusivesFocusGazeDoorTests
{
    [Fact]
    public void FocusGazeCardShowsPlayAndScrollsToTheSwitch() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = true;
        settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.WhatMovedCardShown = 1;
        settings.Current.FocusGazeEnabled = false;
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            shell.Show();
            shell.ShowTab("premium");
            Settle();
            var play = shell.Named<PlayTabView>("PlayTab")!;
            var scroll = play.FindControl<ScrollViewer>("WallScroll")!;
            var slot = play.FindControl<Control>("SlotFocusGaze")!;
            ClickCard(shell, "focusgaze");
            Settle();
            Assert.Equal("play", shell.CurrentTab);
            Assert.True(scroll.Offset.Y > 0);
            var top = slot.TranslatePoint(default, scroll)!.Value.Y;
            Assert.InRange(top, -slot.Bounds.Height, scroll.Bounds.Height);
            Assert.False(CoreSettings.Current.FocusGazeEnabled);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            foreach (var child in shell.OwnedWindows.ToList()) child.Close();
            shell.Close();
            Settle();
            CoreSettings.ServiceProvider = null;
        }
    });

    private static void ClickCard(MainShellWindow shell, string key)
    {
        var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
        var card = view.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "ExCard"
            && c.DataContext is ExclusiveCardRow row && row.Feature.Key == key);
        var model = (ExclusiveCardRow)card.DataContext!;
        Assert.True(model.IsAvailable);
        Assert.Null(model.UnavailableTip);
        card.BringIntoView();
        Settle();
        var point = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), shell)!.Value;
        shell.MouseMove(point);
        shell.MouseDown(point, MouseButton.Left);
        shell.MouseUp(point, MouseButton.Left);
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
