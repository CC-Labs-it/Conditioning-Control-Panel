# Sync7 after the flash clip host

- Port remote clip ownership into the existing Core source; preserve device-cache consent, allowance and download gates. Owned copies survive shared-cache eviction and repeated URLs reuse the same file.
- Adapt `RemoteClipFiles` from read-only trunk commit `734a9e477bd4778f00d7918e5853f6aac8ca7cd9`: oldest-used trimming, 64 soft / 128 hard cap, two-minute recent-use protection. Retention tests derive from WPF `FlashClipSlotTests` on trunk, including `175d692fedfa54c0a93cca3f1ae6498d5ff191c0`.
- Release cancelled/stale/unpublished copies in finally and delete outside the draw lock, following `175d692fedfa54c0a93cca3f1ae6498d5ff191c0`.
- Add Serilog CLIP admission diagnostics from `73438ce6e8333acc04e87476bf76dfcd238f50d9` and its teardown follow-up `175d692fe`.
- Add Core regressions, strengthen the existing head integration test, and add two ledger rows with numbered tester steps. Neither row is marked verified.

## Per-commit disposition

| WPF commit | Disposition | Evidence |
| --- | --- | --- |
| `73438ce6e` | ported (diagnostic remainder); admission already covered | Core `Services/Media/ClipSlotBudget.cs:62` and `:87` already implement live leases, reserved/shared caps and oldest leaving eviction. Head `Views/Overlays/FlashClipPlayer.cs:48` rejects missing paths and releases failed starts; `:154` immediately releases on disposal. Core `FlashClipHostTests.SharedPlayersLeaveThreeSlotsForFlashesAndReleaseIsIdempotent`, `OldestLeavingOwnerIsEvictedAndCannotReleaseTheReplacement`; head `MissingClipNeverConsumesAdmission`. Added Serilog diagnostics. |
| `175d692fe` | ported (remote lifecycle remainder); host admission/exit already covered | Core `RemoteFlashSource.cs:274` publishes only for the current generation/token and releases unpublished copies in finally; `:218` deletes after leaving the draw lock. Existing host `FlashClipPlayer.cs:23`/`:30` bounds native teardown; `FlashOverlayWindow.cs:38` retries cap refusals and `:57`/`:287` lends leases on exit/fade. Core cancellation/failure tests cover cleanup. The head has one-way exit windows, with no WPF reversible linger heartbeat requiring a FlashClipUpgrade state machine. |
| `734a9e477` | ported (owned-file remainder); fresh-path lookup/retry already covered | Core `RemoteClipFiles.cs:44`/`:60` implements retention, wired by `RemoteFlashSource.cs:270`. Core tests `OwnedClipSurvivesDeviceCacheEvictionAndIsReleasedByPanic`, `ClipFilesDrawnJustNowSurviveManyMoreWarms`, `ClipFilesReplaceReleasesTheOldFileAndHardCapHolds`. Host `FlashOverlay.cs:152` already resolves the current clip per URL at Show, without a decoded-clip-path cache; `FlashOverlayWindow.cs:38` already upgrades posters after cap refusal. |

No missing host piece remains for these commits. Existing `PanicSurfaces.cs:48` closes flashes, stops players and cleans the remote source; no new surface is introduced.

## Verification limits

- Before the fix: focused Core suite had 3 failures and 7 passes. Failures proved path aliasing and the missing unpublished-copy lifecycle. Final focused run including stale-result coverage: 13 passed, 0 failed, 0 skipped.
- Core guards passed: forbidden API scan and XAML namespace checks.
- Initial full Core run: 3,964 passed, 1 failed (unrelated board-frame timing, 25.41 ms during concurrent builds), 5 existing Vosk skips. Isolated rerun passed: 3,966 passed, 0 failed, 5 existing Vosk skips (3,971 total).
- Final Linux build matrix: all 14 projects passed with zero errors (Core, Avalonia, and every Linux test csproj in build.yml). Initial disk-exhaustion failures were resolved by sequential builds with completed generated test outputs removed between projects.
- Avalonia head tests are compiled but not run because of the documented font-creation limitation. The supervisor must run head `FlashClipHostTests` on GitHub. No live desktop/native LibVLC or Windows execution verification.
- No Vosk model download attempted: its source is reported blocked in this environment. Required-model speech verification remains for CI.
- `test-plan-coverage.sh` is absent on this base; numbered WPF/Rows tester steps were added in the existing format.

## Assumptions

- Keep the device-cache gate and copy only clips into the media temporary directory. Posters remain cache-owned, matching the WPF poster path; other consumers' cache behavior is unchanged.
- Keep the host's timer-based poster retry and one-way exit lifecycle; do not import an unused WPF linger state machine.
- Original checkout was read-only and had a different HEAD. The requested fallback clone was verified clean at base `7f1bcf6f171cf8e389096cdd147ea84749f9765d` before branch creation. Requester-local changes were unavailable.

### Commands and results

- `dotnet build <project> -c Release --nologo -m:1`: exit 0 for every project below (existing warnings remain).
  - `CCP.Core/CCP.Core.csproj`
  - `CCP.Avalonia/CCP.Avalonia.csproj`
  - `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
  - `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
  - `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
  - `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
  - `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
  - `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
  - `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`
  - `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
  - `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
  - `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
  - `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
  - `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`

- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3,966 passed, 5 existing model-dependent skips. Initial concurrent run exited 1 on the unrelated board timing budget; isolated rerun passed unchanged.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~FlashClipHostTests`: exit 0; 13 passed.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `git diff --check`: exit 0.
- Tests used `DiffEngine_Disabled=true` and the assemblies' CCP_USERDATA_DIR sandboxes. Head `FlashClipHostTests` was compiled, not executed (font limitation above).

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors. Windows code compiled but not run.
