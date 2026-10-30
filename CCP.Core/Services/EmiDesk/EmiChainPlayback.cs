using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// Plays one chain on the supplied surface dispatcher, the clock-driven twin of chains.js <c>playChain</c>: fx fires
/// once at frame 0 (when the chain has one), the body move fires once at frame 0, the pose is
/// set once for the whole chain, and <see cref="EmiChainHooks.Done"/> fires after the LAST
/// frame's hold. <see cref="Cancel"/> kills the pending timer and does NOT call Done.
///
/// One player owns one surface: <see cref="Play"/> cancels whatever was running first.
/// </summary>
public sealed class EmiChainPlayback : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;
    private ITimer? _timer;
    private int _epoch;
    private EmiChain? _chain;
    private EmiChainHooks? _hooks;
    private int _index;
    private bool _disposed;

    /// <summary>The chain currently running, or null.</summary>
    public EmiChain? Current => _chain;

    /// <summary>True while a chain is on screen.</summary>
    public bool IsLive => _chain != null;

    public EmiChainPlayback(TimeProvider clock, Action<Action> post)
    {
        _clock = clock;
        _post = post;
    }

    /// <summary>
    /// Start a chain. <paramref name="bodyFrameOverride"/> is the call-site pose override the
    /// campus uses for the pet streak (a shared chain, a different pose).
    /// </summary>
    public void Play(EmiChain? chain, EmiChainHooks hooks, string? bodyFrameOverride = null)
    {
        if (_disposed) return;
        Cancel();
        if (chain == null || chain.Seq.Count == 0)
        {
            try { hooks?.Done?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chain done hook threw"); }
            return;
        }

        _chain = chain;
        _hooks = hooks;
        _index = 0;

        try
        {
            hooks?.Bubble?.Invoke(null);            // a new chain always starts clean
            var pose = EmiChains.FrameKey(bodyFrameOverride) ?? EmiChains.FrameKey(chain.BodyFrame);
            if (pose != null) hooks?.BodyFrame?.Invoke(pose);
            if (!string.IsNullOrEmpty(chain.Move)) hooks?.Move?.Invoke(chain.Move!);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] chain setup hook threw");
        }

        Step();
    }

    private void Step()
    {
        if (_disposed || _chain == null) return;
        var chain = _chain;
        var hooks = _hooks;
        var frame = chain.Seq[_index];

        try
        {
            if (frame.SetsBubble) hooks?.Bubble?.Invoke(frame.Bubble);
            hooks?.Draw?.Invoke(frame.Text, frame.Small, chain.Flat);
            if (_index == 0 && !string.IsNullOrEmpty(chain.Fx)) hooks?.Fx?.Invoke(chain.Fx!);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] chain frame hook threw");
        }

        _index++;

        int epoch = _epoch;
        _timer = _clock.CreateTimer(_ => _post(() => OnTick(epoch)), null,
            TimeSpan.FromMilliseconds(Math.Max(1, frame.Ms)), Timeout.InfiniteTimeSpan);
    }

    private void OnTick(int epoch)
    {
        try
        {
            if (_disposed || _chain == null || epoch != _epoch) return;
            _timer?.Dispose();
            _timer = null;

            if (_index < _chain.Seq.Count)
            {
                Step();
                return;
            }

            var hooks = _hooks;
            _chain = null;
            _hooks = null;
            try { hooks?.Done?.Invoke(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chain done hook threw"); }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] chain tick failed");
        }
    }

    /// <summary>Kill the running chain. Done is NOT called.</summary>
    public void Cancel()
    {
        _epoch++;
        try
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] chain cancel failed");
        }
        _chain = null;
        _hooks = null;
        _index = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        Cancel();
    }
}
