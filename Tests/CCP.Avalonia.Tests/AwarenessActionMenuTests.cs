using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessActionMenuTests
{
    [Fact]
    public void ExistingActionsHaveAlignedMutedNotes()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var anchor = new Button { Content = "Add" };
            var host = new Window { Content = anchor, Width = 500, Height = 400 };
            var dialog = new AwarenessPresetDetailDialog();
            ContextMenu? menu = null;
            try
            {
                host.Show();
                var trigger = new KeywordTrigger { Actions = new() { new PlayAudioAction(), new HapticAction() } };
                menu = dialog.ShowAddActionMenu(anchor, trigger, new Border());
                Dispatcher.UIThread.RunJobs();
                var rows = menu.Items.OfType<MenuItem>().ToArray();
                var first = Assert.IsType<Grid>(rows[0].Header);
                var other = Assert.IsType<Grid>(rows[2].Header);
                var enabled = Assert.IsType<Grid>(rows[1].Header);
                var firstNote = Assert.IsType<TextBlock>(first.Children[1]);
                var otherNote = Assert.IsType<TextBlock>(other.Children[1]);
                var emptyNote = Assert.IsType<TextBlock>(enabled.Children[1]);
                Assert.False(rows[0].IsEnabled);
                Assert.True(rows[1].IsEnabled);
                Assert.Equal("Play Audio", Assert.IsType<TextBlock>(first.Children[0]).Text);
                Assert.Equal("(already added)", firstNote.Text);
                Assert.True(firstNote.IsVisible);
                Assert.False(emptyNote.IsVisible);
                Assert.Equal(new Thickness(18, 0, 0, 0), firstNote.Margin);
                Assert.Equal(((ISolidColorBrush)host.FindResource("TextMutedBrush")!).Color,
                    ((ISolidColorBrush)firstNote.Foreground!).Color);
                Assert.Equal(firstNote.TranslatePoint(default, menu)!.Value.X,
                    otherNote.TranslatePoint(default, menu)!.Value.X, 2);
                Assert.Equal("(already added)", otherNote.Text);
            }
            finally
            {
                menu?.Close();
                dialog.Close();
                host.Close();
            }
        });
    }
}
