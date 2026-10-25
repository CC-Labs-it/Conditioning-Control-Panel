using System;
using System.Collections.Generic;
using System.Reflection;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The head half of WPF 7.1.5 <c>Services/Companion/BarkService.cs</c>: one Core <see cref="BarkEngine"/>, its
/// mouth (the tube), and the seams WPF App.xaml.cs:313/336-346/604/2826 points at its BarkService.
/// ponytail: remaining WireSubscriptions and direct notify paths are inventoried in
/// docs/cloud/fin5-feat-companion-barks.md; existing hosts below are wired once and detached on Stop.
/// </summary>
internal static class BarkHead
{
    internal static BarkEngine? Engine { get; set; }   // set: tests

    internal static void Start()
    {
        if (Engine != null) return;
        var e = Engine = new BarkEngine();
        Seed(e);
        e.Start();
        WireSources(e);
        App.Brain?.AttachBarkSource(h => e.BarkSpoken += h, h => e.BarkSpoken -= h);   // App.xaml.cs:2826, the echo
        // The two WireSubscriptions the engine's own state needs: chat suppression (:806) and, on a mod switch,
        // the switch bark off the OLD set then the new mod's manifest (:903).
        App.UserMessageSent += _onUserMessage = () => { e.NoteUserMessage(); e.Raise("UserMessageSent"); };
        if ((_mods = App.Mods) is { } mods)
            mods.ModChanged += _onModChanged = (_, mod) =>
            {
                e.RegisterModSwitch();
                e.Raise("ModChanged", c => c.Set("mod", mod?.Id ?? "").Set("mod_switches_60s", e.ModSwitchesWithin(BarkEngine.RapidModSwitchWindow)));
                e.ReloadRules();
            };
    }

    private static Action? _onUserMessage;
    private static EventHandler<ModPackage>? _onModChanged;
    private static ModService? _mods;

    /// <summary>Drops the engine and every subscription Start made (tests; callers restore seams).</summary>
    internal static void Stop()
    {
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _unsubscribe.Clear();
        App.UserMessageSent -= _onUserMessage;
        if (_mods != null) _mods.ModChanged -= _onModChanged;
        (_onUserMessage, _onModChanged, _mods, Engine) = (null, null, null, null);
    }

    /// <summary>The mouth, live reads and Core seams; split from Start so a test can seed its own engine.</summary>
    internal static void Seed(BarkEngine e)
    {
        e.Speak = Speak;
        e.CurrentTier = () => AccountSeed.Patreon?.CurrentTier.ToString() ?? "None";
        e.IsSpeaking = () => Views.AvatarTube.AvatarTubeWindow.Live?.IsSpeaking == true;
        e.IsCompanionBusy = ms => Views.AvatarTube.AvatarTubeWindow.Live?.IsCompanionBusy(ms) == true;
        e.EmiDeskAvatarMuted = () => Views.Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted;
        e.FocusedAppName = () => App.WindowAwareness.CurrentServiceName is { Length: > 0 } svc
            ? svc : App.WindowAwareness.CurrentDetectedName;
        e.LiveField = field => field switch
        {
            "video_playing" => CoreEngine.Video?.IsPlaying == true,
            // WPF ResolveField :1446: the free collection's 100%, the ceiling the tab shows.
            "achievements_all_unlocked" => App.Achievements is { } a && a.GetTotalCount(false) is var total
                && total > 0 && a.GetUnlockedCount(false) >= total,
            _ => null,
        };

        CoreBark.UiAction = a => { if (!string.IsNullOrEmpty(a)) e.Raise("UiAction", c => c.Set("action", a!)); };
        CoreBark.TabNavigated = t => { if (!string.IsNullOrEmpty(t)) e.Raise("TabNavigated", c => c.Set("tab", t!)); };
        CoreBark.FeatureOpened = f => { if (!string.IsNullOrEmpty(f)) e.Raise("FeatureOpened", c => c.Set("feature", f!)); };
        CoreBark.AvatarClicked = () =>
        {
            e.RegisterAvatarClick();
            e.Raise("AvatarClicked", c => c.Set("clicks_60s", (double)e.AvatarClicksWithin(BarkEngine.RapidClickWindow)));
        };
        CoreBark.ChaosDraftAutopick = () => e.Raise("ChaosDraftAutopick");
        CoreBark.ChaosRankUp = rank => e.Raise("ChaosRankUp", c => c.Set("rank", rank));
        CoreBark.ChaosResultsShown = (score, best, pbDelta, isPb, defused, detonated, bestCombo, difficulty) =>
            e.Raise("ChaosResultsShown", c => c.Set("score", score).Set("best_score", best).Set("pb_delta", pbDelta).Set("is_pb", isPb)
                .Set("defused", defused).Set("detonated", detonated).Set("best_combo", (double)bestCombo).Set("difficulty", difficulty));
        CoreBark.AllLinesProvider = e.GetAllBarkLines;
        CoreModsHooks.ReloadBarkRules = e.ReloadRules;
        CoreSettingsHooks.SettingChangedSink = name => OnSettingChanged(e, name);
    }

    private static readonly List<Action> _unsubscribe = new();

