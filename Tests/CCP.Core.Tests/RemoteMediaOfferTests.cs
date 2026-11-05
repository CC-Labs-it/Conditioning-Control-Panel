using ConditioningControlPanel.Services.Startup;
using Xunit;
using Step = ConditioningControlPanel.Services.Startup.RemoteMediaOffer.Step;

namespace CCP.Core.Tests;

/// <summary>The empty-assets offer budget (WPF App.xaml.cs:879-987) plus this head's pool gate.</summary>
public sealed class RemoteMediaOfferTests : System.IDisposable
{
    public RemoteMediaOfferTests() => RemoteMediaOffer.ResetForTests();
    public void Dispose() => RemoteMediaOffer.ResetForTests();

    [Fact]
    public void NoRemotePoolOnThisHeadMeansNoOfferAndNothingParked()
    {
        foreach (var surface in new[] { "flashes", "videos", "custom-assets-path-missing" })
        {
            Assert.Equal(Step.Skip, RemoteMediaOffer.Decide(surface, "local", modalUp: false));
            Assert.Equal(Step.Skip, RemoteMediaOffer.Decide(surface, "local", modalUp: true));
        }
        Assert.Null(RemoteMediaOffer.TakePending());
    }

    [Fact]
    public void EachDeadEndNeedsItsOwnKindOfPool()
    {
        RemoteMediaOffer.VideoPoolLive = true;
        Assert.Equal(Step.Skip, RemoteMediaOffer.Decide("flashes", "local", false));
        Assert.Equal(Step.Offer, RemoteMediaOffer.Decide("videos", "local", false));
    }

    [Fact]
    public void OneOfferPerLaunchAcrossEverySite()
    {
        RemoteMediaOffer.FlashPoolLive = RemoteMediaOffer.VideoPoolLive = true;
        Assert.Equal(Step.Offer, RemoteMediaOffer.Decide("flashes", "local", false));
        Assert.Equal(Step.Skip, RemoteMediaOffer.Decide("videos", "local", false));
        Assert.Equal(Step.Skip, RemoteMediaOffer.Decide("flashes", "LOCAL", false));
    }

    [Fact]
    public void AUserAlreadyOnTheRemoteSourceIsNeverOffered()
    {
        RemoteMediaOffer.FlashPoolLive = true;
        Assert.Equal(Step.Skip, RemoteMediaOffer.Decide("flashes", "online", false));
        Assert.Equal(Step.Skip, RemoteMediaOffer.Decide("flashes", "mixed", false));
        Assert.Equal(Step.Offer, RemoteMediaOffer.Decide("flashes", "local", false));
    }

    [Fact]
    public void BehindAModalTheOfferIsParkedUnspentAndFlushedOnce()
    {
        RemoteMediaOffer.FlashPoolLive = true;
        Assert.Equal(Step.Park, RemoteMediaOffer.Decide("flashes", "local", modalUp: true));
        RemoteMediaOffer.Park("custom-assets-path-missing");   // the first parked surface wins
        Assert.Equal("flashes", RemoteMediaOffer.TakePending());
        Assert.Null(RemoteMediaOffer.TakePending());
        Assert.Equal(Step.Offer, RemoteMediaOffer.Decide("flashes", "local", modalUp: false));
    }
}
