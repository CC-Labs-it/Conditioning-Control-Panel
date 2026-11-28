using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmoteCustomBoxThemeTests
{
    [Fact]
    public void CustomBoxesRenderWpfWatermarksAndBigFocusBorder() => AvaloniaTestDispatcher.Run(() =>
    {
        var shell = new MainShellWindow();
        var view = new RemoteControlTabView();
        var host = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            shell.Show();
            shell.Named<Border>("RemoteControlOverlay")!.IsVisible = true;
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var big = shell.Named<TextBox>("TxtEmoteCustomBig")!;
            var small = view.FindControl<TextBox>("TxtEmoteCustom")!;
            foreach (var box in new[] { big, small })
            {
                var placeholder = box.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "PART_Placeholder");
                Assert.Equal(Loc.Get("watermark_emote_custom"), placeholder.Text);
                Assert.True(placeholder.IsVisible);
                Assert.Equal(FontStyle.Italic, placeholder.FontStyle);
                Assert.Equal(Color.Parse("#5A5A75"), ((ISolidColorBrush)placeholder.Foreground!).Color);
                box.Text = "typed";
                Dispatcher.UIThread.RunJobs();
                Assert.False(placeholder.IsVisible);
                box.Text = "";
            }
            var border = big.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "PART_BorderElement");
            Assert.Equal(Color.Parse("#3A3A55"), ((ISolidColorBrush)border.BorderBrush!).Color);
            big.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(big.IsFocused);
            Assert.True(big.TryFindResource("PinkBrush", out var pink));
            Assert.Equal(((ISolidColorBrush)pink!).Color, ((ISolidColorBrush)border.BorderBrush!).Color);
        }
        finally { host.Close(); shell.Close(); }
    });
}
