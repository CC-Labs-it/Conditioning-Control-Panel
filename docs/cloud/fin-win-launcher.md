# fin-win-launcher

- Base: `97ee118a6f5fce03b4c82005a2ffc09e20c94db8`; branch: `avalonia-port/cloud-fin-win-launcher`.
- Compared WPF `LauncherWindow.xaml` and its partials, `LauncherHost`, `LauncherSfx` and `MotionFx` with the existing head. Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; changes adapt the pinned WPF reference and reuse this head's components.
- Added launcher file-drop handoff to the panel importer and a guard against repeated sign-in dialogs. Added current release notes over the launcher, with WPF's settings fallback.
- Reused `FriendsRailChip` and its drawer, adapting settings, sign-in and invite routes for a launcher owner. Registered notes and friends dismissal in `PanicSurfaces`.
- Connected `PrizeGrants.GrantsChanged` to Core's reveal-flip policy, including background delivery, revocation and hidden-window deferral. The production ownership snapshot producer remains unported.
- Added cursor depth parallax, 14 s art drift with hover pause/resume, existing `HoverPop`, opposing tile-art slide, hover title tint, existing locked-card `TierFxBorder`, 40 s wordmark drift, running-dot breath, panel-stat/XP tweens, and tier-badge wobble/pop-in. The launcher clock steps new loops; hide/minimize parks them. Tile glow uses a sibling `Border.BoxShadow`.
- Launcher cues respect output suppression and use the existing stoppable audio seam to cap hover notes at three, cutting the oldest unfinished note.
- Consolidated duplicated `win-launcher` history into one honest `stub` row. Remaining work exceeds this unit's added-line budget; no `wired` or P122 claim is made.

## WPF gap inventory

The stale base row listed media settings and shortcuts as missing despite existing real callers and tests. Its old no-game-host statements are superseded: this head has Breakout demo, Piece by Piece, Racing Thoughts and DtRH destinations. These were preserved.

| WPF behavior absent at the base | Outcome |
| --- | --- |
| Drop files onto launcher, then import on panel | Implemented using the existing importer, retaining its documented limitations. |
| One sign-in dialog at a time | Implemented; refresh after completion preserved. |
| What's new over launcher, settings fallback on failure | Implemented; current head version/notes via `CoreReleaseContent`. |
| Friends chip and drawer routes from launcher | Implemented with the existing chip; its shared component parity limitations remain. |
| GrantsChanged redraw only when reveals flip | Consumer implemented; live Back Room ownership snapshot feed (`svc-prize-ownership`) still missing. |
| Cursor parallax, Ken-Burns art, art hover pop/slide, title tint, locked tier rim | Implemented using WPF values and existing head primitives. |
| Wordmark drift, running-dot breath, stat/XP tweens, tier-badge wobble/popup scale | Implemented; account SP chip count-up still missing. |
| Sparkle trail, every-eighth star, embers ambient layer | Missing. |
| Hover-tile and running-panel perimeter comets; panel rim breath | Missing; existing head primitives can be reused in a later unit. |
| CTA sheen, 6–10 s wandering tile sheen, 25–40 s wordmark sheen | Missing. |
| Tile entrance/rebuild stagger and CTA/Play press squish | Missing. |
| Double-click maximize and explicit restore-under-pointer title drag | Missing; ordinary native drag remains. |
| Immediate engine-stopped status refresh | Missing; existing one-second status tick remains. |
| Panel banner mod resolution, richer artless radial/road plates, generated Breakout covers | Missing; existing art/fallback retained. |
| Three ringing hover voices and suppressed-output silence | Implemented through existing CoreAudio seams; no Core API added. |
| Full catalogue, unrevealed mystery card launching Back Room | Missing Back Room, full Breakout, Arcademy and Goon destinations; hidden rather than offering a dead action. |
| Sky Lobby chip/dropdown, seen-state dimming, polling leases, sorted open tables, Join; Goon tile open-table badge | Missing Lobby service and Goon host integration (`views-tab-available-subjects`, `feat-friends-open-tables`). |
| WPF game-host bridge FX/media/init/fullscreen/heartbeat and per-game handoffs | Remain owned by game-host rows; current destinations and return path preserved. |

## Verification limits

- SDK 10.0.402; `DiffEngine_Disabled=true` for test commands. `dotnet build <project> -c Release -m:1`: exit 0, 0 errors for all 14 required Linux projects below (existing warnings retained).

| Built projects | Result |
| --- | --- |
| `CCP.Core`, `CCP.Avalonia` | Both exit 0, 0 errors. |
| `Tests/CCP.Core.Tests`, `Tests/CCP.Core.Settings.Tests`, `Tests/CCP.Core.Content.Tests` | All exit 0, 0 errors. |
| `Tests/CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests` (all under `Tests`) | All exit 0, 0 errors. |

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -p:RestoreDisableParallel=true -p:UseSharedCompilation=false`: exit 0, 554 existing warnings, 0 errors. Compiled only; WPF cannot run here.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3545 passed, 5 skipped, 0 failed. Existing Vosk tests skip because the model is unavailable (environment download restriction); no tests weakened or skipped by this unit.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~LauncherSoundParityTests`: before fix exit 1 (2 failures: suppressed cue played; stoppable voice count zero); after fix exit 0 (2 passed).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0.
- Added/updated headless coverage in `LauncherWindowTests` and `LauncherFxTests` for real controls, drops, grants, dialog lifetime/panic, shadows, transforms and clock gates. Compile only: this base lacks the cloud-test-fonts lane and window tests fail during font creation here. Their before-fix failure proofs and runtime results require the supervisor's GitHub rcheck. The test assembly uses its existing `CCP_USERDATA_DIR` sandbox.
- No Windows runtime checks, live desktop inspection or render-all execution. Windows shortcut code is unchanged and compiled in Avalonia; it cannot run on Linux.
- The cloud runtime restarted during validation. Workspace edits survived; temporary logs did not. Final checks were restarted sequentially.

## Assumptions

- Keep the ledger `stub` when independent work remains; prioritize reachable controls/data and reusable motion under the 1000-added-line cap.
- Use the current head's release notes and shared friends drawer instead of duplicating either. Their established shared parity limits apply.
- New ambient loops share the launcher frame clock and park with visibility/focus. Art uses existing HoverPop for finite hover tweens. No independent infinite animations.
- Keep hidden games hidden until real destinations exist. Grant revocation removes Racing until Back Room's mystery destination is ported.
