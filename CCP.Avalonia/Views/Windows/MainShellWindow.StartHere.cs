// WPF MainWindow/MainWindow.StartHere.cs:55-85 and Windows/StartHereCard.xaml.cs:13-106.
// First-run offer and Help replay.
using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal void OfferStartHereIfNeeded()
    {
        if (CoreSettings.Service == null || CoreSettings.Current.StartHereShown || !CoreSettings.Current.HasAcceptedAgeVerification) return;
        Dispatcher.UIThread.Post(() => Platform.StartupLadder.PresentOrInbox(new InboxItem
        {
            Key = "intro:starthere", Glyph = "🧭", Title = Loc.Get("starthere_title"), Summary = Loc.Get("starthere_intro"),
            Open = () =>
            {
                if (CoreSettings.Service != null && !CoreSettings.Current.StartHereShown)
                {
                    CoreSettings.Current.StartHereShown = true;
                    CoreSettings.Save();
                }
                ShowStartHereCard();
            },
        }));
    }

    private void BtnStartHereReplay_Click(object? sender, RoutedEventArgs e)
    {
        CloseTutorialOverlay();
        ShowStartHereCard();
    }

    private void ShowStartHereCard()
    {
        try
        {
            var card = new StartHereCard(StartHereGo);
            Window? owner = IsVisible ? this : LauncherWindow.Instance is { IsVisible: true } launcher ? launcher : null;
            if (owner != null) card.Show(owner); else card.Show();
        }
        catch (Exception ex) { Log.Warning(ex, "Start here card failed to open"); }
    }

    private void StartHereGo(string id)
    {
        if (id == "packs") { _ = Platform.ExternalOpener.OpenAsync(this, DiscordLinks.PackCatalogue); return; }
        if (id == "panic") OpenAppSettingsSection("devices");
        else ShowTab(id == "first" ? "studio" : "appsettings");
    }
}

public sealed partial class StartHereCard : Window, IPassiveStartupSurface
{
    public StartHereCard() : this(_ => { }) { }

    internal StartHereCard(Action<string> go)
    {
        Name = "StartHereCard";
        AvaloniaXamlLoader.Load(this);
        var body = this.FindControl<StackPanel>("Rows")!;
        this.FindControl<Button>("BtnX")!.Click += (_, _) => Close();
        this.FindControl<Button>("BtnDone")!.Click += (_, _) => Close();
        foreach (var (id, glyph) in new[] { ("packs", "📦"), ("first", "🌀"), ("settings", "⚙️"), ("panic", "🛑") })
        {
            var key = "starthere_row_" + id;
            var stored = CoreSettings.Current?.PanicKey;
            var panic = string.IsNullOrWhiteSpace(stored) || stored == "Escape" ? "Esc" : stored == "Scroll" ? "Scroll Lock" : stored;
            var text = id == "panic" ? Loc.GetF(key, panic!) : Loc.Get(key);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var icon = new IconGlyph { Emoji = glyph, Size = 16, Width = 22, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Tag = id, Margin = new Thickness(12, 0, 0, 0), Content = Loc.Get(id == "packs" ? "starthere_open" : "starthere_show"), Theme = this.FindResource("PinkButton") as ControlTheme, Padding = new Thickness(12, 5), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(button, button.Content + ": " + text);
            button.Click += (_, _) => { if (id != "packs") Close(); try { go(id); } catch (Exception ex) { Log.Warning(ex, "Start here action failed: {Id}", id); } };
            Grid.SetColumn(label, 1); Grid.SetColumn(button, 2);
            row.Children.Add(icon); row.Children.Add(label); row.Children.Add(button);
            body.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#1FFFFFFF")), BorderBrush = new SolidColorBrush(Color.Parse("#26FFFFFF")), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9), Child = row });
        }
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key is Key.Enter or Key.Escape) { Close(); if (e.Key == Key.Enter) e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
}
