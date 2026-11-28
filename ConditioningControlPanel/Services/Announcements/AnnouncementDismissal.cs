using System;

namespace ConditioningControlPanel.Services.Announcements
{
    /// <summary>
    /// Answers a SERVER announcement for good: the local slot (<c>DismissedAnnouncementId</c>) and,
    /// when signed in, the per-account record, so the same news does not greet the player again on
    /// this PC or the next one. The bookkeeping the Inbox row and the promo card share with
    /// AnnouncementPopup's own dismiss. Local recurring popups that borrow a window keep their own
    /// records and never call this. Never throws.
    /// </summary>
    internal static class AnnouncementDismissal
    {
        public static void Record(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                s.DismissedAnnouncementId = id;
                App.Settings?.Save();

                // Fire-and-forget; the call never throws and logs its own failures at Debug.
                if (!string.IsNullOrEmpty(s.UnifiedId))
                    _ = App.ProfileSync?.DismissAnnouncementAsync(id);
            }
            catch (Exception ex) { App.Logger?.Debug("Announcement dismiss: {E}", ex.Message); }
        }
    }
}
