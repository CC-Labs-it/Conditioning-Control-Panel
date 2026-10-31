using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Startup;
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
public class PostCodeDialog : Window
{
    private static PostCodeDialog? _open;
    internal static PostCodeDialog? Open => _open;
    internal static PostCodeDialog? Notice { get; private set; }
    internal Func<string?, Task<PostCodeRedeem>> Send = code => PostCodeApi.RedeemAsync(code);
    private static readonly IBrush Pink = Brush.Parse("#FF69B4"), Good = Brush.Parse("#7FD8A6"), Warn = Brush.Parse("#FFB347");
    private readonly TextBox _code = new() { MaxLength = 64 };
    private readonly Button _redeem = new() { Content = Loc.Get("conv_postcode_redeem"), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 10, 0, 0), IsVisible = false };
    private readonly PostCodeFlow _flow = new();
    private bool _closed;
    internal PostCodeDialog(bool notice = false, string? body = null, Window? owner = null)
    {
        Title = "CC Labs";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && CanDrag(e.Source)) { try { BeginMoveDrag(e); } catch (InvalidOperationException) { } } };
        var stack = new StackPanel();
        _code.IsVisible = !notice; _code.Margin = new Thickness(0, 14, 0, 0);
        _code.Background = Brush.Parse("#252542"); _code.Foreground = _code.CaretBrush = Brushes.White;
        _code.BorderBrush = Pink; _code.BorderThickness = new Thickness(1); _code.Padding = new Thickness(8, 6); _code.FontSize = 15;
        _redeem.Content = Loc.Get(notice ? "conv_postcode_enter" : "conv_postcode_redeem"); _redeem.Margin = new Thickness(0, notice ? 16 : 12, 0, 0);
        _redeem.FontSize = 14; _redeem.Background = Pink; _redeem.Foreground = Brushes.White; _redeem.Padding = new Thickness(14, 9); _redeem.Cursor = new Cursor(StandardCursorType.Hand);
        if (Application.Current?.TryFindResource("SmallPinkButton", out var theme) == true && theme is ControlTheme t) _redeem.Theme = t;
        stack.Children.Add(new TextBlock { Text = Loc.Get(notice ? "conv_postcode_title" : "conv_postcode_dialog_title"), Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = 17, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = notice ? body : Loc.Get("conv_postcode_hint"), FontSize = 13, Margin = new Thickness(0, notice ? 8 : 6, 0, 0), Foreground = Brush.Parse("#E0E0E0"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(_code);
        stack.Children.Add(_redeem);
        stack.Children.Add(_status);
        var close = new TextBlock { Name = "CloseLink", Text = Loc.Get("vaultgate_close"), Foreground = Brush.Parse("#B8B1CC"), FontSize = 12, Cursor = new Cursor(StandardCursorType.Hand), TextDecorations = TextDecorations.Underline, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        close.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Close(); };
        stack.Children.Add(close);
        Content = new Border { Background = Brush.Parse("#151528"), CornerRadius = new CornerRadius(14), BorderBrush = Pink,
            BorderThickness = new Thickness(1.5), Margin = new Thickness(12), Padding = new Thickness(22, 18, 22, 16), Child = stack };
        _redeem.Click += (_, _) => { if (notice) { Close(); Present(owner!); } else _ = Redeem(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) { e.Handled = true; _redeem.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }
        };
        Opened += (_, _) => { if (notice) _redeem.Focus(); else _code.Focus(); };
        Closed += (_, _) => { _closed = true; if (ReferenceEquals(_open, this)) _open = null; if (ReferenceEquals(Notice, this)) Notice = null; };
    }
    internal static bool CanDrag(object? source) => source is Control c && !c.GetSelfAndVisualAncestors().Any(x => x is Button or TextBox or TextBlock { Name: "CloseLink" }) && !c.GetSelfAndLogicalAncestors().Any(x => x is Button or TextBox or TextBlock { Name: "CloseLink" });
    internal static void Offer(Window owner, string body) => StartupLadder.PresentOrInbox(new InboxItem
    {
        Key = "conv:" + PostCodeFlow.RowKey, Glyph = "🔖", Title = Loc.Get("conv_postcode_title"), Summary = body,
        Open = () => { Notice?.Close(); Notice = new NoticeCard(body, owner); if (owner.IsVisible) Notice.Show(owner); else Notice.Show(); },
    });
    private sealed class NoticeCard(string body, Window owner) : PostCodeDialog(true, body, owner), IPassiveStartupSurface { }
    public static void Present(Window owner)
    {
        _open?.Close();
        _open = new PostCodeDialog();
        if (owner.IsVisible) _open.Show(owner); else _open.Show();
    }
    internal async Task Redeem()
    {
        if (!_redeem.IsEnabled || !_code.IsEnabled) return;
        _redeem.IsEnabled = false;
        try
        {
            var result = await _flow.RedeemAsync(CoreSettings.Current, _code.Text,
                Send, () => CoreSettings.Save(), () => App.PostCodeInputsChanged?.Invoke(), () => !_closed);
            if (_closed || result == null) return;
            if (result is { Ok: true, Feature: string feature, UntilUtc: DateTime until })
            {
                var info = ExclusiveFeature.All.FirstOrDefault(f => f.DailyFreeKey == feature);
                Say(Loc.GetF("conv_postcode_ok", info == null ? feature : Loc.Get(info.TitleLocKey), until.ToLocalTime().ToString("ddd d MMM, HH:mm")), true);
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
    private void Say(string text, bool good = false) { _status.Foreground = good ? Good : Warn; _status.Text = text; _status.IsVisible = true; }
}
