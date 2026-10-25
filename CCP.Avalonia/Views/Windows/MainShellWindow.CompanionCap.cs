using System;
using System.Linq;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private DispatcherTimer? _capWatch;
    private static Window? _capNotice;
    internal static void CloseCapNotice() => _capNotice?.Close();
    private static int CapTierNow => CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess ? 1 : 0;

    // WPF ConversionSignals -> OnCompanionCapHit: nothing is shown inside the conversation.
    internal void NoteCompanionCap()
    {
        try { NoteCompanionCapCore(); }
        catch (Exception ex) { Serilog.Log.Debug("Companion cap notice deferred: {E}", ex.Message); }
    }

    private void NoteCompanionCapCore()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(NoteCompanionCap); return; }
        if (CapTierNow >= 2 || !CompanionCapRules.Owed(CoreSettings.Current.InboxStamps, CompanionCapRules.DayStamp(DateTime.Now))) return;
        if (_capWatch == null)
        {
            _capWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _capWatch.Tick += (_, _) => TryFileCompanionCap();
            Closed += (_, _) => _capWatch.Stop();
        }
        _capWatch.Start();
    }

    internal void TryFileCompanionCap()
    {
        try { TryFileCompanionCapCore(); }
        catch (Exception ex) { Serilog.Log.Debug("Companion cap notice deferred: {E}", ex.Message); }
    }

    private void TryFileCompanionCapCore()
    {
        if (CurrentTab == "companion" && IsVisible && WindowState != WindowState.Minimized) return;
        _capWatch?.Stop();
        if (CompanionCapRules.CapRowOffers(CapTierNow) is not int tier) return;
        var settings = CoreSettings.Current;
        var day = CompanionCapRules.DayStamp(DateTime.Now);
        if (!CompanionCapRules.Owed(settings.InboxStamps, day)) return;
        settings.InboxStamps[CompanionCapRules.KeyCompanionCap] = day;
        CoreSettings.Save();
        var title = tier >= 2
            ? Loc.GetF("conv_cap_basic", CompanionCapRules.CompanionCapBasic, CompanionCapRules.CompanionCapPrime)
            : Loc.GetF("conv_cap_free", CompanionCapRules.CompanionCapFree, CompanionCapRules.CompanionCapBasic, CompanionCapRules.CompanionCapPrime);
        var body = Loc.Get("conv_cap_body");
        StartupLadder.Inbox.File(new InboxItem
        {
            Key = "conv:" + CompanionCapRules.KeyCompanionCap, Title = title, Summary = body,
            Open = () => ShowCapNotice(title, body, tier)
        });
    }

    // WPF InboxNoticeCard: one neutral, non-modal notice, one checkout button and a quiet close.
    private void ShowCapNotice(string title, string body, int tier)
    {
        CloseCapNotice();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = body, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var primary = new Button { Content = new TextBlock { Text = Loc.Get(tier >= 2 ? "conv_get_prime" : "conv_get_basic") },
            HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(14, 9), Margin = new(0, 16, 0, 0) };
        var close = new Button { Content = new TextBlock { Text = Loc.Get("vaultgate_close") }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 10, 0, 0) };
        var notice = new CompanionCapNotice
        {
            Title = "CC Labs", Width = 400, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#151528"), Foreground = Brushes.White,
            Content = new Border { Padding = new(22, 18, 22, 16), CornerRadius = new(14), BorderThickness = new(1.5), BorderBrush = Brush.Parse("#F5A3D3"), Child = panel }
        };
        panel.Children.Add(primary); panel.Children.Add(close);
        primary.Click += (_, _) => { notice.Close(); ExternalOpener.Open(CompanionCapRules.PlanUrl(tier)); };
        close.Click += (_, _) => notice.Close();
        notice.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(notice).Properties.IsLeftButtonPressed) return;
            if (e.Source is global::Avalonia.Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v is Button)) return;
            notice.BeginMoveDrag(e);
        };
        notice.KeyDown += (_, e) => { if (e.Key == Key.Escape) notice.Close(); };
        notice.Closed += (_, _) => { if (ReferenceEquals(_capNotice, notice)) _capNotice = null; };
        _capNotice = notice;
        if (IsVisible) notice.Show(this); else notice.Show();
    }
}

internal sealed class CompanionCapNotice : Window, IPassiveStartupSurface { }
