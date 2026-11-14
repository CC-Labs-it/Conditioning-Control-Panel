using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionRetitleXpTests
{
    private sealed class Host(Window window) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => null;
        public IFrameSource PossessionFrames => throw new NotSupportedException();
        public PossessionRuntime PossessionEffectRuntime => throw new NotSupportedException();
        public Point PointerPosition => default;
        public Rect? BoundsOnGhostLayer(Control element) => null;
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }

    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new Release();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
        private sealed class Release : IDisposable { public void Dispose() { } }
    }

    private static void WithTitle(Action<Window, TextBlock, TextBlock, PossessionContext, PossessionTarget> body, string prefix = "") =>
        AvaloniaTestDispatcher.Run(() =>
        {
            var title = new TextBlock { Text = "original heading", Width = 180, Height = 30 };
            var bar = new TextBlock { Name = "TxtTitleBarVersion", Text = "original bar", Width = 180, Height = 30 };
            var window = new Window { Title = prefix + "original window", Width = 400, Height = 200,
                Content = new StackPanel { Children = { title, bar } } };
            var ctx = new PossessionContext
            {
                Host = new Host(window), Attribution = new Attribution(), Rung = PossessionRung.Collapse,
                Intensity = PossessionIntensity.FullDoki, Photosafe = false, Rng = new Random(1),
                ElapsedFraction = .6, Remaining = TimeSpan.FromMinutes(5), Name = (_, _) => { }
            };
            var target = new PossessionTarget { Visual = title, Role = PossessionRole.Title, Key = "heading" };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try { body(window, title, bar, ctx, target); }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData("")]
    [InlineData("[SANDBOX] ")]
    public void RetitleRestoresBothShellTitles(string prefix) => WithTitle((window, title, bar, ctx, target) =>
    {
        var effect = PossessionAvaloniaHead.CreateEffects().Single(e => e.Id == "retitle");
        Assert.True(effect.CanApply(ctx, target));
        effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotEqual("original heading", title.Text);
        Assert.Equal(prefix + "original window", window.Title);
        Assert.Equal("original bar", bar.Text);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Equal("original heading", title.Text);
        Assert.Equal(prefix + "original window", window.Title);
        Assert.Equal("original bar", bar.Text);
    }, prefix);

    [Fact]
    public void RetitleLeavesNewerTitleAlone() => WithTitle((_, title, _, ctx, target) =>
    {
        var effect = new RetitleEffect();
        effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotEqual("original heading", title.Text);
        title.Text = "new heading";
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Equal("new heading", title.Text);
    });

    [Theory]
    [InlineData(BindingPriority.LocalValue)]
    [InlineData(BindingPriority.Template)]
    public void RetitleRefusesBoundTitles(BindingPriority priority) => WithTitle((_, title, _, ctx, target) =>
    {
        title.ClearValue(TextBlock.TextProperty);
        title.Bind(TextBlock.TextProperty, new Binding { Source = "bound heading", Priority = priority });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("bound heading", title.Text);
        var effect = new RetitleEffect();
        Assert.False(effect.CanApply(ctx, target));
        effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal("bound heading", title.Text);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(title, TextBlock.TextProperty));
    });

    [Fact]
    public void RetitleRefusesWrongRolesShortTextAndComplexInlines() => WithTitle((_, title, _, ctx, target) =>
    {
        var effect = new RetitleEffect();
        var label = new PossessionTarget { Visual = title, Role = PossessionRole.Label, Key = "label" };
        Assert.False(effect.CanApply(ctx, label));
        title.Text = " ";
        Assert.False(effect.CanApply(ctx, target));
        title.Text = "x";
        Assert.False(effect.CanApply(ctx, target));
        title.Text = "heading";
        title.Inlines = new InlineCollection { new Run("one"), new Run("two") };
        Assert.Equal("heading", title.Text);
        Assert.False(effect.CanApply(ctx, target));
    });

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("x")]
    public void RetitleRefusesShortOrEmptyText(string value) => WithTitle((_, title, _, ctx, target) =>
    {
        title.Text = value;
        Assert.False(new RetitleEffect().CanApply(ctx, target));
    });

    [Fact]
    public void RetitleRefusesComplexInlines() => WithTitle((_, title, _, ctx, target) =>
    {
        title.Text = "heading";
        title.Inlines = new InlineCollection { new Run("one"), new Run("two") };
        Assert.Equal("heading", title.Text);
        Assert.False(new RetitleEffect().CanApply(ctx, target));
    });

    [Fact]
    public void RetitleSupportsPlainSingleRun() => WithTitle((_, title, _, ctx, target) =>
    {
        title.Inlines = new InlineCollection { new Run("original heading") };
        var effect = new RetitleEffect();
        Assert.True(effect.CanApply(ctx, target));
        effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotEqual("original heading", title.Text);
        if (title.Inlines is { Count: > 0 })
            Assert.Equal(title.Text, Assert.IsType<Run>(title.Inlines.First()).Text);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Equal("original heading", title.Text);
    });

    [Fact]
    public void RetitleFindsTextInsideTitleTarget() => WithTitle((window, title, bar, ctx, _) =>
    {
        var container = (StackPanel)window.Content!;
        var target = new PossessionTarget { Visual = container, Role = PossessionRole.Title, Key = "container" };
        var effect = new RetitleEffect();
        Assert.True(effect.CanApply(ctx, target));
        effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotEqual("original heading", title.Text);
        Assert.Equal("original bar", bar.Text);
        window.Title = "navigated";
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Equal("navigated", window.Title);
        Assert.Equal("original heading", title.Text);
    });

    [Fact]
    public void RetitleAvoidsChoosingTheExistingLine() => WithTitle((_, title, _, ctx, target) =>
    {
        var keys = new[] { "possession_retitle_here", "possession_retitle_exit", "possession_retitle_reading",
            "possession_retitle_timer", "possession_retitle_ends" };
        var original = Loc.Get(keys[new Random(1).Next(keys.Length)]);
        title.Text = original.ToUpperInvariant();
        var effect = new RetitleEffect();
        effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(string.Equals(original, title.Text, StringComparison.OrdinalIgnoreCase));
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Equal(original.ToUpperInvariant(), title.Text);
    });

    [Fact]
    public void RetitleReadsTheSelectedLanguage() => WithTitle((_, title, _, ctx, target) =>
    {
        var loc = LocalizationManager.Instance;
        var before = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("fr");
            var lines = new[] { "possession_retitle_here", "possession_retitle_exit", "possession_retitle_reading",
                "possession_retitle_timer", "possession_retitle_ends" }.Select(Loc.Get).ToArray();
            Assert.Contains("toujours là ?", lines);
            var effect = new RetitleEffect();
            effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Contains(title.Text, lines);
            Assert.DoesNotContain("possession_retitle_", title.Text);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        }
        finally { loc.SetLanguage(before); }
    });

    [Fact]
    public void RetitleIsEligibleOnlyAtCollapseAndFullDoki()
    {
        var e = PossessionAvaloniaHead.CreateEffects().Single(e => e.Id == "retitle");
        var meta = new PossessionEffectMeta(e.Id, e.MinRung, e.MinIntensity, e.UsesFlicker, e.Weight, e.Roles);
        Assert.False(PossessionDeck.EffectEligible(meta, PossessionRung.Melt, PossessionIntensity.FullDoki, false));
        Assert.False(PossessionDeck.EffectEligible(meta, PossessionRung.Collapse, PossessionIntensity.Eerie, false));
        Assert.True(PossessionDeck.EffectEligible(meta, PossessionRung.Collapse, PossessionIntensity.FullDoki, false));
        Assert.Equal(TimeSpan.Zero, e.HoldFor);
    }
}
