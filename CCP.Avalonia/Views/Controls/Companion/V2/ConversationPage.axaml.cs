using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;

/// <summary>
/// PORTED from WPF V2/ConversationPage.xaml.cs. The room (<see cref="CompanionRoomView"/>) mounts it
/// as its first section and calls <see cref="Resume"/>/<see cref="Park"/> from its effective-visibility
/// watch (WPF IsVisibleChanged). The fallback sheet is not ported: WPF OpenSheet navigates to the
/// Companion pages whenever they are registered, which is always in the real app.
/// </summary>
public partial class ConversationPage : UserControl
{
    private bool _followBottom = true;
    internal ConversationPageVm Vm { get; }
    private CompanionRoomView? _room;
    /// <summary>Set by the room's constructor; the bindings start then, so Hero is never a null path.</summary>
    internal CompanionRoomView? Room { get => _room; set { _room = value; DataContext = Vm; } }
    public ConversationPage()
    {
        InitializeComponent();
        Vm = new ConversationPageVm(() => Room?.HeroZone.ViewModel, () => Room?.EngineZone.DataContext as EngineRoomVm);
        Vm.Turns.CollectionChanged += TurnsChanged;
        Composer.AddHandler(KeyDownEvent, Composer_KeyDown, RoutingStrategies.Tunnel);
        Transcript.ScrollChanged += (_, e) =>
        {
            if (Math.Abs(e.ExtentDelta.Y) < .1) _followBottom = Transcript.Offset.Y >= Transcript.ScrollBarMaximum.Y - 24;
        };
    }
    internal void Resume() => Vm.Resume();
    internal void Park() => Vm.Detach();
    // A closed shell does not detach its tree (MainShellWindow unsubscribes statics on Closed), so
    // both edges release the static subscriptions (CoreAccount, the ask service, the brain's session).
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Release(); base.OnDetachedFromVisualTree(e); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Services.Companion.Asks.CompanionAskService.Instance.FocusChatRequested -= FocusComposer;
        Services.Companion.Asks.CompanionAskService.Instance.FocusChatRequested += FocusComposer;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null) _window.Closed += WindowClosed;
    }
    private Window? _window;
    private void WindowClosed(object? sender, EventArgs e) => Release();
    private void Release()
    {
        if (_window != null) { _window.Closed -= WindowClosed; _window = null; }
        Vm.Stop();
        Vm.Detach();
        Services.Companion.Asks.CompanionAskService.Instance.FocusChatRequested -= FocusComposer;
    }
    private void TurnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_followBottom) Dispatcher.UIThread.Post(() => Transcript.ScrollToEnd(), DispatcherPriority.Normal);
    }
    internal async void Send_Click(object? sender, RoutedEventArgs e)
    {
        _followBottom = true;
        await Vm.SendAsync();
        if (IsEffectivelyVisible) Composer.Focus();
    }
    /// <summary>Enter sends, Shift+Enter is a new line (WPF Composer_KeyDown).</summary>
    private void Composer_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        Send_Click(sender, e);
    }
    private void Starter_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm.Busy || sender is not Button { Content: TextBlock label }) return;
        Vm.Draft = label.Text ?? string.Empty;
        Composer.Focus();
    }
    private void Start_Click(object? sender, RoutedEventArgs e) { Vm.Start(); Composer.Focus(); }
    private void Stop_Click(object? sender, RoutedEventArgs e) => Vm.Stop();
    private async void New_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner
            && await MessageDialog.ConfirmAsync(owner, Loc.Get("companion_v2_new"), Loc.Get("companion_v2_history_note")))
            Vm.NewChat();
    }
    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            try { await clipboard.SetTextAsync(text); } catch (Exception ex) { Serilog.Log.Debug("Companion copy failed: {E}", ex.Message); }
    }
    private void Activity_Click(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ConversationAction a }) Vm.OpenActivity(a); }
    private void AskChoice_Click(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ConversationAskChoice c }) Vm.AnswerAsk(c); }
    private void Link_Click(object? sender, RoutedEventArgs e) { if (sender is Button { DataContext: ConversationLine l }) Vm.OpenLink(l); }
    private void FocusComposer()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(FocusComposer); return; }
        if (IsEffectivelyVisible) Composer.Focus();
        else AvatarTube.AvatarTubeWindow.Live?.OpenChatInput();
    }
    /// <summary>WPF OpenSheet: who/personality, permissions, videos, memory and connection are Companion pages.</summary>
    internal static string? PageFor(string? kind) => kind switch
    {
        "who" or "personality" => "personality",
        "permissions" => "permissions",
        "videos" => "companionlinks",
        "memory" or "connection" => "companionai",
        _ => null
    };
    private void Sheet_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string kind } && PageFor(kind) is { } page)
            (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab(page);
    }
}
