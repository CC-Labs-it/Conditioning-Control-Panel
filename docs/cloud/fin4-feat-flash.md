# fin4-feat-flash

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; cloud local branch name was `work`.
- Added WPF-order Rounded corners and Draggable rows, settings persistence/reload, and motion-prize ownership visibility. Either Drift or Pendulum ownership unlocks these controls, independently of the selected motion.
- Applied existing Core `FlashCorners` to the picture and white exit plate, leaving the sibling glow outside the clip.
- Connected pointer capture to existing Core `FlashDrag` and the head's shared motion clock: short left tap pops on release, right press pops immediately, dragging preserves the grab offset, stale releases place, quick releases fling, Reduced halves velocity, Off permits placement only, and release uses the monitor work area. Positions feed the active overlap list. Capture loss cancels the grab. Existing panic/CloseAll owns all these windows and stops their clock.
- Accepted ownership snapshots notify the attached card; rebuilding preserves the saved motion choice. Added focused `FlashV2InteractionTests` alongside existing flash suites.
- Replaced the duplicated historical prose within the single `feat-flash` ledger row with current behavior and an explicit Missing list; retained `stub`. Added numbered manual checks.
- Reference source: WPF at the pinned base above. `parity/trunk` was fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported or overwritten.

## WPF gap audit

The cited WPF feature XAML/code-behind and live `Services/Flash/FlashService.cs` callers were compared with the base head. Historical row claims of missing clicks, hydra, XP, static glow, content-pack pictures, exit/stay controls and DND are stale: those paths already exist on this base.

| WPF behavior missing on entry | Result |
| --- | --- |
| Rounded-corner control and rendered clip | Added, Core radius including the glow fallback |
| Draggable control, live setting/ownership check, grab/tap/place/fling/right-click | Added using Core physics and pointer capture |
| GrantsChanged card rebuild | Added for the available accepted ownership-snapshot source; broader profile-sync ownership remains a separate service gap |
| Pendulum choice, rotation and separated live rigs | Still missing; Core maths exists, head renderer does not |
| Shatter dismiss and exit-picker explanatory note | Still missing; Core maths exists, shard rendering does not |
| Jackpot Remix toggle and director | Still missing |
| Mod hero/side artwork and ModChanged repaint | Still missing |
| Whisper shuffle pool, playback, duck/unduck, sound-dependent duration/XP | Still missing |
| Animated GIF/WebP, speed scaling, gentle speed, video clip slots | Still missing; current decode displays the first frame |
| Remote image/clip pools and specific-image delivery | Still missing; local disk/content-pack picture selection already exists |
| Gaze pop/linger, avatar announce and click reactions | Still missing |
| Lucky pulsing halo/toast, Natasha dodge and booking | Still missing; lucky XP/chime and static glow already exist |
| Stop/disable closes active and queued bursts | Still missing for engine Stop/disable; panic/shell shutdown already cancel and close |
| Solid/shared compositor host delivery and associated edge cases | Still missing; this head uses individual overlay windows |

These are live WPF behaviors, so P122 does not justify marking the row wired. This change prioritizes the direct card/interaction gaps and reuses the existing window host; it does not claim to finish the remaining renderer, media and companion integrations. Completing that whole surface with focused tests requires a further unit rather than declaring the row wired prematurely.

## Verification limits

All window tests use the suite's isolated `CCP_USERDATA_DIR`; no real desktop/profile was used. Native Windows overlay code can be compiled here but cannot be executed on Linux. No live multi-monitor pointer capture or native desktop rendering check was possible.

## Assumptions

- Retain the existing per-window renderer and shared drift clock; use Avalonia pointer capture in place of WPF's compositor mouse hook. No second host or interface was introduced.
- Retain first-frame media behavior and the honest stub status while the larger media/rendering integrations remain outstanding.
- Existing panic registration covers this work because it creates no new surface type; tests close held/flung flashes through that same `CloseAll` path.

## Commands and results

- .NET channel install: failed on the channel lookup (403); pinned SDK `10.0.100` installation succeeded.
- `dotnet build -c Release` for `CCP.Core`, `CCP.Avalonia` and every Linux-compatible test project named in `.github/workflows/build.yml`: all succeeded, zero errors. The final explicit `Tests/CCP.Avalonia.Tests` build returned 0 with zero warnings/errors. Initial implementation/test compile errors were corrected before the final build.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. `test-plan-coverage.sh` is absent on this pinned base; numbered coverage was added manually.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3951 passed, 2 failed, 5 skipped (3958 total). Failures were `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, got Clips) and `BoardTilesTests.A_frame_fits_the_budget` (14.93 ms). Neither area was modified. Re-running those two tests in isolation with `--no-build --filter` returned 0: 2 passed. The full-suite failure remains reported; Vosk tests were skipped by their existing model-availability guard.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~FlashV2InteractionTests|FullyQualifiedName~FlashPopTests|FullyQualifiedName~FlashDriftTests|FullyQualifiedName~FlashWireTests'`: exit 1; 27 passed, 3 failed, 0 skipped (30 total). All three failures are the documented environment error `Could not create glyphTypeface`, family Inter, during card/window layout. They affect the existing Drift/Pop card tests and the new attached-card ownership test. The final rerun including the right-press-while-left-held assertion produced the same 27 passed / 3 font failures. The new corner, pointer, placement/fling, stale-release and cleanup tests passed. No tests were weakened or skipped to bypass fonts.
- `git diff --check`: exit 0.

`dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 warnings, 0 errors. Windows tests/native desktop behavior were not executed. The new ownership-layout test cannot supply before/after runtime proof here because font creation blocks its first layout on both implementations; GitHub validation remains necessary.
