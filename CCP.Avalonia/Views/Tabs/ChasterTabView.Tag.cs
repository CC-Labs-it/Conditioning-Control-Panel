// WPF ChasterTabView.xaml.cs:449-465, 752-761; ChasterTabView.xaml:444-491,559-568.
// Static tags reuse Core's landing rules. Swing, tug and bob remain part B.
using System;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private void PaperTag_Click(object? sender, RoutedEventArgs e) => ToggleBill();

    internal void RefreshTag(int balance)
    {
        TxtTagAmount.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
        TxtTagAmount.Foreground = new SolidColorBrush(Color.Parse(balance > 0 ? "#C8244A" : balance < 0 ? "#1E8A6E" : "#241A2E"));
        var chaster = ChasterHead.Service;
        var line = TabPageText.Tag(balance, chaster?.PushableTodaySeconds ?? 0, chaster?.IsPaused == true,
            !string.IsNullOrEmpty(CoreSettings.Current.ChasterLockId) && chaster?.LockLookup != LockLookup.Ambiguous,
            chaster?.AddsBlocked == true);
        TxtTagLands.Text = line.Today == null ? Loc.Get(line.Key) : Loc.GetF(line.Key, line.Today, line.Later!);
        TxtTagStamp.Text = Loc.Get(balance > 0 ? "chaster_tag_unpaid" : balance < 0 ? "chaster_tag_credit" : "chaster_tag_clear");
        var stamp = new SolidColorBrush(Color.Parse(balance > 0 ? "#C8244A" : balance < 0 ? "#1E8A6E" : "#6E6686"));
        TagStamp.BorderBrush = TxtTagStamp.Foreground = stamp;
        TxtCalendarTag.Text = CircesTab.Format(balance);
        CalendarTag.IsVisible = balance > 0 && _tonightCell != null;
        PlaceCalendarTag();
    }

    private void PlaceCalendarTag()
    {
        if (_tonightCell is not { } cell || cell.Bounds.Width <= 0) return;
        if (cell.TransformToVisual(CalendarRow) is not { } transform) return;
        var bounds = new Rect(cell.Bounds.Size).TransformToAABB(transform);
        CalendarTag.Margin = new Thickness(Math.Max(0, bounds.X + bounds.Width / 2 - CalendarTag.Bounds.Width / 2),
            bounds.Bottom - 14, 0, 0);
    }

    private void TagInit()
    {
        // The sheet is rotated. Its final transform is available after arrange, not SizeChanged.
        CalendarRow.LayoutUpdated += (_, _) => PlaceCalendarTag();
    }
}
