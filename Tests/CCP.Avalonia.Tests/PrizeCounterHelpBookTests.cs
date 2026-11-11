using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class PrizeCounterHelpBookTests
{
    [Fact]
    public Task HelpAndBookAreReachable() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var language = LocalizationManager.Instance.CurrentLanguage;
        LocalizationManager.Instance.SetLanguage("en");
        var shell = new MainShellWindow();
        shell.Show();
        var book = new EmiBookWindow();
        try
        {
            shell.FindControl<Border>("MainTutorialOverlay")!.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            var button = shell.FindControl<Button>("BtnHelpPrizeCounter")!;
            Assert.NotNull(button);
            Assert.Equal("PrizeCounter", HelpPopover.SectionOf(button));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(HelpPopover.IsOpen(button));
            Assert.Equal(IconKind.Sparkle, shell.FindControl<IconGlyph>("PrizeCounterHelpIcon")!.Kind);
            Assert.Contains(HelpPopover.PopupContent(button)!.GetVisualDescendants().OfType<IconGlyph>(),
                icon => icon.Kind == IconKind.Sparkle);
            var text = HelpPopover.PopupContent(button)!.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Inlines is { Count: > 0 } ? string.Concat(x.Inlines.OfType<Run>().Select(r => r.Text)) : x.Text).ToArray();
            foreach (var key in new[] { "title", "what", "tip1", "tip2", "tip3", "how" })
                Assert.Contains(Loc.Get("help_counter_" + key).Replace("*", ""), text);

            book.FindControl<Button>("Tab2")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var card = EmiBookCards.All.Single(c => c.Id == "prize-counter");
            var chip = book.FindControl<StackPanel>("Rail")!.Children.OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), card.Title));
            Assert.NotNull(Assert.IsType<Image>(chip.Content).Source);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(card.Title, book.FindControl<TextBlock>("CardTitle")!.Text);
            Assert.Equal(card.Id, book.Painter!.Id);
            Assert.NotNull(book.FindControl<Image>("Stage")!.Source);
        }
        finally { HelpPopover.CloseActive(); book.Close(); shell.Close(); LocalizationManager.Instance.SetLanguage(language); }
        return Task.CompletedTask;
    });
}
