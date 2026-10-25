using System;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>Canonical face classification from WPF EmiFace / face.js, shared by both heads.</summary>
public static class EmiFaceRules
{
    // Canon lists also cover arbitrary caller text, not only chain frames.
    private static readonly string[] FlatSet =
    {
        "._.", "^_^", "^_~", ">.<", "@_@", "-_-", "o_o", "T_T", ">_<", "=_=", "\u00AC_\u00AC",
        "^___^", "x_x", "*_*", "0_0", ";_;", "(\u25C9_\u25C9)", "(\u2299_\u2299)", "(\u25D4_\u25D4)"
    };

    private static readonly string[] SideSet =
    {
        ":)", ":D", ";)", ":'(", ">:(", ":O", ":P", ":|", "<3", "XD", ":3", ">:)", ":/", "B)"
    };

    private static readonly string[] KaoSet =
    {
        "( \u0361\u00B0 \u035C\u0296 \u0361\u00B0)", "(\u00AC\u203F\u00AC)", "(\u25E0\u203F\u25E0)",
        "(\u2310\u25A0_\u25A0)", "(\u0CA0\u203F\u0CA0)", "(\u2716\u256D\u256E\u2716)",
        "(\u273F\u25E1\u203F\u25E1)", "(\u25D5\u203F\u25D5)", "(\u0CA5_\u0CA5)",
        "(\uFF61\u2665\u203F\u2665\uFF61)", "(\u2267\u25E1\u2266)"
    };

    private static readonly string[] SpecialSet =
    {
        "\\o/", "GG", "#ERR", "ZzZ", "!!!", "???", "LV UP", "6.7", "\u2665\u2665\u2665",
        "\u2605\u2605\u2605", "404", "brb"
    };

    private static readonly Regex SideRe =
        new(@"^[>]?[:;=8xXB][-'^]?[)(DPOop|/\\3]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NonAsciiRe =
        new(@"[^\x00-\x7F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True for a short classic ASCII face that rotates 90 degrees.</summary>
    public static bool IsSide(string? t) => !string.IsNullOrEmpty(t) && t!.Length <= 4 && SideRe.IsMatch(t);

    /// <summary>True for a kaomoji (the picked list, or any long non-ASCII face).</summary>
    public static bool IsKao(string? t)
    {
        if (string.IsNullOrEmpty(t)) return false;
        if (Array.IndexOf(KaoSet, t) >= 0) return true;
        return Array.IndexOf(FlatSet, t) < 0
            && Array.IndexOf(SideSet, t) < 0
            && Array.IndexOf(SpecialSet, t) < 0
            && NonAsciiRe.IsMatch(t!)
            && t!.Replace("\u00AC", string.Empty).Length >= 5;
    }

}
