using System;

namespace ConditioningControlPanel.Services
{
    public static class TutorialEventBus
    {
        public static event EventHandler<string>? Event
        {
            add => CoreTutorialEvents.Event += value;
            remove => CoreTutorialEvents.Event -= value;
        }

        public static string? LastSavedEnhancementPath
        {
            get => CoreTutorialEvents.LastSavedEnhancementPath;
            set => CoreTutorialEvents.LastSavedEnhancementPath = value;
        }

        // Set when an interactive Deeper tutorial finishes its in-dialog Part 1
        // and the editor should pick up Part 2 once it's loaded. Replaces the
        // older boolean StartHTPart2OnEditorLoad flag - typed as a TutorialType
        // so the same dispatch handles HT, Local Audio, and Local Video.
        // The dialog only sets this AFTER its own validation passes (so an
        // empty-source Create click doesn't strand a Part 2 flag forever).
        public static TutorialType? PendingPart2Tutorial
        {
            get => Enum.TryParse<TutorialType>(CoreTutorialEvents.PendingPart2Tutorial, out var type) ? type : null;
            set => CoreTutorialEvents.PendingPart2Tutorial = value?.ToString();
        }

        public static void Emit(string name)
        {
            CoreTutorialEvents.Emit(name);
        }
    }
}
