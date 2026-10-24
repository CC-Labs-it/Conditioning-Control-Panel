using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The session-scoped corner GIF in SessionRunner against WPF SessionEngine
/// (SessionEngine.cs:198, 356, 531, 575, 862-896, 2126-2320) and the admission rule moved to Core.</summary>
[Collection(SessionStatics.Name)]
public sealed class SessionRunnerCornerGifTests : IDisposable
{
    private readonly SessionRunner _runner = new(new SessionLogService());
    private readonly List<string> _events = new();
    private bool _standalone, _spiralUp;
    private readonly bool _allowedBefore = CoreSettings.Current.SessionCornerGifAllowed;
    private readonly bool _spiralBefore = CoreSettings.Current.SpiralEnabled;

    public SessionRunnerCornerGifTests()
    {
        PhrasePoolCustody.Seed();
        CoreBouncingText.StartAction = CoreBouncingText.StopAction = null;
        CoreCornerGif.SessionShowHandler = s => _events.Add($"show {s.Position} {s.Size} {s.Opacity}");
        CoreCornerGif.SessionCloseHandler = handBack => _events.Add(handBack ? "close+handback" : "hide");
        CoreCornerGif.StandaloneActiveProvider = () => _standalone;
        CoreCornerGif.SpiralOverlayActiveProvider = () => _spiralUp;
        CoreSettings.Current.SessionCornerGifAllowed = true;
        CoreSettings.Current.SpiralEnabled = true;
    }

    public void Dispose()
    {
        _runner.Stop();
        CoreEngine.Stop();
        CoreSession.IsSessionRunningProvider = null;
        CoreCornerGif.SessionShowHandler = null;
        CoreCornerGif.SessionCloseHandler = null;
        CoreCornerGif.StandaloneActiveProvider = null;
        CoreCornerGif.SpiralOverlayActiveProvider = null;
        CoreSettings.Current.SessionCornerGifAllowed = _allowedBefore;
        CoreSettings.Current.SpiralEnabled = _spiralBefore;
    }

    private static Session Gamer(int start = 0, int end = 0) => new()
    {
        Id = "corner-gif-runner", Name = "Corner", DurationMinutes = 30, HasCornerGifOption = true,
        Settings = new SessionSettings
        {
            CornerGifEnabled = true, CornerGifStartMinute = start, CornerGifEndMinute = end,
            CornerGifPosition = CornerPosition.TopRight, CornerGifSize = 250, CornerGifOpacity = 40,
        },
    };

    [Fact]
    public void ShownAtStart_HiddenByPause_BackOnResume_EndMinuteHandsTheCornerBack()
    {
        _runner.Start(Gamer(end: 5));
        Assert.True(_runner.CornerGifShown);
        Assert.Equal(new[] { "show TopRight 250 40" }, _events);

        _runner.Pause();
        Assert.False(_runner.CornerGifShown);
        Assert.Equal("hide", _events[^1]);   // hide only: the claim survives a pause
        _runner.Resume();
        Assert.True(_runner.CornerGifShown);

        _runner.Tick(TimeSpan.FromMinutes(5));
        Assert.False(_runner.CornerGifShown);
        Assert.Equal("close+handback", _events[^1]);
        _runner.Tick(TimeSpan.FromMinutes(6));
        Assert.False(_runner.CornerGifShown);   // no re-raise after the end minute

        _runner.Stop();
        Assert.Equal(new[] { "show TopRight 250 40", "hide", "show TopRight 250 40", "close+handback" }, _events);
    }

    [Fact]
    public void AStandaloneOverlayKeepsTheCorner_ThenTheDelayedStartRaisesIt_UntilItsEndMinute()
    {
        _standalone = true;
        _runner.Start(Gamer(start: 2, end: 4));
        Assert.False(_runner.CornerGifShown);
        _standalone = false;
        _runner.Tick(TimeSpan.FromMinutes(1));
        Assert.False(_runner.CornerGifShown);   // not before its minute
        _runner.Tick(TimeSpan.FromMinutes(2));
        Assert.True(_runner.CornerGifShown);
        _runner.Tick(TimeSpan.FromMinutes(4));
        _runner.Tick(TimeSpan.FromMinutes(5));
        Assert.False(_runner.CornerGifShown);   // the delayed start does not re-raise it past its end

        _runner.Stop();   // the handback was paid at the end minute
        Assert.Equal(new[] { "show TopRight 250 40", "close+handback" }, _events);
    }

    [Fact]
    public void StopHandsTheCornerBack()
    {
        _runner.Start(Gamer());
        _runner.Stop();
        Assert.Equal(new[] { "show TopRight 250 40", "close+handback" }, _events);
    }

    [Fact]
    public void UnTickingTheUserMasterMidSession_TakesItDown_AndTheFullscreenSpiralRefusesASpiralCorner()
    {
        _runner.Start(Gamer());
        CoreSettings.Current.SessionCornerGifAllowed = false;
        _runner.Tick(TimeSpan.FromMinutes(1));
        Assert.False(_runner.CornerGifShown);
        Assert.Equal("close+handback", _events[^1]);
        _runner.Stop();

        CoreSettings.Current.SessionCornerGifAllowed = true;
        _spiralUp = true;   // the corner art is the spiral (no CornerGifPath), so it would be a second one
        _events.Clear();
        _runner.Start(Gamer());
        Assert.False(_runner.CornerGifShown);
        Assert.Empty(_events);
    }

    [Fact]
    public void ThePolicyRefresh_TakesItDownAtOnce_AndBringsAMinuteZeroOneBack()
    {
        _runner.Start(Gamer());
        CoreSettings.Current.SessionCornerGifAllowed = false;   // the Spiral card's switch, no tick
        _runner.RefreshCornerGifPolicy();
        Assert.False(_runner.CornerGifShown);
        CoreSettings.Current.SessionCornerGifAllowed = true;
        _runner.RefreshCornerGifPolicy();
        Assert.True(_runner.CornerGifShown);
        Assert.Equal(new[] { "show TopRight 250 40", "close+handback", "show TopRight 250 40" }, _events);

        _runner.Pause();
        _runner.RefreshCornerGifPolicy();   // paused: stays hidden until Resume
        Assert.False(_runner.CornerGifShown);
    }

    [Fact]
    public void ALiveEdit_ReShowsWithTheNewValue_WithoutAHandback()
    {
        _runner.Start(Gamer());
        _runner.UpdateCornerGif(ss => ss.CornerGifOpacity = 55);
        Assert.Equal(new[] { "show TopRight 250 40", "hide", "show TopRight 250 55" }, _events);
        Assert.Equal(55, _runner.CurrentSession!.Settings.CornerGifOpacity);
    }

    [Theory]
    [InlineData(true, true, false, true, true, false, true)]
    [InlineData(false, true, false, false, true, false, false)]   // the template did not ask
    [InlineData(true, false, false, false, true, false, false)]   // the user's master is off
    [InlineData(true, true, true, false, true, false, false)]     // a standalone slot owns the corner
    [InlineData(true, true, false, true, false, false, false)]    // spiral corner, user's spiral off
    [InlineData(true, true, false, true, true, true, false)]      // spiral corner, fullscreen spiral up
    [InlineData(true, true, false, false, false, true, true)]     // own art is neither
    public void AdmissionRule(bool template, bool user, bool standalone, bool spiralArt, bool userSpiral, bool spiralUp, bool want) =>
        Assert.Equal(want, CornerGifPlanner.AllowSessionCornerGif(template, user, standalone, spiralArt, userSpiral, spiralUp));
}
