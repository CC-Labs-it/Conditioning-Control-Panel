using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    // WPF Services/Friends/FriendsSfx.cs:47-51,66-69: both cues stay silent, without spending the gate.
    [Theory]
    [InlineData("output", true)] [InlineData("output", false)]
    [InlineData("video", true)] [InlineData("video", false)]
    [InlineData("hold", true)] [InlineData("hold", false)]
    public Task RailDrawerSoundsRespectSuppression(string state, bool open) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var tags = new List<string>();
        var old = (CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider, CoreEngine.Video);
        var engine = EmiLineEngine.Instance;
        var video = new MandatoryVideoScheduler(new RailQuietVideoHost(), new RailStillTime());
        try
        {
            Assert.False(engine.HoldActive);
            CoreSettings.Current.MasterVolume = 80;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            CoreAudio.IsOutputSuppressedProvider = () => state == "output";
            CoreEngine.Video = video;
            if (state == "video") Assert.True(video.Trigger(path: "review-video.mp4"));
            if (state == "hold") { engine.Draw("intakeRunning"); Assert.True(engine.HoldActive); }
            FriendsLanding.SfxGate = new FriendsSfxGate();
            Action cue = open ? FriendsLanding.DrawerOpen : FriendsLanding.DrawerClose;
            cue();
            Assert.Empty(tags);
            // Deterministic proof that no suppressed cue consumed the gap, with no wall-clock wait.
            Assert.True(FriendsLanding.SfxGate.TryPass(DateTime.MinValue.AddSeconds(1), false));
            CoreAudio.IsOutputSuppressedProvider = () => false;
            video.ForceCleanup();
            if (state == "hold") engine.ReleaseHold("intakeRunning");
            Assert.False(engine.HoldActive);
            cue();
            Assert.Equal(new[] { open ? "friends-open" : "friends-close" }, tags);
            await Task.CompletedTask;
        }
        finally
        {
            if (state == "hold") engine.ReleaseHold("intakeRunning");
            video.ForceCleanup();
            (CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider, CoreEngine.Video) = old;
        }
    });

    // WPF Controls/Friends/FriendsRailChip.cs:224-228,251-269: activation uses the fresh resolver.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task RailActivationReplacesAccountAndUnsubscribesOldService(bool injected) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (first, _) = Service();
        var (second, wire) = Service("u_replacement");
        wire.StateBody = State.Replace("\"online\":false", "\"online\":true");
        FriendsHead.Service = first;
        var chip = new FriendsRailChip(injected ? first : null);
        var owner = new Window { Content = chip, Width = 400, Height = 600 };
        bool Subscribed(FriendsService service) =>
            (typeof(FriendsService).GetField(nameof(FriendsService.SnapshotChanged), BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(service) as Delegate)?.GetInvocationList().Any(d => ReferenceEquals(d.Target, chip)) == true;
        try
        {
            await first.RefreshAsync(); await second.RefreshAsync();
            owner.Show();
            // Prime the drawer's cached service, reproducing the stale account path from review.
            chip.Drawer.OnOpened(); chip.Drawer.OnClosed();
            Assert.Same(first, chip.Drawer.Service);
            Assert.Equal(1, chip.PillCount);
            Assert.True(Subscribed(first));
            FriendsHead.Service = second;
            OwnerActive(owner, true);
            Assert.Equal(injected ? 1 : 2, chip.PillCount);
            Assert.Equal(injected, Subscribed(first));
            Assert.Equal(!injected, Subscribed(second));
            FriendsHead.Service = null;
            OwnerActive(owner, true);
            Assert.Equal(injected ? 1 : 0, chip.PillCount);
            Assert.False(Subscribed(second));
            owner.Content = null;
            Assert.False(Subscribed(first));
        }
        finally { owner.Close(); first.Dispose(); second.Dispose(); }
    });

    private sealed class RailQuietVideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }
    private sealed class RailStillTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Never();
        private sealed class Never : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
