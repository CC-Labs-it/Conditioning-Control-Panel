// Adapted from WPF ChasterTabView.xaml.cs:770-847. Core owns clamping and the 24-hour raise delay.
using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private void BtnLimits_Click(object? sender, RoutedEventArgs e) => LimitsCard.IsVisible = !LimitsCard.IsVisible;

    private void RefreshLimits()
    {
        var caps = ChasterHead.Service?.Caps ?? TabLimits.Default;
        var settings = CoreSettings.Current;
        _loading = true;
        try
        {
            SliderDayLimit.Value = Wanted(settings.ChasterDayLimit, caps.DailySeconds / 60);
            SliderBacklogLimit.Value = Wanted(settings.ChasterBacklogLimit, caps.BacklogSeconds / 60);
            ChkRelock.IsChecked = settings.ChasterRelockPastEnd;
        }
        finally { _loading = false; }
        PaintLimits(caps);
    }

    private static int Wanted(LimitSetting setting, int inForce)
    {
        var settled = LimitChange.Settle(setting, DateTime.UtcNow);
        return settled.HasPending ? settled.PendingMinutes : inForce;
    }

    private void PaintLimits(TabLimits caps)
    {
        TxtDayLimit.Text = CircesTab.Format(caps.DailySeconds, signed: false);
        TxtBacklogLimit.Text = CircesTab.Format(caps.BacklogSeconds, signed: false);
        TxtFactCap1.Text = TxtFactCap2.Text = TxtDayLimit.Text;
        PaintPending(TxtDayPending, CoreSettings.Current.ChasterDayLimit);
        PaintPending(TxtBacklogPending, CoreSettings.Current.ChasterBacklogLimit);
    }

    private static void PaintPending(TextBlock line, LimitSetting setting)
    {
        var settled = LimitChange.Settle(setting, DateTime.UtcNow);
        line.IsVisible = settled.HasPending;
        if (settled.HasPending)
            line.Text = Loc.GetF("chaster_limit_pending", CircesTab.Format(settled.PendingMinutes * 60, signed: false),
                settled.PendingAtUtc!.Value.ToLocalTime().ToString("ddd HH:mm"));
    }

    private void LimitSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading || !_attached) return;
        var settings = CoreSettings.Current;
        var wanted = TabLimits.FromMinutes((int)SliderDayLimit.Value, (int)SliderBacklogLimit.Value);
        var now = DateTime.UtcNow;
        settings.ChasterDayLimit = LimitChange.Request(settings.ChasterDayLimit, wanted.DailySeconds / 60, now);
        settings.ChasterBacklogLimit = LimitChange.Request(settings.ChasterBacklogLimit, wanted.BacklogSeconds / 60, now);
        _loading = true;
        try { SliderBacklogLimit.Value = wanted.BacklogSeconds / 60; }
        finally { _loading = false; }
        PaintLimits(ChasterHead.Service?.Caps ?? wanted);
        RefreshNumbers();
        CoreSettings.Save();
    }

    private void ChkRelock_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading || !_attached) return;
        CoreSettings.Current.ChasterRelockPastEnd = ChkRelock.IsChecked == true;
        CoreSettings.Save();
    }
}
