using System;
using System.Linq;
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

/// <summary>WPF Dialogs/InboxNoticeCard.cs: one non-modal conversion card at a time.</summary>
public sealed class InboxNoticeCard : Window, IPassiveStartupSurface
{
    internal static InboxNoticeCard? Open { get; private set; }

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
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source
                || source is Button || source.GetVisualAncestors().OfType<Button>().Any()) return;
            try { BeginMoveDrag(e); } catch (InvalidOperationException) { }
        };
    }

    public static void Present(Window? owner, string heading, string body, string? primary, Action? onPrimary,
        string? secondary = null, Action? onSecondary = null)
    {
        var card = new InboxNoticeCard();
        card.Build(heading, body, primary, onPrimary, secondary, onSecondary);
        Open?.Close();
        Open = card;
        card.Closed += (_, _) => { if (ReferenceEquals(Open, card)) Open = null; };
        if (owner is { IsVisible: true }) card.Show(owner);
        else card.Show();
    }

    private void Build(string heading, string body, string? primary, Action? onPrimary, string? secondary, Action? onSecondary)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = heading, Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(body)) stack.Children.Add(new TextBlock
        {
            Text = body, Foreground = Brush.Parse("#E0E0E0"), FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        if (!string.IsNullOrEmpty(primary))
        {
            var button = new Button
            {
                Content = new TextBlock { Text = primary }, Cursor = new Cursor(StandardCursorType.Hand), FontSize = 14,
                Padding = new Thickness(14, 9), Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            if (Application.Current?.TryFindResource("SmallPinkButton", out var theme) == true && theme is ControlTheme t) button.Theme = t;
            button.Click += (_, _) => { Close(); Run(onPrimary); };
            stack.Children.Add(button);
        }
        var quiet = new Button
        {
            Content = new TextBlock
            {
                Text = string.IsNullOrEmpty(secondary) ? Loc.Get("vaultgate_close") : secondary,
                Foreground = Brush.Parse("#B8B1CC"), FontSize = 12, TextDecorations = TextDecorations.Underline,
            },
            Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Theme = new ControlTheme(typeof(Button))
            {
                Setters = { new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Button>((b, _) =>
                    new ContentPresenter { [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty] })) },
            },
        };
        quiet.Click += (_, _) => { Close(); Run(onSecondary); };
        stack.Children.Add(quiet);
        Content = new Border
        {
            Background = Brush.Parse("#FF151528"), BorderBrush = Brush.Parse("#FF69B4"),
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5),
            Margin = new Thickness(12), Padding = new Thickness(22, 18, 22, 16), Child = stack,
        };
    }

    private static void Run(Action? action)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "[Inbox] notice card action failed"); }
    }
}
