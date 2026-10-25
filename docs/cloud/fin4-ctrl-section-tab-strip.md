# fin4-ctrl-section-tab-strip

- Base: `eef2c7e0bea2e4147bdf8889210a0b0d461fbd7f`, checked out clean in the prescribed fallback clone after the snapshot fetch failed read-only. Only the requested unit branch is changed.
- The strip now includes Just Drop in Core table order. It asks before dispatching the window request, using WPF's localized title/body/buttons, placement, hue and active-page behavior. Confirmation uses the existing server-gated GameWindow host; cancellation does not dispatch.
- Escape, light dismissal, rebuild, section change, detach and panic close the confirmation. Added the `nav-window-confirm` panic registry entry.
- Added headless tests for affirmative/negative answers at all motion levels, keyboard window activation, dismissal/cleanup/panic, Settings label rebuilding after language change and moved-note fade timer completion. Updated the existing Studio-order assertion and added numbered manual tester steps.
- Source: frozen WPF reference on the pinned base, especially `SectionTabStrip.Help.cs` (AskBeforeOpening/AnswerConfirm/CloseConfirm), `SectionTabStrip.xaml.cs` (Choose/BuildPills), and `MainWindow.SectionChrome.cs`. Fetched `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` read-only; no code imported from trunk, so no imported trunk commits to credit.

## Gap audit and scope

The initial ledger's remaining gaps were the Just Drop host/card, Companion/Premium routes/pills, edge lift lap + fog/embers, Settings language-rebuild proof, and moved-note fade completion proof. The existing base already has the Just Drop GameWindow and Companion/Premium routes. This unit connects the window to the strip and supplies the missing tests. Premium is intentionally hidden in Core's Home table, exactly as WPF; no Premium pill should be added.

Missing behavior after this unit (row stays `stub`, never promoted to wired):

- Companion personality/permissions/links still land on room anchors instead of WPF's separate pages. These depend on the unported `views-companion-v2-conversation` host and Companion page host. Existing routes are preserved rather than replaced with empty pages.
- Full-motion edge lift's lap, motion/tier-gated edge fog and embers, and fog-dependent band thinning. WPF's `AmbientFxCanvas.EdgeFog.cs` alone is 590 lines, `NavRail/EdgeParticles.cs` 207, and `MainWindow.SectionEdge.cs` adds host/clock logic. An adapted renderer, host, pure motion rules and meaningful tests would exceed the remaining 1000-added-line allowance. Static edge paint is retained.
- The SectionChrome header also identifies depth-painter fan-out and cloud-sync title suffix as pending. Those need the corresponding painters and cloud-profile sync host, outside this strip connection.
- Confirmation fades in over 150 ms above Off, matching the WPF popup fade, and appears immediately at Off. No looping animation/effect was added.

## Verification limits

- .NET SDK 10.0.402 installed in a fresh task directory after the channel installer and direct installer overlapped and left the first runtime incomplete.
- Core and Avalonia Release builds: passed, zero errors (existing warnings).
- All Linux-buildable test projects named in `.github/workflows/build.yml`: compiled with zero errors. Builds were retried sequentially with generated outputs removed between projects after native dependency copies exhausted disk space.
- Focused Avalonia filter: `SectionStripWindowTests`, `SectionTabStripShellTests`, `SectionStripMovedNoteTests`, `SectionStripChoosePressTests`. Compilation passed; execution failed during font initialization (`Could not create glyphTypeface`, Inter ExtraBold), then the fatal testhost was terminated. No behavioral pass or fail-before-fix proof is claimed. GitHub headless verification is still required.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `git diff --check`: passed. Test-plan coverage script does not exist on this base; numbered WPF/Rows entries were supplied anyway.
- No real desktop, live shop/network handoff, Windows native code or WPF UI was exercised. Test profiles use the repository's `CCP_USERDATA_DIR` module initializer.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`, `DiffEngine_Disabled=true`: exit 0; 2811 passed, 5 skipped, 0 failed, 2816 total. Existing speech tests skipped because no Vosk model is available; no test was weakened or disabled.

## Assumptions

- Keep the WPF window pill visible regardless of server door availability; the existing launch gate rechecks availability when confirmed.
- Preserve existing Companion room routes until the separate-page host lands. Keep Premium hidden in Home's table, following WPF.
- Map WPF's standard popup fade to a finite 150 ms opacity transition, disabled at Off.

## Final build results

- Final `dotnet build Tests/CCP.Avalonia.Tests -c Release -m:1`: exit 0, 223 warnings, 0 errors (includes the app and final fade assertion).
- WPF test build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: the initial attempt hit disk exhaustion, a retry hit MSB4166 worker failure, and a serial retry compiled the WPF app but aborted (exit 134) copying content assets when disk filled again. The WPF build is not reported as passed. Generated outputs were removed afterward. No WPF tests ran.
