using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

/// <summary>Dispatcher adapter for Core playback. WPF EmiChainPlayer.cs clock semantics unchanged.</summary>
public sealed class EmiChainPlayer : IDisposable, IEmiChainClock
{
    private readonly Dispatcher _dispatcher;
    private readonly EmiChainPlayback _playback;
    private DispatcherTimer? _timer;
    private Action? _tick;
    public EmiChainPlayer(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _playback = new EmiChainPlayback(this);
    }
    public EmiChain? Current => _playback.Current;
    public bool IsLive => _playback.IsLive;
    public void Play(EmiChain? chain, EmiChainHooks hooks, string? bodyFrameOverride = null) => _playback.Play(chain, hooks, bodyFrameOverride);
    public void Cancel() => _playback.Cancel();
    public void Dispose() => _playback.Dispose();
    bool IEmiChainClock.CheckAccess() => _dispatcher.CheckAccess();
    void IEmiChainClock.Post(Action action) => _dispatcher.Post(action);
    void IEmiChainClock.Start(int ms, Action tick)
    {
        _tick = tick;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ms) };
        _timer.Tick += OnTick;
        _timer.Start();
    }
    private void OnTick(object? sender, EventArgs e)
    {
        var tick = _tick;
        ((IEmiChainClock)this).Stop();
        tick?.Invoke();
    }
    void IEmiChainClock.Stop()
    {
        if (_timer != null) { _timer.Stop(); _timer.Tick -= OnTick; _timer = null; }
        _tick = null;
    }
}
