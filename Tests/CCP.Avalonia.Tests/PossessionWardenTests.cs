using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionWardenTests
{
    private sealed class FakeTube
    {
        public readonly List<string> Moves = new();
        public readonly List<string> Barks = new();
        public string? Note;
        public PixelPoint Position = new(10, 20);
        public readonly Warden Warden;
        public FakeTube(LockdownService? lockdown = null)
        {
            Warden = new Warden(lockdown)
            {
                Available = () => true,
                TubeRect = () => new PixelRect(Position, new PixelSize(100, 150)),
                TargetRect = _ => new PixelRect(400, 300, 100, 80),
                WindowRect = () => new PixelRect(100, 100, 800, 600),
                ScreenBottom = _ => 1000,
                GlideRect = (rect, ct) => { Moves.Add("rect"); Position = new PixelPoint(rect.X, rect.Y); return Task.CompletedTask; },
                GlidePoint = (point, ct, clamp) => { Moves.Add(clamp ? "point" : "leave"); Position = point; return Task.CompletedTask; },
                Home = ct => { Moves.Add("home"); Position = new PixelPoint(10, 20); return Task.CompletedTask; },
                Note = text => Note = text,
                Bark = (verb, _) => Barks.Add(verb),
                Delay = (ms, ct) => ms == WardenRules.GoHomeDelayMs ? Task.Delay(Timeout.Infinite, ct) : Task.CompletedTask,
            };
        }
    }

    [Fact]
    public Task InstallAssignsWarden() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var lockdown = new LockdownService();
        using var director = PossessionAvaloniaHead.Install(lockdown);
        Assert.IsType<Warden>(director.Warden);
        return Task.CompletedTask;
    });

    [Fact]
    public Task KnockGlidesShovesAndReturnsHome() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fake = new FakeTube();
        using var cancel = new CancellationTokenSource();
        await fake.Warden.KnockAsync(new PossessionTarget { Visual = new object(), Role = PossessionRole.Card, Key = "card" }, cancel.Token);
        Assert.Equal(new[] { "rect", "point", "point", "point", "point" }, fake.Moves);
        Assert.Contains(WardenRules.Knock, fake.Barks);
        Assert.False(fake.Warden.IsAvailable);
        await fake.Warden.ReturnAsync(CancellationToken.None);
        Assert.Equal(new PixelPoint(10, 20), fake.Position);
        cancel.Cancel();
    });

    [Fact]
    public Task KnockSchedulesHomeAfterTheBeat() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fake = new FakeTube();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var home = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Warden.Delay = (ms, _) => ms == WardenRules.GoHomeDelayMs ? release.Task : Task.CompletedTask;
        fake.Warden.Home = _ => { fake.Moves.Add("home"); home.SetResult(); return Task.CompletedTask; };
        await fake.Warden.KnockAsync(new PossessionTarget { Visual = new object(), Role = PossessionRole.Card, Key = "card" }, CancellationToken.None);
        Assert.DoesNotContain("home", fake.Moves);
        release.SetResult();
        await home.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("home", fake.Moves[^1]);
    });

    [Fact]
    public Task StareGlidesBarksAndReturnsHome() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fake = new FakeTube();
        await fake.Warden.StareAsync("close", CancellationToken.None);
        Assert.Equal(new[] { "point", "home" }, fake.Moves);
        Assert.Equal(new[] { WardenRules.Stare }, fake.Barks);
        Assert.Equal(new PixelPoint(10, 20), fake.Position);
        await fake.Warden.StareAsync("close", CancellationToken.None);
        Assert.Equal(2, fake.Moves.Count);
    });

    [Fact]
    public Task LeaveShowsNoteAndReturnClearsItOnce() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fake = new FakeTube();
        await fake.Warden.LeaveAsync(CancellationToken.None);
        Assert.Equal(new PixelPoint(10, 1000), fake.Position);
        Assert.False(string.IsNullOrWhiteSpace(fake.Note));
        Assert.Contains(WardenRules.Leave, fake.Barks);
        await fake.Warden.ReturnAsync(CancellationToken.None);
        Assert.Null(fake.Note);
        Assert.Equal(new PixelPoint(10, 20), fake.Position);
        Assert.Equal(new[] { WardenRules.Leave, WardenRules.Return }, fake.Barks);
        await fake.Warden.ReturnAsync(CancellationToken.None);
        Assert.Equal(2, fake.Barks.Count);
    });

    [Fact]
    public Task ReturnCancelsAndDrainsLeaveBeforeHome() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fake = new FakeTube();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Warden.GlidePoint = async (_, ct, _) => { fake.Moves.Add("leave"); started.SetResult(); await Task.Delay(Timeout.Infinite, ct); };
        var leave = fake.Warden.LeaveAsync(CancellationToken.None);
        await started.Task;
        await fake.Warden.ReturnAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        await leave;
        Assert.Equal(new[] { "leave", "home" }, fake.Moves);
        Assert.Null(fake.Note);
        Assert.Empty(fake.Barks);
        Assert.False(fake.Warden.IsBusy);
    });

    [Fact]
    public Task ReturnWaitsForLeaveEvenWhenGlideIgnoresCancellation() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fake = new FakeTube();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Warden.GlidePoint = async (_, _, _) => { fake.Moves.Add("leave"); started.SetResult(); await finish.Task; };
        var leave = fake.Warden.LeaveAsync(CancellationToken.None);
        await started.Task;
        var returning = fake.Warden.ReturnAsync(CancellationToken.None);
        Assert.Equal(new[] { "leave" }, fake.Moves);
        finish.SetResult();
        await returning;
        await leave;
        Assert.Equal(new[] { "leave", "home" }, fake.Moves);
        Assert.Null(fake.Note);
        Assert.Empty(fake.Barks);
    });

    [Fact]
    public Task ActivationClearsCooldownAndNote() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var lockdown = new LockdownService();
        var fake = new FakeTube(lockdown);
        await fake.Warden.LeaveAsync(CancellationToken.None);
        Assert.False(fake.Warden.IsAvailable);
        Assert.NotNull(fake.Note);
        var activated = (Action?)typeof(LockdownService).GetField("LockdownActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lockdown);
        activated!();
        Assert.True(fake.Warden.IsAvailable);
        Assert.Null(fake.Note);
        await fake.Warden.ReturnAsync(CancellationToken.None);
        Assert.DoesNotContain(WardenRules.Return, fake.Barks);
    });

    [Fact]
    public void TubeOwnershipIsExclusive()
    {
        var warden = new FakeTube().Warden;
        Assert.True(warden.TryTakeTube());
        Assert.True(warden.IsBusy);
        Assert.False(warden.TryTakeTube());
        Assert.False(warden.IsAvailable);
        warden.ReleaseTube();
        Assert.True(warden.TryTakeTube());
        warden.ReleaseTube();
    }

    [Fact]
    public Task WorkerThreadVerbMovesOnUiThread() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fake = new FakeTube();
        bool onUi = false;
        fake.Warden.ScreenBottom = _ => { onUi = Dispatcher.UIThread.CheckAccess(); return 1000; };
        await Task.Run(() => fake.Warden.LeaveAsync(CancellationToken.None));
        Assert.True(onUi);
        await fake.Warden.ReturnAsync(CancellationToken.None);
    });

    [Fact]
    public Task LiveTubeReceivesAndClearsTheNote() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        try
        {
            tube.Show();
            AvatarTubeWindow.TestLive = tube;
            tube.Position = new PixelPoint(200, 250);
            var warden = new Warden { ScreenBottom = _ => 1000, Bark = (_, _) => { } };
            Assert.True(warden.IsAvailable);
            await warden.LeaveAsync(CancellationToken.None);
            var card = tube.FindControl<Border>("PossessionNoteCard")!;
            Assert.True(card.IsVisible);
            await warden.ReturnAsync(CancellationToken.None);
            Assert.False(card.IsVisible);
            Assert.Equal(new PixelPoint(200, 250), tube.Position);
        }
        finally { AvatarTubeWindow.TestLive = null; tube.Close(); CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task UnsupportedTubeIsUnavailableAndDirectorSkipsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = false };
        try
        {
            tube.Show();
            AvatarTubeWindow.TestLive = tube;
            var warden = new Warden();
            Assert.False(warden.IsAvailable);
            int glides = 0;
            warden.GlidePoint = (_, _, _) => { glides++; return Task.CompletedTask; };
            warden.WindowRect = () => new PixelRect(100, 100, 800, 600);
            warden.TubeRect = () => new PixelRect(10, 20, 100, 150);
            warden.Delay = (_, _) => Task.CompletedTask;
            warden.Home = _ => Task.CompletedTask;
            warden.Bark = (_, _) => { };
            using var lockdown = new LockdownService();
            var head = new PossessionDirectorHead { Settings = () => new AppSettings { LockdownPossessionEnabled = true, LockdownWardenEnabled = true, LockdownTripwiresEnabled = true } };
            using var director = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>(), head) { Warden = warden };
            typeof(PossessionDirector).GetMethod("OnLockdownActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(director, null);
            typeof(PossessionDirector).GetMethod("OnEscapeAttempted", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(director, new object[] { new EscapeAttempt("close", 3, 3, DateTime.Now) });
            await Task.Yield();
            Assert.Equal(0, glides);
            warden.Available = () => true;
            using var director2 = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>(), head) { Warden = warden };
            typeof(PossessionDirector).GetMethod("OnLockdownActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(director2, null);
            typeof(PossessionDirector).GetMethod("OnEscapeAttempted", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(director2, new object[] { new EscapeAttempt("close", 3, 3, DateTime.Now) });
            Assert.Equal(1, glides);
        }
        finally { AvatarTubeWindow.TestLive = null; tube.Close(); CoreSettings.ServiceProvider = null; }
    });
}
