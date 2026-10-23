- Adapted the Core Dose keeper and its tests from trunk commit `4b79a20070a8deccecc5a2a7c63714a470815375` (read-only trunk tip inspected: `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Existing head files were merged, never overwritten.
- Installed the keeper through the real shell: two-second kickoff, engine restart grace, escalating empty-dose refill, session stand-down, starvation tripwire, warden bark, panic restart hold, borrowed-toggle restoration and startup recovery. Automatic starts preserve the user session count.
- Restored mod-aware Lockdown hero/side art, hover and Emergency Exit art. Connected the existing plate pulse to navigation, with a sibling BoxShadow on the shared beat and photosafe/motion gates. Matched Emergency Exit press/release timing to WPF's 60/140 ms.
- Kept exactly one ledger row, honestly `stub`; no WPF source changed and no PR was opened.

## WPF gap analysis
Reference pinned at base `db079bf91474c77a5ea67ec306f6337a7de44b83`: `Views/Tabs/LockdownTabView.xaml(.cs)`, `MainWindow/MainWindow.Lab.cs`, `MainWindow/MainWindow.Animations.cs`, and `Services/Haptics/LockdownDoseKeeper.cs` under the WPF head.
- Already present: premium gate/double warning, duration and quest minimum, settings persistence and greying, clock masking, panels, badge, crimson theme/activation flash, Strict holds, shell refusals, timer/phrase exits and safety recovery. Windows system-key suppression and CC Labs greying were already wired; their ledger gap statements were stale.
- Missing at entry, now implemented: Dose lifecycle/census/picker/graces/restoration/recovery, Dose consent paragraph, side/hero/Emergency Exit bitmaps and mod refresh, hover, the uncalled plate pulse, and WPF press timing.
- Still missing: Possession haunt lifecycle, rung readout/pips, first-time rules and Dose edge attribution. `MainShellWindow.Possession.cs` and `PossessionRuntime.cs` explicitly defer these to **shell-possession**; Core's director exists but this head supplies no live director/host. Showing a fabricated rung or promising possession in consent would misrepresent that dependency.
- Emergency Exit games/verdicts still require a portable game host. The explicit Lockdown/Emergency Exit decision in `docs/avalonia-decisions.md` requires the existing tripwire + Chaster safety hold + phrase-step notice until then. That behavior is preserved. No P122 dead-caller claim is made.

## Verification limits
- `dotnet build -c Release`: Core, Avalonia and all 13 workflow test projects succeeded, exit 0 and zero errors. The WPF test build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; retries also used `-m:1 -p:UseSharedCompilation=false`.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” `git diff --check`: exit 0.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3,596 passed, 5 existing speech tests skipped, 0 failed. Focused `--filter 'FullyQualifiedName~LockdownDoseKeeperTests|FullyQualifiedName~CoreEngineTests'`: exit 0, 48 passed, no skips.
- Regression proof: temporarily restored unconditional `TotalSessions++`; `--filter FullyQualifiedName~AutomaticDoseStartsDoNotCountAsUserSessions` failed, exit 1, expected 0 / actual 1. Restored the fix; focused and full suites then passed.
- Linux test projects are built sequentially; successful generated test outputs are removed between builds to fit the runtime disk quota. The initial batch hit disk exhaustion; retries use the same source and one MSBuild worker.
- Avalonia tests (LockdownTests, LockdownVeilTests, AmbientBeatLoopsTests, PanicSurfacesTests and Win32PanicKeyTests) are compile-only under the supplied cloud font limitation; the supervisor must run these filters on GitHub. No real desktop or real profile was used.
- Windows keyboard-hook behavior is compiled, not executed on Linux. WPF is compile-only with Windows targeting. Vosk model download is unavailable (403), so real-model speech coverage cannot be established here.

## Assumptions
- Preserve the explicit Emergency Exit notice decision and withhold Possession promises until shell-possession supplies its lifecycle.
- Use a distinct Core keeper type name to avoid colliding with the frozen WPF implementation; the host uses the existing wall, engine and bark seams.
- Prefer the enforced Windows system-key behavior already on the base over the stale ledger text. No equivalent global key suppression is promised on Linux.
