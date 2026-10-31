using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Startup.Conversion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

/// <summary>WPF d545a21cb: one code field, one request per press.</summary>
public sealed class PostCodeDialog : Window
{
    private static PostCodeDialog? _open;
    private readonly TextBox _code = new() { MaxLength = 64 };
    private readonly Button _redeem = new() { Content = Loc.Get("conv_postcode_redeem"), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly PostCodeFlow _flow = new();
    private bool _closed;

    internal PostCodeDialog()
    {
        Title = "CC Labs";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = Loc.Get("conv_postcode_dialog_title"), FontSize = 17 });
        stack.Children.Add(new TextBlock { Text = Loc.Get("conv_postcode_hint"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(_code);
        stack.Children.Add(_redeem);
        stack.Children.Add(_status);
        var close = new Button { Content = Loc.Get("vaultgate_close"), HorizontalAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();
        stack.Children.Add(close);
        Content = stack;
        _redeem.Click += (_, _) => Redeem();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) { e.Handled = true; Redeem(); }
        };
        Opened += (_, _) => _code.Focus();
        Closed += (_, _) => { _closed = true; if (ReferenceEquals(_open, this)) _open = null; };
    }

    public static void Present(Window owner)
    {
        _open?.Close();
        _open = new PostCodeDialog();
        if (owner.IsVisible) _open.Show(owner); else _open.Show();
    }

    private async void Redeem()
    {
        if (!_redeem.IsEnabled || !_code.IsEnabled) return;
        _redeem.IsEnabled = false;
        try
        {
            var result = await _flow.RedeemAsync(CoreSettings.Current, _code.Text,
                code => PostCodeApi.RedeemAsync(code), () => CoreSettings.Save(), () => App.PostCodeInputsChanged?.Invoke());
            if (_closed || result == null) return;
            if (result is { Ok: true, Feature: string feature, UntilUtc: DateTime until })
            {
                var info = ExclusiveFeature.All.FirstOrDefault(f => f.DailyFreeKey == feature);
                Say(Loc.GetF("conv_postcode_ok", info == null ? feature : Loc.Get(info.TitleLocKey), until.ToLocalTime().ToString("ddd d MMM, HH:mm")));
                _redeem.IsVisible = false;
                _code.IsEnabled = false;
            }
            else Say(Loc.Get(result.Reason switch
            {
                "bad_code" => "conv_postcode_empty", "unknown_code" => "conv_postcode_unknown",
                "already_redeemed" => "conv_postcode_used", "expired" => "conv_postcode_expired", _ => "conv_postcode_offline",
            }));
        }
        catch (Exception ex) { Log.Warning(ex, "Post code redeem failed"); if (!_closed) Say(Loc.Get("conv_postcode_offline")); }
        finally { _redeem.IsEnabled = true; }
    }

    private void Say(string text) { _status.Text = text; _status.IsVisible = true; }
}
