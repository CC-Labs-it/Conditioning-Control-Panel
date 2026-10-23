using System;
using Avalonia.Input;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Matches a stored key name (the panic key, any hotkey setting) against an Avalonia <see cref="Key"/>.
/// The settings store <c>Key.ToString()</c>, but several keys share one value ("Enter"/"Return",
/// "CapsLock"/"Capital", "PageDown"/"Next", "Oem3"/"OemTilde" ...) and print as only one of the names,
/// so comparing <c>e.Key.ToString()</c> to the stored text silently misses a key bound by its other name.
/// Parsing the stored name to the enum value compares values, which is alias-aware; the global listeners
/// already do this (Core <c>VirtualKeys.Of</c>, <see cref="X11PanicKey"/>).
/// </summary>
internal static class HotkeyNames
{
    /// <summary>The key a stored name means (case-insensitive, any alias); false for blank, numeric or unknown.</summary>
    internal static bool TryParse(string? name, out Key key)
    {
        key = Key.None;
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0])) return false;   // "6" would parse as Return
        return Enum.TryParse(name, ignoreCase: true, out key) && key != Key.None;
    }

    /// <summary>True when <paramref name="pressed"/> is the key <paramref name="stored"/> names.</summary>
    internal static bool Matches(Key pressed, string? stored) => TryParse(stored, out var k) && k == pressed;

    /// <summary>The stored name as <c>Key.ToString()</c> prints it, so name-based rules (Core
    /// <c>MandatoryVideoScheduler.KeyAction</c>/<c>GraceKey</c>) see the same text for the same key.
    /// Unknown names pass through unchanged.</summary>
    internal static string? Canonical(string? stored) => TryParse(stored, out var k) ? k.ToString() : stored;
}
