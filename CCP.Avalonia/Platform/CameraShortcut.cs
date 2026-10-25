using System;
using System.Threading;
using Avalonia.Input;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

// Both global listeners read settings per press; the focused route uses the same matcher.
internal static class CameraShortcut
{
    internal static Action? Pressed;
    private static int _held;
    internal static bool TryPress() => Interlocked.Exchange(ref _held, 1) == 0;
    internal static void Release() => Interlocked.Exchange(ref _held, 0);
    internal static KeyGesture Gesture
    {
        get
        {
            var s = CoreSettings.Current.CompanionPrompt;
            var key = HotkeyNames.TryParse(s?.CameraShortcutKey, out var parsed) ? parsed : Key.K;
            var text = (s?.CameraShortcutModifiers ?? "Control,Alt").Replace("Windows", "Meta");
            var mods = Enum.TryParse<KeyModifiers>(text, true, out var m) ? m : KeyModifiers.Control | KeyModifiers.Alt;
            return new KeyGesture(key, mods);
        }
    }
    internal static bool Matches(Key key, KeyModifiers mods) => Gesture.Key == key && Gesture.KeyModifiers == mods;
    internal static void Route(Key key, KeyModifiers mods)
    {
        if (CoreSettings.Current.CompanionPrompt?.CameraShortcutGlobal != false && Matches(key, mods) && TryPress()) Pressed?.Invoke();
    }
}
