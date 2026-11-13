using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Headless;
using Avalonia.Controls;
using System.Reflection;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LockCardGentleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SolvedCardUsesGentleTiming(bool gentle) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup(gentle);
        try
        {
            LockCardWindow.ShowOnAllMonitors("a", 1, false, true);
            var card = LockCardWindow.Primary!;
            if (gentle)
                Assert.Equal(TimeSpan.FromMilliseconds(750), Assert.IsType<DoubleTransition>(Assert.Single(card.Transitions!)).Duration);
            else Assert.Null(card.Transitions);
            card.FindControl<TextBox>("TxtInput")!.Focus();
            card.KeyTextInput("a");
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsCompleted);
            await Task.Delay(1650);
            Assert.Equal(gentle, LockCardWindow.IsAnyOpen());
            if (gentle)
                Assert.Equal(TimeSpan.FromSeconds(.75), Assert.IsType<DoubleTransition>(Assert.Single(card.Transitions!)).Duration);
            await Task.Delay(800);
            Assert.False(LockCardWindow.IsAnyOpen());
        }
        finally { LockCardWindow.ForceCloseAll(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicOrReplacementDuringFadeDoesNotCloseNewCard(bool replace) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup(true);
        try
        {
            LockCardWindow.ShowOnAllMonitors("a", 1, false, true);
            var card = LockCardWindow.Primary!;
            card.FindControl<TextBox>("TxtInput")!.Focus();
            card.KeyTextInput("a");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1650);
            Assert.True(LockCardWindow.IsAnyOpen());
            if (!replace) LockCardWindow.ForceCloseAll();
            LockCardWindow.ShowOnAllMonitors("new", 1, false, true);
            Assert.NotSame(card, LockCardWindow.Primary);
            await Task.Delay(800);
            Assert.Equal("new", LockCardWindow.Primary!.PhraseShown);
        }
        finally { LockCardWindow.ForceCloseAll(); }
    });

    [Fact]
    public Task AllMirrorsFadeAndClose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup(true);
        try
        {
            LockCardWindow.ShowOnAllMonitors("a", 1, false, true);
            var card = LockCardWindow.Primary!;
            var build = typeof(LockCardWindow).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)!;
            var mirror = (LockCardWindow)build.Invoke(null, new object[] { "a", 1, false, false, true, false })!;
            typeof(LockCardWindow).GetMethod("ShowWithGentle", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mirror, null);
            card.FindControl<TextBox>("TxtInput")!.Focus();
            card.KeyTextInput("a");
            Dispatcher.UIThread.RunJobs();
            Assert.True(mirror.IsCompleted);
            await Task.Delay(1650);
            Assert.True(card.IsVisible);
            Assert.True(mirror.IsVisible);
            Assert.Equal(TimeSpan.FromSeconds(.75), Assert.IsType<DoubleTransition>(Assert.Single(mirror.Transitions!)).Duration);
            mirror.Close(); // A closed mirror must not strand the remaining cover.
            await Task.Delay(800);
            Assert.False(card.IsVisible);
            Assert.False(mirror.IsVisible);
        }
        finally { LockCardWindow.ForceCloseAll(); }
    });

    private static void Setup(bool gentle)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        settings.Current.GentleVisuals = gentle;
        settings.Current.GentleFadeSeconds = .75;
        CoreSettings.ServiceProvider = () => settings;
    }
}
