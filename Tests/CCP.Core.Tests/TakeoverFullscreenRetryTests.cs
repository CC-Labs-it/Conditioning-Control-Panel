using System.Text.Json;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace CCP.Core.Tests;

public class TakeoverFullscreenRetryTests
{
    [Fact]
    public void OnlyFirstRefusalWithoutActivationRequestsRetry()
    {
        const string denied = "{\"type\":\"fsDenied\",\"activation\":false}";
        Assert.True(TakeoverFullscreenRetry.ShouldRetryWithGesture(denied, false));
        Assert.False(TakeoverFullscreenRetry.ShouldRetryWithGesture(denied, true));
        Assert.False(TakeoverFullscreenRetry.ShouldRetryWithGesture("{\"type\":\"fsDenied\",\"activation\":true}", false));
        Assert.False(TakeoverFullscreenRetry.ShouldRetryWithGesture("{\"type\":\"fsDenied\",\"reason\":\"recheck\",\"activation\":false}", false));
        Assert.False(TakeoverFullscreenRetry.ShouldRetryWithGesture(null, false));
        Assert.False(TakeoverFullscreenRetry.ShouldRetryWithGesture("{\"type\":\"fsAsk\",\"activation\":false}", false));
    }

    [Fact]
    public void EvaluateParamsPreserveScriptAndRequestGesture()
    {
        const string script = "a('\\\"b\\\"')";
        using var json = JsonDocument.Parse(TakeoverFullscreenRetry.EvaluateParams(script));
        Assert.Equal(script, json.RootElement.GetProperty("expression").GetString());
        Assert.True(json.RootElement.GetProperty("userGesture").GetBoolean());
    }

    [Fact]
    public void FocusOnlyRaisesVisibleUnminimizedInactiveHost()
    {
        Assert.True(TakeoverFullscreenRetry.ShouldRaiseHost(true, false, false));
        Assert.False(TakeoverFullscreenRetry.ShouldRaiseHost(false, false, false));
        Assert.False(TakeoverFullscreenRetry.ShouldRaiseHost(true, true, false));
        Assert.False(TakeoverFullscreenRetry.ShouldRaiseHost(true, false, true));
        Assert.Equal(150, TakeoverFullscreenRetry.FocusSettleMs);
    }
}
