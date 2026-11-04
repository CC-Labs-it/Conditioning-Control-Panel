using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiTourVoiceTests
{
    [Fact]
    public void TickIsDeterministicAndUsesTheWpfWave()
    {
        var first = EmiTypingTick.Render();
        Assert.Equal(first, EmiTypingTick.Render());
        Assert.Equal(4470, first.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(first, 0, 4));
        Assert.Contains(first[44..], b => b != 0);
    }

    [Fact]
    public void TickObeysVolumeBreakerHoldAndPanicAndStops()
    {
        int volume = CoreSettings.Current.MasterVolume;
        var play = CoreAudio.PlayStoppableProvider;
        var breaker = CoreAudio.IsOutputSuppressedProvider;
        var field = typeof(EmiLineEngine).GetField("_heldBy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int plays = 0, stops = 0;
        float playedVolume = 0;
        try
        {
            PanicAftermath.EndQuiet();
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = (_, v, _, _, _) => { plays++; playedVolume = v; return () => stops++; };
            using var tick = new EmiTypingTick();
            Assert.True(tick.Tick());
            Assert.Equal(.5f, playedVolume);
            tick.Stop();
            Assert.Equal(1, stops);
            CoreSettings.Current.MasterVolume = 0;
            Assert.False(tick.Tick());
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => true;
            Assert.False(tick.Tick());
            CoreAudio.IsOutputSuppressedProvider = () => false;
            field.SetValue(EmiLineEngine.Instance, "test");
            Assert.False(tick.Tick());
            field.SetValue(EmiLineEngine.Instance, null);
            PanicAftermath.ArmQuiet();
            Assert.False(tick.Tick());
            Assert.Equal(1, plays);
        }
        finally
        {
            field.SetValue(EmiLineEngine.Instance, null);
            PanicAftermath.EndQuiet();
            CoreSettings.Current.MasterVolume = volume;
            CoreAudio.PlayStoppableProvider = play;
            CoreAudio.IsOutputSuppressedProvider = breaker;
        }
    }

    [Theory]
    [InlineData("volume")]
    [InlineData("breaker")]
    [InlineData("hold")]
    [InlineData("panic")]
    public void EachSilenceGatePreventsPlayback(string gate)
    {
        int volume = CoreSettings.Current.MasterVolume;
        var play = CoreAudio.PlayStoppableProvider;
        var breaker = CoreAudio.IsOutputSuppressedProvider;
        var field = typeof(EmiLineEngine).GetField("_heldBy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int plays = 0;
        try
        {
            PanicAftermath.EndQuiet();
            CoreSettings.Current.MasterVolume = gate == "volume" ? 0 : 50;
            CoreAudio.IsOutputSuppressedProvider = () => gate == "breaker";
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
            if (gate == "hold") field.SetValue(EmiLineEngine.Instance, "test");
            if (gate == "panic") PanicAftermath.ArmQuiet();
            using var tick = new EmiTypingTick();
            Assert.False(tick.Tick());
            Assert.Equal(0, plays);
        }
        finally
        {
            field.SetValue(EmiLineEngine.Instance, null);
            PanicAftermath.EndQuiet();
            CoreSettings.Current.MasterVolume = volume;
            CoreAudio.PlayStoppableProvider = play;
            CoreAudio.IsOutputSuppressedProvider = breaker;
        }
    }

    [Fact]
    public Task BubbleDotsTickAndCancellationCutsPlayback() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        int volume = CoreSettings.Current.MasterVolume;
        var play = CoreAudio.PlayStoppableProvider;
        var breaker = CoreAudio.IsOutputSuppressedProvider;
        int plays = 0, stops = 0;
        var desk = new EmiDeskWindow();
        try
        {
            PanicAftermath.EndQuiet();
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => stops++; };
            desk.Show();
            desk.Say("hello");
            Assert.Equal(1, plays);
            desk.AdvanceTourSpeech(); desk.AdvanceTourSpeech();
            Assert.Equal(3, plays);
            desk.AdvanceTourSpeech();
            Assert.Equal(3, plays);
            Assert.Equal(3, stops);
            desk.Say("again");
            desk.CancelChain();
            Assert.Equal(4, stops);
            desk.Say("dismiss");
            desk.RunDismiss();
            Assert.Equal(5, stops);
            desk.ShutDown();
            var other = new EmiDeskWindow();
            try
            {
                other.Show();
                other.Say("teardown");
                other.ShutDown();
                Assert.Equal(6, stops);
            }
            finally { other.ShutDown(); }
        }
        finally
        {
            desk.ShutDown();
            CoreSettings.Current.MasterVolume = volume;
            CoreAudio.PlayStoppableProvider = play;
            CoreAudio.IsOutputSuppressedProvider = breaker;
            PanicAftermath.EndQuiet();
        }
        return Task.CompletedTask;
    });
}
