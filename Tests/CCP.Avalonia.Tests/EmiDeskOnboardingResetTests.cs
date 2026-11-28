using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskOnboardingResetTests
{
    // WPF EmiDeskWindow.xaml.cs:1603-1608 and EmiDeskService.cs:1392: Ctrl+Shift+Alt+click on her body
    // replays onboarding instead of patting her.
    [Fact]
    public Task QaChordClickResetsOnboardingAndDoesNotPat() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var scope = new EmiTestScope();
        var desk = new EmiDeskWindow();
        try
        {
            var st = EmiState.Current;
            st.PetsTotal = 7; st.PetGistGot = true; st.RingOpens = 3; st.RingGistGot = true; st.PinGistGot = true;
            st.NudgeFires["pet"] = 2;
            desk.Show();
            Dispatcher.UIThread.RunJobs();
            var body = (Control)typeof(EmiDeskWindow).GetField("_bodyRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(desk)!;
            var point = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), desk)!.Value;
            const RawInputModifiers qa = RawInputModifiers.Control | RawInputModifiers.Shift | RawInputModifiers.Alt;
            desk.MouseDown(point, MouseButton.Left, qa);
            desk.MouseUp(point, MouseButton.Left, qa);
            st = EmiState.Current;
            Assert.Equal(0, st.PetsTotal);
            Assert.False(st.PetGistGot);
            Assert.Equal(0, st.RingOpens);
            Assert.False(st.RingGistGot);
            Assert.False(st.PinGistGot);
            Assert.Empty(st.NudgeFires);
            Assert.False(desk.ChainLive);   // the chord is not a pat

            desk.MouseDown(point, MouseButton.Left);
            desk.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, EmiState.Current.PetsTotal);   // a plain click still pats
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });
}
