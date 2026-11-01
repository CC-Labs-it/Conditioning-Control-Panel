using System;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeMenuBrushThreadTests
{
    // WPF AvatarTubeWindow.ChatInput.cs:1447-1508: fixed menu ink, never animated or edited.
    [Theory]
    [InlineData("MenuRed", "#FFFF6347")]
    [InlineData("MenuGreen", "#FF90EE90")]
    [InlineData("MenuPatreon", "#FF9B59B6")]
    [InlineData("MenuLocked", "#FF606070")]
    public async Task MenuInkRendersWhenTheTubeWasFirstTouchedOnAWorker(string field, string hex)
    {
        // A fresh copy makes static initialization order deterministic even if another test
        // already opened the tube. Pure helpers such as DoubleBounceY can first touch it off UI.
        var copy = new AssemblyLoadContext("tube-menu-ink", isCollectible: true);
        try
        {
            var ink = await Task.Run(() =>
            {
                var type = copy.LoadFromAssemblyPath(typeof(AvatarTubeWindow).Assembly.Location)
                    .GetType(typeof(AvatarTubeWindow).FullName!)!;
                return (IBrush)type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            });
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                Assert.Equal(Color.Parse(hex), Assert.IsAssignableFrom<ISolidColorBrush>(ink).Color);
                var icon = new IconGlyph { Kind = FluentIcons.Common.Symbol.Play, Foreground = ink, Size = 24 };
                var host = new Window { Width = 64, Height = 64, Content = icon };
                try
                {
                    host.Show();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(icon.Bounds.Width > 0);
                }
                finally { host.Close(); }
                return Task.CompletedTask;
            });
        }
        finally { copy.Unload(); }
    }
}
