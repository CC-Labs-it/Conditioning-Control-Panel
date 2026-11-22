using System.Collections.Generic;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Studio rack's reading rules (declutter wave 1, 2026-11-15). Decisions only, no painting,
    /// so the Avalonia twin copies this file and repaints.
    ///
    /// <para>Two signals that never share a channel. SELECTED is the row surface: a fill in the
    /// detail pane's own surface brush plus the 3 px section hue bar, so row and pane read as one
    /// object. ON is the row's content: a filled dot and a full-strength label; OFF is a hollow
    /// ring and a dimmed label. State is never colour alone - the dot's shape and the label's
    /// brightness carry it too.</para>
    /// </summary>
    public static class StudioRackRules
    {
        /// <summary>Label opacity of a row whose module is off.</summary>
        public const double OffLabelOpacity = 0.55;

        /// <summary>Stroke of the hollow ring an off dot draws, inside the same 7 px box.</summary>
        public const double OffRingThickness = 1.5;

        /// <summary>The selected row's fill: the brush the detail pane's cards sit on.</summary>
        public const string SelectedFillBrushKey = "SurfaceBgBrush";

        /// <summary>The selected row's 3 px bar: the Studio section hue, same as the strip.</summary>
        public const string SelectedBarBrushKey = "SectionHueStudioBrush";

        /// <summary>The rack card's ground, one step under the pane surface so the selected
        /// fill shows against it.</summary>
        public const string RackGroundBrushKey = "DarkerBgBrush";

        /// <summary>Loc key of the group header count; format args are on, total.</summary>
        public const string GroupCountKey = "st4_studio_group_count";

        /// <summary>Unknown reads as off: a settings object that is null for a tick must not
        /// light a dot.</summary>
        public static bool IsOn(bool? state) => state == true;

        /// <summary>Filled when on, a hollow ring when off.</summary>
        public static bool DotIsFilled(bool? state) => IsOn(state);

        /// <summary>Full strength when on, dimmed when off. Selection does not change it.</summary>
        public static double LabelOpacity(bool? state) => IsOn(state) ? 1.0 : OffLabelOpacity;

        /// <summary>
        /// Counts one group. Each item is a row's state, or <c>hasState: false</c> for a row with
        /// no switch of its own (Visuals), which counts on neither side.
        /// </summary>
        public static (int On, int Total) Count(IEnumerable<(bool HasState, bool? State)> rows)
        {
            int on = 0, total = 0;
            foreach (var (hasState, state) in rows)
            {
                if (!hasState) continue;
                total++;
                if (IsOn(state)) on++;
            }
            return (on, total);
        }

        /// <summary>The header text ("5 of 7 on"), or null when the group has nothing to count
        /// and the header shows no number at all.</summary>
        public static string? CountText(string format, int on, int total)
        {
            if (total <= 0 || string.IsNullOrEmpty(format)) return null;
            try { return string.Format(format, on, total); }
            catch (System.FormatException) { return null; }
        }
    }
}
