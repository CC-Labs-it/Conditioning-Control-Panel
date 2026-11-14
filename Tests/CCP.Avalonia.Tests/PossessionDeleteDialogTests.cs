using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionDeleteDialogTests
{
    private sealed class Host : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow { get; } = new Window { Content = new Border(), Width = 500, Height = 400 };
        public Canvas? GhostLayer => null;
        public IFrameSource PossessionFrames { get; } = new FakeFrames();
        public PossessionRuntime PossessionEffectRuntime => throw new NotSupportedException();
        public Point PointerPosition => default;
        public Rect? BoundsOnGhostLayer(Control element) => null;
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
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

    [Fact]
    public void InventedNamesProgressAndCancelWithoutTouchingSessions() => AvaloniaTestDispatcher.Run(() =>
    {
        var folder = Path.Combine(TestUserDataProfile.Root, "delete-dialog-spy");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "private.session.json");
        File.WriteAllText(file, "private session");
        int changes = 0;
        using var spy = new FileSystemWatcher(folder) { EnableRaisingEvents = true, IncludeSubdirectories = true };
        spy.Changed += (_, _) => Interlocked.Increment(ref changes);
        spy.Created += (_, _) => Interlocked.Increment(ref changes);
        spy.Deleted += (_, _) => Interlocked.Increment(ref changes);
        spy.Renamed += (_, _) => Interlocked.Increment(ref changes);
        var win = new DeleteDialogWindow();
        try
        {
            win.Show();
            var rows = win.FindControl<StackPanel>("Names")!.Children.OfType<TextBlock>().Select(x => x.Text).ToArray();
            Assert.Equal(new[] { "  Velvet Reverie", "  Crimson Daydream", "  Ember Waltz", "  Midnight Bloom" }, rows);
            Assert.DoesNotContain(rows, x => x!.Contains("private") || x.Contains('/') || x.Contains('\\'));
            Assert.DoesNotContain("possession_delete", win.FindControl<TextBlock>("Status")!.Text);
            win.Step();
            Assert.Equal(25, win.FindControl<ProgressBar>("Progress")!.Value);
            Assert.Equal(0.35, ((TextBlock)win.FindControl<StackPanel>("Names")!.Children[0]).Opacity);
            win.FindControl<Button>("Cancel")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(win.IsVisible);
            SpinWait.SpinUntil(() => Volatile.Read(ref changes) > 0, 200);
            Assert.Equal(0, Volatile.Read(ref changes));
            Assert.Equal("private session", File.ReadAllText(file));
        }
        finally { if (win.IsVisible) win.Close(); spy.EnableRaisingEvents = false; Directory.Delete(folder, true); }
    });

    [Fact]
    public void FinishedWalkClosesOnFailsafeTick() => AvaloniaTestDispatcher.Run(() =>
    {
        var dialog = new DeleteDialogWindow();
        dialog.Show();
        try
        {
            for (int i = 0; i < DeleteDialogWindow.FakeNames.Length; i++) dialog.Step();
            Assert.True(dialog.IsVisible);
            dialog.Step();
            Assert.False(dialog.IsVisible);
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    });

    [Fact]
    public void CancelClosesTheDialog() => AvaloniaTestDispatcher.Run(() =>
    {
        var dialog = new DeleteDialogWindow();
        dialog.Show();
        try
        {
            dialog.FindControl<Button>("Cancel")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(dialog.IsVisible);
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    });

    [Fact]
    public void DirectorUndoAllClosesTheRegisteredFullDokiDialog() => AvaloniaTestDispatcher.Run(() =>
    {
        var host = new Host();
        var effect = Assert.Single(PossessionAvaloniaHead.CreateEffects().OfType<DeleteDialogEffect>());
        Assert.Equal(PossessionRung.ItKnows, effect.MinRung);
        Assert.Equal(PossessionIntensity.FullDoki, effect.MinIntensity);
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, new[] { effect }, new PossessionDirectorHead { Attribution = _ => new Attribution() });
        host.PossessionWindow!.Show();
        try
        {
            director.AttachHost(host);
            typeof(PossessionDirector).GetField("_intensity", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(director, PossessionIntensity.FullDoki);
            var hosts = (IList)typeof(PossessionDirector).GetField("_hosts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(director)!;
            var start = typeof(PossessionDirector).GetMethod("StartOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((Task)start.Invoke(director, new object[] { hosts[0]!, PossessionRung.ItKnows, TimeSpan.FromMinutes(1), 0.9 })!).GetAwaiter().GetResult();
            Assert.Equal(1, director.LiveEffectCount);
            var dialog = (DeleteDialogWindow)typeof(DeleteDialogEffect).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(effect)!;
            Assert.True(dialog.IsVisible);
            director.UndoAll();
            Assert.False(dialog.IsVisible);
            Assert.False(effect.IsLive);
            Assert.Equal(0, director.LiveEffectCount);
        }
        finally { director.UndoAll(); host.PossessionWindow.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
