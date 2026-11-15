using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace CCP.Core.Tests.Awareness;

/// <summary>The awareness mouth moved from WPF Services/Companion/Brain/AwarenessRouting.cs to Core: its App.*
/// reads are now head seams, and each one must still carry the same gate.</summary>
[Collection("AwarenessObserverCore")]
public sealed class AwarenessRoutingTests : IDisposable
{
    private readonly Func<SettingsService?>? _oldSettings = CoreSettings.ServiceProvider;
    private readonly Func<bool>? _oldEntitlement = AwarenessObserver.EntitlementProvider;
    // Saved, not nulled, on Dispose: these are process-wide seams a head or earlier test may have set.
    private readonly (Func<ContextFrame, bool>?, Func<string, bool, bool>?, Func<int, bool>?, Action?, Action<string, int>?, Func<string?>?) _oldSpeaker =
        (AvatarAwarenessSpeaker.RaiseBark, AvatarAwarenessSpeaker.DeliverLine, AvatarAwarenessSpeaker.CompanionBusy,
         AvatarAwarenessSpeaker.LineSpoken, AvatarAwarenessSpeaker.MuteKeywordEcho, AvatarAwarenessSpeaker.ForegroundTitle);
    private readonly (Func<CompanionBrain?>?, Func<IAiService?>?) _oldBrain =
        (BrainAwarenessLineSource.BrainProvider, BrainAwarenessLineSource.AiProvider);
    private readonly SettingsService _settings = new();
    private readonly List<(string Line, bool Bounce)> _delivered = new();
    private readonly List<(string Line, int Ms)> _muted = new();
    private readonly List<int> _busyWindows = new();
    private int _spoken;
    private bool _busy, _avatar = true;

    public AwarenessRoutingTests()
    {
        _settings.Current.UseAwarenessV2 = true;
        _settings.Current.AwarenessModeEnabled = true;
        _settings.Current.AwarenessConsentGiven = true;
        _settings.Current.AwarenessConsentShownV2 = true;
        _settings.Current.BarkChatSuppressionMs = 4321;
        CoreSettings.ServiceProvider = () => _settings;
        AwarenessObserver.EntitlementProvider = () => true;
        AvatarAwarenessSpeaker.DeliverLine = (line, bounce) => { if (_avatar) _delivered.Add((line, bounce)); return _avatar; };
        AvatarAwarenessSpeaker.CompanionBusy = ms => { _busyWindows.Add(ms); return _busy; };
        AvatarAwarenessSpeaker.LineSpoken = () => _spoken++;
        AvatarAwarenessSpeaker.MuteKeywordEcho = (line, ms) => _muted.Add((line, ms));
    }

    public void Dispose()
    {
        (AvatarAwarenessSpeaker.RaiseBark, AvatarAwarenessSpeaker.DeliverLine, AvatarAwarenessSpeaker.CompanionBusy,
         AvatarAwarenessSpeaker.LineSpoken, AvatarAwarenessSpeaker.MuteKeywordEcho, AvatarAwarenessSpeaker.ForegroundTitle) = _oldSpeaker;
        (BrainAwarenessLineSource.BrainProvider, BrainAwarenessLineSource.AiProvider) = _oldBrain;
        CoreSettings.ServiceProvider = _oldSettings;
        AwarenessObserver.EntitlementProvider = _oldEntitlement;
    }

    [Fact]
    public void CleanupRestoresTheSeamsItFoundRatherThanNullingThem()
    {
        Func<ContextFrame, bool> bark = _ => true; Func<string, bool, bool> deliver = (_, _) => true; Func<int, bool> busy = _ => false;
        Action spoken = () => { }; Action<string, int> mute = (_, _) => { }; Func<string?> title = () => "x";
        Func<CompanionBrain?> brain = () => null; Func<IAiService?> ai = () => null;
        (AvatarAwarenessSpeaker.RaiseBark, AvatarAwarenessSpeaker.DeliverLine, AvatarAwarenessSpeaker.CompanionBusy) = (bark, deliver, busy);
        (AvatarAwarenessSpeaker.LineSpoken, AvatarAwarenessSpeaker.MuteKeywordEcho, AvatarAwarenessSpeaker.ForegroundTitle) = (spoken, mute, title);
        (BrainAwarenessLineSource.BrainProvider, BrainAwarenessLineSource.AiProvider) = (brain, ai);

        new AwarenessRoutingTests().Dispose();

        Assert.Same(bark, AvatarAwarenessSpeaker.RaiseBark);
        Assert.Same(deliver, AvatarAwarenessSpeaker.DeliverLine);
        Assert.Same(busy, AvatarAwarenessSpeaker.CompanionBusy);
        Assert.Same(spoken, AvatarAwarenessSpeaker.LineSpoken);
        Assert.Same(mute, AvatarAwarenessSpeaker.MuteKeywordEcho);
        Assert.Same(title, AvatarAwarenessSpeaker.ForegroundTitle);
        Assert.Same(brain, BrainAwarenessLineSource.BrainProvider);
        Assert.Same(ai, BrainAwarenessLineSource.AiProvider);
    }

