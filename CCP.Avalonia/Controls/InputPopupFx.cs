using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>Inputs.xaml PopupAnimation: WPF slides in, and fades both in and out, over 150ms.
/// Keep Fluent's popup/keyboard parts; delay only the submenu's visual close, never its input.</summary>
public static class InputPopupFx
{
    public enum Motion { None, Slide, Fade }
    public static readonly AttachedProperty<Motion> AnimationProperty =
        AvaloniaProperty.RegisterAttached<Popup, Motion>("Animation", typeof(InputPopupFx));
    public static readonly AttachedProperty<bool> OpenRequestedProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>("OpenRequested", typeof(InputPopupFx));
    public static readonly AttachedProperty<bool> IsPresentedProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>("IsPresented", typeof(InputPopupFx));
    private static readonly HashSet<State> Live = new();
    private static readonly AttachedProperty<State?> StateProperty =
        AvaloniaProperty.RegisterAttached<Popup, State?>("State", typeof(InputPopupFx));

    public static Motion GetAnimation(Popup p) => p.GetValue(AnimationProperty);
    public static void SetAnimation(Popup p, Motion v) => p.SetValue(AnimationProperty, v);
    public static bool GetOpenRequested(Popup p) => p.GetValue(OpenRequestedProperty);
    public static void SetOpenRequested(Popup p, bool v) => p.SetValue(OpenRequestedProperty, v);
    public static bool GetIsPresented(Popup p) => p.GetValue(IsPresentedProperty);
    public static void SetIsPresented(Popup p, bool v) => p.SetValue(IsPresentedProperty, v);

    static InputPopupFx()
    {
        AnimationProperty.Changed.AddClassHandler<Popup>((p, _) =>
        {
            p.GetValue(StateProperty)?.Dispose();
            var state = GetAnimation(p) == Motion.None ? null : new State(p);
            p.SetValue(StateProperty, state);
            state?.Request();
        });
        OpenRequestedProperty.Changed.AddClassHandler<Popup>((p, _) => p.GetValue(StateProperty)?.Request());
    }

    // Also used by focused tests to prove endpoints/duration without wall-clock timing assertions.
    internal static Animation CreateAnimation(Motion motion, bool opening, double height)
    {
        var property = motion == Motion.Slide ? TranslateTransform.YProperty : Visual.OpacityProperty;
        var from = motion == Motion.Slide ? height : opening ? 0d : 1d;
        var to = motion == Motion.Slide ? 0d : opening ? 1d : 0d;
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } }
            }
        };
    }

    internal static Func<Animation, Control, CancellationToken, Task> RunAnimation =
        (animation, child, token) => animation.RunAsync(child, token);

    internal static Task Pending(Popup p) => p.GetValue(StateProperty)?.Pending ?? Task.CompletedTask;

    internal static void CloseAllForPanic()
    {
        foreach (var state in Live.ToArray()) state.CloseImmediately();
    }

    private sealed class State : IDisposable
    {
        private readonly Popup _popup;
        private CancellationTokenSource? _cancel;
        private bool _disposed;
        public Task Pending { get; private set; } = Task.CompletedTask;

        public State(Popup popup)
        {
            _popup = popup;
            popup.Opened += Opened;
            popup.Closed += Closed;
            popup.DetachedFromVisualTree += Detached;
        }

        public void Request()
        {
            Cancel();
            if (GetOpenRequested(_popup))
            {
                if (_popup.Child is { } child) child.IsHitTestVisible = true;
                if (GetIsPresented(_popup)) Start(opening: true); // reopen during a closing fade
                else SetIsPresented(_popup, true);
            }
            else if (GetIsPresented(_popup) && GetAnimation(_popup) == Motion.Fade)
            {
                if (_popup.Child is { } child) child.IsHitTestVisible = false;
                Start(opening: false);
            }
            else SetIsPresented(_popup, false); // WPF Slide never animates the close
        }

        private void Opened(object? sender, EventArgs e)
        {
            Live.Add(this);
            Start(opening: true);
        }
        private void Start(bool opening)
        {
            Cancel();
            _cancel = new CancellationTokenSource();
            Pending = Animate(opening, _cancel.Token);
        }

        private async Task Animate(bool opening, CancellationToken token)
        {
            try
            {
                // The popup host must measure its child before the full-height slide is known.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                if (token.IsCancellationRequested || _disposed || !_popup.IsOpen || _popup.Child is not { } child) return;
                var motion = GetAnimation(_popup);
                var height = -child.Bounds.Height;
                if (motion == Motion.Slide)
                {
                    // When placement flips above the combo, reveal from below (WPF AnimateFromBottom).
                    if (_popup.PlacementTarget is { } target &&
                        child.PointToScreen(default).Y < target.PointToScreen(default).Y) height = -height;
                    if (child.GetVisualParent() is { } host) host.ClipToBounds = true;
                }
                // RunAsync targets the Visual, never a Transform (Avalonia's Transform cast trap).
                await RunAnimation(CreateAnimation(motion, opening, height), child, token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                // A faulted fade must still close; only a cancel (reopen/owner close) keeps it.
                if (!token.IsCancellationRequested && !opening && !_disposed) SetIsPresented(_popup, false);
            }
        }

        private void Closed(object? sender, EventArgs e)
        {
            Live.Remove(this);
            Cancel();
            SetIsPresented(_popup, false);
            // Native light-dismiss also updates the owner through the two-way requested binding.
            _popup.SetCurrentValue(OpenRequestedProperty, false);
        }
        private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => CloseImmediately();
        public void CloseImmediately()
        {
            Live.Remove(this);
            Cancel();
            SetIsPresented(_popup, false);
            _popup.Close(); // owner close/panic never waits for the visual fade
        }
        private void Cancel() { _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null; }
        public void Dispose()
        {
            _disposed = true;
            Live.Remove(this);
            Cancel();
            _popup.Opened -= Opened;
            _popup.Closed -= Closed;
            _popup.DetachedFromVisualTree -= Detached;
            SetIsPresented(_popup, false);
            if (_popup.Child is { } child) child.IsHitTestVisible = true;
        }
    }
}
