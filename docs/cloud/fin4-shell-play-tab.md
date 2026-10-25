# fin4-shell-play-tab

- Restore the WPF Games wall's Back Room, Down the Rabbit Hole, Arcademy, Racing Thoughts and Web App cards after Piece by Piece, with existing localized copy, mod-aware hero art and WPF three-column sizing.
- Wire Web App through the existing rail browser door: portal/shell fallback, copied-link prompt on failure, then banner-beat retirement. No second URL, launcher implementation or new window type.
- Restore DtRH/Arcademy tier bands and the DtRH FREE TODAY stamp; Lab owners do not get a gift stamp.
- Extend PlayWallTests and PremiumGatesTests for unavailable doors, browser fallback/retirement and the added bands/free-day stamp. Add numbered manual steps. Keep shell-play-tab **stub** and replace its duplicated historical claims with current gaps.
- Source: frozen WPF at base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## Gap audit at the pinned base

Read `MainWindow/MainWindow.PlayTab.cs`, `Views/Tabs/PlayTabView.xaml`, `PlayTabView.Cards.cs`, and `MainWindow/MainWindow.LabTab.cs` in the frozen WPF head. The row's repeated assertions that Intake and Remote/For You stamps were absent were stale: the base already paints those, subscribes to pass/language changes and refreshes on arrival/account changes. Zone scroll/glow and Mantra start already exist too.

The actual missing wall behavior was:

1. Breakout demo/full launches, including full-access gate, and their shared Back Room media/window host.
2. Piece by Piece launch, focus/reuse, lobby account flow, media/friends bridge and window lifecycle.
3. Goon account refusal and host launch/focus/lifecycle, including the 20-second paint-stall watchdog (UI-thread stall ignored; per-launch recovery reset).
4. Back Room, DtRH, Arcademy, Racing Thoughts and Web App cards and shims. This slice restores all five cards and the live Web App path. The four added native-game doors remain unavailable, matching this head's existing disabled-game convention.
5. DtRH/Arcademy bands and DtRH free-day stamp. Restored here, including ownership suppression.
6. Focus Gaze ON-tier refusal with immediate toggle reversion, consent, asynchronous webcam prewarm, calibration/start failure copy, master-consumer enable/disable, persisted intent, status updates and boot restart. The existing empty handler is still not a real consumer.

## Remaining host dependencies and size limit

`git ls-tree -r --name-only 73b1ccf0d793bb3fde6091c18a9fa6b5c692964a` shows the named native-game host implementations and GazeFocusService only in WPF; no Avalonia host counterpart exists. The Avalonia ChaosRunHost is the separate bubble-run host, not the browser-based DtRH or Racing Thoughts bridge.

WPF source sizes alone: BackRoomHostService 778 lines (plus its window/media bridge and Breakout wrapper), GoonHostService 2151, PieceByPieceHostService partials 1212, GazeFocusService 1226, CaucusHostService 1530. Reusing Core gates/rules does not supply those missing UI/media/lifecycle bridges. A faithful port of these dependencies together with this wall cannot fit the 1000-added-line unit cap. In particular Racing Thoughts must preserve its first-Escape pause, not use a generic close-on-Escape window. The card shims are live WPF callers, so P122 does not apply.

No new surface type is introduced: the browser failure uses the existing MessageDialog/ShowDialogSafe lifecycle and panic handling. Native games remain disabled rather than opening an unregistered placeholder.

## Assumptions

- Preserve WPF card order and localized copy while using this head's existing unavailable-build tooltip for native hosts.
- Reuse the already ported rail browser strategy for Web App rather than importing Windows browser-process strategies.
- Keep the row stub; the remaining host and Focus Gaze gaps are user-visible and live in WPF.

## Verification limits

Verification results are recorded below after the required commands complete. Windows-specific runtime behavior cannot run on Linux. The supplied environment limit reports headless font creation failures unless cloud-test-fonts has landed; that test setup is absent here, so Avalonia tests will be compiled but not run. No fail-before-fix runtime claim is made.

- `dotnet --version`: 10.0.402. Default installer initially received 403 from its CI lookup, then installed successfully from the supported builds feed.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `git diff --check`: exit 0. This base has no `.github/scripts/test-plan-coverage.sh`; numbered WPF/Rows steps were added anyway.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3952 passed, 5 skipped (Vosk model unavailable), one frame-budget test failed at 17.03 ms during concurrent compilation. The isolated rerun with `--no-build --no-restore --filter FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget` passed (exit 0, 1 passed).
- The first build sweep exhausted disk while copying duplicated native dependencies. Removed only generated test `bin` directories; retried each project sequentially with output cleanup between builds.
- Avalonia `PlayWallTests` and `PremiumGatesTests` were not run (supplied font-creation environment limit); the focused tests compile. Windows runtime behavior, real browser launches and render-all were not run. No new Windows-specific implementation was added.

- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release`: exit 0, 0 errors, including the final WPF three-column layout and focused test assertions.

All workflow test projects built with `-c Release` on the sequential retry (exit codes):

```text
Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj: exit 0
Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj: exit 0
Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj: exit 0
Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj: exit 0
Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj: exit 0
Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj: exit 0
Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj: exit 0
Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj: exit 0
Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj: exit 0
Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj: exit 0
Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj: exit 0
Tests/CCP.Core.Tests/CCP.Core.Tests.csproj: exit 0
Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj: exit 0
```

The WPF test build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: 0 errors (554 existing warnings); compiled only, not run on Linux.
