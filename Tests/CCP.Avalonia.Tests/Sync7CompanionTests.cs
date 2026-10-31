using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class Sync7CompanionTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
    }

    [Fact]
    public Task SpiceToggleShowsWhereToUseIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        CoreSettings.Current.SlutModeEnabled = false;
        ExplicitContentGate.MarkAcknowledged(CoreSettings.Current.CompanionPrompt);
        var shell = new MainShellWindow();
        var tube = new AvatarTubeWindow(shell);
        typeof(MainShellWindow).GetField("_avatarTubeWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(shell, tube);
        try
        {
            tube.Show();
            Assert.True(await shell.SetSlutModeAsync(true));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("companion_spice_on_line"), tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            // Clear the first bubble so the low-priority preset line may show again.
            tube.PanicSilence();
            tube.SpeechClock = new LaterClock();
            Assert.False(await shell.SetSlutModeAsync(false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("companion_spice_off_line"), tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        }
        finally { tube.Close(); shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private sealed class LaterClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddMinutes(1);
    }

    [Fact]
    public Task SendClosesInputAndChatKeyCanReopenIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        CoreSettings.Current.AiChatEnabled = false;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.OpenChatInput();
            Dispatcher.UIThread.RunJobs();
            var panel = tube.FindControl<Border>("InputPanel")!;
            var input = tube.FindControl<TextBox>("TxtUserInput")!;
            input.Text = "   ";
            await tube.SendChatAsync();
            Assert.True(panel.IsVisible);
            input.Text = "hello";
            await tube.SendChatAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(panel.IsVisible);
            Assert.Equal(string.Empty, input.Text);
            tube.OpenChatInput();
            Dispatcher.UIThread.RunJobs();
            Assert.True(panel.IsVisible);
        }
        finally { tube.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
