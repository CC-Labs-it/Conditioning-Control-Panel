using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ConditioningControlPanel.Services
{
    /// <summary>When the Mod Creator may write its draft (ccp-bugs #1667). A draft still being offered (strip up, not
    /// restored or discarded) is never overwritten, and a form a failed restore left half filled is never saved.</summary>
    internal sealed class ModCreatorDraftGate
    {
        public bool Pending { get; private set; }
        public bool Broken { get; private set; }
        public bool Dirty { get; private set; }
        public bool ChangedSinceOpen { get; private set; }

        public void Offered() => Pending = true;
        public void Edited() { Dirty = true; ChangedSinceOpen = true; }
        public void Saved() => Dirty = false;
        public void SaveFailed() => Dirty = true;

        /// <summary>Restored or discarded: the offer is settled and saves resume.</summary>
        public void Settled() => Pending = false;

        /// <summary>A restore threw. The offer stays; if the form could not be put back, nothing is saved again.</summary>
        public void RestoreFailed(bool formPutBack) { Pending = true; if (!formPutBack) Broken = true; }

        public void Exported() { Dirty = false; ChangedSinceOpen = false; Pending = false; }

        public bool MaySave(bool closing) => !Pending && !Broken && (closing ? ChangedSinceOpen : Dirty);
    }

    /// <summary>Which routed events in the Mod Creator form are real value changes. A ComboBox opening its dropdown
    /// (its inner ToggleButton), an Expander header, a scrollbar track (RepeatButton), a list row selection and an audio
    /// preview button are not.</summary>
    internal static class ModCreatorEditSignal
    {
        private static readonly ConditionalWeakTable<Button, object> Passive = new();

        /// <summary>A button that never changes the mod (preview, export a list).</summary>
        public static void MarkPassive(Button button) => Passive.AddOrUpdate(button, Passive);

        public static bool IsEdit(RoutedEvent evt, object? source, int changedItems = 1)
        {
            if (evt == TextBoxBase.TextChangedEvent) return source is TextBox { IsReadOnly: false };
            if (evt == ToggleButton.CheckedEvent || evt == ToggleButton.UncheckedEvent) return source is CheckBox or RadioButton;
            if (evt == Selector.SelectionChangedEvent) return source is ComboBox && changedItems > 0;
            if (evt == ButtonBase.ClickEvent) return source is Button b && !IsPassive(b);
            return false;
        }

        private static bool IsPassive(Button b) =>
            Passive.TryGetValue(b, out _) || b.Content is string s && (s == "▶" || s == "⏹");
    }
}
