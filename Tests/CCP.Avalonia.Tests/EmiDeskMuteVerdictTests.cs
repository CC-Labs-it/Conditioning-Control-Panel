using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class EmiDeskMuteVerdictTests
{
    private static Task WithDesk(System.Func<EmiDeskService, Task> run) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var state = new EmiTestScope();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var desk = EmiDeskService.Instance;
        var oldAsk = desk.AskMute;
        var oldTakeover = desk.TakeoverTalking;
        var oldRemote = desk.RemoteTalking;
        try
        {
            var s = CoreSettings.Current;
            s.EmiDeskEnabled = true; s.EmiDeskMuteAvatar = true; s.EmiDeskMuteDontAsk = false;
            s.SpeechWakeWordEnabled = false; s.AiChatEnabled = false; s.AwarenessModeEnabled = false;
            desk.ResetMutePrompt();
            desk.TakeoverTalking = () => false; desk.RemoteTalking = () => false;
            await run(desk);
        }
        finally
        {
            desk.PanicSilence(); desk.Window?.ShutDown(); Dispatcher.UIThread.RunJobs();
            desk.SummonMoment = null; desk.AskMute = oldAsk;
            desk.TakeoverTalking = oldTakeover; desk.RemoteTalking = oldRemote;
            desk.ResetMutePrompt(); CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Theory]
    [InlineData(true, EmiMuteChoice.Mute, "avatarMuted")]
    [InlineData(false, EmiMuteChoice.Keep, "avatarKept")]
    public Task LiveTalkerAsksAndVerdictRidesTheGreeting(bool takeover, EmiMuteChoice choice, string verdict) => WithDesk(async desk =>
    {
        int asks = 0;
        var moments = new List<string>();
        desk.TakeoverTalking = () => takeover;
        desk.RemoteTalking = () => !takeover;
        desk.AskMute = () => { asks++; return Task.FromResult(choice); };
        desk.SummonMoment = (id, _) => moments.Add(id);
        await desk.Summon();
        Assert.Equal(1, asks);
        Assert.Equal(choice == EmiMuteChoice.Mute, desk.AvatarMuted);
        Assert.Empty(moments);
        desk.GreetingDue();
        Assert.Equal(verdict, moments[0]);
        Assert.Equal(2, moments.Count);
        desk.GreetingDue();
        Assert.Equal(2, moments.Count);
    });

    [Fact]
    public Task SilentAppDoesNotAsk() => WithDesk(async desk =>
    {
        int asks = 0;
        desk.AskMute = () => { asks++; return Task.FromResult(EmiMuteChoice.Mute); };
        await desk.Summon();
        Assert.Equal(0, asks);
        Assert.False(desk.AvatarMuted);
    });

    [Fact]
    public Task PanicSurfaceClosesTheRealPrompt() => WithDesk(async desk =>
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var answer = EmiMutePromptWindow.Ask(owner);
            Assert.Single(owner.OwnedWindows.OfType<EmiMutePromptWindow>());
            PanicSurfaces.All.Single(x => x.Id == "emi-desk-feedback").Stop(null);
            Assert.Equal(EmiMuteChoice.Keep, await answer);
            Assert.Empty(owner.OwnedWindows.OfType<EmiMutePromptWindow>());
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task PanicSurfaceCancelsPendingPromptAndVerdict() => WithDesk(async desk =>
    {
        var answer = new TaskCompletionSource<EmiMuteChoice>();
        var moments = new List<string>();
        desk.RemoteTalking = () => true;
        desk.AskMute = () => answer.Task;
        desk.SummonMoment = (id, _) => moments.Add(id);
        var summon = desk.Summon();
        Assert.False(summon.IsCompleted);
        PanicSurfaces.All.Single(x => x.Id == "emi-desk-feedback").Stop(null);
        answer.SetResult(EmiMuteChoice.Mute);
        await summon;
        desk.GreetingDue();
        Assert.False(desk.IsOut);
        Assert.False(desk.AvatarMuted);
        Assert.Empty(moments);
    });
}
