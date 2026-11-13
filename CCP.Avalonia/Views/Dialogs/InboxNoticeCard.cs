using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

/// <summary>WPF InboxNoticeCard: one nonmodal passive card, with an optional primary action.</summary>
public sealed class InboxNoticeCard : Window, IPassiveStartupSurface
{
    private static InboxNoticeCard? _open;
    /// <summary>The one card on screen, for the shell's tests and the startup presenter.</summary>
    internal static InboxNoticeCard? Open => _open;

    /// <summary>Panic (PanicSurfaces "inbox-notice"): close the card without running either action.</summary>
    internal static void CloseForPanic()
    {
        try { _open?.Close(); } catch (InvalidOperationException) { }
    }

    private InboxNoticeCard()
    {
        Title = "CC Labs";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !OnClickable(e.Source as Visual))
                try { BeginMoveDrag(e); } catch (InvalidOperationException) { }
        };
    }

    public static InboxNoticeCard Present(Window? owner, string heading, string body, string? primary, Action? onPrimary,
        string? secondary = null, Action? onSecondary = null)
    {
        var card = new InboxNoticeCard();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = heading, Foreground = Brushes.White, FontSize = 17,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(body)) stack.Children.Add(new TextBlock { Text = body,
            Foreground = Brush.Parse("#E0E0E0"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        if (!string.IsNullOrEmpty(primary))
        {
            var button = new Button { Name = "NoticePrimary", Content = new TextBlock { Text = primary }, Cursor = CursorHand, FontSize = 14,
                Padding = new Thickness(14, 9), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            if (Application.Current?.TryFindResource("SmallPinkButton", out var theme) == true && theme is ControlTheme t) button.Theme = t;
            button.Click += (_, _) => { card.Close(); Run(onPrimary); };
            stack.Children.Add(button);
        }
        // WPF: the quiet choice is a plain-text button (named for the close / Escape tests).
        var quiet = new Button
        {
            Name = "NoticeClose", Theme = new ControlTheme(typeof(Button))
            {
                Setters = { new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Button>((b, _) =>
                    new ContentPresenter { Name = "PART_ContentPresenter", Background = Brushes.Transparent,
                        [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty] })) },
            }, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(0), Foreground = Brush.Parse("#B8B1CC"), FontSize = 12,
            Cursor = CursorHand, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Content = new TextBlock { Text = string.IsNullOrEmpty(secondary) ? Loc.Get("vaultgate_close") : secondary,
                TextDecorations = TextDecorations.Underline },
        };
        quiet.Click += (_, _) => { card.Close(); Run(onSecondary); };
        stack.Children.Add(quiet);
        card.Content = new Border { Background = Brush.Parse("#FF151528"), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1.5), [!Border.BorderBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("PinkBrush"), Margin = new Thickness(12),
            Padding = new Thickness(22, 18, 22, 16), Child = stack };
        try { _open?.Close(); } catch (InvalidOperationException) { }
        _open = card;
        card.Closed += (_, _) => { if (ReferenceEquals(_open, card)) _open = null; };
        try { if (owner is { IsVisible: true }) card.Show(owner); else card.Show(); }
        catch (InvalidOperationException) { card.Show(); }
        return card;
    }

    private static void Run(Action? action)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "[Inbox] notice card action failed"); }
    }

    private static bool OnClickable(Visual? visual)
    {
        for (; visual != null; visual = visual.GetVisualParent())
            if (visual is Button || visual is InputElement { Cursor: { } cursor } && cursor == CursorHand) return true;
        return false;
    }
    private static readonly Cursor CursorHand = new(StandardCursorType.Hand);
}
