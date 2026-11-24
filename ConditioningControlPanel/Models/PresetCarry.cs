using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Globalization;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// The settings added after the preset's fixed field list stopped growing (7.1 onwards: Steam and
    /// the filter colour, floating videos, flash motion, every V2 option box). A preset carries them in
    /// <see cref="Preset.Carried"/> as AppSettings name to value (ccp-bugs #1695 #1696: white Steam, the
    /// fog cap and the floating-video drift leaked from one preset into every other one).
    ///
    /// <para><see cref="Looks"/>: a preset WITHOUT the name (saved before 7.3.7, or a built-in) puts the
    /// AppSettings default back, so an old preset no longer inherits what was last live (owner,
    /// 2026-11-20). <see cref="Screens"/>: monitor targets are this PC's hardware, so a preset without
    /// them leaves them as they are. <see cref="Modes"/>: the floating-video choice is the player's
    /// own comfort pick, so a preset without it leaves it too (owner, 2026-11-21, ccp-bugs #1711:
    /// old presets turned floating videos back to full screen).</para>
    ///
    /// <para>Scalars only (bool, int, double, string, enum); <c>PresetCarryTests</c> checks every name.
    /// A new player-tunable effect option belongs in <see cref="Looks"/>.</para>
    /// </summary>
    public static class PresetCarry
    {
        public static readonly string[] Looks =
        {
            // Pink filter / Steam
            nameof(AppSettings.PinkFilterColor),
            nameof(AppSettings.PinkFilterLinkRamp),
            nameof(AppSettings.PinkFilterUnderEffects),
            nameof(AppSettings.SuperCreepSpeed),
            nameof(AppSettings.SuperCreepCap),
            nameof(AppSettings.SteamReach),
            nameof(AppSettings.SteamDrift),
            nameof(AppSettings.SteamFogReturn),
            nameof(AppSettings.SteamWipeSize),
            nameof(AppSettings.SteamWordsInFog),

            // Mandatory videos (the floating switch itself is in Modes)
            nameof(AppSettings.MandatoryVideoFloatingSizePercent),
            nameof(AppSettings.MandatoryVideoFloatingSpeedPercent),
            nameof(AppSettings.MandatoryVideoFloatingCount),
            nameof(AppSettings.MandatoryVideoFloatingTogether),
            nameof(AppSettings.MandatoryVideoFloatingKeepSound),
            nameof(AppSettings.MandatoryVideoReplayAfterFailedCheck),

            // Flash motion
            nameof(AppSettings.FlashMotionStyle),
            nameof(AppSettings.FlashDriftSpeed),

            // Whirlpool
            nameof(AppSettings.WhirlpoolSpiral),
            nameof(AppSettings.WhirlpoolDragsFog),
            nameof(AppSettings.WhirlpoolDragsPictures),
            nameof(AppSettings.WhirlpoolDragsBubbles),
            nameof(AppSettings.WhirlpoolDragsWords),
            nameof(AppSettings.WhirlpoolSize),
            nameof(AppSettings.WhirlpoolStayCentred),
            nameof(AppSettings.WhirlpoolNewEvery),
            nameof(AppSettings.MovingEffectsPop),

            // Undertow, Lights Down, Deck
            nameof(AppSettings.UndertowLensSize),
            nameof(AppSettings.UndertowShrinkSpeed),
            nameof(AppSettings.UndertowClickWiden),
            nameof(AppSettings.LightsDownDarkness),
            nameof(AppSettings.LightsDownDimSpeed),
            nameof(AppSettings.LightsDownReturnSpeed),
            nameof(AppSettings.LightsDownFollow),
            nameof(AppSettings.DeckFlips),
            nameof(AppSettings.DeckBreakReach),

            // Afterglow
            nameof(AppSettings.AfterglowFrequency),
            nameof(AppSettings.AfterglowCount),
            nameof(AppSettings.AfterglowSize),
            nameof(AppSettings.AfterglowColorA),
            nameof(AppSettings.AfterglowColorB),
            nameof(AppSettings.AfterglowDuration),
            nameof(AppSettings.AfterglowOpacity),
            nameof(AppSettings.AfterglowTilt),
            nameof(AppSettings.AfterglowOnly),
            nameof(AppSettings.AfterglowLineWords),
            nameof(AppSettings.AfterglowLineHold),
            nameof(AppSettings.AfterglowGhostOpacity),
            nameof(AppSettings.AfterglowAlsoBase),

            // Scrawl
            nameof(AppSettings.ScrawlStampSeconds),
            nameof(AppSettings.ScrawlStampCap),
            nameof(AppSettings.ScrawlStampSize),
            nameof(AppSettings.ScrawlSlam),

            // Froth
            nameof(AppSettings.FrothMaxSize),
            nameof(AppSettings.FrothPopReach),
            nameof(AppSettings.FrothCursorPush),
        };

        public static readonly string[] Screens =
        {
            nameof(AppSettings.GlobalTargetMonitor),
            nameof(AppSettings.SpiralTargetMonitor),
            nameof(AppSettings.PinkFilterTargetMonitor),
            nameof(AppSettings.VideoTargetMonitor),
        };

        /// <summary>Carried and applied, but a preset without them leaves them as they are (#1711).</summary>
        public static readonly string[] Modes =
        {
            nameof(AppSettings.MandatoryVideoFloating),
        };

        private static readonly Lazy<AppSettings> Defaults = new(() => new AppSettings());

        private static PropertyInfo Prop(string name) =>
            typeof(AppSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"PresetCarry: AppSettings has no property {name}");

        /// <summary>Every carried setting as the player has it now, as invariant text (the preset is
        /// written by Newtonsoft in settings.json and by System.Text.Json in .preset.json files).</summary>
        public static Dictionary<string, string> Snapshot(AppSettings settings)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in Looks.Concat(Screens).Concat(Modes))
            {
                var text = Format(Prop(name).GetValue(settings));
                if (text != null) map[name] = text;
            }
            return map;
        }

        /// <summary>
        /// Writes the preset's carried values. A missing look goes back to its default; a missing
        /// screen or mode stays; an unreadable value is skipped. Assigns only on a real change.
        /// </summary>
        public static void Apply(AppSettings settings, IReadOnlyDictionary<string, string>? carried)
        {
            foreach (var name in Looks) ApplyOne(settings, name, carried, resetWhenMissing: true);
            foreach (var name in Screens) ApplyOne(settings, name, carried, resetWhenMissing: false);
            foreach (var name in Modes) ApplyOne(settings, name, carried, resetWhenMissing: false);
        }

        private static void ApplyOne(AppSettings settings, string name, IReadOnlyDictionary<string, string>? carried, bool resetWhenMissing)
        {
            var prop = Prop(name);
            object? want;
            if (carried != null && carried.TryGetValue(name, out var text) && text != null)
            {
                if (!TryParse(text, prop.PropertyType, out want)) return;
            }
            else if (resetWhenMissing)
            {
                want = prop.GetValue(Defaults.Value);
            }
            else return;

            if (Equals(prop.GetValue(settings), want)) return;
            prop.SetValue(settings, want);
        }

        internal static string? Format(object? value) => value switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            Enum e => e.ToString(),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => null
        };

        internal static bool TryParse(string text, Type type, out object? value)
        {
            value = null;
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(string)) { value = text; return true; }
            if (t == typeof(bool)) { if (!bool.TryParse(text, out var b)) return false; value = b; return true; }
            if (t == typeof(int)) { if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return false; value = i; return true; }
            if (t == typeof(double))
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsNaN(d) || double.IsInfinity(d)) return false;
                value = d; return true;
            }
            if (t.IsEnum)
            {
                // Names only: a number would let a hand-edited file pick a value the enum never had.
                if (!Enum.TryParse(t, text, ignoreCase: true, out var e) || e == null || !Enum.IsDefined(t, e)
                    || int.TryParse(text, out _)) return false;
                value = e; return true;
            }
            return false;
        }
    }
}
