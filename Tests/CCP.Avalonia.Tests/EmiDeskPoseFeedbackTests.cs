using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskPoseFeedbackTests
{
    // WPF React.cs:98-101, 153-179; Alive.cs:114-127, 761-777; xaml.cs:1834-1842.
    [Fact]
    public Task ClickAndHoverUseTheirWpfPosesAndReturnToIdle() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var clock = new ManualClock();
        var desk = new EmiDeskWindow { PatClock = clock };
        int pets = EmiState.Current.PetsTotal;
        bool gist = EmiState.Current.PetGistGot;
        try
        {
            desk.Show();
            void Step(int ms) { clock.Advance(ms); desk.AdvancePatFeedback(); }
            Pat(desk); Assert.Equal("pet", Pose(desk));
            Step(899); Assert.Equal("pet", Pose(desk));
            Step(1); Assert.Equal("idle", Pose(desk));
            Pat(desk); Assert.Equal("pet", Pose(desk)); // cooldown flick
            Step(500); Assert.Equal("idle", Pose(desk));
            Step(500); Pat(desk); Assert.Equal("pet", Pose(desk)); // streak override
            Step(1700); Assert.Equal("idle", Pose(desk));
            Pat(desk); Assert.Equal("idle", Pose(desk)); // annoyed
            Step(880); Pat(desk); Assert.Equal("shock", Pose(desk)); // rage
            Step(2100); Assert.Equal("idle", Pose(desk));
            typeof(EmiDeskWindow).GetMethod("OnPetTick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(desk, new object?[] { null, EventArgs.Empty });
            Assert.Equal("pet", Pose(desk)); // hover wink override within cooldown
            Step(1280); Assert.Equal("idle", Pose(desk));
        }
        finally { desk.ShutDown(); EmiState.Current.PetsTotal = pets; EmiState.Current.PetGistGot = gist; }
        return Task.CompletedTask;
    });

    private static void Pat(EmiDeskWindow desk) => typeof(EmiDeskWindow)
        .GetMethod("PetFromClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(desk, null);
    private static string Pose(EmiDeskWindow desk) => (string)typeof(EmiDeskWindow)
        .GetField("_pose", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(desk)!;
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int ms) => _now = _now.AddMilliseconds(ms);
    }
}
