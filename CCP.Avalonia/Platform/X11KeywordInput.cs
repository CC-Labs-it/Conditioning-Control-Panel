using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Converts an already resolved X keysym to the shared keyword input format.
/// XKB must resolve the active group and modifier state before calling this helper.</summary>
internal static class X11KeywordInput
{
    internal static KeywordInputKey? Special(ulong keysym) => keysym switch
    {
        0xff0d or 0xff1b or 0xff09 => KeywordInputKey.Clear, // Return, Escape, Tab
        0xff08 => KeywordInputKey.Backspace,
        0x20 => KeywordInputKey.Space,
        _ => null,
    };

    internal static char? Character(ulong keysym)
    {
        // X11 defines Latin-1 keysyms directly and Unicode keysyms with a 0x01000000 prefix.
        var value = keysym is >= 0x20 and <= 0x7e or >= 0xa0 and <= 0xff
            ? keysym : (keysym & 0xff000000) == 0x01000000 ? keysym & 0x00ffffff : 0;
        if (value > char.MaxValue || value == 0) return null;
        var c = (char)value;
        return char.IsControl(c) || char.IsSurrogate(c) ? null : c;
    }
}
