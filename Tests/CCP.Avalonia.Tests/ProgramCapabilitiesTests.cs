using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// programs-3a decision (docs/avalonia-decisions.html; evidence/oracle/programs-3a-canenroll.md tests 4-5):
/// the head's static capability table decides which built-in programs it can ever finish.
/// </summary>
public sealed class ProgramCapabilitiesTests
{
    /// <summary>What each program still needs on this head. A new program fails until classified here;
    /// 3b (rituals + the seeded roadmap) emptied presentation and the_takeover.</summary>
    private static readonly Dictionary<string, string[]> Needs = new()
    {
        ["first_week"] = new string[0],
        ["presentation"] = new string[0],
        ["the_takeover"] = new string[0],
        ["kept"] = new string[0],
        ["firmware_install"] = new string[0],
    };

    [Fact]
    public void EveryBuiltInProgramIsClassifiedAndOnlyTheFinishableOnesAreEnrollable()
    {
        var oldKeyword = ProgramCapabilities.KeywordInputAvailable;
        ProgramCapabilities.KeywordInputAvailable = () => true;   // a head with a key source (Windows hook)
        try { AssertClassified(); }
        finally { ProgramCapabilities.KeywordInputAvailable = oldKeyword; }
    }

    private static void AssertClassified()
    {
        var all = BuiltInPrograms.All();
        Assert.Equal(Needs.Keys.OrderBy(k => k), all.Select(p => p.Id).OrderBy(k => k));
        foreach (var p in all)
        {
            var missing = ProgramService.UnavailableTasks(p, ProgramCapabilities.IsAvailable)
                .Select(t => t.Kind == ConditioningControlPanel.Models.Program.ProgramTaskKind.Ritual ? "Ritual" : t.Verifier.ToString())
                .Distinct().OrderBy(s => s);
            Assert.True(Needs[p.Id].OrderBy(s => s).SequenceEqual(missing), $"{p.Id}: {string.Join(",", missing)}");
        }
        Assert.Equal(new[] { "first_week", "presentation", "firmware_install", "the_takeover", "kept" },
            all.Where(p => ProgramService.UnavailableTasks(p, ProgramCapabilities.IsAvailable).Count == 0).Select(p => p.Id));
    }

