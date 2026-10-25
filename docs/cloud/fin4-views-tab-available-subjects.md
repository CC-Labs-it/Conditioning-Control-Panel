# fin4: Lobby discovery and Chess doors

- Added a portable LobbyService using the existing IUiTimer seam, LobbyPollPlan, LobbyMerge, Chess and Goon listing clients. The actual tab owns a lease while visible, shares overlapping rounds, polls every 10 seconds, doubles failures to 60 seconds, avoids signed-out requests, and releases/disposes polling on hide/detach.
- Adapted the WPF Remote directory list wire into Core; sends caller identity separately from subject identity, skips malformed entries and retains the last successful list on error. It honours the existing LobbyWire sandbox endpoint policy.
- Added Host/Join/Watch Chess intents through the existing GameWindow, including post-identity ordering, fresh-spectator settings and existing-board reuse. The board already participates in the panic registry; no new window or notification surface was introduced.
- Added Fluent host locks and entitlement-event repaint, including immediate row clearing on sign-out. Added focused Core, page lifecycle, gate and Chess protocol tests; the page tests use the existing bundled font registration.
- Replaced the stale duplicate-history ledger description with one honest `stub` row and added numbered tester steps.
- Source: adapted WPF files at pinned base `b6d389a871b8ef5ade86e49c4935b4c5b62d5798`. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported and no existing file was overwritten from trunk.

## WPF gap inventory at the pinned base

The cited view delegates to MainWindow.Lobby; its live callers are reachable from the tab's row and host buttons. P122 does not apply.

1. Watched LobbyService polling, shared in-flight rounds, signed-out suppression, error state/backoff and Chess/Goon/Remote/friend merging: implemented for the page.
2. Shared page/rail/launcher watcher leases, Social open-count badge and launcher dropdown snapshot feed: still missing. This slice owns a page service, not an application-wide producer.
3. Chess host, join and public-match spectate, input validation, reuse of the live board, identity ordering and fresh-spectator settings: implemented.
4. Goon Host and Join via GoonHostService: still missing. The WPF host alone is 2,151 lines, before its game surface/media/session integrations. A correct port cannot fit the remaining added-line allowance. Core listing and game rules already exist; the Avalonia web host does not.
5. Remote claim: still missing. Requires the AvailableSubjects claim wire and MainWindow.RemoteControl claim browser-launch/toast/error/refresh flow. Directory listing is implemented; the Remote consent host path was already wired and remains unchanged.
6. Account/tier/free-day host lock/opacity repaint and sign-out clearing: implemented using the existing shell entitlement refresh path. Goon host and Goon/Remote row doors remain disabled until their hosts land.
7. Feature art, card depth/plank styles, hover sheen/lift/press and entrance FX: still missing. WPF uses reduced/off motion gates, first-fill stagger, then only new identities pop over 320ms and emit a 520ms ring when particles are allowed. No extra ambient animation loop was added.

## Verification limits

- SDK: .NET 10.0.100. Channel installation could not discover the version through the network policy; pinned installation succeeded.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and every Linux test project in build.yml succeeded (exit 0, zero errors): CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Snapshot.Tests.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final full run exit 0; 3,965 passed, five existing Vosk skips. Initial run had one unrelated DtrhRemoteMediaTests failure (expected Loops, got Clips); that case passed alone and the full rerun passed. No tests were changed to suppress it.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~Lobby`: exit 0; 34 passed, zero skipped.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~LobbyPageTests|FullyQualifiedName~GameWindowTests'`: exit 0; 31 passed, zero skipped. A separate focused page/Chess-intent run also passed all 12 cases. Bundled-font registration allowed headless execution in this snapshot; all UI tests used the existing sandbox dispatcher/profile.
- Gate regression proof: temporarily removed the host-lock assignments and shell entitlement repaint hook; the two new gate tests both failed (exit 1). Restored the fixes before the passing runs.
- Windows test compilation with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -m:1 -nodeReuse:false -p:UseSharedCompilation=false`: exit 0, zero errors. Initial shared-compiler attempt exited with MSB4166; a stalled shared-compiler retry was stopped, and the non-shared retry succeeded. WPF runtime and Windows behavior were not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. Test-plan coverage script does not exist on the pinned base; numbered WPF/Rows tester steps were still added.
- Production-server interactions, actual browser-embedded Chess play and `--render-all` were not run. Protocol tests exercised actual game-host frames and panic close through headless fake transport, not a live online match.

## Assumptions

- Preserve WPF behavior and the smallest diff by reusing Core merge/wires, the existing timer seam and panic-registered game host.
- Keep the ledger `stub`: discovery and Chess are user-reachable, but the remaining Goon/Remote doors, shared feeds and card presentation do not have complete WPF parity.
- Keep Goon/Remote actions disabled rather than send users into an incomplete host. The dependency-only NOOP rule does not apply because this branch closes independent live-discovery, Chess and gate gaps.
