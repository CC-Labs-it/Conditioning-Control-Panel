using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.GoonGame
{
    internal static class GoonDiscordPresence
    {
        internal static void State(string state, bool enabled, Action<string> setActivity)
        {
            if (state != "lobby" && state != "live" && state != "recap" && state != "off") return;
            if (!enabled) return;
            setActivity(state);
        }

        internal static void Prefs(AppSettings settings, bool? avatar, bool? dm, bool? presence,
            bool? seen, Action save, Action<string> setActivity, Action sync)
        {
            var sharedChanged = false;
            if (avatar.HasValue && settings.GoonShareAvatar != avatar.Value) { settings.GoonShareAvatar = avatar.Value; sharedChanged = true; }
            if (dm.HasValue && settings.GoonShareDiscordDm != dm.Value) { settings.GoonShareDiscordDm = dm.Value; sharedChanged = true; }
            var turnedOff = presence.HasValue && settings.GoonRichPresence && !presence.Value;
            if (presence.HasValue && settings.GoonRichPresence != presence.Value) settings.GoonRichPresence = presence.Value;
            if (seen.HasValue && settings.GoonSeenSharePrompt != seen.Value) settings.GoonSeenSharePrompt = seen.Value;
            save();
            if (turnedOff) setActivity("off");
            if (sharedChanged) sync();
        }

        internal static void Teardown(Action<string> setActivity) => setActivity("off");
    }
}
