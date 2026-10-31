using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeMenuThreadingTests
{
    [Fact]
    public async Task MenuColoursRenderAfterWorkerTypeInitialization()
    {
        // The isolation hook has established the UI dispatcher. Pure tube maths may then
        // initialize this partial type on a worker before the first tube window exists.
        await Task.Run(() => RuntimeHelpers.RunClassConstructor(typeof(AvatarTubeWindow).TypeHandle));
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var panel = new StackPanel();
            foreach (var name in new[] { "MenuRed", "MenuGreen", "MenuPatreon", "MenuLocked" })
            {
                var brush = (IBrush)typeof(AvatarTubeWindow).GetField(name,
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
                panel.Children.Add(new global::FluentIcons.Avalonia.SymbolIcon
                { Symbol = global::FluentIcons.Common.Symbol.Heart, Foreground = brush, FontSize = 24 });
            }
            var window = new Window { Width = 200, Height = 150, Content = panel };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame); // includes compositor AddRefOnCompositor for every menu colour
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }
}
