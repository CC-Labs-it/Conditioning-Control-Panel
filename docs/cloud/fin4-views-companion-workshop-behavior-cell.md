- Added Windows RegisterHotKey companion slots (MOD_NOREPEAT), independent rebind/failure handling, UI dispatch and shutdown cleanup. Startup, both chat editors, camera editor and settings replacement reach the registrations. Existing in-window bindings remain the fallback. Global chat restores the shell/tube; camera shares the consent-aware toggle.
- Added the tube's idle cadence, immediate full-interval slider restart, speech/listening activity resets, tick-time settings refresh, stale-latch recovery and close cleanup. Reused the existing tube-speech panic registration; no new window or notification surface.
- Added shared Core idle bark selection: eligible-rule rotation, 35% gated-rule bias, last-rule exclusion at recycle, persisted inventory of spoken rules and ordinary gate/variant behavior.
- Added focused Core and headless seam tests and numbered manual coverage; kept the ledger row `stub`.
- Imports: none. Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapted from the frozen WPF reference at base `151b7800632cbd90dabf9f2f8dcf699066976885`, preserving existing head files.

## Initial gaps and remaining dependency

At the pinned base the row lacked system-wide chat/camera registration, global foreground chat wake-up, idle-timer startup/restart/reset/tick/cleanup and the corresponding Core idle bark selection, plus live Arcademy wallet reads. The cell also needed its WPF visibility-time ownership refresh, now restored.

Remaining: `ArcademyHostService.WalletOwnsSku` checks its active `_meta` before disk (WPF lines 852-865). This head has no Arcademy purchase/runtime host. The WPF host is 6,091 lines and owns the WebView lesson lifecycle, purchase messages and flush/teardown; inserting an unused live-wallet delegate would not create a real user path. That host cannot be adapted faithfully within this unit's 1,000-added-line budget. Existing persisted-wallet checks remain. The row therefore stays `stub`; this is a partial implementation, not a dependency-only NOOP.

## Verification limits

All test commands used `DiffEngine_Disabled=true`.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release --no-restore` | 0 | 0 warnings, 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-restore` | 0 | 0 warnings, 0 errors (final incremental build) |
| `dotnet build Tests/<project>/<project>.csproj -c Release` for every Linux workflow test project | 0 each | 0 errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 512 existing warnings, 0 errors; compiled, not run |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed |
| `dotnet test Tests/CCP.Core.Tests --no-build -c Release` (final serial run) | 0 | 2,813 passed, 5 skipped, 0 failed |
| `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~BarkEngineTests` | 0 | 10 passed, 0 failed (includes three bias cases added after the full-suite binary was launched) |
| `dotnet test Tests/CCP.Avalonia.Tests --no-build -c Release --filter FullyQualifiedName~SlotsRebindIndependentlyFailureKeepsOtherSlotAndDisposeUnregisters` | 0 | 1 passed, 0 failed; fake native registrations, no windows |
| `git diff --check` | 0 | no whitespace errors |

Linux workflow test projects compiled: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests. The final Avalonia suite rebuild also compiled the added live-slider assertion in CameraShortcutTests and the idle cadence test.

Earlier concurrent app builds hit a shared PDB lock; the final sequential build passed. Two Core runs during compilation each failed one unrelated timing test (Goon clock scheduling, then board-frame budget); the isolated Goon retry also failed. The final serial full-suite run passed without changing those tests or their implementation.

Windows RegisterHotKey delivery cannot run on Linux. Native browser execution and a real camera are not exercised. CameraShortcutTests, WorkshopBehaviorBrowserTests and the new idle window test were compiled but not run under the documented headless font limitation; the supervisor must run those on GitHub. No render-all claim is made. Five existing Vosk tests skipped because the model is unavailable (documented environment download restriction); no tests were weakened or disabled. `test-plan-coverage.sh` is absent on this base; numbered manual coverage was added to docs/avalonia-test-plan.md.

## Assumptions

- Global companion registration follows WPF's Windows-only OS contract; other platforms retain the existing in-window shortcuts.
- Keep the live Arcademy purchase host as the explicit remaining dependency rather than inventing ownership state without a producer.
