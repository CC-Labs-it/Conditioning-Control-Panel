using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionReorderDoorsTests
{
    private sealed class Host : IAvaloniaPossessionHost
    {
        public required Window PossessionWindow { get; init; }
        public Canvas? GhostLayer => null;
        public FakeFrames Frames { get; } = new();
        public IFrameSource PossessionFrames => Frames;
        public PossessionRuntime PossessionEffectRuntime => new(Frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control element) => null;
        public required IReadOnlyList<PossessionTarget> Targets { get; init; }
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new CancellationTokenSource();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }
    private static async Task Run(bool panic)
    {
        var rail = new StackPanel { Name = "NavSectionRows", Width = 120, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        var doors = Enumerable.Range(0, 4).Select(i => new Button { Width = 120, Height = 40, Tag = "door" + i, Content = "door" + i }).ToArray();
        var lockdown = new Button { Width = 120, Height = 40, Name = "BtnNavLockdown", Tag = "lockdown", Content = "lockdown" };
        rail.Children.Add(doors[0]);
        rail.Children.Add(lockdown);
        foreach (var door in doors.Skip(1)) rail.Children.Add(door);
        var window = new Window { Width = 180, Height = 240, Content = rail };
        var targets = doors.Append(lockdown).Select((d, i) => new PossessionTarget { Visual = d, Role = PossessionRole.TabHeader, Key = i.ToString() }).ToArray();
        var host = new Host { PossessionWindow = window, Targets = targets };
        var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(7), ElapsedFraction = 0,
            Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new ReorderDoorsEffect();
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        try
        {
            var positions = doors.Select(d => d.Bounds).ToArray();
            var lockBounds = lockdown.Bounds;
            var firstOriginal = doors[0].TranslatePoint(new Point(60, 20), window)!.Value;
            int pressed = -1;
            for (int i = 0; i < doors.Length; i++) { int n = i; doors[i].Click += (_, _) => pressed = n; }
            Assert.True(effect.CanApply(context, targets[0]));
            var apply = effect.ApplyAsync(context, targets[0], CancellationToken.None);
            host.Frames.Frame(TimeSpan.Zero);
            host.Frames.Frame(TimeSpan.FromMilliseconds(800));
            await apply;
            Assert.Equal(TimeSpan.FromSeconds(10), effect.HoldFor);
            Assert.Equal(positions, doors.Select(d => d.Bounds).ToArray());
            Assert.Equal(lockBounds, lockdown.Bounds);
            Assert.False(PossessionLease.IsLeased(lockdown));
            for (int i = 0; i < doors.Length; i++)
            {
                Assert.True(PossessionLease.IsLeased(doors[i]));
                var center = doors[i].TranslatePoint(new Point(60, 20), window)!.Value;
                Assert.Equal(positions[i ^ 1].Y + 20, center.Y, 1);
            }
            window.MouseMove(firstOriginal);
            window.MouseDown(firstOriginal, MouseButton.Left);
            window.MouseUp(firstOriginal, MouseButton.Left);
            Assert.Equal(1, pressed);
            var undo = effect.UndoAsync(TimeSpan.FromMilliseconds(520));
            host.Frames.Frame(TimeSpan.FromMilliseconds(900));
            host.Frames.Frame(TimeSpan.FromMilliseconds(1160));
            var midway = doors[0].TranslatePoint(new Point(60, 20), window)!.Value;
            double halfGap = (positions[1].Y - positions[0].Y) / 2;
            Assert.InRange(midway.Y, firstOriginal.Y + halfGap - 1, firstOriginal.Y + halfGap + 1);
            if (panic)
            {
                PossessionRuntime.UndoAllLive();
                Assert.False(PossessionLease.IsLeased(doors[0]));
            }
            else host.Frames.Frame(TimeSpan.FromMilliseconds(1420));
            await undo;
            for (int i = 0; i < doors.Length; i++)
            {
                Assert.False(PossessionLease.IsLeased(doors[i]));
                Assert.Equal(positions[i].Y + 20, doors[i].TranslatePoint(new Point(60, 20), window)!.Value.Y, 1);
            }
            Assert.Equal(0, host.Frames.Count);
            Assert.False(effect.IsLive);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    }

    [Fact] public Task PairsSwapStayClickableAndGlideBack() => AvaloniaTestDispatcher.RunAsync(() => Run(false));
    [Fact] public Task PanicDuringReturnReleasesEveryLease() => AvaloniaTestDispatcher.RunAsync(() => Run(true));
    [Fact] public Task ShellDiscoversAndNavigatesTheMovedDoor() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var frames = new FakeFrames();
        var shell = new MainShellWindow { PossessionFramesOverride = frames };
        var effect = new ReorderDoorsEffect();
        shell.Show(); Dispatcher.UIThread.RunJobs(); shell.UpdateLayout();
        try
        {
            var host = (IAvaloniaPossessionHost)shell;
            var home = shell.Named<Button>("DoorHome")!;
            var studio = shell.Named<Button>("DoorStudio")!;
            var target = host.Targets.Single(t => ReferenceEquals(t.Visual, home));
            var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
                Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(7), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var original = home.TranslatePoint(new Point(home.Bounds.Width / 2, home.Bounds.Height / 2), shell)!.Value;
            Assert.True(effect.CanApply(context, target));
            var apply = effect.ApplyAsync(context, target, CancellationToken.None);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(900));
            await apply;
            Assert.True(PossessionLease.IsLeased(studio));
            shell.MouseMove(original); shell.MouseDown(original, MouseButton.Left); shell.MouseUp(original, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(NavSections.Studio, NavSections.SectionForTab(shell.CurrentTab));
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); shell.RequestExit(); shell.Close(); }
    });

    [Fact] public void CatalogOffersReorderDoors() => Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "reorderdoors");
}
