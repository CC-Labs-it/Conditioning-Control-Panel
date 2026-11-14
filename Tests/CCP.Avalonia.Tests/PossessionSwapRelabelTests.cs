using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionSwapRelabelTests
{
    private sealed class Host : IPossessionHost
    {
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution(Action? onCharge = null) : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) { onCharge?.Invoke(); return Task.CompletedTask; }
        public IDisposable Possess(object target) => new CancellationTokenSource();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }
    private sealed class CaptionSource : INotifyPropertyChanged
    {
        private string _text = "Start Session";
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Text
        {
            get => _text;
            set { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); }
        }
    }
    private static PossessionContext Context(Action? onCharge = null) => new()
    {
        Host = new Host(), Attribution = new Attribution(onCharge), Rung = PossessionRung.Melt,
        Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1),
        ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { }
    };
    private static PossessionTarget Target(Button b) => new() { Visual = b, Role = PossessionRole.Button, Key = "button" };
    private static Window Show(Button b)
    {
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { b } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    public void CatalogIncludesRelabel() => Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e is RelabelEffect);

    [Fact]
    public void RelabelRechecksSafetyAfterCharge() => AvaloniaTestDispatcher.Run(() =>
    {
        var button = new Button { Content = "Start", Width = 150, Height = 40 };
        var window = Show(button);
        try
        {
            var effect = new RelabelEffect();
            var target = Target(button);
            var ctx = Context(() => PossessionTree.SetExclude(button, true));
            Assert.True(effect.CanApply(ctx, target));
            effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("Start", button.Content);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        }
        finally { window.Close(); }
    });

    [Fact]
    public void RelabelChangesAndRestoresTheOriginalObject() => AvaloniaTestDispatcher.Run(() =>
    {
        var original = new string("Start tracking".ToCharArray());
        var button = new Button { Content = original, Width = 150, Height = 40 };
        var window = Show(button);
        try
        {
            var effect = new RelabelEffect();
            var target = Target(button);
            var ctx = Context();
            Assert.True(effect.CanApply(ctx, target));
            effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
            Assert.NotEqual(original, button.Content);
            Assert.Equal("Stay", button.Content);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Same(original, button.Content);
            Assert.False(target.IsLive);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void RelabelRestoresCurrentBoundNestedLabelAndKeepsBinding() => AvaloniaTestDispatcher.Run(() =>
    {
        var source = new CaptionSource();
        var icon = new TextBlock { Text = "▶" };
        var label = new TextBlock();
        label.Bind(TextBlock.TextProperty, new Binding(nameof(CaptionSource.Text)) { Source = source });
        var content = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Children = { icon, label } };
        var button = new Button { Content = content, Width = 200, Height = 40 };
        var window = Show(button);
        try
        {
            var effect = new RelabelEffect();
            var target = Target(button);
            var ctx = Context();
            Assert.Equal("Start Session", label.Text);
            Assert.True(effect.CanApply(ctx, target));
            effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("Stay", label.Text);
            Assert.Equal("▶", icon.Text);
            Assert.Same(content, button.Content);
            source.Text = "Stop Session";
            Assert.Equal("Stay", label.Text);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Equal("Stop Session", label.Text);
            source.Text = "Start Again";
            Assert.Equal("Start Again", label.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void RelabelRestoresUnboundNestedLabel() => AvaloniaTestDispatcher.Run(() =>
    {
        var original = new string("Stop Session".ToCharArray());
        var label = new TextBlock { Text = original };
        var button = new Button { Content = new StackPanel { Children = { new TextBlock { Text = "▶" }, label } }, Width = 200, Height = 40 };
        var window = Show(button);
        try
        {
            var effect = new RelabelEffect();
            var ctx = Context();
            var target = Target(button);
            Assert.True(effect.CanApply(ctx, target));
            effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("Stay", label.Text);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Same(original, label.Text);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(BindingPriority.LocalValue)]
    [InlineData(BindingPriority.Template)]
    public void RelabelRestoresCurrentBoundCaptionAndKeepsBinding(BindingPriority priority) => AvaloniaTestDispatcher.Run(() =>
    {
        var source = new CaptionSource { Text = "Start Guide" };
        var button = new Button { Width = 150, Height = 40 };
        button.Bind(ContentControl.ContentProperty, new Binding(nameof(CaptionSource.Text)) { Source = source, Mode = BindingMode.OneWay, Priority = priority });
        var window = Show(button);
        try
        {
            var effect = new RelabelEffect();
            var target = Target(button);
            var ctx = Context();
            Assert.Equal("Start Guide", button.Content);
            Assert.True(effect.CanApply(ctx, target));
            effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("Stay", button.Content);
            source.Text = "Stop Guide";
            Assert.Equal("Stay", button.Content);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Equal("Stop Guide", button.Content);
            source.Text = "Start Again";
            Assert.Equal("Start Again", button.Content);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void RelabelRefusesOffLimitsButton() => AvaloniaTestDispatcher.Run(() =>
    {
        var button = new Button { Content = "Stop", Width = 150, Height = 40 };
        PossessionTree.SetExclude(button, true);
        var window = Show(button);
        try { Assert.False(new RelabelEffect().CanApply(Context(), Target(button))); }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("Start", true)]
    [InlineData("Stop session", true)]
    [InlineData("Startup", false)]
    [InlineData("Stay", false)]
    public void MatchesWholeWordPrefixes(string text, bool expected) => Assert.Equal(expected, RelabelEffect.IsStartOrStop(text));
}
