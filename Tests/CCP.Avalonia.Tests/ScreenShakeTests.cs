using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ScreenShakeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void VoiceGrammarOffersShake() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        try { Assert.Contains("shake the screen", shell.VoiceCmds.Grammar()); }
        finally { shell.Close(); }
    });

    [Fact]
    public void RepeatedShakeClampsAndRestoresTransforms() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var prior = new ScaleTransform(0.8, 0.8);
        var root = new Border { RenderTransform = prior };
        var window = new Window { Content = root };
        var service = new ScreenShakeService();
        window.Show();
        try
        {
            service.Start(new[] { window }, 60, 1200);
            var first = Assert.IsType<TranslateTransform>(root.RenderTransform);
            for (var i = 0; i < 100; i++)
            {
                service.Tick();
                Assert.InRange(first.X, -28, 28);
                Assert.InRange(first.Y, -28, 28);
            }
            service.Start(new[] { window }, 60, 1200);
            Assert.NotSame(first, root.RenderTransform);
            service.Reset();
            Assert.Same(prior, root.RenderTransform);
        }
        finally { service.Reset(); window.Close(); }
    });
    [Fact]
    public Task RecognizedWakeShakeRestartsAndExpires() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var prior = new ScaleTransform(0.8, 0.8);
        var root = new Border { RenderTransform = prior };
        var window = new Window { Content = root };
        var service = ScreenShakeService.Instance;
        // The host mapping does not need the rest of the shell's visual tree or a microphone.
        var shell = (MainShellWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainShellWindow));
        service.WindowsForTests = () => new[] { window };
        window.Show();
        try
        {
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi shake the screen", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs();
            var first = Assert.IsType<TranslateTransform>(root.RenderTransform);
            await Task.Delay(700);
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi shake it", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<TranslateTransform>(root.RenderTransform);
            Assert.NotSame(first, root.RenderTransform);
            await Task.Delay(700); // past the first deadline, before the second
            Assert.IsType<TranslateTransform>(root.RenderTransform);
            await Task.Delay(800);
            Assert.Same(prior, root.RenderTransform); // dispatcher timer restored it
        }
        finally { service.Reset(); service.WindowsForTests = null; window.Close(); }
    });

    [Fact]
    public void VisibleWebHostsReceiveTheRestartableShakeScript() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var scripts = new List<string>();
        var visible = new WebHost { ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>(null); } };
        var hidden = new WebHost { IsVisible = false, ScriptInvoker = js => { scripts.Add("hidden"); return Task.FromResult<string?>(null); } };
        var panel = new StackPanel();
        panel.Children.Add(visible); panel.Children.Add(hidden);
        var window = new Window { Content = panel };
        var service = new ScreenShakeService();
        window.Show();
        try
        {
            service.Start(new[] { window }, 60, 1200);
            service.Start(new[] { window }, 0.5, 400);
            Assert.Equal(2, scripts.Count);
            Assert.Contains("document.querySelector('video')", scripts[0]);
            Assert.Contains("old.restore()", scripts[0]);
            Assert.Contains("state.restore()", scripts[0]);
            Assert.EndsWith(")(28,1200);", scripts[0]);
            Assert.EndsWith(")(14,400);", scripts[1]);
        }
        finally { service.Reset(); window.Close(); }
    });

}
