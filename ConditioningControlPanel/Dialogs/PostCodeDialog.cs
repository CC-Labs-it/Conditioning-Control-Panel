using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup.Conversion;

namespace ConditioningControlPanel;

/// <summary>
/// The redeem box for this week's Patreon post code (brief C6, proposal 6c): one text field, one
/// request per press. A code that lands is stored in <c>AppSettings.PostGrants</c> at once, so the
/// door opens before the next profile read confirms it. Code-only, one at a time.
/// </summary>
public sealed class PostCodeDialog : Window
{
    private static readonly Brush CardBg = Frozen("#FF151528");
    private static readonly Brush FieldBg = Frozen("#252542");
    private static readonly Brush Text = Frozen("#E0E0E0");
    private static readonly Brush Muted = Frozen("#B8B1CC");
    private static readonly Brush Good = Frozen("#7FD8A6");
    private static readonly Brush Warn = Frozen("#FFB347");

    private static PostCodeDialog? _open;

    private readonly TextBox _code = new();
    private readonly Button _redeem = new();
    private readonly TextBlock _status = new();
    private bool _busy;

    private PostCodeDialog()
    {
        Title = "CC Labs";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) { e.Handled = true; Redeem(); }
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed || e.OriginalSource is TextBox || OnClickable(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch (InvalidOperationException) { } // swallow: the button was already up
        };
        Build();
        Loaded += (_, _) => _code.Focus();
    }

    public static void Present(Window? owner)
    {
        var dialog = new PostCodeDialog();
        try { if (owner is { IsVisible: true }) dialog.Owner = owner; }
        catch (InvalidOperationException) { } // swallow: owner closing; the box stands alone
        try { _open?.Close(); }
        catch (InvalidOperationException) { } // swallow: the previous box was already closing
        _open = dialog;
        dialog.Closed += (_, _) => { if (ReferenceEquals(_open, dialog)) _open = null; };
        dialog.Show();
    }

    private void Build()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Loc.Get("conv_postcode_dialog_title"), Foreground = Brushes.White, FontSize = 17,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = Loc.Get("conv_postcode_hint"), Foreground = Text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        _code.Margin = new Thickness(0, 14, 0, 0);
        _code.Padding = new Thickness(8, 6, 8, 6);
        _code.FontSize = 15;
        _code.MaxLength = 64;
        _code.Background = FieldBg;
        _code.Foreground = Brushes.White;
        _code.CaretBrush = Brushes.White;
        _code.BorderThickness = new Thickness(1);
        _code.SetResourceReference(Control.BorderBrushProperty, "PinkBrush");
        stack.Children.Add(_code);

        _redeem.Content = Loc.Get("conv_postcode_redeem");
        _redeem.Cursor = Cursors.Hand;
        _redeem.FontSize = 14;
        _redeem.Padding = new Thickness(14, 9, 14, 9);
        _redeem.Margin = new Thickness(0, 12, 0, 0);
        _redeem.HorizontalAlignment = HorizontalAlignment.Stretch;
        _redeem.SetResourceReference(StyleProperty, "SmallPinkButton");
        _redeem.Click += (_, _) => Redeem();
        stack.Children.Add(_redeem);

        _status.FontSize = 12.5;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        _status.Visibility = Visibility.Collapsed;
        stack.Children.Add(_status);

        var close = new TextBlock
        {
            Text = Loc.Get("vaultgate_close"), Foreground = Muted, FontSize = 12, Cursor = Cursors.Hand,
            TextDecorations = TextDecorations.Underline, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };
        close.MouseLeftButtonUp += (_, _) => Close();
        stack.Children.Add(close);

        var card = new Border
        {
            Background = CardBg, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5),
            Margin = new Thickness(12), Padding = new Thickness(22, 18, 22, 16), Child = stack,
        };
        card.SetResourceReference(Border.BorderBrushProperty, "PinkBrush");
        Content = card;
    }

    /// <summary>One request per press; a press while one is out does nothing.</summary>
    private async void Redeem()
    {
        if (_busy) return;
        if (PostCodeApi.NormalizeCode(_code.Text) == null) { Say(Loc.Get("conv_postcode_empty"), Warn); return; }
        _busy = true;
        _redeem.IsEnabled = false;
        try
        {
            var result = await PostCodeApi.RedeemAsync(_code.Text);
            if (!IsLoaded) return;
            if (result.Ok && result.Feature is string feature && result.UntilUtc is DateTime until)
            {
                var settings = App.Settings?.Current;
                if (settings != null)
                {
                    // Copy and swap: engine threads read this map through DailyFreeService.IsFreeToday.
                    settings.PostGrants = new System.Collections.Generic.Dictionary<string, string>(settings.PostGrants, StringComparer.OrdinalIgnoreCase)
                    {
                        [feature] = until.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    };
                    App.Settings?.Save();
                }
                App.DailyFree?.NotifyInputsChanged();
                var name = Models.ExclusiveFeature.All.FirstOrDefault(f => f.DailyFreeKey == feature) is { } f
                    ? Loc.Get(f.TitleLocKey) : feature;
                Say(Loc.GetF("conv_postcode_ok", name, until.ToLocalTime().ToString("ddd d MMM, HH:mm")), Good);
                _redeem.Visibility = Visibility.Collapsed;
                _code.IsEnabled = false;
                return;
            }
            Say(Loc.Get(result.Reason switch
            {
                "unknown_code" or "bad_code" => "conv_postcode_unknown",
                "already_redeemed" => "conv_postcode_used",
                "expired" => "conv_postcode_expired",
                _ => "conv_postcode_offline",
            }), Warn);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "[PostCode] redeem box failed");
            Say(Loc.Get("conv_postcode_offline"), Warn);
        }
        finally
        {
            _busy = false;
            _redeem.IsEnabled = true;
        }
    }

    private void Say(string text, Brush brush)
    {
        _status.Text = text;
        _status.Foreground = brush;
        _status.Visibility = Visibility.Visible;
    }

    private static bool OnClickable(DependencyObject? d)
    {
        for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement fe && fe.Cursor == Cursors.Hand) return true;
            if (d is System.Windows.Controls.Primitives.ButtonBase || d is TextBox) return true;
            if (d is Window) return false;
        }
        return false;
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
