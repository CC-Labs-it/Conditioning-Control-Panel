using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionGlyphRotTests
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

    [Theory]
    [InlineData(false, 1440)]
    [InlineData(true, 1920)]
    public void RotStepsThenHealsInReverseWithPhotosafeHold(bool photosafe, int healAt) => Run(() =>
    {
        var label = new TextBlock { Text = "Cat" };
        Host(label, (frames, runtime) =>
        {
            runtime.Photosafe = photosafe;
            var effect = Assert.Single(PossessionRuntime.CreateAll().Where(e => e.Id == "glyphrot"));
            Assert.Equal(PossessionRung.Melt, effect.MinRung);
            Assert.Equal(PossessionIntensity.Eerie, effect.MinIntensity);
            Assert.False(effect.UsesFlicker);
            Assert.Equal(2, effect.Weight);
            var targets = new[] { new PossessionHeadTarget(label, PossessionRole.Label, "label") };
            Assert.False(runtime.PickAndApply(new[] { effect }, targets, PossessionRung.Drift, PossessionIntensity.Eerie));
            Assert.False(runtime.PickAndApply(new[] { effect }, targets, PossessionRung.Melt, PossessionIntensity.Gentle));
            Assert.True(runtime.PickAndApply(new[] { effect }, targets, PossessionRung.Melt, PossessionIntensity.Eerie));
            frames.At(1000); frames.At(1059);
            Assert.Equal("Cat", label.Text);
            frames.At(1060); var first = label.Text;
            Assert.NotEqual("Cat", first);
            frames.At(1120); var second = label.Text;
            Assert.NotEqual(first, second);
            frames.At(1180); var third = label.Text;
            Assert.NotEqual(second, third);
            frames.At(1000 + healAt - 1); Assert.Equal(third, label.Text);
            frames.At(1000 + healAt); Assert.Equal(second, label.Text);
            frames.At(1060 + healAt); Assert.Equal(first, label.Text);
            frames.At(1120 + healAt); Assert.Equal("Cat", label.Text);
            frames.At(4500); Assert.Equal(0, runtime.LiveCount);
        });
    });

    // WPF GlyphRotEffect.cs:83-103, TypoEffect.cs:50-74 and RewriteEffect.cs:63-98 only write text.
    [Theory]
    [InlineData("glyphrot", false)]
    [InlineData("glyphrot", true)]
    [InlineData("typo", false)]
    [InlineData("typo", true)]
    [InlineData("rewrite", false)]
    [InlineData("rewrite", true)]
    public void TextEffectsLeaveExistingAndNewTransformsAlone(string id, bool panic) => Run(() =>
    {
        var prior = TransformOperations.Parse("scale(2)");
        var newer = TransformOperations.Parse("scale(3)");
        var label = new TextBlock { Text = "Pause", RenderTransform = prior };
        Host(label, (frames, runtime) =>
        {
            var effect = Assert.Single(PossessionRuntime.CreateAll().Where(e => e.Id == id));
            Assert.True(runtime.TryApply(effect, new(label, PossessionRole.Label, "label")));
            Assert.Equal(2, label.RenderTransform!.Value.M11);
            Assert.Same(prior, label.RenderTransform);
            Assert.False(PossessionLease.IsLeased(label));
            frames.At(0); frames.At(60);
            Assert.NotEqual("Pause", label.Text);
            Assert.Same(prior, label.RenderTransform);

            label.RenderTransform = newer;
            var origin = new RelativePoint(0.2, 0.3, RelativeUnit.Relative);
            label.RenderTransformOrigin = origin;
            if (panic) PossessionRuntime.UndoAllLive(); else frames.At(effect.HoldFor.TotalMilliseconds);
            Assert.Equal("Pause", label.Text);
            Assert.Same(newer, label.RenderTransform);
            Assert.Equal(3, label.RenderTransform!.Value.M11);
            Assert.Equal(origin, label.RenderTransformOrigin);
            Assert.Equal(0, runtime.LiveCount);
        });
    });

    [Fact]
    public void NewTextWinsDuringAnimationAndPanic() => Run(() =>
    {
        var label = new TextBlock { Text = "Cat" };
        Host(label, (frames, runtime) =>
        {
            Assert.True(runtime.TryApply(new GlyphRotHeadEffect(), new(label, PossessionRole.Title, "title")));
            frames.At(0); frames.At(60);
            Assert.NotEqual("Cat", label.Text);
            label.Text = "New value";
            frames.At(120); frames.At(1800);
            Assert.Equal("New value", label.Text);
            PossessionRuntime.UndoAllLive();
            Assert.Equal("New value", label.Text);
            Assert.Equal(0, runtime.LiveCount);
        });
    });

    [Fact]
    public void PanicAndHoldBoundaryRestoreAnOwnedPartialRot() => Run(() =>
    {
        foreach (bool panic in new[] { true, false })
        {
            var label = new TextBlock { Text = new string('a', 40) };
            string original = label.Text;
            Host(label, (frames, runtime) =>
            {
                Assert.True(runtime.TryApply(new GlyphRotHeadEffect(), new(label, PossessionRole.TabHeader, "tab")));
                frames.At(0); frames.At(60);
                Assert.NotEqual(original, label.Text);
                if (panic) PossessionRuntime.UndoAllLive(); else frames.At(3500);
                Assert.Equal(original, label.Text);
                Assert.Equal(0, runtime.LiveCount);
            });
        }
    });

    [Fact]
    public void RejectsBoundRichShortAndNonwordTextAndTimerRole() => Run(() =>
    {
        var effect = new GlyphRotHeadEffect();
        var bound = new TextBlock();
        using var binding = bound.Bind(TextBlock.TextProperty, new Binding("Text") { Source = new { Text = "Cat" } });
        Assert.False(effect.CanApply(bound));
        Assert.False(effect.CanApply(new TextBlock { Text = "ab" }));
        Assert.False(effect.CanApply(new TextBlock { Text = "12 ab 34" }));
        Assert.False(effect.CanApply(new TextBlock { Inlines = { new Run("Cat"), new Run("Dog") } }));
        Assert.True(effect.CanApply(new TextBlock { Text = "a Cat" }));
        var label = new TextBlock { Text = "Cat" };
        Host(label, (_, runtime) => Assert.False(runtime.TryApply(effect, new(label, PossessionRole.Timer, "timer"))));
    });
}
