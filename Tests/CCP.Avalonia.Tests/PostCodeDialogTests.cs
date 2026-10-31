using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PostCodeDialogTests
{
    [Fact]
    public Task EmptyPressKeepsTheBoxEditableAndShowsTheReason() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var box = new PostCodeDialog();
        try
        {
            box.Show();
            var children = box.GetLogicalDescendants().ToArray();
            var field = Assert.Single(children.OfType<TextBox>());
            Assert.Equal(64, field.MaxLength);
            var redeem = children.OfType<Button>().Single(b => Equals(b.Content, Loc.Get("conv_postcode_redeem")));
            redeem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(field.IsEnabled);
            Assert.True(redeem.IsEnabled);
            Assert.Contains(children.OfType<TextBlock>(), t => t.IsVisible && t.Text == Loc.Get("conv_postcode_empty"));
        }
        finally { box.Close(); }
        return Task.CompletedTask;
    });
}