    [Fact]
    public void LineReachesTheAvatarThenSharesTheFloorAndMutesItsEcho()
    {
        var speaker = new AvatarAwarenessSpeaker(() => null);
        Assert.True(speaker.TrySpeakLine("hello there", RarityTier.Rare));
        Assert.True(speaker.TrySpeakLine("quip", RarityTier.Uncommon));
        Assert.Equal(new[] { ("hello there", true), ("quip", false) }, _delivered);
        Assert.Equal(new[] { 4321, 4321 }, _busyWindows);   // WPF reads BarkChatSuppressionMs
        Assert.Equal(2, _spoken);
        Assert.Equal(new[] { ("hello there", 8000), ("quip", 8000) }, _muted);
    }

    [Fact]
    public void BusyNoAvatarOrStoppedWatchingSpeaksNothing()
    {
        var speaker = new AvatarAwarenessSpeaker(() => null);
        _busy = true;
        Assert.False(speaker.TrySpeakLine("a", RarityTier.Common));
        _busy = false;
        _avatar = false;
        Assert.False(speaker.TrySpeakLine("b", RarityTier.Common));
        _avatar = true;
        _settings.Current.UseAwarenessV2 = false;   // she was told to stop watching mid-request
        Assert.False(speaker.TrySpeakLine("c", RarityTier.Common));
        AvatarAwarenessSpeaker.DeliverLine = null;
        _settings.Current.UseAwarenessV2 = true;
        Assert.False(speaker.TrySpeakLine("d", RarityTier.Common));
        Assert.Empty(_delivered);
        Assert.Equal(0, _spoken);
        Assert.Empty(_muted);
    }

    [Fact]
    public void BarkGoesThroughTheHeadAndOnlyWhileWatching()
    {
        var speaker = new AvatarAwarenessSpeaker(() => null);
        var frame = new ContextFrame();
        Assert.False(speaker.TrySpeakBark(frame));   // unseeded: no bark service
        ContextFrame? seen = null;
        AvatarAwarenessSpeaker.RaiseBark = f => { seen = f; return true; };
        Assert.True(speaker.TrySpeakBark(frame));
        Assert.Same(frame, seen);
        AwarenessObserver.EntitlementProvider = null;
        seen = null;
        Assert.False(speaker.TrySpeakBark(frame));
        Assert.Null(seen);
    }

    [Fact]
    public void UnseededAppIdClassifiesTheHeadForegroundTitle()
    {
        var speaker = new AvatarAwarenessSpeaker();
        Assert.Null(speaker.CurrentAppId);
        AvatarAwarenessSpeaker.ForegroundTitle = () => "Discord";
        var expected = AppClusterMap.Classify("Discord").app;
        Assert.NotNull(expected);
        Assert.Equal(expected, speaker.CurrentAppId);
        AvatarAwarenessSpeaker.ForegroundTitle = () => "";
        Assert.Null(speaker.CurrentAppId);
    }

    [Fact]
    public void LineSourceNeedsTheSeededBrainAndAi()
    {
        var ai = DispatchProxy.Create<IAiService, AvailableAi>();
        var source = new BrainAwarenessLineSource();
        _settings.Current.AiChatEnabled = true;
        BrainAwarenessLineSource.AiProvider = () => ai;
        Assert.False(source.IsAvailable);   // no brain
        BrainAwarenessLineSource.BrainProvider = () => (CompanionBrain)RuntimeHelpers.GetUninitializedObject(typeof(CompanionBrain));
        Assert.True(source.IsAvailable);
        _settings.Current.AiChatEnabled = false;
        Assert.False(source.IsAvailable);
        _settings.Current.AiChatEnabled = true;
        BrainAwarenessLineSource.AiProvider = null;
        Assert.False(source.IsAvailable);
    }

    public class AvailableAi : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "get_IsAvailable") return true;
            var t = method?.ReturnType;
            return t != null && t.IsValueType && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
    }
}
