using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The ring's catalogue on this head: Core <see cref="EmiDoors"/> (ids, art, hues, order) with the
    /// openers this head has. WPF twin: ConditioningControlPanel/Services/EmiDesk/EmiTargets.cs, whose
    /// availability, lock probe and opener this mirrors door for door.
    ///
    /// <para>ponytail: HIDDEN until this head has their surface: arcademy, spiral (overlay),
    /// goon and backroom. Each is a host window or overlay no
    /// Avalonia code launches yet; a null from <see cref="Door"/> keeps the card out of the ring the
    /// same way an unavailable door does. Also missing: the moments WPF's Pick fires
    /// (<c>ringPick</c>, <c>lockedCardTapped</c>) - there is no <c>App.EmiDesk.Fire</c> on this head.</para>
    /// </summary>
    internal static class EmiTargets
    {
        // Declared before All: static initialisers run in text order, and All's doors capture these.
        private static readonly Func<bool> Always = () => true;
        private static readonly Func<bool> Never = () => false;

        public static IReadOnlyList<EmiTarget> All { get; } = EmiDoors.Build(id =>
            Door(id) is { } d ? (d.Available, d.Locked, () => Pick(id, d.Locked, d.Open)) : null);

        public static EmiTarget? Find(string? id) =>
            All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

        /// <summary>Every navigation route teaches the same ring as a card pick (WPF NoteTabOpened).</summary>
        public static void NoteTabOpened(string? tabKey)
        {
            try
            {
                if (EmiDoors.TargetForTab(tabKey) is { } id) EmiState.NoteUsage(id);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tab open not counted for {Tab}", tabKey); }
        }

        /// <summary>A rack open scores its module, never Studio (WPF NoteRackOpened).</summary>
        public static void NoteRackOpened(string? rackKey)
        {
            try
            {
                if (EmiDoors.TargetForRack(rackKey) is { } id) EmiState.NoteUsage(id);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] rack open not counted for {Rack}", rackKey); }
        }

        private static (Func<bool> Available, Func<bool> Locked, Action Open) Free(Action open) => (Always, Never, open);

        /// <summary>A premium door: WPF's PremiumOk probe and PremiumPrompt refusal, over Core TierGate.</summary>
        private static (Func<bool> Available, Func<bool> Locked, Action Open) Premium(string id, string? daily, Action open)
        {
            string name() => Loc.Get("emi_desk_target_" + id);
            return (Always,
                () => !(daily == null ? TierGate.RequiresPremium(name()) : TierGate.RequiresPremium(name(), daily)).Allowed,
                open);
        }

        // WPF Services/EmiDesk/EmiTargets.cs:179,182,204 and ShowGate:295-296.
        private static (Func<bool> Available, Func<bool> Locked, Action Open)? Door(string id) => id switch
        {
            "dtrh" => (Always, Views.Games.GameWindow.DtrhLocked,
                () => Views.Games.GameWindow.Launch(Views.Games.GameWindow.DtrhId)),
            "intake" => (Always, () => !App.IntakePass.CanStartIntake,
                () => Views.Tabs.GradedIntakeTabView.OpenIntake(Shell, duckMain: true)),
            "justdrop" => (() => Services.JustDrop.JustDropService.DoorAvailable, Never, () => Nav("justdrop")),
            "loom" => Free(() => Rack("spiral")),
            "sessions" => Free(() => Nav("presets")),
            "flashes" => Free(() => Rack("flash")),
            "codex" => Free(() => EmiDeskService.Instance.Window?.OpenBook()),
            "videos" => Free(() => Rack("video")),
            "subliminals" => Free(() => Rack("subliminal")),
            "bubbles" => Free(() => Rack("bubbles")),
            "pinkfilter" => Free(() => Rack("pinkfilter")),
            "braindrain" => Free(() => Rack("braindrain")),
            "mindwipe" => Free(() => Rack("mindwipe")),
            "awareness" => Premium(id, "awareness", () => Nav("awareness")),
            "remote" => Premium(id, "remote", () => Nav("remotecontrol")),
            "takeover" => Premium(id, "takeover", () => Nav("bambitakeover")),
            "lockdown" => Premium(id, null, () => Nav("lockdown")),
            // WPF :215 opens the feed directly (Mw.OpenFypFeed), not the panel; the feed's gate demands premium.
            "fyp" => Premium(id, "fyp", () => Views.Games.GameWindow.Launch(Views.Games.GameWindow.FypId)),
            "vault" => Free(() => Nav("premium")),
            "companion" => Free(() => Nav("companion")),
            "progression" => Free(() => Nav("progression")),
            "profile" => Free(() => Nav("discord")),
            "settings" => Free(() => Nav("appsettings")),
            _ => null,
        };

        internal static Func<MainShellWindow?> ShellProvider = () =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainShellWindow;
        private static MainShellWindow? Shell => ShellProvider();

        private static void Nav(string tab)
        {
            var mw = Shell;
            if (mw == null) { Log.Debug("[EmiDesk] no main window, cannot show tab {Tab}", tab); return; }
            try { mw.ShowFromTray(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowFromTray failed"); }
            mw.ShowTab(tab);
        }

        private static void Rack(string rack)
        {
            var mw = Shell;
            if (mw == null) { Log.Debug("[EmiDesk] no main window, cannot open rack {Rack}", rack); return; }
            try { mw.ShowFromTray(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowFromTray failed"); }
            mw.OpenStudioModule(rack);
        }

        /// <summary>WPF EmiTargets.Pick: a locked door asks for the tier, an open one opens and scores.</summary>
        private static void Pick(string id, Func<bool> lockedProbe, Action open)
        {
            bool locked;
            try { locked = lockedProbe(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] lock probe threw at pick for {Target}", id); locked = true; }

            try
            {
                if (locked)
                {
                    var name = Loc.Get("emi_desk_target_" + id);
                    // WPF ShowGate: these two refusals are Lab prompts, never Premium.
                    if (id is "dtrh" or "intake")
                    {
                        TierGate.DemandLab(name, id == "dtrh" ? id : null);
                        return;
                    }
                    string? daily = id is "awareness" or "remote" or "takeover" ? id : null;
                    if (daily == null) TierGate.DemandPremium(name); else TierGate.DemandPremium(name, daily);
                    return;
                }
                open();
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] target {Target} failed to open", id); }

            try { EmiState.NoteUsage(id); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pick bookkeeping failed for {Target}", id); }
        }
    }
}
