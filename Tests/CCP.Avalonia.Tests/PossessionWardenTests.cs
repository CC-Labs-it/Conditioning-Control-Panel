using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Reflection;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Bark;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionWardenTests
{
    [Fact]
    public void InstallConnectsDirectorToWarden() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var lockdown = new LockdownService();
        var oldWarden = PossessionAvaloniaHead.Warden;
        var oldPrompt = ConditioningControlPanel.Services.Companion.Brain.PromptAssembler.PossessionState;
        PossessionAvaloniaHead.Warden = null;
        PossessionDirector? director = null;
        try
        {
            director = PossessionAvaloniaHead.Install(lockdown);
            Assert.NotNull(PossessionAvaloniaHead.Warden);
            Assert.Same(PossessionAvaloniaHead.Warden, director.Warden);
        }
        finally
        {
            director?.Dispose(); PossessionAvaloniaHead.Warden?.Dispose(); PossessionAvaloniaHead.Warden = oldWarden;
            PossessionAudio.Uninstall(); PossessionRemember.ResetForTests();
            ConditioningControlPanel.Services.Companion.Brain.PromptAssembler.PossessionState = oldPrompt;
            lockdown.Dispose(); CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public void AvailabilityFollowsWardenToggle() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true; CoreSettings.Current.LockdownWardenEnabled = true;
        var lockdown = new LockdownService();
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(lockdown);
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube;
            Assert.True(warden.IsAvailable);
            CoreSettings.Current.LockdownWardenEnabled = false;
            Assert.False(warden.IsAvailable);
        }
        finally { warden.Stop(); AvatarTubeWindow.TestLive = null; tube.Close(); lockdown.Dispose(); CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task LeaveShowsNoteAndReturnRestoresTube() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        var lockdown = new LockdownService();
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(lockdown);
        var oldBark = BarkHead.Engine; var spoken = new List<string>();
        var bark = new BarkEngine(() => new BarkRuleSet(new[] { new BarkRule { Id = "warden_leave",
            Trigger = PossessionBarkTriggers.Warden, VariantPool = new() { new BarkVariant("{verb}") } } }))
            { Speak = line => spoken.Add(line.Text) };
        try
        {
            bark.Start(); BarkHead.Engine = bark; tube.Show(); AvatarTubeWindow.TestLive = tube;
            tube.Position = new PixelPoint(200, 250);
            var note = tube.FindControl<Border>("PossessionNoteCard")!;
            Assert.True(warden.IsAvailable);
            await warden.LeaveAsync(CancellationToken.None);
            Assert.NotEqual(new PixelPoint(200, 250), tube.Position);
            Assert.True(note.IsVisible);
            Assert.False(string.IsNullOrWhiteSpace(tube.FindControl<TextBlock>("TxtPossessionNote")!.Text));
            Assert.False(warden.IsAvailable); // appearance cooldown
            await warden.ReturnAsync(CancellationToken.None);
            Assert.False(note.IsVisible);
            Assert.Equal(new PixelPoint(200, 250), tube.Position);
            Assert.Equal(new[] { "leave", "return" }, spoken);
        }
        finally { warden.Stop(); BarkHead.Engine = oldBark; AvatarTubeWindow.TestLive = null; tube.Close(); lockdown.Dispose(); CoreSettings.ServiceProvider = null; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicRestoresDuringLeaveOrReturn(bool returning) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        var lockdown = new LockdownService();
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(lockdown);
        var previous = PossessionAvaloniaHead.Warden;
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            var home = new PixelPoint(200, 250); tube.Position = home;
            Task move;
            if (returning) { await warden.LeaveAsync(CancellationToken.None); move = warden.ReturnAsync(CancellationToken.None); }
            else move = warden.LeaveAsync(CancellationToken.None);
            await Task.Delay(90);
            Assert.NotEqual(home, tube.Position);
            PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
            Assert.Equal(home, tube.Position);
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            Assert.False(warden.IsAvailable);
            await move;
            await Task.Delay(120);
            Assert.Equal(home, tube.Position);
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
        }
        finally { warden.Stop(); PossessionAvaloniaHead.Warden = previous; AvatarTubeWindow.TestLive = null; tube.Close(); lockdown.Dispose(); CoreSettings.ServiceProvider = null; }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task KnockAndStareMoveThenPanicRestores(bool stare) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.AvatarMuted = false;
        CoreSettings.Current.LockdownWardenEnabled = true;
        var lockdown = new LockdownService();
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        var button = new Button { Content = "card", Width = 120, Height = 60 };
        var window = new Window { Width = 320, Height = 240, Content = button, Position = new PixelPoint(500, 300) };
        var field = typeof(Application).GetField("_applicationLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldLifetime = field.GetValue(Application.Current);
        using var lifetime = new ClassicDesktopStyleApplicationLifetime { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var warden = new Warden(lockdown);
        var previous = PossessionAvaloniaHead.Warden;
        try
        {
            field.SetValue(Application.Current, lifetime);
            window.Show(); tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            var home = new PixelPoint(50, 50); tube.Position = home;
            var move = stare ? warden.StareAsync("stop", CancellationToken.None)
                : warden.KnockAsync(new PossessionTarget { Visual = button, Key = "card", Role = PossessionRole.Card }, CancellationToken.None);
            await Task.Delay(stare ? 550 : 200);
            Assert.NotEqual(home, tube.Position);
            if (stare && BarkHead.Engine == null)
            {
                var bubble = tube.FindControl<Border>("SpeechBubble")!;
                for (int i = 0; i < 120 && !bubble.IsVisible; i++) await Task.Delay(25);
                Assert.True(bubble.IsVisible);
            }
            PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
            Assert.Equal(home, tube.Position);
            Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            await move;
            await Task.Delay(100);
            Assert.Equal(home, tube.Position);
        }
        finally
        {
            warden.Stop(); PossessionAvaloniaHead.Warden = previous; AvatarTubeWindow.TestLive = null;
            tube.Close(); window.Close(); field.SetValue(Application.Current, oldLifetime); lifetime.MainWindow = null;
            lockdown.Dispose(); CoreSettings.ServiceProvider = null;
        }
    });

    [Theory]
    [InlineData("lockdown")]
    [InlineData("engine")]
    [InlineData("remote")]
    [InlineData("remote-session")]
    public Task StopRoutesCancelAnActiveGlide(string route) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        var lockdown = new LockdownService();
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(lockdown);
        var previous = PossessionAvaloniaHead.Warden;
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            var home = new PixelPoint(200, 250); tube.Position = home;
            if (route == "lockdown") lockdown.Activate(TimeSpan.FromMinutes(2));
            var move = warden.LeaveAsync(CancellationToken.None);
            await Task.Delay(90);
            Assert.NotEqual(home, tube.Position);
            if (route == "lockdown") lockdown.Deactivate();
            else if (route == "engine") MainShellWindow.StopEngine();
            else if (route == "remote-session") MainShellWindow.StopSessionFromRemote();
            else
            {
                var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                RemoteStopPass.Post();
                Dispatcher.UIThread.Post(() => dispatched.SetResult());
                await dispatched.Task;
            }
            Assert.Equal(home, tube.Position);
            await move;
            await Task.Delay(70);
            Assert.Equal(home, tube.Position);
        }
        finally { warden.Stop(); PossessionAvaloniaHead.Warden = previous; AvatarTubeWindow.TestLive = null; tube.Close(); lockdown.Dispose(); CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task PanicRedocksAttachedTube() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = false;
        CoreSettings.Current.LockdownWardenEnabled = true;
        var lockdown = new LockdownService();
        var parent = new Window { Width = 400, Height = 350, Position = new PixelPoint(300, 200) };
        var tube = new AvatarTubeWindow(parent) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(lockdown);
        var previous = PossessionAvaloniaHead.Warden;
        try
        {
            parent.Show(); tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            tube.UpdatePosition(); var home = tube.Position;
            var move = warden.LeaveAsync(CancellationToken.None);
            await Task.Delay(100);
            Assert.NotEqual(home, tube.Position);
            PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
            Assert.Equal(home, tube.Position);
            await move;
            Assert.Equal(home, tube.Position);
        }
        finally { warden.Stop(); PossessionAvaloniaHead.Warden = previous; AvatarTubeWindow.TestLive = null; tube.Close(); parent.Close(); lockdown.Dispose(); CoreSettings.ServiceProvider = null; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task KnockAndStareCompleteAndNameTheirVerb(bool stare) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true; CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true; CoreSettings.Current.BarkChatSuppressionMs = 0;
        var lockdown = new LockdownService();
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        var button = new Button { Content = "card", Width = 120, Height = 60 };
        var window = new Window { Width = 320, Height = 240, Content = button, Position = new PixelPoint(500, 300) };
        var field = typeof(Application).GetField("_applicationLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldLifetime = field.GetValue(Application.Current); var oldBark = BarkHead.Engine;
        using var lifetime = new ClassicDesktopStyleApplicationLifetime { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var warden = new Warden(lockdown);
        string? spoken = null;
        var bark = new BarkEngine(() => new BarkRuleSet(new[] { new BarkRule { Id = "warden_test",
            Trigger = PossessionBarkTriggers.Warden, VariantPool = new() { new BarkVariant("{verb}") } } }))
            { Speak = line => spoken = line.Text };
        try
        {
            bark.Start(); BarkHead.Engine = bark; field.SetValue(Application.Current, lifetime);
            window.Show(); tube.Show(); AvatarTubeWindow.TestLive = tube;
            var home = new PixelPoint(50, 50); tube.Position = home;
            if (stare) await warden.StareAsync("stop", CancellationToken.None);
            else await warden.KnockAsync(new PossessionTarget { Visual = button, Key = "card", Role = PossessionRole.Card }, CancellationToken.None);
            Assert.Equal(stare ? "stare" : "knock", spoken);
            if (stare) Assert.Equal(home, tube.Position);
            else { Assert.NotEqual(home, tube.Position); warden.Stop(); Assert.Equal(home, tube.Position); }
        }
        finally
        {
            warden.Stop(); BarkHead.Engine = oldBark; AvatarTubeWindow.TestLive = null;
            tube.Close(); window.Close(); field.SetValue(Application.Current, oldLifetime); lifetime.MainWindow = null;
            lockdown.Dispose(); CoreSettings.ServiceProvider = null;
        }
    });

}
