using System;

namespace ConditioningControlPanel.Services.Startup.Conversion
{
    /// <summary>
    /// Moments a conversion row may follow, raised where they happen and answered by the main
    /// window's Inbox (MainWindow.Inbox.cs). Raising one never shows anything by itself: the row
    /// waits until the moment is over (the conversation surface has closed).
    /// </summary>
    public static class ConversionSignals
    {
        /// <summary>The companion refused a message for the daily cap. Raised from the
        /// conversation page; the row is filed only after that page is gone.</summary>
        public static event Action? CompanionCapHit;

        public static void NoteCompanionCap()
        {
            try { CompanionCapHit?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("[Inbox] companion cap signal failed: {E}", ex.Message); }
        }
    }
}
