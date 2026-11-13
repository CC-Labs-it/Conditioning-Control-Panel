using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Launcher slice 5 (WPF LauncherWindow.Fx/Choreo/Backdrop/Edge/Crossfade.cs): the frame clock runs
/// only while the launcher is shown and not minimised, honours the motion setting, and hover / Play beats
/// change state on a stepped clock. Sounds go through CoreAudio, captured here, never played.</summary>
public sealed class LauncherEntranceTests
{
    private static void Run(MotionLevel motion, Action<MainShellWindow, LauncherWindow, List<string>> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            s.WhatMovedCardShown = 1;   // P52: the upgrade-only What moved card would take the real clicks
            s.MotionLevel = motion;
            s.PerformanceMode = false;
            s.MasterVolume = 50;
            s.LauncherSoundEnabled = true;
            var cues = new List<string>();
            var (oldIn, oldLab, oldAudio) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreAudio.PlayOneShotProvider);
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => cues.Add(tag);
            var oldStoppable = CoreAudio.PlayStoppableProvider;
            CoreAudio.PlayStoppableProvider = (_, _, tag, _, done) => { cues.Add(tag); done?.Invoke(); return () => { }; };
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => true;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(LauncherWindow.BackToLauncher(shell));
                shell.StepLauncherFade(1); // with motion on the door first fades the panel (WPF MainWindow.Launcher.cs)
                Dispatcher.UIThread.RunJobs();
                var launcher = LauncherWindow.Instance!;
                launcher.UpdateLayout();
                body(shell, launcher, cues);
            }
            finally
            {
                LauncherWindow.Instance?.Close();
                LockdownService.Current = null;
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreAudio.PlayOneShotProvider) = (oldIn, oldLab, oldAudio);
                shell.Close();
                CoreAudio.PlayStoppableProvider = oldStoppable;
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private static TranslateTransform Slide(Border tile) => (TranslateTransform)((TransformGroup)tile.RenderTransform!).Children.Last();

    [Fact]
    public void Entrance_StaggersOnEveryShow_AndSettles() => Run(MotionLevel.Full, (_, w, _) =>
    {
        w.Hide(); w.Show();
        var grid = w.FindControl<UniformGrid>("GamesGrid")!;
        var a = (Border)grid.Children[0];
        var b = (Border)grid.Children[1];
        Assert.Equal(0, a.Opacity); Assert.Equal(10, Slide(a).Y);
        w.StepFx(0.04);
        Assert.InRange(a.Opacity, 0.32, 0.34); Assert.Equal(0, b.Opacity, 5);
        Assert.InRange(Slide(a).Y, 7.15, 7.17); Assert.Equal(10, Slide(b).Y, 5);
        while (grid.Children.Count < 8)
            grid.Children.Add(new Border { RenderTransform = new TransformGroup { Children = { new TranslateTransform() } } });
        w.StepFx(0.21);
        var sixthDelay = (Border)grid.Children[6];
        var capped = (Border)grid.Children[7];
        Assert.InRange(sixthDelay.Opacity, 0.08, 0.10);
        Assert.Equal(sixthDelay.Opacity, capped.Opacity, 5);
        Assert.Equal(Slide(sixthDelay).Y, Slide(capped).Y, 5);
        w.StepFx(1);
        Assert.Equal(1, a.Opacity); Assert.Equal(0, Slide(a).Y);
        w.Hide(); w.Show();
        a = (Border)grid.Children[0];
        Assert.Equal(0, a.Opacity); Assert.Equal(10, Slide(a).Y);
    });

    [Fact]
    public void Entrance_OffIsInstant() => Run(MotionLevel.Off, (_, w, _) =>
    {
        foreach (Border tile in w.FindControl<UniformGrid>("GamesGrid")!.Children)
        { Assert.Equal(1, tile.Opacity); Assert.Equal(0, Slide(tile).Y); }
    });

    [Fact]
    public void Buttons_PressAndReleaseSquish() => Run(MotionLevel.Off, (_, w, _) =>
    {
        var tile = (Border)w.FindControl<UniformGrid>("GamesGrid")!.Children[0];
        var play = tile.GetVisualDescendants().OfType<Button>().Last();
        foreach (var button in new[] { play, w.FindControl<Button>("PanelCta")! })
        {
            w.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var p = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), w)!.Value;
            var scale = (ScaleTransform)button.RenderTransform!;
            Assert.True(button.IsVisualAncestorOf((Visual)w.InputHitTest(p)!) || ReferenceEquals(button, w.InputHitTest(p)), $"hit: {w.InputHitTest(p)}, button: {button}, point: {p}");
            w.MouseDown(p, MouseButton.Left);
            Assert.Equal(ConditioningControlPanel.Services.UI.InteractionPressMotion.ScaleFor(true), scale.ScaleX);
            w.MouseUp(new Point(0, 0), MouseButton.Left);
            Assert.Equal(ConditioningControlPanel.Services.UI.InteractionPressMotion.ScaleFor(false), scale.ScaleX);
        }
    });

    [Fact]
    public void TitleBar_DoublePressTogglesMaximise() => Run(MotionLevel.Off, (_, w, _) =>
    {
        var bar = w.FindControl<Border>("TitleBar")!;
        void DoublePress()
        {
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            bar.RaiseEvent(new PointerPressedEventArgs(bar, pointer, w, new Point(10, 10), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 2));
        }
        DoublePress(); Assert.Equal(WindowState.Maximized, w.WindowState);
        DoublePress(); Assert.Equal(WindowState.Normal, w.WindowState);
    });
    [Theory]
    [InlineData(LauncherTileTrigger.ModChanged)]
    [InlineData(LauncherTileTrigger.GrantsChanged)]
    public void RefreshTiles_RebuildRestartsEntrance(LauncherTileTrigger trigger) => Run(MotionLevel.Full, (_, w, _) =>
    {
        w.StepFx(1);
        var grid = w.FindControl<UniformGrid>("GamesGrid")!;
        var old = (Border)grid.Children[0];
        if (trigger == LauncherTileTrigger.GrantsChanged)
        {
            // Simulate a changed reveal snapshot. Current catalogue entries are all revealed.
            var snapshot = (Dictionary<string, bool>)typeof(LauncherWindow)
                .GetField("_revealedAtBuild", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!;
            snapshot[(string)old.Tag!] = false;
        }
        w.RefreshTiles(trigger);
        var tile = (Border)grid.Children[0];
        Assert.NotSame(old, tile);
        Assert.Equal(0, tile.Opacity); Assert.Equal(10, Slide(tile).Y);
        w.StepFx(0.04);
        Assert.InRange(tile.Opacity, 0.32, 0.34);
        Assert.Equal(0, ((Border)grid.Children[1]).Opacity, 5);
        w.StepFx(1);
        Assert.Equal(1, tile.Opacity); Assert.Equal(0, Slide(tile).Y);
    });

    [Fact]
    public void RefreshTiles_OffIsInstant_AndUnchangedGrantsDoNotRebuild() => Run(MotionLevel.Off, (_, w, _) =>
    {
        var grid = w.FindControl<UniformGrid>("GamesGrid")!;
        var old = grid.Children[0];
        w.RefreshTiles(LauncherTileTrigger.GrantsChanged);
        Assert.Same(old, grid.Children[0]);
        w.RefreshTiles(LauncherTileTrigger.ModChanged);
        var tile = (Border)grid.Children[0];
        Assert.NotSame(old, tile);
        Assert.Equal(1, tile.Opacity); Assert.Equal(0, Slide(tile).Y);
    });

    [Fact]
    public void SignInClose_RebuildRestartsEntrance() => Run(MotionLevel.Full, (_, w, _) =>
    {
        w.StepFx(1);
        var grid = w.FindControl<UniformGrid>("GamesGrid")!;
        var old = grid.Children[0];
        var opacities = new List<double>();
        var rises = new List<double>();
        grid.Children.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (Border added in e.NewItems)
            {
                added.PropertyChanged += (_, change) => { if (change.Property == Visual.OpacityProperty) opacities.Add(added.Opacity); };
                var slide = Slide(added);
                slide.PropertyChanged += (_, change) => { if (change.Property == TranslateTransform.YProperty) rises.Add(slide.Y); };
            }
        };
        w.OpenSignIn();
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(w.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.LoginDialog>());
        dialog.Close();
        Dispatcher.UIThread.RunJobs();
        var tile = (Border)grid.Children[0];
        Assert.NotSame(old, tile);
        Assert.Contains(0, opacities); Assert.Contains(10, rises);
        w.StepFx(1);
        Assert.Equal(1, tile.Opacity); Assert.Equal(0, Slide(tile).Y);
    });

    [Fact]
    public void MaximisedTitleBarPress_RestoresUnderPointer() => Run(MotionLevel.Off, (_, w, _) =>
    {
        w.WindowState = WindowState.Maximized;
        var bar = w.FindControl<Border>("TitleBar")!;
        var at = new Point(200, 20);
        var screen = w.PointToScreen(at);
        var expected = new PixelPoint(screen.X - LauncherPlacement.ToPx(w.Width / 2, w.RenderScaling),
            screen.Y - LauncherPlacement.ToPx(15, w.RenderScaling));
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        bar.RaiseEvent(new PointerPressedEventArgs(bar, pointer, w, at, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        Assert.Equal(WindowState.Normal, w.WindowState);
        Assert.Equal(expected, w.Position);
    });

}
