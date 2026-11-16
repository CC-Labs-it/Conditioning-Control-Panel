using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Views.Features;

public partial class BubblePopFeatureControl
{
    private readonly StackPanel _frothRows = new() { IsVisible = false, Name = "FrothRows" };
    private readonly Slider _frothSize = new() { Name = "SliderFrothSize", Minimum = 2.5, Maximum = 6, SmallChange = 0.5, LargeChange = 0.5, TickFrequency = 0.5, IsSnapToTickEnabled = true };
    private readonly Slider _frothReach = new() { Name = "SliderFrothReach", Minimum = 0, Maximum = 200, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly Slider _frothPush = new() { Name = "SliderFrothPush", Minimum = 0, Maximum = 200, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly TextBlock _frothSizeText = new(), _frothReachText = new(), _frothPushText = new();
    private bool _loadingFroth;

    private void InitFrothRows()
    {
        _frothRows.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#33FFE08A")), Margin = new Thickness(0, 2, 0, 6) });
        AddFrothRow("super_froth_size", _frothSize, _frothSizeText);
        AddFrothRow("super_froth_reach", _frothReach, _frothReachText);
        AddFrothRow("super_froth_push", _frothPush, _frothPushText);
        SuperBoxMain.Body = _frothRows;
        _frothSize.ValueChanged += (_, e) => { if (_loadingFroth) return; CoreSettings.Current.FrothMaxSize = FrothOptions.ClampMaxSize(e.NewValue); CoreSettings.Save(); RefreshFrothRows(); };
        _frothReach.ValueChanged += (_, e) => { if (_loadingFroth) return; CoreSettings.Current.FrothPopReach = FrothOptions.ClampReach((int)Math.Round(e.NewValue)); CoreSettings.Save(); RefreshFrothRows(); };
        _frothPush.ValueChanged += (_, e) => { if (_loadingFroth) return; CoreSettings.Current.FrothCursorPush = FrothOptions.ClampPush((int)Math.Round(e.NewValue)); CoreSettings.Save(); RefreshFrothRows(); };
        RefreshFrothRows();
    }

    private void AddFrothRow(string key, Slider slider, TextBlock value)
    {
        var row = new Grid { Height = 36, ColumnDefinitions = new ColumnDefinitions("120,*,56"), Tag = key };
        ToolTip.SetTip(row, Loc.Get("tooltip_" + key));
        var title = new TextBlock { Text = Loc.Get(key), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        slider.Margin = new Thickness(14, 0); slider.VerticalAlignment = VerticalAlignment.Center;
        value.HorizontalAlignment = HorizontalAlignment.Right; value.VerticalAlignment = VerticalAlignment.Center; value.Foreground = Brushes.HotPink; value.FontWeight = FontWeight.Bold;
        Grid.SetColumn(slider, 1); Grid.SetColumn(value, 2);
        row.Children.Add(title); row.Children.Add(slider); row.Children.Add(value);
        _frothRows.Children.Add(row);
    }

    private void OnFrothAccessChanged(SuperEffect effect)
    {
        if (effect != SuperEffect.InnerBloom) return;
        if (Dispatcher.UIThread.CheckAccess()) RefreshFrothRows(); else Dispatcher.UIThread.Post(RefreshFrothRows);
    }

    private void OnFrothLanguageChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshFrothRows(); else Dispatcher.UIThread.Post(RefreshFrothRows);
    }

    private void RefreshFrothRows()
    {
        var s = CoreSettings.Current;
        _loadingFroth = true;
        try
        {
            bool lit = SuperAccess.IsSelected(SuperEffect.InnerBloom);
            bool unlocked = SuperAccess.IsUnlocked(SuperEffect.InnerBloom);
            _frothRows.IsVisible = lit || !unlocked;
            _frothRows.Opacity = lit ? 1 : 0.45;
            _frothRows.IsHitTestVisible = lit;
            KeyboardNavigation.SetTabNavigation(_frothRows, lit ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
            foreach (var child in _frothRows.Children)
                if (child is Grid row && row.Tag is string key && row.Children[0] is TextBlock title)
                {
                    title.Text = Loc.Get(key);
                    ToolTip.SetTip(row, Loc.Get("tooltip_" + key));
                }
            _frothSize.Value = s.FrothMaxSize; _frothSizeText.Text = s.FrothMaxSize.ToString("0.0") + "x";
            _frothReach.Value = s.FrothPopReach; _frothReachText.Text = s.FrothPopReach <= 0 ? Loc.Get("super_opt_off") : $"{s.FrothPopReach}%";
            _frothPush.Value = s.FrothCursorPush; _frothPushText.Text = s.FrothCursorPush <= 0 ? Loc.Get("super_opt_off") : $"{s.FrothCursorPush}%";
        }
        finally { _loadingFroth = false; }
    }
}
