using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Where a quest is done. ccp-bugs#1534: "click the quest for a 20-minute Lockdown and it takes
    /// you right to the Lockdown page, ready to start" - Lockdown lives inside Play with no pill of
    /// its own, so players could not find it.
    ///
    /// <para>One row per <see cref="QuestCategory"/>, keyed on the category because that is what
    /// progress counts against (a server quest cannot move without the right one). A destination is
    /// a ShowTab key, optionally a Studio rack module, optionally the x:Name of the control to ring
    /// once the page is up. It is fed to <c>SettingsPaletteWindow.Navigate</c>, so a quest click
    /// lands exactly like a Ctrl+K hit: navigation only, never a start, never a game launch or a
    /// dialog (a front-page tile navigates, never launches a modal).</para>
    ///
    /// <para>Categories with no single home (a streak, "any overlay", XP from anything) are listed
    /// in <see cref="NoHome"/> and their cards stay plain. <c>QuestDestinationsTests</c> pins that
    /// every category is in exactly one of the two tables.</para>
    /// </summary>
    internal static class QuestDestinations
    {
        internal sealed record Destination(string TabKey, string? RackKey = null, string? ElementName = null)
        {
            internal SettingsPaletteEntry ToPaletteEntry(QuestCategory category) => new()
            {
                Id = "quest." + category,
                TabKey = TabKey,
                RackKey = RackKey,
                ElementNames = ElementName == null ? System.Array.Empty<string>() : new[] { ElementName },
            };
        }

        internal static readonly IReadOnlyDictionary<QuestCategory, Destination> Map =
            new Dictionary<QuestCategory, Destination>
            {
                [QuestCategory.Flash] = new("studio", RackKey: "flash"),
                [QuestCategory.Video] = new("studio", RackKey: "video"),
                [QuestCategory.Spiral] = new("studio", RackKey: "spiral"),
                [QuestCategory.PinkFilter] = new("studio", RackKey: "pinkfilter"),
                [QuestCategory.Bubbles] = new("studio", RackKey: "bubbles"),
                [QuestCategory.LockCard] = new("studio", RackKey: "lockcard"),
                [QuestCategory.BubbleCount] = new("studio", RackKey: "bubblecount"),
                [QuestCategory.Session] = new("presets"),
                [QuestCategory.Autonomy] = new("bambitakeover", ElementName: "BtnAutonomyStartStop"),
                [QuestCategory.Lockdown] = new("lockdown", ElementName: "BtnActivateLockdown"),
                [QuestCategory.Remote] = new("remotecontrol", ElementName: "ChkRemoteControlEnabled"),
                [QuestCategory.RemoteIssue] = new("availablesubjects"),
                [QuestCategory.KeywordTrigger] = new("awareness", ElementName: "KeywordPanel"),
                [QuestCategory.BlinkTrainer] = new("blinktrainer", ElementName: "BtnBlinkTrainerStartSession"),
            };

        /// <summary>No single page finishes these, so their cards are not clickable.</summary>
        internal static readonly IReadOnlyCollection<QuestCategory> NoHome = new HashSet<QuestCategory>
        {
            QuestCategory.Streak,    // days in a row, not a place
            QuestCategory.Combined,  // any overlay, or XP from anything
            QuestCategory.Mantra,    // counted from mantra commands and Intake; no page of its own
        };

        /// <summary>The destination for a category, or null when it has no single home.</summary>
        internal static Destination? For(QuestCategory category)
            => Map.TryGetValue(category, out var d) ? d : null;
    }
}
