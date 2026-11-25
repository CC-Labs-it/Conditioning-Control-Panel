using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Avalonia.Controls.Super;

/// <summary>
/// WPF Controls/SuperRows: how a gold box's option rows show (owner rule: what Basic buys stays
/// visible, locked). Lit: live. Free account: shown faded, no input. Unlocked but switched off:
/// folded away. One head-only addition (owner call 2026-11-25): an effect this head does not host
/// yet (see <see cref="SuperBox.HostedOnThisHead"/>, Undertow until a Brain Drain blur lands)
/// keeps its rows folded while its box says "coming soon", so nobody tunes an effect that cannot
/// draw here.
/// </summary>
public static class SuperRows
{
    public const double LockedOpacity = 0.45;

    public static void Apply(Control rows, SuperEffect effect)
    {
        bool lit = false, unlocked = true;
        bool hosted = SuperBox.HostedOnThisHead.Contains(effect);
        try
        {
            lit = hosted && SuperAccess.IsSelected(effect);
            unlocked = SuperAccess.IsUnlocked(effect);
        }
        catch { /* a row must never take the page down */ }
        rows.IsVisible = hosted && (lit || !unlocked);
        rows.Opacity = lit ? 1 : LockedOpacity;
        rows.IsHitTestVisible = lit;
        KeyboardNavigation.SetTabNavigation(rows, lit ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
    }
}

/// <summary>
/// The option rows inside one gold box, built in code from the Core <c>*Options</c> clamps (the
/// twin of the WPF XAML rows). Each row reads its value back from the settings on every refresh,
/// so a clamp shows at once; titles and tips re-read on a language change. The rows listen only
/// while attached, and <see cref="SuperRows.Apply"/> runs on every refresh.
/// </summary>
public sealed class SuperOptionRows : StackPanel
{
    public SuperEffect Effect { get; }
    private readonly List<Action> _refresh = new();
    private bool _loading;

    /// <summary>Runs after the rows re-read (a page dims a dependent row here).</summary>
    public event Action? Refreshed;

    public SuperOptionRows(SuperEffect effect, string name)
    {
        Effect = effect;
        Name = name;
        IsVisible = false;
        Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#33FFE08A")), Margin = new Thickness(0, 2, 0, 6) });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SuperAccess.Changed += OnAccessChanged;
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnReplaced;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SuperAccess.Changed -= OnAccessChanged;
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
        if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnReplaced;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAccessChanged(SuperEffect effect) { if (effect == Effect) Ui(Refresh); }
    private void OnLanguageChanged(object? sender, EventArgs e) => Ui(Refresh);
    private void OnReplaced() => Ui(Refresh);

    private static void Ui(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }

    /// <summary>Show state, every value and every string, read again.</summary>
    public void Refresh()
    {
        SuperRows.Apply(this, Effect);
        _loading = true;
        try { foreach (var r in _refresh) r(); }
        catch { /* see SuperRows.Apply */ }
        finally { _loading = false; }
        Refreshed?.Invoke();
    }

    /// <summary>A "More options" fold under the rows (WPF feat:MoreFold); rows go in with <c>into</c>.</summary>
    public ConditioningControlPanel.Avalonia.Views.Features.MoreFold Fold(Func<AppSettings, bool> openWhen)
    {
        var fold = new ConditioningControlPanel.Avalonia.Views.Features.MoreFold();
        Children.Add(fold);
        // It opens on its own when a row inside is off its default and never closes itself.
        _refresh.Add(() => { if (openWhen(CoreSettings.Current)) fold.IsOpen = true; });
        return fold;
    }

    /// <summary>A title over a group of switches (WPF's "Drags" heading over its chips).</summary>
    public TextBlock Heading(string key, Panel? into = null)
    {
        var head = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
            Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent,
        };
        _refresh.Add(() => { head.Text = Loc.Get(key); ToolTip.SetTip(head, Loc.Get("tooltip_" + key)); });
        (into ?? this).Children.Add(head);
        return head;
    }

    /// <summary>A slider row; <paramref name="set"/> gets the raw value and clamps it in Core.</summary>
    public Grid Slider(string key, double min, double max, double step, Func<AppSettings, double> get,
        Action<AppSettings, double> set, Func<AppSettings, string> text, bool tip = true, Panel? into = null, double large = 0)
    {
        var slider = new global::Avalonia.Controls.Slider
        {
            Minimum = min, Maximum = max, SmallChange = step, LargeChange = large > 0 ? large : step,
            TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(14, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        var value = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.HotPink, FontWeight = FontWeight.Bold,
        };
        var row = Row(key, tip, slider, value, "120,*,56", into);
        slider.ValueChanged += (_, e) =>
        {
            if (_loading) return;
            set(CoreSettings.Current, e.NewValue);
            CoreSettings.Save();
            Refresh();
        };
        _refresh.Add(() =>
        {
            var s = CoreSettings.Current;
            slider.Value = get(s);
            value.Text = text(s);
        });
        return row;
    }

    /// <summary>An on/off row in the page's ToggleStyle.</summary>
    public Grid Toggle(string key, Func<AppSettings, bool> get, Action<AppSettings, bool> set, bool tip = true, Panel? into = null)
    {
        var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (Application.Current?.TryFindResource("ToggleStyle", out var theme) == true && theme is ControlTheme ct) box.Theme = ct;
        var row = Row(key, tip, box, null, "*,Auto", into);
        box.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            set(CoreSettings.Current, box.IsChecked == true);
            CoreSettings.Save();
            Refresh();
        };
        _refresh.Add(() => box.IsChecked = get(CoreSettings.Current));
        return row;
    }

    /// <summary>A drop-down row; items are rebuilt on every refresh (names follow the language).</summary>
    public Grid Combo(string key, Func<IEnumerable<(string Label, string Tag)>> items, Func<AppSettings, string> get,
        Action<AppSettings, string> set, bool tip = true, Panel? into = null)
    {
        var combo = new ComboBox { Width = 180, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (Application.Current?.TryFindResource("DarkComboBoxStyle", out var theme) == true && theme is ControlTheme ct) combo.Theme = ct;
        var row = Row(key, tip, combo, null, "*,Auto", into);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            set(CoreSettings.Current, tag);
            CoreSettings.Save();
            Refresh();
        };
        _refresh.Add(() =>
        {
            string current = get(CoreSettings.Current);
            combo.Items.Clear();
            foreach (var (label, tag) in items())
            {
                var item = new ComboBoxItem { Content = label, Tag = tag };
                combo.Items.Add(item);
                if (tag == current || combo.SelectedItem == null) combo.SelectedItem = item;
            }
        });
        return row;
    }

    private Grid Row(string key, bool tip, Control input, Control? value, string columns, Panel? into)
    {
        var row = new Grid { MinHeight = 36, ColumnDefinitions = new ColumnDefinitions(columns), Tag = key, Background = Brushes.Transparent };
        var title = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(input, 1);
        row.Children.Add(title);
        row.Children.Add(input);
        if (value != null) { Grid.SetColumn(value, 2); row.Children.Add(value); }
        _refresh.Add(() =>
        {
            title.Text = Loc.Get(key);
            AutomationPropertiesName(input, title.Text);
            if (tip) ToolTip.SetTip(row, Loc.Get("tooltip_" + key));
        });
        (into ?? this).Children.Add(row);
        return row;
    }

    private static void AutomationPropertiesName(Control c, string name)
        => global::Avalonia.Automation.AutomationProperties.SetName(c, name);
}
