using System;
using System.Diagnostics;
using Avalonia.Controls;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Fyp.Online;
namespace ConditioningControlPanel.Avalonia.Helpers;
internal sealed class OnlineSpentToast : IDisposable
{
    private readonly FriendNoticeStack _stack = new();
    private readonly BeatLoop _beat;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly NotificationService _service;
    private readonly Func<bool> _holding;
    private readonly Action _fold;
    internal Border Border { get; }
    internal OnlineSpentToast(NotificationService service, Panel host, Func<bool> holding, Action fold,
        string message, string? label, Action? action)
    {
        (_service, _holding, _fold) = (service, holding, fold);
        Border = service.BuildToast(message, NotificationType.Info, null, label, action);
        host.Children.Add(Border);
        Border.Opacity = 1;
        _stack.Add(new FriendNotice(NoticeKind.Watch, "ccp:online-media", "", DateTimeOffset.UtcNow, OnlineMeterRules.NoticeLifetimeMs));
        Border.PointerEntered += (_, _) => { _stack.Paused = true; _clock.Restart(); };
        Border.PointerExited += (_, _) => { _stack.Paused = false; _clock.Restart(); };
        _beat = new BeatLoop(Border, t => { if (t == 0) return; var elapsed = _clock.Elapsed.TotalMilliseconds; _clock.Restart(); Tick(elapsed); });
        Border.DetachedFromVisualTree += (_, _) => _beat.Stop();
        _beat.Start();
    }
    internal bool Active => Border.IsHitTestVisible && Border.Parent != null;
    internal void PollHold() { if (Active && _holding()) { Dispose(); _fold(); } }
    internal void Tick(double elapsedMs)
    {
        PollHold();
        if (!Active) { _beat.Stop(); return; }
        if (_stack.Tick(elapsedMs).Count == 0) return;
        _beat.Stop();
        _service.FadeOutAndRemove(Border);
    }
    public void Dispose()
    {
        _beat.Stop();
        if (Border.Parent is Panel host) host.Children.Remove(Border);
    }
}
