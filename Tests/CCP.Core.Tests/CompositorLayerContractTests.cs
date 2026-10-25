using System;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;
using Xunit;

namespace CCP.Core.Tests;

// IWpfLayer + BaseLayer (moved from ConditioningControlPanel/Services/Compositor): the activity flag
// wakes the head's engine exactly once per transition, and the contract defaults stay as WPF had them.
public sealed class CompositorLayerContractTests
{
    private sealed class Layer : BaseLayer
    {
        public Layer(Action wake) : base(wake) { }
        public override int ZIndex => CompositorLayers.PinkTint;
        public void Set(bool value) => SetActive(value);
        public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed) { }
    }

    private sealed class BareLayer : IWpfLayer
    {
        public int ZIndex => 0;
        public bool IsActive => false;
        public void OnActivated() { }
        public void OnDeactivated() { }
        public void Update(TimeSpan delta) { }
        public void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed) { }
    }

    [Fact]
    public void SetActiveWakesTheEngineOncePerTransition()
    {
        int wakes = 0;
        var layer = new Layer(() => wakes++);
        Assert.False(layer.IsActive);

        layer.Set(false);
        Assert.Equal(0, wakes);

        layer.Set(true);
        layer.Set(true);
        Assert.True(layer.IsActive);
        Assert.Equal(1, wakes);

        layer.Set(false);
        Assert.False(layer.IsActive);
        Assert.Equal(2, wakes);
    }

    [Fact]
    public void BaseLayerDefaultsDrawOnTheMainSurfaceEveryScreenEveryFrame()
    {
        IWpfLayer layer = new Layer(() => { });
        Assert.False(layer.ExcludeFromCapture);
        Assert.False(layer.WorldSpacePx);
        Assert.True(layer.ShouldRenderOnScreen(new System.Drawing.Rectangle(1920, 0, 1920, 1080)));
        Assert.True(layer.Dirty);
        layer.ClearDirty();
        Assert.True(layer.Dirty);
    }

    [Fact]
    public void InterfaceDefaultsMatchBaseLayer()
    {
        IWpfLayer layer = new BareLayer();
        Assert.False(layer.ExcludeFromCapture);
        Assert.False(layer.WorldSpacePx);
        Assert.True(layer.ShouldRenderOnScreen(System.Drawing.Rectangle.Empty));
        Assert.True(layer.Dirty);
    }
}
