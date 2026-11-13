using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class InputMenuItemThemeTests
{
    [Theory]
    [InlineData("geometry")]
    [InlineData("disabled")]
    [InlineData("selected")]
    public Task DndPickerUsesContextRowChrome(string part) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldApps = PerformanceSettingsSection.RunningApps;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        Window? window = null;
        try
        {
            CoreSettings.Current.DndProcessList = new List<string> { "vlc" };
            PerformanceSettingsSection.RunningApps = () => new List<string> { "mpv", "vlc" };
            var section = new PerformanceSettingsSection();
            window = new Window { Content = section, Width = 700, Height = 1400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            section.FindControl<Button>("BtnDndPickApp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var rows = section.PickerMenu!.Items.Cast<MenuItem>().ToArray();
            var row = rows[0];
            if (part == "geometry")
            {
                Assert.Equal(new Thickness(10, 6), row.Padding);
                Assert.Equal(new Thickness(0, 1), row.Margin);
                Assert.Equal(new CornerRadius(6), row.CornerRadius);
                Assert.Equal("Hand", row.Cursor!.ToString());
            }
            if (part == "disabled")
            {
                Assert.True(rows[1].IsChecked);
                Assert.False(rows[1].IsEnabled);
                Assert.Equal(0.45, rows[1].Opacity);
                Assert.Equal("Arrow", rows[1].Cursor!.ToString());
            }
            if (part == "selected")
            {
                row.IsSelected = true;
                Dispatcher.UIThread.RunJobs();
                var border = row.GetVisualDescendants().OfType<Border>().First(x => x.Name == "PART_LayoutRoot");
                Assert.Equal(((ISolidColorBrush)window.FindResource("TransparentPink20Brush")!).Color,
                    ((ISolidColorBrush)border.Background!).Color);
            }
            section.PickerMenu.Close();
        }
        finally
        {
            window?.Close();
            PerformanceSettingsSection.RunningApps = oldApps;
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });
}
