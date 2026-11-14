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

public class PossessionTypoTests
{
    private sealed class Frames : ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource
    {
        private readonly List<Action<TimeSpan>> _callbacks = new();
        public void Subscribe(Action<TimeSpan> callback) => _callbacks.Add(callback);
        public void Unsubscribe(Action<TimeSpan> callback) => _callbacks.Remove(callback);
        public void At(double ms) { foreach (var callback in _callbacks.ToArray()) callback(TimeSpan.FromMilliseconds(ms)); }
        public int Count => _callbacks.Count;
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

    private static void Host(string text, Action<TextBlock, Frames, PossessionRuntime> body)
    {
        var label = new TextBlock { Text = text, Width = 120, Height = 30 };
        var window = new Window { Width = 400, Height = 300, Content = new Border { Child = label } };
        var frames = new Frames();
        var runtime = new PossessionRuntime(frames, rng: new Random(7));
        try { window.Show(); Dispatcher.UIThread.RunJobs(); body(label, frames, runtime); }
        finally { runtime.UndoAll(); window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    private static bool Apply(PossessionRuntime runtime, TextBlock label) =>
        runtime.TryApply(new TypoHeadEffect(), new PossessionHeadTarget(label, PossessionRole.Label, "label"));

    [Theory]
    [InlineData("boo")]
    [InlineData("bcd")]
    public void ChangesOneLegalSlipAndRestoresAtFourSeconds(string original) => Run(() => Host(original, (label, frames, runtime) =>
    {
        var effect = Assert.Single(PossessionRuntime.CreateAll().Where(e => e.Id == "typo"));
        Assert.Equal(PossessionRung.Settle, effect.MinRung);
        Assert.Equal(PossessionIntensity.Gentle, effect.MinIntensity);
        Assert.Equal(3, effect.Weight);
        Assert.False(effect.UsesFlicker);
        Assert.True(runtime.PickAndApply(new[] { effect }, new[] { new PossessionHeadTarget(label, PossessionRole.Label, "label") },
            PossessionRung.Settle, PossessionIntensity.Gentle));
        if (original == "boo") Assert.Contains(label.Text, new[] { "b0o", "bo0" });
        else Assert.Equal("bdc", label.Text);
        frames.At(1000); frames.At(4999);
        Assert.NotEqual(original, label.Text);
        frames.At(5000);
        Assert.Equal(original, label.Text);
        Assert.Equal(0, runtime.LiveCount);
        Assert.Equal(0, frames.Count);
    }));

    [Fact]
    public void RefusesBindingsRichInlinesAndUnmutableText() => Run(() =>
    {
        var effect = new TypoHeadEffect();
        var label = new TextBlock();
        using var binding = label.Bind(TextBlock.TextProperty, new Binding("Text") { Source = new { Text = "boo" } });
        Assert.False(effect.CanApply(label));
        foreach (var text in new[] { "", "ab", "bbb", "obbb" })
            Assert.False(effect.CanApply(new TextBlock { Text = text }));
        var rich = new TextBlock { Inlines = { new Run("boo"), new Run("boo") } };
        Assert.False(effect.CanApply(rich));
        var span = new TextBlock { Inlines = { new Span { Inlines = { new Run("boo") } } } };
        Assert.False(effect.CanApply(span));
        Assert.True(effect.CanApply(new Border { Child = new TextBlock { Text = "boo" } }));
    });

    [Fact]
    public void RuntimeRefusesTimerRoleAndBoundTargets() => Run(() => Host("boo", (label, _, runtime) =>
    {
        Assert.False(runtime.TryApply(new TypoHeadEffect(), new PossessionHeadTarget(label, PossessionRole.Timer, "timer")));
        using var binding = label.Bind(TextBlock.TextProperty, new Binding("Text") { Source = new { Text = "boo" } });
        Assert.False(Apply(runtime, label));
        Assert.Equal("boo", label.Text);
        Assert.Equal(0, runtime.LiveCount);
    }));

    [Fact]
    public void PanicRestoresOurWriteAndAllowsEffectReuse() => Run(() => Host("boo", (label, _, runtime) =>
    {
        var effect = new TypoHeadEffect();
        var target = new PossessionHeadTarget(label, PossessionRole.Label, "label");
        Assert.True(runtime.TryApply(effect, target));
        Assert.NotEqual("boo", label.Text);
        PossessionRuntime.UndoAllLive();
        Assert.Equal("boo", label.Text);
        Assert.True(runtime.TryApply(effect, target));
        Assert.NotEqual("boo", label.Text);
        runtime.UndoAll();
        Assert.Equal("boo", label.Text);
    }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndoNeverOverwritesANewerLabel(bool panic) => Run(() => Host("boo", (label, frames, runtime) =>
    {
        Assert.True(Apply(runtime, label));
        label.Text = "new level";
        if (panic) PossessionRuntime.UndoAllLive();
        else { frames.At(1000); frames.At(5000); }
        Assert.Equal("new level", label.Text);
        Assert.Equal(0, runtime.LiveCount);
    }));
}
