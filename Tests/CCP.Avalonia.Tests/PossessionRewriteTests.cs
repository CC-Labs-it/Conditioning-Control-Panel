using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionRewriteTests
{
    private sealed class Frames : ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource
    {
        private readonly List<Action<TimeSpan>> _callbacks = new();
        public void Subscribe(Action<TimeSpan> callback) => _callbacks.Add(callback);
        public void Unsubscribe(Action<TimeSpan> callback) => _callbacks.Remove(callback);
        public void At(double ms) { foreach (var callback in _callbacks.ToArray()) callback(TimeSpan.FromMilliseconds(ms)); }
    }

    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        body();
    });

    private static void Host(Control control, Action<Frames, PossessionRuntime> body)
    {
        control.Width = 120; control.Height = 30;
        var window = new Window { Width = 400, Height = 300, Content = control };
        var frames = new Frames();
        var runtime = new PossessionRuntime(frames, rng: new Random(7));
        try { window.Show(); Dispatcher.UIThread.RunJobs(); body(frames, runtime); }
        finally { runtime.UndoAll(); window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    [Fact]
    public void DeckRewritesLabelForThreeSecondsAtDrift() => Run(() =>
    {
        var label = new TextBlock { Text = "Pause" };
        Host(label, (frames, runtime) =>
        {
            var effect = Assert.Single(PossessionRuntime.CreateAll().Where(e => e.Id == "rewrite"));
            Assert.Equal(PossessionRung.Drift, effect.MinRung);
            Assert.Equal(PossessionIntensity.Gentle, effect.MinIntensity);
            Assert.Equal(4, effect.Weight);
            var targets = new[] { new PossessionHeadTarget(label, PossessionRole.Label, "label") };
            Assert.False(runtime.PickAndApply(new[] { effect }, targets, PossessionRung.Settle, PossessionIntensity.Gentle));
            Assert.True(runtime.PickAndApply(new[] { effect }, targets, PossessionRung.Drift, PossessionIntensity.Gentle));
            Assert.Equal("Stay", label.Text);
            frames.At(1000); frames.At(3999);
            Assert.Equal("Stay", label.Text);
            frames.At(4000);
            Assert.Equal("Pause", label.Text);
            Assert.Equal(0, runtime.LiveCount);
        });
    });

    [Fact]
    public void PlainButtonContentRestoresTheSameObjectOnPanic() => Run(() =>
    {
        var original = new string("Pause".ToCharArray());
        var button = new Button { Content = original };
        Host(button, (_, runtime) =>
        {
            Assert.True(runtime.TryApply(new RewriteHeadEffect(), new(button, PossessionRole.Button, "button")));
            Assert.Equal("Stay", button.Content);
            PossessionRuntime.UndoAllLive();
            Assert.Same(original, button.Content);
        });
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestorePreservesANewerWrite(bool content) => Run(() =>
    {
        Control target = content ? new Button { Content = "Pause" } : new TextBlock { Text = "Pause" };
        Host(target, (frames, runtime) =>
        {
            Assert.True(runtime.TryApply(new RewriteHeadEffect(), new(target, content ? PossessionRole.Button : PossessionRole.Label, "target")));
            if (target is Button button) button.Content = "new value";
            else ((TextBlock)target).Text = "new value";
            frames.At(100); frames.At(3100);
            Assert.Equal("new value", content ? ((Button)target).Content : ((TextBlock)target).Text);
        });
    });

    [Fact]
    public void RejectsBoundRichShortAndTimerText() => Run(() =>
    {
        var effect = new RewriteHeadEffect();
        var bound = new TextBlock();
        using var binding = bound.Bind(TextBlock.TextProperty, new Binding("Text") { Source = new { Text = "Pause" } });
        Assert.False(effect.CanApply(bound));
        Assert.False(effect.CanApply(new TextBlock { Text = "x" }));
        Assert.False(effect.CanApply(new TextBlock { Inlines = { new Run("Pause"), new Run("Pause") } }));
        Assert.False(effect.CanApply(new Button { Content = new string('a', 49) }));
        Assert.True(effect.CanApply(new Border { Child = new TextBlock { Text = "Pause" } }));
        var timer = new TextBlock { Text = "Pause" };
        Host(timer, (_, runtime) =>
        {
            Assert.False(runtime.TryApply(effect, new(timer, PossessionRole.Timer, "timer")));
            Assert.Equal("Pause", timer.Text);
        });
    });
}
