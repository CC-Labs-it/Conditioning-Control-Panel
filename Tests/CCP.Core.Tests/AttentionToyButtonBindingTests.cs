using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AttentionToyButtonBindingTests
{
    [Fact]
    public void A_press_catches_one_live_batch_and_disposal_rejects_queued_events()
    {
        using var h = new HapticService(new HapticSettings { ToyInputEnabled = true, AttentionCheckToyButton = true });
        using var p = new ButtonProvider();
        h.DeviceManager.Register(p);
        var pending = new Queue<Action>();
        var old = CoreDispatch.PostProvider;
        CoreDispatch.PostProvider = pending.Enqueue;
        var rewards = new List<string>();
        h.HapticTriggered += (_, label) => rewards.Add(label);
        try
        {
            var hits = 0;
            using var batch = AttentionToyButtonBinding.Arm(h, () => true, () => hits++);
            p.Press("one"); p.Press("two");
            while (pending.TryDequeue(out var work)) work();
            Assert.Equal(1, hits);
            Assert.Single(rewards, r => r.StartsWith(nameof(HapticEventKind.ToyButtonReward) + ":"));

            var live = false;
            using var expired = AttentionToyButtonBinding.Arm(h, () => live, () => hits++);
            p.Press("expired");
            while (pending.TryDequeue(out var work)) work();
            live = true;
            p.Press("after-expiry");
            while (pending.TryDequeue(out var work)) work();
            Assert.Equal(1, hits);

            using var stopped = AttentionToyButtonBinding.Arm(h, () => true, () => hits++);
            p.Press("queued-before-stop");
            stopped!.Dispose();
            while (pending.TryDequeue(out var work)) work();
            Assert.Equal(1, hits);
            Assert.Single(rewards, r => r.StartsWith(nameof(HapticEventKind.ToyButtonReward) + ":"));
        }
        finally { CoreDispatch.PostProvider = old; }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Both_user_settings_are_required(bool input, bool attention)
    {
        using var h = new HapticService(new HapticSettings { ToyInputEnabled = input, AttentionCheckToyButton = attention });
        Assert.Null(AttentionToyButtonBinding.Arm(h, () => true, () => throw new Exception("must not hit")));
        Assert.Null(AttentionToyButtonBinding.Arm(null, () => true, () => throw new Exception("must not hit")));
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
