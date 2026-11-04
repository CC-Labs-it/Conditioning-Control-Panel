using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class InputComboThemeTests
{
    [Fact]
    public Task ImplicitComboAndLocalTheme() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two" } };
        var localTheme = new ControlTheme(typeof(ComboBox))
        {
            BasedOn = (ControlTheme)Application.Current!.FindResource(typeof(ComboBox))!,
        };
        localTheme.Setters.Add(new Setter(ComboBox.PaddingProperty, new Thickness(19)));
        var local = new ComboBox { Theme = localTheme };
        var window = new Window { Content = new StackPanel { Children = { combo, local } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(28, combo.MinHeight);
            Assert.Equal(new Thickness(10, 0), combo.Padding);
            combo.SelectedIndex = 1;
            Assert.Equal("Two", combo.SelectedItem);
            combo.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.45, combo.Opacity);
            Assert.Same(localTheme, local.Theme);
            Assert.Equal(new Thickness(19), local.Padding);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });
}
