using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Tests;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Pins the WPF references for unreachable surfaces at 74ac6ce3d. A changed count
/// requires reviewing the caller and porting it if live. Strings remain so reflection and XAML
/// references count. These guards do not establish visual parity or change row status.</summary>
public sealed class WpfDeadSurfaceTests
{
    // row, pattern, every WPF file (relative to ConditioningControlPanel/) allowed to match, and how often.
    private static readonly (string Row, string Pattern, (string File, int Count)[] Allowed)[] Dead =
    {
        // Pin each step from the unsubscribed quick-login handlers through the account helpers to the dialog.
        ("dialogs-username-picker", @"\bBtnQuickPatreonLogin_Click\b", new[] { ("MainWindow/MainWindow.AccountShell.cs", 1) }),
        ("dialogs-username-picker", @"\bBtnQuickDiscordLogin_Click\b", new[] { ("MainWindow/MainWindow.AccountShell.cs", 1) }),
        ("dialogs-username-picker", @"\bHandleQuickPatreonLoginAsync\b", new[] { ("MainWindow/MainWindow.AccountShell.cs", 2) }),
        ("dialogs-username-picker", @"\bHandleDiscordLoginAsync\b", new[] { ("MainWindow/MainWindow.AccountShell.cs", 2) }),
        ("dialogs-username-picker", @"\bHandlePostAuthV2Async\b",
            new[] { ("MainWindow/MainWindow.AccountShell.cs", 2), ("Services/Account/AccountService.cs", 2) }),
        ("dialogs-username-picker", @"\bUsernamePickerDialog\b",
            new[] { ("Dialogs/UsernamePickerDialog.xaml", 1), ("Dialogs/UsernamePickerDialog.xaml.cs", 2), ("Services/Account/AccountService.cs", 1) }),

        // Only ShowIfNeeded constructs it, and 6.11.x removed its last caller (MainWindow.xaml.cs:515-521).
        ("dialogs-mod-picker", @"ModPickerDialog\s*\.\s*ShowIfNeeded\b", new (string, int)[0]),
        ("dialogs-mod-picker", @"\bShowIfNeeded\b",
            new[] { ("Dialogs/ModPickerDialog.xaml.cs", 2), ("Dialogs/WelcomeDialog.xaml.cs", 1) }),
        ("dialogs-mod-picker", @"\bModPickerDialog\b",
            new[] { ("Dialogs/ModPickerDialog.xaml", 1), ("Dialogs/ModPickerDialog.xaml.cs", 3), ("Windows/FirstRunWizard.xaml.cs", 1) }),

        // Scrapped pre-ship: App constructs the service for BarkService's events and nothing starts it.
        ("ctrl-attention-check", @"AttentionCheck\??\.(?:Start|FireNow)\s*\(", new (string, int)[0]),
        ("ctrl-attention-check", @"\bAttentionCheckControl\b",
            new[] { ("Controls/AttentionCheckControl.xaml", 1), ("Controls/AttentionCheckControl.xaml.cs", 2), ("Services/AttentionCheckService.cs", 2) }),
        ("ctrl-attention-check", @"\bAttentionCheckService\b", new[] { ("App.xaml.cs", 2), ("Services/AttentionCheckService.cs", 3) }),
        ("ctrl-attention-check", @"App\.AttentionCheck\b", new[] { ("Services/Companion/BarkService.cs", 5) }),
    };

    public static TheoryData<string, string> Symbols()
    {
        var data = new TheoryData<string, string>();
        foreach (var d in Dead) data.Add(d.Row, d.Pattern);
        return data;
    }

    [Theory]
    [MemberData(nameof(Symbols))]
    public void WpfHasNoLiveCallerFor(string row, string pattern)
    {
        var expected = Dead.Single(d => d.Row == row && d.Pattern == pattern).Allowed
            .ToDictionary(a => a.File, a => a.Count);
        var regex = new Regex(pattern);
        var actual = WpfSources()
            .Select(f => (f.File, Count: regex.Matches(f.Code).Count))
            .Where(f => f.Count > 0)
            .ToDictionary(f => f.File, f => f.Count);

        var diff = actual.Keys.Union(expected.Keys)
            .Where(k => actual.GetValueOrDefault(k) != expected.GetValueOrDefault(k))
            .Select(k => $"{k}: {actual.GetValueOrDefault(k)} (pinned {expected.GetValueOrDefault(k)})")
            .ToList();
        Assert.True(diff.Count == 0,
            $"{row}: WPF references to /{pattern}/ changed, so WPF may have a live caller now. Re-grep; if it does, " +
            "review the caller and port it if live. " + string.Join("; ", diff));
    }

    // Comments go, strings stay: nameof/Type.GetType("...")/log text are references a caller could use.
    private static readonly Regex CsComments = new(
        @"(@""(?:""""|[^""])*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])')|//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex XamlComments = new(@"<!--.*?-->", RegexOptions.Singleline);

    private static List<(string File, string Code)>? _wpf;

    private static List<(string File, string Code)> WpfSources() => _wpf ??=
        SourceRoots.EnumerateProductSources("*.cs").Concat(SourceRoots.EnumerateProductSources("*.xaml"))
            .Select(p => (Rel: SourceRoots.Relative(p), Path: p))
            .Where(f => f.Rel.StartsWith("ConditioningControlPanel/"))
            .Select(f => (f.Rel.Substring("ConditioningControlPanel/".Length),
                f.Rel.EndsWith(".xaml")
                    ? XamlComments.Replace(File.ReadAllText(f.Path), "")
                    : CsComments.Replace(File.ReadAllText(f.Path), m => m.Groups[1].Value)))
            .ToList();
}
