using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class Sync7HistoryGentleTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task AiTubeLinesReachThePageWithoutEnteringPromptsOrPersistence() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var dir = Directory.CreateTempSubdirectory("tube-history-").FullName;
        using var ai = new AiService("http://127.0.0.1:1");
        using var memory = new MemoryStore(Path.Combine(dir, "memory.json"));
        using var brain = new CompanionBrain(ai, memory: memory,
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        AvApp.Brain = brain;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.GigglePriority("new thought", playSound: false, aiGenerated: true);
            Dispatcher.UIThread.RunJobs();
            var turn = Assert.Single(brain.Session.Turns);
            Assert.Equal(TurnKind.TubeLine, turn.Kind);
            Assert.True(ConditioningControlPanel.Services.Companion.ConversationRules.Shows(turn.Kind));
            Assert.Empty(ChatSession.ToMessages(brain.Session.Turns));
            Assert.False(turn.IsDialogue);
            tube.GigglePriority("new thought", playSound: false, aiGenerated: true);
            tube.GigglePriority("preset words", playSound: false, aiGenerated: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(brain.Session.Turns);
            brain.Session.Append(TurnKind.AmbientReply, "already logged");
            tube.GigglePriority("already logged", playSound: false, aiGenerated: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, brain.Session.Turns.Count);
        }
        finally { tube.Close(); AvApp.Brain = null; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task IncompleteGentlePromiseStaysOutOfTheSettingsUi(bool enabled) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        s.GentleVisuals = enabled;
        var section = new PerformanceSettingsSection();
        section.OnSectionShown();
        // Every control, including the tooltip's grid, must have an invisible ancestor.
        // A saved preference or a settings refresh must never reveal the pending promise.
        var toggle = section.FindControl<CheckBox>("ChkGentleVisuals")!;
        var choices = section.FindControl<StackPanel>("PnlGentleChoices")!;
        Assert.Equal(enabled, toggle.IsChecked);
        Assert.Equal(enabled, choices.IsVisible);
        var controls = section.GetLogicalDescendants().OfType<Control>()
            .Where(c => c.Name is "ChkGentleVisuals" or "CmbGentleFade" or "CmbGentleSpeed"
                || ToolTip.GetTip(c) is not null && c.GetLogicalDescendants().Contains(toggle)).ToArray();
        Assert.Equal(4, controls.Length);
        foreach (var control in controls)
            Assert.Contains(control.GetLogicalAncestors().OfType<Control>(), ancestor => !ancestor.IsVisible);
        Assert.Equal(enabled, s.GentleVisuals);
        return Task.CompletedTask;
    });

    [Fact]
    public Task GentleControlsSeedWithoutRoundingAndRoundTripWithoutChangingMotion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        s.GentleVisuals = false;
        s.GentleFadeSeconds = 0.6;
        s.GentleSpeed = 0.4;
        s.MotionLevel = MotionLevel.Full;
        var section = new PerformanceSettingsSection();
        var toggle = section.FindControl<CheckBox>("ChkGentleVisuals")!;
        var choices = section.FindControl<StackPanel>("PnlGentleChoices")!;
        var fade = section.FindControl<ComboBox>("CmbGentleFade")!;
        var speed = section.FindControl<ComboBox>("CmbGentleSpeed")!;
        Assert.False(choices.IsVisible);
        Assert.Equal(0.6, s.GentleFadeSeconds);
        Assert.Equal(0.4, s.GentleSpeed);
        Assert.Equal(1, fade.SelectedIndex);
        Assert.Equal(2, speed.SelectedIndex);
        var gates = 0;
        void Changed() => gates++;
        AmbientFxCanvas.Env.MotionGateChanged += Changed;
        try
        {
            toggle.IsChecked = true;
            Assert.True(s.GentleVisuals);
            Assert.True(choices.IsVisible);
            Assert.Equal(1, gates);
            Assert.Equal(MotionLevel.Reduced, AmbientFxCanvas.Env.Level);
            Assert.Equal(MotionLevel.Full, s.MotionLevel);
            fade.SelectedIndex = 4;
            speed.SelectedIndex = 3;
            Assert.Equal(1.5, s.GentleFadeSeconds);
            Assert.Equal(0.25, s.GentleSpeed);
            toggle.IsChecked = false;
            Assert.False(s.GentleVisuals);
            Assert.False(choices.IsVisible);
            Assert.Equal(1.5, s.GentleFadeSeconds);
            Assert.Equal(0.25, s.GentleSpeed);
        }
        finally { AmbientFxCanvas.Env.MotionGateChanged -= Changed; }
        return Task.CompletedTask;
    });
}
