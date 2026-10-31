using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Fyp.Online;
namespace ConditioningControlPanel.Avalonia.Helpers;
// WPF Windows/Friends/FriendNotices.cs:72-73,142-160; Services/Friends/FriendsLanding.cs:66;
// MainWindow/MainWindow.OnlineMeter.cs:541-548. A folded notice returns through the same-day queue.
internal sealed class OnlineSpentToast : IDisposable
{
    private readonly FriendNoticeStack _stack = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
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
        Border.Tag = _timer;
        _stack.Add(new FriendNotice(NoticeKind.Watch, "ccp:online-media", "", DateTimeOffset.UtcNow, OnlineMeterRules.NoticeLifetimeMs));
        Border.PointerEntered += (_, _) => { _stack.Paused = true; _clock.Restart(); };
        Border.PointerExited += (_, _) => { _stack.Paused = false; _clock.Restart(); };
        _timer.Tick += (_, _) => { var elapsed = _clock.Elapsed.TotalMilliseconds; _clock.Restart(); Tick(elapsed); };
        _timer.Start();
    }
    internal void Tick(double elapsedMs)
    {
        if (!Border.IsHitTestVisible || Border.Parent == null) { _timer.Stop(); return; }
        if (_holding()) { Dispose(); _fold(); return; }
        if (_stack.Tick(elapsedMs).Count == 0) return;
        _timer.Stop();
        _service.FadeOutAndRemove(Border);
    }
    public void Dispose()
    {
        _timer.Stop();
        if (Border.Parent is Panel host) host.Children.Remove(Border);
    }
}