    private static void Wire<T>(Action<T> add, Action<T> remove, T handler) where T : Delegate
    {
        add(handler);
        _unsubscribe.Add(() => remove(handler));
    }

    // WPF WireSubscriptions: use the existing head/Core hosts; retain exact payload names.
    internal static void WireSources(BarkEngine e)
    {
        Wire<Action<int>>(h => ProgressionBank.LevelUp += h, h => ProgressionBank.LevelUp -= h,
            level => e.Raise("LevelUp", c => c.Set("level", level)));
        Wire<Action<double, string>>(h => ProgressionBank.Awarded += h, h => ProgressionBank.Awarded -= h,
            (_, _) => e.Raise("XPChanged", c => c.Set("xp", CoreSettings.Current.PlayerXP)));
        Wire<Action<CompanionId, int>>(h => ConditioningControlPanel.Services.Companion.CompanionCore.LevelUp += h,
            h => ConditioningControlPanel.Services.Companion.CompanionCore.LevelUp -= h,
            (_, level) => e.Raise("CompanionLevelUp", c => c.Set("level", level)));
        if (App.Achievements is { } achievements)
            Wire<EventHandler<Achievement>>(h => achievements.Unlocked += h, h => achievements.Unlocked -= h,
                (_, a) => e.Raise("AchievementUnlocked", c => c.Set("achievement", a.Id)));
        if (App.Quests is { } quests)
        {
            Wire<EventHandler<QuestCompletedEventArgs>>(h => quests.QuestCompleted += h, h => quests.QuestCompleted -= h,
                (_, _) => e.Raise("QuestCompleted"));
            Wire<EventHandler<QuestProgressEventArgs>>(h => quests.QuestProgressChanged += h, h => quests.QuestProgressChanged -= h,
                (_, _) => e.Raise("QuestProgressChanged"));
            Wire<EventHandler>(h => quests.QuestsRefreshed += h, h => quests.QuestsRefreshed -= h,
                (_, _) => e.Raise("QuestsRefreshed"));
        }
        var mantra = App.Mantra;
        Wire<Action>(h => mantra.MantraCompleted += h, h => mantra.MantraCompleted -= h, () => e.Raise("MantraCompleted"));
        Wire<Action<int>>(h => mantra.StreakChanged += h, h => mantra.StreakChanged -= h,
            streak => e.Raise("MantraStreakChanged", c => c.Set("streak", streak)));
        Wire<Action>(h => mantra.StreakBroken += h, h => mantra.StreakBroken -= h, () => e.Raise("MantraStreakBroken"));
        if (LockdownService.Current is { } lockdown)
        {
            Wire<Action>(h => lockdown.LockdownActivated += h, h => lockdown.LockdownActivated -= h,
                () => e.Raise("LockdownActivated"));
            Wire<Action>(h => lockdown.LockdownDeactivated += h, h => lockdown.LockdownDeactivated -= h,
                () => e.Raise("LockdownDeactivated"));
            Wire<Action<TimeSpan>>(h => lockdown.CountdownTick += h, h => lockdown.CountdownTick -= h,
                ts => e.Raise("LockdownCountdownTick", c => c.Set("remaining_sec", ts.TotalSeconds)));
        }
        Wire<Action>(h => Views.Overlays.BubbleOverlay.Popped += h, h => Views.Overlays.BubbleOverlay.Popped -= h,
            () => e.Raise("BubblePopped"));
    }

    /// <summary>WPF Speak: ordinary clips queue via Giggle; only preempting rules use priority.</summary>
    internal static void Speak(BarkSpeech s) => Speak(s, Views.AvatarTube.AvatarTubeWindow.Live);

    internal static void Speak(BarkSpeech s, Views.AvatarTube.AvatarTubeWindow? tube)
    {
        if (tube == null) return;
        if (s.SilentEgg || !s.Priority) { tube.Giggle(s.Text, s.AudioPath, s.AudioPath != null); return; }
        tube.GigglePriority(s.Text, playSound: s.AudioPath == null, aiGenerated: false,
            phraseAudioPath: s.AudioPath, barkVoice: s.AudioPath != null, mood: s.Mood);
    }

    private static readonly Dictionary<string, PropertyInfo?> SettingProps = new(StringComparer.Ordinal);

    /// <summary>WPF NotifySettingChanged (:357) + TryGetSettingNumber (:596).</summary>
    internal static void OnSettingChanged(BarkEngine e, string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (name == nameof(AppSettings.IdleGiggleIntervalSeconds))
            Views.AvatarTube.AvatarTubeWindow.Live?.RestartIdleTimer();
        PropertyInfo? pi;
        lock (SettingProps)
            if (!SettingProps.TryGetValue(name!, out pi)) SettingProps[name!] = pi = typeof(AppSettings).GetProperty(name!);
        object? raw;
        try { raw = pi?.GetValue(CoreSettings.Current); } catch { return; }
        double? value = raw switch { bool b => b ? 1 : 0, int i => i, long l => l, double d => d, float f => f, Enum en => Convert.ToDouble(en), _ => null };
        if (value is { } v) e.Raise("SettingChanged", c => c.Set("setting", name!).Set("value", v));
    }
}
