using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Media.Immutable;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HeadlessInputTests
{
    [Theory]
    [InlineData("MenuRed")]
    [InlineData("MenuGreen")]
    [InlineData("MenuPatreon")]
    [InlineData("MenuLocked")]
    public void SharedMenuBrushesHaveNoCreatingThreadAffinity(string field) =>
        Assert.IsType<ImmutableSolidColorBrush>(typeof(AvatarTubeWindow)
            .GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));

    [Fact]
    public Task PointerUsesTheNewLayoutAfterACompositorCommit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var button = new Button { Width = 80, Height = 40, Content = "Move", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
        var window = new Window { Width = 500, Height = 200, Content = button };
        try
        {
            window.Show();
            HeadlessInput.RenderLiveTree(window);
            button.Margin = new Thickness(300, 0, 0, 0);
            window.UpdateLayout();
            HeadlessInput.RenderLiveTree(window);
            int clicked = 0;
            button.Click += (_, _) => clicked++;
            var at = button.TranslatePoint(new Point(40, 20), window)!.Value;
            var hit = window.InputHitTest(at) as Visual;
            Assert.True(hit == button || (hit is not null && button.IsVisualAncestorOf(hit)));
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.Equal(1, clicked);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HitAncestryIsCapturedBeforePressedTemplateIsReplaced() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var before = new Border { Background = global::Avalonia.Media.Brushes.Red };
        var button = new Button { Width = 200, Height = 80,
            Template = new FuncControlTemplate<Button>((_, _) => before) };
        var window = new Window { Width = 200, Height = 80, Content = button };
        try
        {
            window.Show();
            HeadlessInput.RenderLiveTree(window);
            button.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                button.Template = new FuncControlTemplate<Button>((_, _) => new Border
                    { Background = global::Avalonia.Media.Brushes.Green });
                button.ApplyTemplate();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            var at = before.TranslatePoint(new Point(before.Bounds.Width / 2, before.Bounds.Height / 2), window)!.Value;
            var result = HeadlessInput.PressAt(button, window, at);
            HeadlessInput.RenderLiveTree(window);
            Assert.True(result.Hit);
            Assert.True(result.Pressed);
            Assert.Same(before, result.Target);
            Assert.False(button.IsVisualAncestorOf(result.Target!));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
