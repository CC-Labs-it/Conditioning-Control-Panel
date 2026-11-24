// WPF Services/Possession/Scenes/WhereYouAreScene.cs:24-72.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Scenes;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class WhereYouAreScene : IPossessionScene
{
    private static readonly List<WhereYouAreScene> Live = new();
    private PossessionLease? _lease;
    private IDisposable? _outline;
    private IFrameSource? _frames;
    private TaskCompletionSource? _done;
    private TimeSpan? _start;
    private double _amp, _lean;
    private int _generation;
    private CancellationTokenRegistration _cancellation;
    public string Id => "scene_where_you_are";
    public PossessionRung MinRung => PossessionRung.Melt;
    public int Beats => 2;
    public bool IsRunning { get; private set; }

    public async Task RunAsync(PossessionContext ctx, IPossessionHost host, Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct)
    {
        if (IsRunning || host is not IAvaloniaPossessionHost av) return;
        IsRunning = true;
        int generation = ++_generation;
        try
        {
            var target = pick(PossessionRole.Card) ?? pick(PossessionRole.Button);
            if (target?.Visual is not Control el || PossessionTree.IsOffLimits(el) || PossessionLease.IsLeased(el)) return;
            await ctx.Attribution.ChargeAsync(el, ct, 320).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (generation != _generation) return;
            _outline = ctx.Attribution.Possess(el);
            ctx.Name(Id, target.DisplayName);
            _lease = PossessionLease.Take(el);
            if (_lease == null) return;
            _lease.SetOrigin(0.5, 1);
            _amp = ctx.Photosafe ? 0.5 : 1;
            _lean = 1.8 * _amp * (ctx.Rng.Next(2) == 0 ? -1 : 1);
            _frames = av.PossessionFrames;
            _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Live.Add(this);
            _frames.Subscribe(OnFrame);
            var done = _done;
            _cancellation = ct.Register(() =>
            {
                void Stop() { if (generation == _generation) _ = UndoAsync(TimeSpan.Zero); }
                if (Dispatcher.UIThread.CheckAccess()) Stop(); else Dispatcher.UIThread.Post(Stop);
            });
            await done.Task.ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Possession scene {Id} failed: {Error}", Id, ex.Message); _ = UndoAsync(TimeSpan.Zero); }
        finally { IsRunning = false; }
    }

    private void OnFrame(TimeSpan now)
    {
        if (_lease == null) return;
        _start ??= now;
        double ms = (now - _start.Value).TotalMilliseconds;
        double breath = ms < 700 ? Math.Clamp(ms / 700, 0, 1) : 1 - Math.Clamp((ms - 700) / 700, 0, 1);
        breath = (1 - Math.Cos(Math.PI * breath)) / 2;
        double sag = Math.Clamp((ms - 1460) / 1100, 0, 1);
        sag = sag * sag * (3 - 2 * sag);
        _lease.Scale.ScaleX = 1 + 0.035 * _amp * breath;
        _lease.Scale.ScaleY = 1 + 0.035 * _amp * breath - 0.03 * _amp * sag;
        _lease.Skew.AngleX = _lean * sag;
        _lease.Translate.Y = 7 * _amp * sag;
        if (ms >= 2860) { _frames?.Unsubscribe(OnFrame); _frames = null; _done?.TrySetResult(); }
    }

    public Task UndoAsync(TimeSpan duration)
    {
        ++_generation;
        _cancellation.Dispose(); _cancellation = default;
        _frames?.Unsubscribe(OnFrame); _frames = null; _start = null;
        _lease?.ReleaseImmediate(); _lease = null;
        _outline?.Dispose(); _outline = null;
        Live.Remove(this);
        _done?.TrySetResult(); _done = null;
        return Task.CompletedTask;
    }

    internal static void UndoAllLive()
    {
        foreach (var scene in Live.ToArray()) _ = scene.UndoAsync(TimeSpan.Zero);
    }
}
