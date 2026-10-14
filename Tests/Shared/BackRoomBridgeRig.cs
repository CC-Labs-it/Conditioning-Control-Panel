using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Tests;

// The Back Room bridge rig: a manual clock, a fake relay and a recording page. Linked into
// Tests/CCP.Core.Tests (the bridge suites) and Tests/ConditioningControlPanel.Tests (the WPF head's
// haptic, voice, media and room-option suites drive the same bridge).
public partial class BackRoomBridgeTests
{
    internal const string Req = "req-0123456789abcdef";

    internal sealed class Clock
    {
        public readonly List<(TimeSpan At, Action Fn, bool Cancelled)> Timers = new();
        public Action Schedule(TimeSpan at, Action fn)
        {
            int i = Timers.Count;
            Timers.Add((at, fn, false));
            return () => Timers[i] = (Timers[i].At, Timers[i].Fn, true);
        }
        public void Fire(TimeSpan at)
        {
            foreach (var t in Timers.ToList()) if (!t.Cancelled && t.At == at) t.Fn();
        }
    }

    internal sealed class Relay : IBackRoomRelay
    {
        public readonly List<(string Station, string Op, string? Idem, JObject? Body)> Calls = new();
        public TaskCompletionSource<BackRoomStationResult> Next = New();
        public static TaskCompletionSource<BackRoomStationResult> New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem, JObject? body, CancellationToken ct)
        {
            Calls.Add((station, op, idem, body));
            return op == "cursor" ? Task.FromResult(new BackRoomStationResult(true, 200, null, null)) : Next.Task;
        }
    }

    /// <summary>The null fx object's answers, plus a count of every CancelAll.</summary>
    internal sealed class CountingFx : IBackRoomFx
    {
        private readonly NullBackRoomFx _inner = new();
        public int Cancelled;
        public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal) =>
            _inner.Fire(fxId, station, symbolKeys, deal);
        public void CancelAll() => Cancelled++;
    }

    internal sealed class Rig
    {
        public readonly CountingFx Fx = new();
        public readonly List<JObject> Posted = new();
        public readonly Clock Clock = new();
        public readonly Relay Relay = new();
        public readonly List<string> Events = new();
        public int Closed;
        public int Sp;
        public BackRoomBridge Bridge = null!;

        public Rig()
        {
            Bridge = new BackRoomBridge(new BackRoomBridge.Deps
            {
                Post = m => { lock (Posted) Posted.Add(JObject.FromObject(m)); },
                Relay = Relay,
                Fx = Fx,
                BuildInit = () => new { type = "init", protocol = 1 },
                CloseWindow = () => Closed++,
                Schedule = Clock.Schedule,
                NoteEvent = Events.Add,
                SetSp = v => { Sp = v; Bridge.OnSpChanged(v, "earn"); },
                NextSeed = () => 42,
            });
        }

        public void Send(string json) => Bridge.Handle(JObject.Parse(json));
        public List<JObject> Of(string type) { lock (Posted) return Posted.Where(p => (string?)p["type"] == type).ToList(); }

        public async Task<List<JObject>> SettleAsync(string type, int count)
        {
            for (int i = 0; i < 200 && Of(type).Count < count; i++) await Task.Delay(5);
            return Of(type);
        }
    }
}
