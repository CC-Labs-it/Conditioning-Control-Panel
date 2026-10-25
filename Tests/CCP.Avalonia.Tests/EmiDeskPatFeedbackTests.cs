using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskPatFeedbackTests
{
    [Fact]
    public Task ClickPatShowsTheCooldownFlickAndStreakWithoutCountingExtraPets() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var played = new List<string>();
        var previous = CoreAudio.PlayOneShotProvider;
        int volume = CoreSettings.Current.MasterVolume;
        var st = EmiState.Current;
        int pets = st.PetsTotal; bool gist = st.PetGistGot;
        var clock = new ManualClock();
        var desk = new EmiDeskWindow { PatClock = clock };
        try
        {
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
            CoreSettings.Current.MasterVolume = 50;
            EmiSfx.Clock = clock;
            desk.Show();
            void Pat() => typeof(EmiDeskWindow).GetMethod("PetFromClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(desk, null);
            Pat();
            Assert.Equal("^_^", desk.Face);
            Assert.Equal(pets + 1, st.PetsTotal);
            int cues = played.Count;
            clock.Advance(200); Pat(); // WPF React.cs:132: a live chain eats the click - no cue, no poke
            Assert.Equal(cues, played.Count);
            Assert.Equal("^_^", desk.Face);
            clock.Advance(800); desk.AdvancePatFeedback();
            Assert.Equal("0_0", desk.Face);
            Pat();
            Assert.Equal("^_~", desk.Face);
            clock.Advance(320); desk.AdvancePatFeedback();
            Assert.Equal("0_0", desk.Face);
            clock.Advance(680); desk.AdvancePatFeedback(); Pat(); // third touch inside four seconds: glee
            Assert.Contains("emi-sfx-chime", played);
            clock.Advance(300); desk.AdvancePatFeedback();
            Assert.Equal("(≧◡≦)", desk.Face);
            clock.Advance(1400); desk.AdvancePatFeedback(); Pat();
            Assert.Equal("-_-", desk.Face);
            clock.Advance(900); desk.AdvancePatFeedback(); Pat();
            Assert.Equal(">.<", desk.Face);
            clock.Advance(600); desk.AdvancePatFeedback();
            Assert.Equal(">:(", desk.Face);
            Assert.Equal(pets + 1, st.PetsTotal);
            desk.Hide(); // hidden, dismissed and closed widgets must not retain a timer/face
            clock.Advance(10000); desk.AdvancePatFeedback();
            Assert.Equal("0_0", desk.Face);
            desk.Show(); Pat();
            Assert.Equal(pets + 2, st.PetsTotal);
            var svc = EmiDeskService.Instance;
            var windowField = typeof(EmiDeskService).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var previousWindow = windowField.GetValue(svc);
            try
            {
                windowField.SetValue(svc, desk);
                PanicSurfaces.All.Single(surface => surface.Id == "emi-desk-feedback").Stop(null);
                Assert.Equal("0_0", desk.Face);
                clock.Advance(1000); desk.AdvancePatFeedback();
                Assert.Equal("0_0", desk.Face);
            }
            finally { windowField.SetValue(svc, previousWindow); }
            clock.Advance(1000);
            typeof(EmiDeskWindow).GetMethod("OnPetTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(desk, new object?[] { null, EventArgs.Empty });
            Assert.Equal("^_^", desk.Face); // hover retains WPF's longer wink
            clock.Advance(420); desk.AdvancePatFeedback();
            Assert.Equal("^_~", desk.Face);
            clock.Advance(260); desk.AdvancePatFeedback();
            Assert.Equal("^_^", desk.Face);
            clock.Advance(600); desk.AdvancePatFeedback();
            Assert.Equal("0_0", desk.Face);
            Assert.Equal(pets + 2, st.PetsTotal);
        }
        finally
        {
            desk.ShutDown();
            CoreAudio.PlayOneShotProvider = previous;
            CoreSettings.Current.MasterVolume = volume;
            EmiSfx.Clock = TimeProvider.System;
            st.PetsTotal = pets; st.PetGistGot = gist;
        }
        return Task.CompletedTask;
    });

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        public void Advance(int ms) => _now = _now.AddMilliseconds(ms);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
