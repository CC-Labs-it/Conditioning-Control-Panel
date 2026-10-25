// Shared WPF/Avalonia avatar-slot ownership rule (bug #847).
namespace ConditioningControlPanel
{
    /// <summary>
    /// Where the real profile picture stands for the card on screen. The preset avatar and the
    /// picture share one slot, so "no picture" and "no picture YET" have to be distinguishable -
    /// an empty bubble on its own means neither (#847).
    /// </summary>
    internal enum ProfilePictureLoad
    {
        /// <summary>A load is in flight; the empty slot belongs to it and nothing else may take it.</summary>
        Pending,
        /// <summary>A real picture is in the slot.</summary>
        Loaded,
        /// <summary>The load finished with nothing: no avatar, sharing off, or a lookup that failed.</summary>
        None
    }

    /// <summary>
    /// The avatar slot's claim rule, kept as a pure function so it can be reasoned about (and
    /// tested) without standing a Window up. See MainWindow.ApplyProfileAvatarPreset.
    /// </summary>
    internal static class ProfileAvatarSlot
    {
        /// <summary>Whether the preset avatar may write the shared avatar slot right now.</summary>
        internal static bool PresetMayClaim(bool slotHoldsOurPreset, bool slotHasPicture, ProfilePictureLoad load)
        {
            // Already ours: swapping one preset for another, or clearing back to the blank circle.
            if (slotHoldsOurPreset) return true;
            // Someone's real picture is in there. It always wins.
            if (slotHasPicture) return false;
            // Empty - but only a load that has DEFINITIVELY come back with nothing hands it over.
            return load == ProfilePictureLoad.None;
        }
    }
}
