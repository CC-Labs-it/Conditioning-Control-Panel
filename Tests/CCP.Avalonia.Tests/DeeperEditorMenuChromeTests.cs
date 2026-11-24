using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperEditorMenuChromeTests
{
    [Fact]
    public Task TimelineMenuUsesDeeperSurface() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show();
            var canvas = editor.FindControl<Canvas>("TimelineCanvas")!;
            var menu = canvas.ContextMenu!;
            menu.Open(canvas);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            var border = menu.GetVisualDescendants().OfType<Border>().First();
            Assert.Equal(new CornerRadius(8), border.CornerRadius);
            Assert.Equal(new Thickness(2), border.BorderThickness);
            Assert.Equal(new Thickness(4), border.Padding);
            Assert.Equal(((ISolidColorBrush)editor.FindResource("DarkerBgBrush")!).Color, ((ISolidColorBrush)border.Background!).Color);
            Assert.Equal(((ISolidColorBrush)editor.FindResource("DeeperAccentBrush")!).Color, ((ISolidColorBrush)border.BorderBrush!).Color);
            menu.Close();
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });
}
