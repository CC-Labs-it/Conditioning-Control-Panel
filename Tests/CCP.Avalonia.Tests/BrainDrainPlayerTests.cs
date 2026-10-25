using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;
namespace CCP.Avalonia.Tests;
public class BrainDrainPlayerTests
{
    private sealed class FrozenClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new IdleTimer();
        private sealed class IdleTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Voice : MindWipePlayer.IVoice
    {
        public double Gain; public bool Disposed;
        public double Volume { set => Gain = value; }
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public void PlaysToEndUpdatesVolumeAndPanicStops()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        var oldPath = CorePaths.EffectiveAssetsProvider;
        var s = CoreSettings.Current; var oldEnabled = s.BrainDrainEnabled;
        var oldVolume = s.BrainDrainVolume; var oldMaster = s.MasterVolume;
        var voice = new Voice(); int plays = 0; Action? ended = null;
        var player = new BrainDrainPlayer((_, gain, _, done) => { plays++; voice.Gain = gain; ended = done; return voice; }, time: new FrozenClock(), roll: () => 0);
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            s.BrainDrainEnabled = true; s.MasterVolume = 50; s.BrainDrainVolume = 40;
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.Equal(1, plays); Assert.Equal(0.2, voice.Gain, 8);
            player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(1, plays);
            s.BrainDrainVolume = 80; Assert.Equal(0.4, voice.Gain, 8);
            ended!(); player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(2, plays);
            player.Stop(); Assert.True(voice.Disposed); Assert.False(player.IsRunning);
            player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(2, plays);
        }
        finally { player.Stop(); CorePaths.EffectiveAssetsProvider = oldPath; s.BrainDrainEnabled = oldEnabled; s.BrainDrainVolume = oldVolume; s.MasterVolume = oldMaster; Directory.Delete(root, true); }
    }
    [Fact]
    public void StopOfARunningPlayerClearsTheDiscordPresence()
    {
        // WPF LockCard/BrainDrainService.cs:270: Stop of a running service goes back to idle; a stopped one leaves
        // whatever activity another feature set.
        var oldRpc = global::ConditioningControlPanel.Avalonia.App.DiscordRpc;
        var rpc = global::ConditioningControlPanel.Avalonia.App.DiscordRpc = new ConditioningControlPanel.Services.DiscordRichPresenceService();
        var s = CoreSettings.Current; var oldEnabled = s.BrainDrainEnabled;
        var player = new BrainDrainPlayer((_, _, _, _) => null, time: new FrozenClock());
        try
        {
            s.BrainDrainEnabled = true;
            player.Start(); Assert.Equal("Deep conditioning", rpc.CurrentState);
            player.Stop(); Assert.Equal("Browsing", rpc.CurrentState);
            rpc.SetVideoActivity(); var video = rpc.CurrentState;
            player.Stop(); Assert.Equal(video, rpc.CurrentState);
        }
        finally { player.Stop(); rpc.Dispose(); global::ConditioningControlPanel.Avalonia.App.DiscordRpc = oldRpc; s.BrainDrainEnabled = oldEnabled; }
    }
    [Fact]
    public void FollowsASettingsRestoreForLiveGain()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        var oldPath = CorePaths.EffectiveAssetsProvider; var oldProvider = CoreSettings.ServiceProvider;
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var saved = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var voice = new Voice();
        var player = new BrainDrainPlayer((_, gain, _, _) => { voice.Gain = gain; return voice; }, time: new FrozenClock(), roll: () => 0);
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            var service = new ConditioningControlPanel.Services.SettingsService();
            CoreSettings.ServiceProvider = () => service;
            (service.Current.BrainDrainEnabled, service.Current.MasterVolume, service.Current.BrainDrainVolume) = (true, 100, 100);
            player.Start(); player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(1.0, voice.Gain, 8);
            service.RestoreFrom(new ConditioningControlPanel.Models.AppSettings { BrainDrainEnabled = true, MasterVolume = 100, BrainDrainVolume = 50 });
            Assert.Equal(0.5, voice.Gain, 8);
            service.Current.BrainDrainVolume = 20; Assert.Equal(0.2, voice.Gain, 8);
        }
        finally
        {
            player.Stop(); CorePaths.EffectiveAssetsProvider = oldPath; CoreSettings.ServiceProvider = oldProvider;
            if (saved is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, saved);
            Directory.Delete(root, true);
        }
    }
    [Theory]
    [InlineData("ChkEnable", "BrainDrainEnabled")]
    [InlineData("ChkMelt", "BrainDrainMeltEnabled")]
    [InlineData("ChkHighRefresh", "BrainDrainHighRefresh")]
    [InlineData("ChkAllowCapture", "AllowOverlayCapture")]
    public Task TogglesPersistAndLoad(string name, string property) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current; var member = settings.GetType().GetProperty(property)!;
        var previous = member.GetValue(settings);
        try
        {
            member.SetValue(settings, false);
            var card = new BrainDrainFeatureControl();
            var toggle = card.FindControl<CheckBox>(name)!;
            Assert.False(toggle.IsChecked);
            toggle.IsChecked = true; Assert.Equal(true, member.GetValue(settings));
            var reopened = new BrainDrainFeatureControl();
            Assert.True(reopened.FindControl<CheckBox>(name)!.IsChecked);
        }
        finally { member.SetValue(settings, previous); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task PanelLoadsWritesAndRepaintsExternalChanges() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current; var volume = s.BrainDrainVolume; var intensity = s.BrainDrainIntensity; var blur = s.BrainDrainBlurStrength;
        Window? host = null;
        try
        {
            s.BrainDrainVolume = 41; s.BrainDrainIntensity = 27;
            var card = new BrainDrainFeatureControl(); host = new Window { Content = card }; host.Show();
            Assert.Equal(41, card.FindControl<Slider>("SliderVolume")!.Value);
            Assert.True(card.FindControl<MoreFold>("FoldMore")!.IsOpen);
            card.FindControl<Slider>("SliderBlurStrength")!.Value = 0;
            Assert.Equal(0, s.BrainDrainBlurStrength);
            card.FindControl<Slider>("SliderIntensity")!.Value = 38; Assert.Equal(38, s.BrainDrainIntensity);
            s.BrainDrainVolume = 63; Dispatcher.UIThread.RunJobs();
            Assert.Equal("63%", card.FindControl<TextBlock>("TxtVolume")!.Text);
            Assert.True(card.FindControl<Border>("WithheldNotice")!.IsVisible);
        }
        finally { host?.Close(); s.BrainDrainVolume = volume; s.BrainDrainIntensity = intensity; s.BrainDrainBlurStrength = blur; }
        return Task.CompletedTask;
    });
}
