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
using Avalonia.Media;
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
        public PossessionPointer PossessionPointer { get; } = new();
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

    private sealed class PickCaption : Random
    {
        private readonly int _choice;
        public PickCaption(int choice) => _choice = choice;
        public override int Next(int maxValue) => _choice;
    }

    [Fact]
    public void CaptionsVaryAndClosingLineUsesSelectedLanguage() => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("fr");
            for (int i = 0; i < DeleteDialogWindow.Captions.Length; i++)
            {
                var dialog = new DeleteDialogWindow(new[] { "Session" }, rng: new PickCaption(i));
                Assert.Equal(DeleteDialogWindow.Captions[i], dialog.FindControl<TextBlock>("Caption")!.Text);
                dialog.Step();
                Assert.Equal("rien n’a été supprimé. je voulais juste te voir le lire.", dialog.FindControl<TextBlock>("Status")!.Text);
            }
        }
        finally { loc.SetLanguage(previous); }
    });

    [Fact]
    public Task OpeningNamesSessionsAndUndoClosesDialog() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var host = new Host();
        var effect = new DeleteDialogEffect(() => new[] { "Session" });
        var named = new List<(string, string?)>();
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.ItKnows,
            Intensity = PossessionIntensity.FullDoki, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.Zero, Name = (id, name) => named.Add((id, name)) };
        host.PossessionWindow!.Show();
        try
        {
            var applying = effect.ApplyAsync(ctx, null, CancellationToken.None);
            Assert.Contains(("deletedialog", "your sessions"), named);
            var dialog = (DeleteDialogWindow)typeof(DeleteDialogEffect).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(effect)!;
            Assert.True(dialog.IsVisible);
            await effect.UndoAsync(TimeSpan.Zero);
            Assert.False(dialog.IsVisible);
            await applying;
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); host.PossessionWindow.Close(); }
    });

    [Fact]
    public Task PanicClosesLiveDeleteDialog() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var host = new Host();
        var effect = new DeleteDialogEffect(() => new[] { "Session" });
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, new[] { effect }, new PossessionDirectorHead { Attribution = _ => new Attribution() });
        var previous = ConditioningControlPanel.Avalonia.App.Possession;
        ConditioningControlPanel.Avalonia.App.Possession = director;
        host.PossessionWindow!.Show();
        try
        {
            director.AttachHost(host);
            typeof(PossessionDirector).GetField("_intensity", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(director, PossessionIntensity.FullDoki);
            var hosts = (IList)typeof(PossessionDirector).GetField("_hosts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(director)!;
            var start = typeof(PossessionDirector).GetMethod("StartOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var starting = (Task)start.Invoke(director, new object[] { hosts[0]!, PossessionRung.ItKnows, TimeSpan.FromMinutes(1), 0.9 })!;
            var dialog = (DeleteDialogWindow)typeof(DeleteDialogEffect).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(effect)!;
            Assert.True(dialog.IsVisible);
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
            Assert.False(dialog.IsVisible);
            Assert.False(effect.IsLive);
            await starting.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { director.UndoAll(); ConditioningControlPanel.Avalonia.App.Possession = previous; host.PossessionWindow.Close(); }
    });

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
    public void ReadsSessionNamesWithoutWriting() => AvaloniaTestDispatcher.Run(() =>
    {
        var folder = Path.Combine(TestUserDataProfile.Root, "delete-dialog-read");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "one.session.json");
        File.WriteAllText(file, "{\"name\":\"My session\"}");
        int changes = 0;
        using var spy = new FileSystemWatcher(folder) { EnableRaisingEvents = true };
        spy.Changed += (_, _) => Interlocked.Increment(ref changes);
        spy.Created += (_, _) => Interlocked.Increment(ref changes);
        spy.Deleted += (_, _) => Interlocked.Increment(ref changes);
        spy.Renamed += (_, _) => Interlocked.Increment(ref changes);
        try
        {
            var names = DeleteDialogEffect.ReadSessionNames(folder);
            Assert.Equal("My session", names[0]);
            var dialog = new DeleteDialogWindow(names);
            Assert.Equal("  My session", ((TextBlock)dialog.FindControl<StackPanel>("Names")!.Children[0]).Text);
            Assert.NotEmpty(DeleteDialogEffect.ReadSessionNames(Path.Combine(folder, "missing")));
            SpinWait.SpinUntil(() => Volatile.Read(ref changes) > 0, 200);
            Assert.Equal(0, Volatile.Read(ref changes));
            Assert.Equal("{\"name\":\"My session\"}", File.ReadAllText(file));
        }
        finally { spy.EnableRaisingEvents = false; Directory.Delete(folder, true); }
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
    public Task FailsafeRunsFromOpeningAndLeavesClosingLineVisible() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var dialog = new DeleteDialogWindow(Enumerable.Range(1, 6).Select(i => "Session " + i).ToArray(), TimeSpan.FromSeconds(4));
        dialog.Show();
        try
        {
            await dialog.Walk.WaitAsync(TimeSpan.FromSeconds(6));
            Assert.True(dialog.IsVisible);
            Assert.Equal("nothing was deleted. i just wanted to watch you read it.", dialog.FindControl<TextBlock>("Status")!.Text);
            await Task.Delay(3300);
            Assert.True(dialog.IsVisible);
            await Task.Delay(1100);
            Assert.False(dialog.IsVisible);
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
    });

    [Fact]
    public void LongSessionNamesAreTrimmedWithinTheList() => AvaloniaTestDispatcher.Run(() =>
    {
        var name = new string('W', 500);
        var dialog = new DeleteDialogWindow(new[] { name });
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var list = dialog.FindControl<StackPanel>("Names")!;
            var row = Assert.IsType<TextBlock>(Assert.Single(list.Children));
            Assert.Equal("  " + name, row.Text);
            Assert.Equal(TextTrimming.CharacterEllipsis, row.TextTrimming);
            Assert.True(row.Bounds.Width > 0 && row.Bounds.Width <= list.Bounds.Width);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void LongSessionNameWrapsInStatus() => AvaloniaTestDispatcher.Run(() =>
    {
        var name = string.Join(" ", Enumerable.Repeat("Long session name", 20));
        var dialog = new DeleteDialogWindow(new[] { name });
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var status = dialog.FindControl<TextBlock>("Status")!;
            Assert.Contains(name, status.Text);
            Assert.Equal(TextWrapping.Wrap, status.TextWrapping);
            Assert.True(status.Bounds.Height > status.FontSize * 2);
        }
        finally { dialog.Close(); }
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
    public Task DirectorWaitsForSixNameWalkBeforeHold() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var host = new Host();
        var names = Enumerable.Range(1, 6).Select(i => "Session " + i).ToArray();
        var effect = new DeleteDialogEffect(() => names);
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, new[] { effect }, new PossessionDirectorHead { Attribution = _ => new Attribution() });
        host.PossessionWindow!.Show();
        try
        {
            director.AttachHost(host);
            typeof(PossessionDirector).GetField("_intensity", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(director, PossessionIntensity.FullDoki);
            var hosts = (IList)typeof(PossessionDirector).GetField("_hosts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(director)!;
            var start = typeof(PossessionDirector).GetMethod("StartOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var starting = (Task)start.Invoke(director, new object[] { hosts[0]!, PossessionRung.ItKnows, TimeSpan.FromMinutes(1), 0.9 })!;
            var dialog = (DeleteDialogWindow)typeof(DeleteDialogEffect).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(effect)!;
            Assert.False(starting.IsCompleted);
            await starting.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(dialog.IsVisible);
            Assert.Equal(100, dialog.FindControl<ProgressBar>("Progress")!.Value);
            Assert.All(dialog.FindControl<StackPanel>("Names")!.Children.OfType<TextBlock>(), row => Assert.Equal(0.35, row.Opacity));
            Assert.Equal("nothing was deleted. i just wanted to watch you read it.", dialog.FindControl<TextBlock>("Status")!.Text);
            await Task.Delay(1100);
            Assert.True(dialog.IsVisible);
            Assert.Equal(1, director.LiveEffectCount);
        }
        finally { director.UndoAll(); host.PossessionWindow.Close(); }
    });

    [Fact]
    public Task DirectorUndoAllClosesTheRegisteredFullDokiDialog() => AvaloniaTestDispatcher.RunAsync(async () =>
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
            var starting = (Task)start.Invoke(director, new object[] { hosts[0]!, PossessionRung.ItKnows, TimeSpan.FromMinutes(1), 0.9 })!;
            Assert.Equal(1, director.LiveEffectCount);
            var dialog = (DeleteDialogWindow)typeof(DeleteDialogEffect).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(effect)!;
            Assert.True(dialog.IsVisible);
            director.UndoAll();
            Assert.False(dialog.IsVisible);
            Assert.False(effect.IsLive);
            Assert.Equal(0, director.LiveEffectCount);
            await starting.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { director.UndoAll(); host.PossessionWindow.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
