# fin4-shell-profile-bubble

- Base verified at `85d30ccc4b6cb241d136e8299a0d2e844b88585a` (cloud branch `work`, clean). Work is on `avalonia-port/cloud-fin4-shell-profile-bubble`.
- Added successful-display notifications to the existing flash and subliminal hosts; subscribed/unsubscribed in the shell, dispatching to UI. Notifications do not use the feature-use memory signal, which can fire when no visual was shown.
- Added the WPF flash tilt (90/220/340/480 ms, -12/+9/-5/0 degrees, 2500 ms throttle) and subliminal opacity dip (300 ms each direction, SineEase InOut, 4000 ms throttle). Both use finite BeatLoop runs with no idle frame subscription. Motion Off and shell close reset them, and panic has an explicit registry entry. Queued notifications after close are ignored.
- Reused the existing BackEaseOutAmplitude for the tier hover's exact WPF 0.45 amplitude and retained 260 ms duration, placement and restacking.
- Added real-host tests with a stepped clock for reactions, throttle boundaries, motion gates, close teardown and panic; exact easing sample assertions; numbered manual steps. Ledger remains `stub` with one current row.
- Reference: WPF files at the pinned base above. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing files and shared helpers win.

## Gap inventory and scope

The row's advanced slice already covers the face/preset/photo fallback, rim art, tier hover placement and restacking, 8-second tier wobble, account menu delays and dismissal, account/public-profile/navigation actions, XP/level/achievement reactions and header burst. The remaining gaps on arrival were:

1. Actual FlashDisplayed hook, UI dispatch, 2500 ms throttle and 480 ms wobble — implemented.
2. Actual SubliminalDisplayed hook, UI dispatch, 4000 ms throttle and 600 ms shimmer — implemented.
3. Exact WPF BackEase Out amplitude 0.45 for the tier enlargement — implemented through the existing shared easing.
4. Live spiral row: subscribe to block changes, re-read on menu paint, hide for missing block or migration withhold, apply the glyph, localized Day N · stage name, close menu and enter Spiral Room — still missing.

The last gap is a live dependency, not dead WPF code: InitializeProfileBubble calls WireProfileSpiral; RefreshProfileMenu calls RefreshProfileMenuSpiral; ProfileMenuSpiral_Click calls OpenSpiralMapFromProfile. These are present in the pinned WPF reference. This is not P122 and not a pure dependency NOOP because gaps 1–3 were independently actionable.

The base has Core Descent models/parser/refresh gate and a Spiral Room surface, but no live DescentService. SpiralTabView.Refresh still passes no block and its migration integration lacks live offer/ceremony inputs. The WPF DescentService (357 lines) and DescentMigrationService (448 lines) total 805 lines before adapting fetch/auth/poll/sync/account-reset integration, wiring the two profile doors, integrating the room and adding focused tests. Completing those hosts correctly with this unit's reactions and tests exceeds 1000 added lines. No fake block provider or visible nonfunctional spiral door was added. Follow-up dependency: live DescentService and migration host / shell-profile-spiral.

## Verification limits

- SDK: .NET 10.0.402.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: sequential retry exit 0, 0 errors. An earlier overlapping build returned empty AVLN9999; subsequent sequential builds passed.
- `dotnet build <project> -c Release` for all 12 Linux test projects named in `.github/workflows/build.yml`: all exit 0. This includes Core, Core.Settings, Core.Content, Avalonia, CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler and Snapshot tests. Latest headless test compilation: 0 errors.
- Windows test build attempted with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 1, MSB4166 (child node exited prematurely). WPF tests were not executed.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” The base has no `test-plan-coverage.sh`; numbered coverage was added anyway.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 1, 3741 passed, 2 failed, 5 skipped (3748 total). Failures: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, got Clips) and `BoardTilesTests.A_frame_fits_the_budget` (20.14 ms). Core is unchanged. Five Vosk tests self-skipped for the unavailable model; no tests were modified or disabled.
- Focused new reaction/easing tests after dispatcher-first test setup: exit 0, 6 passed, 0 failed, 0 skipped. Initial filtered runs exposed startup-order affinity in the test class; setup now initializes the head on its dispatcher before reading shell statics.
- Full `--filter FullyQualifiedName~ProfileBubble` run after sequential restoration: exit 0, 18 passed, 0 failed, 0 skipped. An initial overlapping run had 17 passed / 1 gold-ring timing failure; the sequential rerun passed without changing that test.
- Separate retry of the two failed Core cases: exit 1, 1 passed / 1 failed. Remote-media passed; the board-frame budget still failed (31.44 ms). The full Core suite is therefore not green in this environment.
- An initial negative-control build failed with MSB4166 before executing tests; the script restored the implementation and rebuilt successfully. Single-node retry (`dotnet build ... -m:1 -nr:false`) exited 0. With both display notifications removed and easing reverted to the pre-port default amplitude 1.0, the same focused tests exited 1: 4 failed / 2 passed. Failures were the flash tilt (-12 expected, 0 actual), subliminal opacity (0.55 expected, 1 actual), and two interior easing samples. Sources were restored in `finally`; no negative-control change is committed. Final restored single-node build: exit 0, 0 errors. Final full profile test run: exit 0, 18 passed / 0 failed / 0 skipped (1 minute 5 seconds). Windows native popup ordering and visual comparison with WPF cannot be run in this Linux environment. No new Windows-specific code was introduced. No desktop or real user profile is used by the headless tests.

## Assumptions

- Emit a notification per successfully presented subliminal card; multiple monitors are coalesced by the WPF 4000 ms throttle.
- Panic and changing motion Off also reset in-flight header reactions; this extends teardown safety without adding a window.
- Keep `stub` while the service-backed spiral door is unavailable, and complete the independent user-visible gaps first under the line cap.

Final whitespace check: `git diff --cached --check` exited 0. Added lines versus the pinned base: 226 (below the 1000-line cap).
