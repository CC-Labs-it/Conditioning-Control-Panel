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
    [Fact]
    public void SessionNamesAreReadOnlyCustomFirstAndEmptyIsRefused() => AvaloniaTestDispatcher.Run(() =>
    {
        var folder = Path.Combine(TestUserDataProfile.Root, "real-session-names");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "source.session.json"), "{\"name\":\"My Session\"}");
        File.WriteAllText(Path.Combine(folder, "secret.session.json"), "{\"name\":\"secret.session.json\"}");
        File.WriteAllText(Path.Combine(folder, "unnamed.session.json"), "{}");
        int changes = 0;
        using var spy = new FileSystemWatcher(folder) { EnableRaisingEvents = true };
        spy.Changed += (_, _) => Interlocked.Increment(ref changes);
        spy.Created += (_, _) => Interlocked.Increment(ref changes);
        spy.Deleted += (_, _) => Interlocked.Increment(ref changes);
        try
        {
            var names = DeleteDialogEffect.ReadNames(folder, new[] { "my session", "Built in", "C:/private", "Three", "Four", "Five", "Six", "Seven" });
            Assert.Equal(new[] { "My Session", "Built in", "Three", "Four", "Five", "Six" }, names);
            Assert.Empty(DeleteDialogEffect.ReadNames(Path.Combine(folder, "absent"), Array.Empty<string>()));
            var host = new Host();
            host.PossessionWindow!.Show();
            try
            {
                var effect = new DeleteDialogEffect { Names = () => Array.Empty<string>() };
                var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.ItKnows,
                    Intensity = PossessionIntensity.FullDoki, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                    Remaining = TimeSpan.Zero, Name = (_, _) => { } };
                Assert.False(effect.CanApply(ctx, null));
                effect.Names = () => names;
                Assert.True(effect.CanApply(ctx, null));
            }
            finally { host.PossessionWindow.Close(); }
            SpinWait.SpinUntil(() => Volatile.Read(ref changes) > 0, 200);
            Assert.Equal(0, Volatile.Read(ref changes));
        }
        finally { spy.EnableRaisingEvents = false; Directory.Delete(folder, true); }
    });

    [Fact]
    public void PortraitCaptionsAndProgressUseTheProvidedData() => AvaloniaTestDispatcher.Run(() =>
    {
        using var image = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
        var captions = new HashSet<string>();
        for (int i = 0; i < 3; i++)
        {
            var dialog = new DeleteDialogWindow(new[] { "Real name", "Another" }, i, image);
            dialog.Show();
            try
            {
                Assert.Same(image, dialog.FindControl<Image>("Portrait")!.Source);
                Assert.False(dialog.FindControl<Control>("Fallback")!.IsVisible);
                captions.Add(dialog.FindControl<TextBlock>("Caption")!.Text!);
                Assert.Equal("  Real name", ((TextBlock)dialog.FindControl<StackPanel>("Names")!.Children[0]).Text);
                dialog.Advance(1775);
                Assert.Equal(25, dialog.FindControl<ProgressBar>("Progress")!.Value, 3);
                dialog.Advance(7100);
                Assert.Contains("nothing was deleted", dialog.FindControl<TextBlock>("Status")!.Text);
                Assert.True(dialog.IsVisible);
                dialog.Advance(11000);
                Assert.False(dialog.IsVisible);
            }
            finally { if (dialog.IsVisible) dialog.Close(); }
        }
        Assert.Equal(3, captions.Count);
    });

    [Fact]
    public async Task DirectorKeepsTheCompletionVisibleUntilFailsafe()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var host = new Host();
            var effect = new DeleteDialogEffect { Names = () => new[] { "Real session" } };
            using var lockdown = new LockdownService();
            using var director = new PossessionDirector(lockdown, new[] { effect }, new PossessionDirectorHead { Attribution = _ => new Attribution() });
            host.PossessionWindow!.Show();
            try
            {
                director.AttachHost(host);
                typeof(PossessionDirector).GetField("_intensity", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(director, PossessionIntensity.FullDoki);
                var hosts = (IList)typeof(PossessionDirector).GetField("_hosts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(director)!;
                var start = typeof(PossessionDirector).GetMethod("StartOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)start.Invoke(director, new object[] { hosts[0]!, PossessionRung.ItKnows, TimeSpan.FromMinutes(1), 0.9 })!;
                var dialog = (DeleteDialogWindow)typeof(DeleteDialogEffect).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(effect)!;
                await Task.Delay(8500);
                Assert.True(dialog.IsVisible);
                Assert.Contains("nothing was deleted", dialog.FindControl<TextBlock>("Status")!.Text);
                await Task.Delay(3000);
                Assert.False(dialog.IsVisible);
            }
            finally { director.UndoAll(); host.PossessionWindow.Close(); }
        });
    }

    [Fact]
    public void NewStringsExistInAllLanguages() => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            foreach (var (code, _, _) in ConditioningControlPanel.Localization.LocalizationManager.AvailableLanguages)
            {
                loc.SetLanguage(code);
                var strings = (IDictionary<string, string>)typeof(ConditioningControlPanel.Localization.LocalizationManager)
                    .GetField("_strings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(loc)!;
                foreach (var key in new[] { "possession_delete_caption_tidy", "possession_delete_caption_remove", "possession_delete_caption_housekeeping", "possession_delete_complete", "possession_delete_consent" })
                    Assert.True(strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) && value != key, code + ": " + key);
            }
        }
        finally { loc.SetLanguage(previous); }
    });

    [Fact]
    public void CaptionsVary() => AvaloniaTestDispatcher.Run(() =>
    {
        var captions = Enumerable.Range(0, 3).Select(i => new DeleteDialogWindow(new[] { "Session" }, i, null))
            .Select(w => w.FindControl<TextBlock>("Caption")!.Text).ToArray();
        Assert.Equal(3, captions.Distinct().Count());
    });

    [Fact]
    public void PortraitUsesCompanionImage() => AvaloniaTestDispatcher.Run(() =>
    {
        using var image = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
        var dialog = new DeleteDialogWindow(new[] { "Session" }, 0, image);
        Assert.Same(image, dialog.FindControl<Image>("Portrait")!.Source);
        Assert.False(dialog.FindControl<Control>("Fallback")!.IsVisible);
    });

    [Fact]
    public void ProgressAdvancesBetweenNames() => AvaloniaTestDispatcher.Run(() =>
    {
        var dialog = new DeleteDialogWindow(new[] { "First", "Second" }, 0, null);
        dialog.Show();
        try
        {
            dialog.Advance(1775);
            Assert.Equal(25, dialog.FindControl<ProgressBar>("Progress")!.Value, 3);
        }
        finally { dialog.Close(); }
    });

}
