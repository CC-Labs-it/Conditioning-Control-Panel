using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleCountOrderingTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public Task ClickRestoresEveryLiveBubbleAndIgnoresClosedGame(bool handled, bool closeBeforeDispatch, bool activate) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var saved = CountBubble.Restack;
            var calls = new List<Window>();
            // Ordering does not need text layout. Keep the real window/input lifecycle
            // while avoiding the unavailable Inter font in Linux headless runs.
            var game = new BubbleCountWindow { Content = new Grid() };
            try
            {
                game.Show();
                Dispatcher.UIThread.RunJobs();
                CountBubble.Restack = (bubble, owner) => { Assert.Same(game, owner); calls.Add(bubble); };
                var bubbles = (List<CountBubble>)typeof(BubbleCountWindow)
                    .GetField("_activeBubbles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
                bubbles.Add(new CountBubble(null, 120, 0, 0, new Random(1), null, game));
                bubbles.Add(new CountBubble(null, 120, 0, 0, new Random(2), null, game));
                calls.Clear();
                if (activate)
                    typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(game, null);
                else game.RaiseEvent(new PointerPressedEventArgs(game, new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true),
                    game, new Point(10, 10), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton,
                        PointerUpdateKind.LeftButtonPressed), KeyModifiers.None) { Handled = handled });
                if (closeBeforeDispatch) game.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(closeBeforeDispatch ? 0 : 2, calls.Count);
                if (!closeBeforeDispatch) Assert.NotSame(calls[0], calls[1]);
            }
            finally
            {
                game.Close();
                CountBubble.Restack = saved;
                BubbleCountWindow.ForceCloseAll();
            }
            return Task.CompletedTask;
        });
}
