# fin4-shell-marquee PR-body draft

- Restore the WPF banner dimensions (31px high, maximum 744px wide, 14px text) and the support hyperlink.
- Respect motion Off in the opacity crossfade. Port Full drum roll, Reduced pop, ring flash and glow flare, throttled/forced sheen, staggered support sparkles, Full-only repeat and glow breath. Use the existing shared frame clock; no infinite Avalonia animation or animated Effect. Stop and reset on close, deactivation, minimize or motion Off.
- Add the One Account link's localized clipboard/no-browser prompt and register its dismissal with panic. Retire the beat on click as WPF does, including the fallback case.
- Add focused tests and numbered manual WPF steps. Keep `shell-marquee` **stub**, with the remaining dependencies named below.
- No trunk code imported. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; existing head files won. Behavior adapted from WPF at base `6de330ff78307db3d57cd732506f6d27ee104f87`.

## Gap analysis at the pinned base

The base's ledger and partials lack these WPF behaviors:

1. `ShowWhatsNewIfNeeded` (`MainWindow.Marquee.cs:290-394`): fresh-install version stamping; changed-version patch notes; priority-30 modal presentation; post-dialog stamping. Avalonia's existing WhatsNewDialog is reachable manually, but StartupLadder implements only passive surfaces, not modal ownership/order. Remains missing. A correct port must also coordinate first-run, upgrade tour, mod picker and updater participants rather than show a competing popup from OnLoaded.
2. `TryPresentSeasonRecap` (`:265-288`): server-confirmed monthly key, snapshot-before-rollover and reset acknowledgement. It is silent bookkeeping now, not a startup recap modal. Core has a snapshot store but lacks the rollover service and its tracking/sync publication; remains missing. WPF's service also depends on achievements and leaderboard state, so stamping the month alone would lose data.
3. `ShowOgWelcomePopup` (`:153`): dead code. `rg -n ShowOgWelcomePopup ConditioningControlPanel --glob '*.cs'` finds only the definition at this SHA. No new welcome popup is added; this is not a P122 promotion of the row.
4. `BannerRotationTimer_Tick`, `MainWindow.BannerFx.cs` and `SweepBannerSheen`: missing motion gates, drum/pop, flash/flare, support particles/repeat, breath, and 20-second sheen throttle. Implemented here using the head's shared clock and motion/performance policy. The 750ms sheen and 500ms rotation transition keep their WPF timings.
5. `BannerWebLink_Click`: missing no-default-browser clipboard/explanation. Implemented with the existing portable launcher/shell seam and localized WPF text. Native Windows explorer/cmd/rundll32 recovery strategies are not imported into the cross-platform control.
6. `CheckIntakePassNudge`: base already checks sessions, GameWindow and IntakeHostWindow, but lacks stand-down for the unported Bureau/Loom/For You/Arcademy host lifecycles. Remains missing; a boolean permanently false would not implement those hosts.
7. The base also rendered the support URL as inert text and used pre-polish banner dimensions. Both corrected.

Existing pool draws, server refresh/update feeds, welcome identity fallback and SettingsTab's strip already have live head paths and are retained. This unit prioritizes visible banner behavior within the line cap; the remaining multi-participant startup and season services are explicitly not claimed as wired.

## Verification limits

- `dotnet build -c Release -m:1`: Core, Avalonia and all 11 Linux test projects listed in the workflow compiled with zero errors (existing warnings remain). Generated test outputs were removed between builds to stay within the disk limit.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`, with `DiffEngine_Disabled=true`: exit 0; 2,814 passed, five Vosk tests skipped because the model is unavailable.
- Focused shell filter (BannerMotionTests, BannerPoolShellTests, IntakePassNudgeTests and the existing web-link test): exit 1; all 13 shell tests fail at Segoe UI Black glyph creation before their behavior assertions. They require the supervisor's font-equipped runner; no behavioral pass is claimed.
- The separate font-independent Reduced-to-Off regression was run against the old transition logic: exit 1, `Assert.Null` found a remaining DoubleTransition. After restoring the fix: exit 0, one passed, zero failed.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 existing warnings, zero errors. WPF and Windows code compiled but were not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. Test-plan coverage script is absent on this base; numbered steps were still added.
- The first overlapping builds failed on generated-file contention/worker exit; the first build sweep exhausted disk. Serial builds with per-test output cleanup resolved these infrastructure failures.

Windows runtime behavior and real browser associations cannot be exercised here. Browser-failure tests use the existing isolated profile and external-opening sandbox. No live desktop or real user profile is used.

- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-view MainShellWindow <output>` with an explicit isolated profile: exit 0; 1661x1002 PNG rendered and visually inspected. This is a static support-beat render, not evidence of timing or the other FX states.

## Assumptions

- Use the existing portal-aware launcher and guarded shell seam for browser opening; preserve WPF's final copy-and-explain behavior rather than add Windows-only shell commands to a cross-platform control.
- A sibling Border BoxShadow replaces the WPF DropShadowEffect, including its finite flare. Decorative four-point particle geometry matches WPF and is not an icon.
- Do not invent callers for the dead OG welcome popup, or stamp season state without its snapshot-before-clear service.
