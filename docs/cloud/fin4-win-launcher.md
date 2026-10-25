# fin4: launcher depth and grant refresh

- Ported cursor depth from WPF: 33 ms pointer throttle; 260 ms quadratic ease; glow/pool/ambient follow by 18/14/8 px, spirals move against by 12/8.4 px. Leaving settles; hidden/minimized/deactivated windows park depth.
- Ported tile art depth (7 px against the pointer, 90 ms linear movement, 160 ms eased return), title tint (160 ms), and rounded art clipping. Retargeted tweens continue from their current value.
- Added 14 s sine art zoom legs, capped at 12 fps, paused over art and on deactivation. Reused existing HoverPop on the image; its transform wraps the drift scale. Added 40 s wordmark zoom legs at 10 fps. All ambient drift uses the existing launcher clock and motion/performance gates.
- Replaced tile DropShadowEffect with a sibling Border BoxShadow, retaining WPF hue/blur and 0.35/0.70 alpha endpoints. No new surfaces or panic registrations are needed: the existing launcher surface is retained.
- Connected existing PrizeGrants.GrantsChanged on the UI dispatcher; compare revealed states for available destinations, including hidden race, so both grant and revoke repaint and unchanged syncs preserve tile identity. Unsubscribe on close.
- Added focused LauncherFxTests and numbered WPF tester steps. Replaced the enormous duplicated historical launcher ledger cell with one current stub row and an honest Missing list.
- Source credit: WPF reference at base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Fetched read-only parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files were imported or overwritten.

## WPF gap audit

The base's sole win-launcher row repeated historical evidence and stale Missing lists. Current code takes precedence over those historical claims. WPF reference is pinned to the base SHA above.

Before this unit, the remaining differences were:

1. Backdrop.cs: cursor parallax and its throttle/ease/reset; art Ken-Burns drift, hover pause/resume and performance gating. Implemented here.
2. Tiles.cs: image slide against tilt, art HoverPop, hover title tint; animated TierFxBorder on locked tiles. First three implemented; tier rim remains missing. The art-less WPF radial/road glyph plate remains a simpler hue/glyph fallback here.
3. Fx.cs/Choreo.cs: sparkle pointer trail, every-eighth sparkle star, ember layer; hovered-tile perimeter comet; running-panel comet and rim breath; CTA, wandering and wordmark sheens; wordmark drift; running-dot breath. Only wordmark drift implemented here. Existing open/return bursts, spiral turn, edge effects, launch beats and crossfades remain intact.
4. Choreo.cs/xaml.cs/TierBadge.cs: odometer animations for stats/SP, XP bar tween, 8 s tier wobble and popup scale entrance. Still missing; numeric values and popup content already work.
5. Sound/Sfx: three simultaneous hover voices, stop/dispose oldest voice and audio-output-suppressed probe. Still missing: CoreAudio's one-shot seam returns no handle; speaker toggle/master-volume gating already work.
6. xaml.cs: GrantsChanged refresh. Implemented using the existing ownership service; it was incorrectly described as absent in old ledger text. Engine-stop status still waits up to the existing 1 s tick, rather than WPF's immediate event refresh. Media settings and shortcut controls were already implemented on base and are not gaps.
7. xaml.cs/Lobby.cs/OpenTables.cs/xaml: friends chip, What's New link/dialog entry, Sky Lobby chip with unread dimming, 60 s visible open-table polling, leased 10 s dropdown with six rows/friends-first Join/Open lobby, per-game open-table badges. Still missing from launcher (other head social surfaces exist); no pretend clickable placeholders were added.
8. Tiles.cs/LauncherCatalogue.cs/LauncherHost.cs: mystery card routes to Back Room counter; Back Room, full Breakout, Arcademy and Goon destinations, their gates, active-game handoffs and lobby joins. Their hosts are not registered in this head's GameWindow catalogue. Existing demo/Piece by Piece/race/DtRH/intake paths stay available. Full host/service work plus the remaining FX/social changes and meaningful tests cannot fit this unit's 1,000-added-line limit.

The row remains **stub**, never wired/verified. This is not a pure dependency NOOP: several visible gaps were independent of those hosts and are implemented above. Remaining work is explicitly retained rather than claiming parity from dead-code reasoning (P122 does not apply: WPF has live callers).

## Assumptions

- Keep the base's policy of hiding destinations without a host, including unrevealed race, until a real Back Room counter can receive the mystery card's Play action.
- Adapt WPF storyboards to the head's single launcher frame clock; reuse the existing HoverPop helper for its short entrance rather than inventing another animation framework.
- A sibling BoxShadow approximates WPF's tile effect while following the port's explicit no-Effect-on-tiles rule.
- Preserve current wordmark artwork/content and add only WPF's transform motion.

## Verification limits

Results are recorded below after the final checks. Windows-specific code can compile here but cannot run on this Linux worker. No Windows shortcut, native desktop, or live visual smoke was performed. Headless tests use the existing CCP_USERDATA_DIR sandbox and memory-only SecretStore; audio is captured by a test provider.

The initial LauncherFxTests attempt compiled, then all 22 tests failed before launcher assertions: PremiumSpark could not create the Segoe UI Black glyphTypeface while constructing MainShellWindow. This is the documented worker font limitation. It provides no behavioral pass or valid red/green proof for the grant/shadow fixes; those assertions require the supervisor's font-capable runner. No test was skipped, weakened or deleted to hide this failure.

- `dotnet build <project> -c Release` (exit 0 each): CCP.Core, CCP.Avalonia, CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Avalonia.Tests. Every Linux test csproj named by build.yml was compiled; 0 errors.
- Final Avalonia rebuild: exit 0, 0 errors. Final head-test rebuild initially hit MSB4166 (child MSBuild node exited); retry with `-m:1 -nr:false` passed, exit 0, 0 errors.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors; compiled only, never run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,953 passed, 5 existing Vosk tests skipped, 0 failed (3,958 total). No tests were edited to cause those skips.
- `.github/scripts/test-plan-coverage.sh` is absent on this base; numbered WPF/Rows tester steps were added anyway.
- `git diff --check`: exit 0. No live render-all/native desktop/Windows execution was performed.
- Final focused run: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~LauncherFxTests|FullyQualifiedName~LauncherWindowTests|FullyQualifiedName~LauncherLinksTests'`: exit 1; 5 passed, 50 failed, 0 skipped (55 total). 49 failures report missing Segoe UI Black and one missing Fredoka glyphTypeface before the shell/launcher assertions. New motion/grant assertions remain unvalidated on this worker.
