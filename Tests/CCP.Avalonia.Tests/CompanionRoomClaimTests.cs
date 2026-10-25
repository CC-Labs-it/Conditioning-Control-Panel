using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF CompanionRoomView.Claim / CompanionRoomRuntimeVm.Navigator: taking the room off the
/// tree lets go of the navigator and detaches Z2 from the brain's turn log; putting it back
/// re-claims, and the show re-reads every zone (Engine included, WPF CompanionRoomRuntimeVm.Sync).</summary>
public sealed class CompanionRoomClaimTests
{
    [Fact]
    public void DetachLetsGoOfTheNavigatorAndTheTurnLog_ReattachReclaimsAndResyncs() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var (previousAi, previousBrain, previousUser) = (AvApp.Ai, AvApp.Brain, CoreAccount.UnifiedUserId);
        var dir = Directory.CreateTempSubdirectory("ccp-room-claim-").FullName;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        CoreSettings.Current.AiChatEnabled = true;
        CoreSettings.Current.UseCompanionBrain = true;
        CoreSettings.Current.CompanionPrompt.AiProvider = AiProviderType.Cloud;
        CoreAccount.UnifiedUserId = "u-room-claim";
        AvApp.Ai = new AiService("http://127.0.0.1:9");
        AvApp.Brain = new CompanionBrain(AvApp.Ai, memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        var room = new CompanionRoomView();
        var win = new Window { Content = room, Width = 1240, Height = 900 };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();
            var chat = room.FindControl<ChatThresholdView>("ChatZone")!.ViewModel!;
            var hero = room.FindControl<CompanionHeroCard>("HeroZone")!.ViewModel!;
            var engine = (EngineRoomVm)room.FindControl<EngineRoomDrawer>("EngineZone")!.DataContext!;
            Assert.True(chat.IsAttached);
            Assert.Same(room, hero.Host);

            win.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(chat.IsAttached);
            Assert.Null(hero.Host);

            CoreSettings.Current.CompanionPrompt.AiOllamaHost = "http://claim-test:11434";
            win.Content = room;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(room, hero.Host);
            Assert.True(chat.IsAttached);
            Assert.Equal("http://claim-test:11434", engine.OllamaHost);
        }
        finally
        {
            win.Close();
            Dispatcher.UIThread.RunJobs();
            (AvApp.Ai, AvApp.Brain) = (previousAi, previousBrain);
            CoreAccount.UnifiedUserId = previousUser;
            CoreSettings.ServiceProvider = oldSettings;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    });
}
