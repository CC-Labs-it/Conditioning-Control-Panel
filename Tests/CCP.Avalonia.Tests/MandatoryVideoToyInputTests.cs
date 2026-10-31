using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MandatoryVideoToyInputTests
{
    [Fact]
    public async Task Toy_input_catches_visible_targets_and_expires_with_the_batch_or_panic()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            using var h = new HapticService(new HapticSettings { ToyInputEnabled = true, AttentionCheckToyButton = true });
            using var p = new ButtonProvider();
            h.DeviceManager.Register(p);
            var o = MandatoryVideoOverlay.Instance;
            var old = (CoreHaptics.Service, CoreProgression.AddXPProvider, CoreDispatch.PostProvider, o.Scheduler);
            var pending = new Queue<Action>();
            CoreDispatch.PostProvider = pending.Enqueue;
            CoreHaptics.Service = h;
            var xp = 0.0;
            CoreProgression.AddXPProvider = (x, _) => xp += x;
            o.Scheduler = new MandatoryVideoScheduler(o, new StillTime(), Array.Empty<string>);
            var layer = new Canvas { Width = 800, Height = 600 };
            var w = new Window { Content = layer, Width = 800, Height = 600 };
            w.Show();
            var buttons = new MandatoryVideoOverlay.VideoButtons(o.ToggleUserMute, o.CloseFromButton);
            ((List<MandatoryVideoOverlay.Surface>)o.Surfaces).Add(new(w, new Image(), new Border(), new Image(), layer, new Border(), new TextBlock(), buttons, new Panel()));
            void Drain() { while (pending.TryDequeue(out var work)) work(); Dispatcher.UIThread.RunJobs(); }
            try
            {
                Assert.True(o.Scheduler.Trigger(false, "toy-test.mp4"));   // playing; the pre-roll never fires
                o.Spawn(0);
                Assert.Equal(1, o.LiveTargets);
                p.Press("a"); p.Press("b"); Drain();
                Assert.Equal(0, o.LiveTargets);
                Assert.Equal(1, o.Scheduler.AttentionHits);
                Assert.Equal(15, xp);

                o.Spawn(0);
                o.Step(CoreSettings.Current.AttentionLifespan + 1, 0);
                p.Press("expired"); Drain();
                Assert.Equal(1, o.Scheduler.AttentionHits);
                Assert.Equal(0, o.LiveTargets);

                o.Spawn(0);
                p.Press("queued-before-panic");
                o.Scheduler.Stop(); Drain();   // engine stop / panic -> ForceCleanup -> host CloseAll
                Assert.False(o.Scheduler.IsPlaying);
                Assert.Equal(1, o.Scheduler.AttentionHits);
                Assert.Empty(o.Windows);
            }
            finally
            {
                o.CloseAll();
                (CoreHaptics.Service, CoreProgression.AddXPProvider, CoreDispatch.PostProvider, o.Scheduler) = old;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Test_video_reset_prompt_offers_Yes_and_No_and_No_keeps_the_clip()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var old = CoreEngine.Video;
            var fake = new VideoHost();
            var scheduler = new MandatoryVideoScheduler(fake);
            CoreEngine.Video = scheduler;
            var card = new VideoFeatureControl();
            var owner = new Window { Content = card };
            owner.Show();
            try
            {
                Assert.True(scheduler.Trigger(path: "/test/clip.mp4"));
                card.FindControl<Button>("BtnTestVideo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal("Video Playing", dialog.Title);
                Assert.Equal("Yes", Assert.IsType<TextBlock>(dialog.FindControl<Button>("BtnOk")!.Content).Text);
                var no = dialog.FindControl<Button>("BtnCancel")!;
                Assert.Equal("No", Assert.IsType<TextBlock>(no.Content).Text);
                no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await card.TestVideoRun;   // the click's own completion, not a yield (P57)
                Assert.True(scheduler.IsPlaying);
                Assert.Equal(0, fake.Closed);
            }
            finally { scheduler.ForceCleanup(); owner.Close(); CoreEngine.Video = old; }
        });
    }

    private sealed class VideoHost : IMandatoryVideoHost
    {
        public int Closed;
        public void Show(string path, bool strict) { }
        public double CloseAll() { Closed++; return 0; }
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }

    private sealed class StillTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => new Never();
        private sealed class Never : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

    private sealed class ButtonProvider : IHapticProviderV2
    {
        public string Key => "test-buttons";
        public string DisplayName => Key;
        public bool IsConnected => true;
        public IReadOnlyList<HapticDevice> Devices => Array.Empty<HapticDevice>();
        public event EventHandler<HapticToyEvent>? ToyEvent;
#pragma warning disable CS0067
        public event EventHandler? DevicesChanged;
        public event EventHandler<string>? Error;
#pragma warning restore CS0067
        public void Press(string key) => ToyEvent?.Invoke(this, new HapticToyEvent { DeviceKey = key, Kind = ToyEventKind.ButtonPressed });
        public Task<bool> ConnectAsync(CancellationToken ct) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task SetOutputsAsync(string id, IReadOnlyList<ActuatorOutput> outputs, CancellationToken ct) => Task.CompletedTask;
        public Task StopAllAsync() => Task.CompletedTask;
        public Task<bool> PingAsync() => Task.FromResult(true);
        public void Dispose() { }
    }
}