    /// <summary>No key source (Linux, VR): keyword programs stay refused, or the run lapses on tasks nobody can credit.</summary>
    [Fact]
    public void KeywordProgramsAreRefusedWithoutAKeySource()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-programs-keyword-").FullName;
        var (oldKeyword, oldTask, oldPremium) =
            (ProgramCapabilities.KeywordInputAvailable, CoreProgram.TaskAvailableProvider, CoreProgram.HasPremiumProvider);
        try
        {
            ProgramCapabilities.KeywordInputAvailable = () => false;
            CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
            CoreProgram.HasPremiumProvider = () => true;
            using var svc = new ProgramService(Path.Combine(dir, "programs.json"), readOnly: true);
            foreach (var p in svc.Library)
            {
                var keyword = p.Id is "kept" or "firmware_install";
                Assert.Equal(!keyword, svc.CanEnroll(p, out var reason));
                if (keyword) Assert.Equal(ProgramService.UnavailableReason(p, ProgramCapabilities.IsAvailable), reason);
            }
        }
        finally
        {
            (ProgramCapabilities.KeywordInputAvailable, CoreProgram.TaskAvailableProvider, CoreProgram.HasPremiumProvider) =
                (oldKeyword, oldTask, oldPremium);
            Directory.Delete(dir, true);
        }
    }

    /// <summary>The default gate reads the real key source: a sandbox profile (Sync never subscribes there) and a
    /// failed hook install both refuse keyword tasks, also on Windows.</summary>
    [Fact]
    public void KeywordGateFollowsTheSandboxAndTheHookInstall()
    {
        var keywordTask = BuiltInPrograms.All().SelectMany(p => p.Chapters).SelectMany(c => c.Days).SelectMany(d => d.Tasks)
            .First(t => t.Verifier == QuestCategory.KeywordTrigger);
        Assert.False(KeywordTriggerHead.KeyboardSourceAvailable(true, "/tmp/ccp-sandbox", () => true));
        Assert.False(KeywordTriggerHead.KeyboardSourceAvailable(true, null, () => false));
        Assert.False(KeywordTriggerHead.KeyboardSourceAvailable(false, null, () => true));
        Assert.True(KeywordTriggerHead.KeyboardSourceAvailable(true, null, () => true));

        var (oldGate, oldFailed) = (ProgramCapabilities.KeywordInputAvailable, Win32PanicKey.InstallFailed);
        try
        {
            ProgramCapabilities.KeywordInputAvailable = KeywordTriggerHead.KeyboardSourceAvailable;
            Win32PanicKey.InstallFailed = false;
            // This process runs in a CCP_USERDATA_DIR profile (TestUserDataProfile), so no key source on any OS.
            Assert.False(string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")));
            Assert.False(ProgramCapabilities.IsAvailable(keywordTask));
        }
        finally { (ProgramCapabilities.KeywordInputAvailable, Win32PanicKey.InstallFailed) = (oldGate, oldFailed); }
    }

    [Fact]
    public void StartupRolloverWaitsForConfirmedKeyboardHook()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-program-hook-").FullName;
        var path = Path.Combine(dir, "programs.json");
        var (oldKeyword, oldTask, oldPremium) =
            (ProgramCapabilities.KeywordInputAvailable, CoreProgram.TaskAvailableProvider, CoreProgram.HasPremiumProvider);
        var now = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Local);
        var attempts = 0;
        try
        {
            CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
            CoreProgram.HasPremiumProvider = () => true;
            ProgramCapabilities.KeywordInputAvailable = () => KeywordTriggerHead.KeyboardSourceAvailable(true, null, () => true);
            using (var svc = new ProgramService(path, readOnly: false, now: () => now))
            {
                Assert.NotNull(svc.Enroll(svc.Library.First(p => p.Id == "kept")));
                svc.Save();
            }

            now = now.AddDays(7);
            ProgramCapabilities.KeywordInputAvailable = () => KeywordTriggerHead.KeyboardSourceAvailable(true, null, () => { attempts++; return false; });
            using var reopened = new ProgramService(path, readOnly: false, now: () => now);
            Assert.True(attempts > 0);
            Assert.NotNull(reopened.State.Active);
            Assert.Equal(ProgramEnrollmentState.Active, reopened.State.Active!.State);
            Assert.Equal(1, reopened.State.Active.CurrentDay);
            Assert.Equal(4, reopened.State.Active.DaysOffRemaining);
        }
        finally
        {
            (ProgramCapabilities.KeywordInputAvailable, CoreProgram.TaskAvailableProvider, CoreProgram.HasPremiumProvider) =
                (oldKeyword, oldTask, oldPremium);
            Directory.Delete(dir, true);
        }
    }

    /// <summary>The QuestService call that raises each category.</summary>
    private static readonly Dictionary<QuestCategory, string> TrackCall = new()
    {
        [QuestCategory.KeywordTrigger] = "TrackKeywordTrigger",
        [QuestCategory.Flash] = "TrackFlashImage",
        [QuestCategory.Video] = "TrackVideoMinutes",
        [QuestCategory.Spiral] = "TrackSpiralMinutes",
        [QuestCategory.PinkFilter] = "TrackPinkFilterMinutes",
        [QuestCategory.Bubbles] = "TrackBubblePopped",
        [QuestCategory.LockCard] = "TrackLockCardCompleted",
        [QuestCategory.BubbleCount] = "TrackBubbleCountCompleted",
        [QuestCategory.Mantra] = "TrackMantraCompleted",
        [QuestCategory.Autonomy] = "TrackAutonomyMinutes",
        [QuestCategory.Lockdown] = "TrackLockdownCompleted",
        [QuestCategory.BlinkTrainer] = "TrackBlinkTrainerBlink",
    };

    [Fact]
    public void EveryRaisedCategoryHasAReachableTrackCall()
    {
        var code = string.Join("\n", new[] { "CCP.Avalonia", "CCP.Core" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot(), d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && Path.GetFileName(f) != "QuestService.cs")
            .Select(f => Code(File.ReadAllText(f))));

        foreach (var category in ProgramCapabilities.Raised)
        {
            Assert.True(TrackCall.TryGetValue(category, out var call), $"{category} has no Track call mapped");
            Assert.True(Regex.IsMatch(code, $@"\bQuests\??\.{call}\s*\("), $"{category}: no {call}( call in the head or Core");
        }
    }

    /// <summary>The hooks that make those calls fire: both overlays start/stop their minute tick when their
    /// windows open and close, and the Lockdown credit is subscribed at startup (WPF App.xaml.cs:3295).</summary>
    [Fact]
    public void OverlayAndLockdownHooksAreWired()
    {
        string Src(params string[] p) => Code(File.ReadAllText(Path.Combine(new[] { RepoRoot(), "CCP.Avalonia" }.Concat(p).ToArray())));
        foreach (var overlay in new[] { "PinkFilterOverlay.cs", "SpiralOverlay.cs" })
        {
            var src = Src("Views", "Overlays", overlay);
            Assert.Matches(@"QuestMinutes\.Follow\(Windows\.Count > 0\)", src);
            Assert.Matches(@"QuestMinutes\.Follow\(false\)", src);
        }
        Assert.Matches(@"LockdownDeactivated \+=[^;]*?TrackLockdownCompleted\(", Src("App.axaml.cs"));

        // The achievement half (WPF AchievementService:226-275, :508): totals, unlocks, continuous spiral reset.
        // Whitespace is stripped first, so reformatting the lambdas does not break the scan.
        string Flat(params string[] p) => Regex.Replace(Src(p), @"\s+", "");
        Assert.Matches(@"QuestMinutes=new\(m=>\{App\.Achievements\?\.TrackPinkFilterMinutes\(m\);", Flat("Views", "Overlays", "PinkFilterOverlay.cs"));
        Assert.Matches(@"QuestMinutes=new\(m=>\{App\.Achievements\?\.TrackSpiralMinutes\(m\);[^;]*;\},\(\)=>App\.Achievements\?\.ResetContinuousSpiral\(\)\)", Flat("Views", "Overlays", "SpiralOverlay.cs"));
        Assert.Matches(@"App\.Achievements\?\.TrackLockCardCompletion\(completionTime,_totalErrors,_requiredRepeats\)", Flat("Views", "Windows", "LockCardWindow.axaml.cs"));
        Assert.Contains("Platform.ChasterHead.Service?.Note(\"typo\", _totalErrors); Platform.ChasterHead.Service?.Note(\"lockcard\");",
            File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Windows", "LockCardWindow.axaml.cs")));
    }

    // Strips comments and string literals (P23), as ShellInitializerScanTests does.
    private static string Code(string src) =>
        Regex.Replace(src, @"//[^\n]*|/\*.*?\*/|'(?:[^'\\]|\\.)'|[$@]{0,2}""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
