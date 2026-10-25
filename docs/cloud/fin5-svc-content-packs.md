# fin5-svc-content-packs: dependency audit (PR-body draft)

- No production or test code changed. `svc-content-packs` remains `stub`; no
  ledger promotion, new surface, panic registration, or tester step is claimed.
- Rechecked the exact required base `0a04ae5d3f630a54decf8766c1599ba583ac7e85`.
  The checkout initially had a different commit; fetched the requested base and
  reset the clean snapshot before creating this unit's branch.
- Fetched `parity/trunk` read-only at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; no import
  credit applies. Existing Core/head files take precedence.

## Remaining WPF behavior

All source locations below are pinned to the required base SHA above, including
its frozen WPF reference. The last (and only) `svc-content-packs` row is the audit
starting point. Existing manifest/fallback, resumable signed-URL download,
encryption/install, active-file selection, library browsing/preview, flash/video
media pools, startup/rescan, and temporary-file cleanup already exist.

1. **Bureau evidence lockers and local ZIP intake.**
   `MainWindow/MainWindow.Lab.cs:219` launches `BureauHostService`.
   `Services/Bureau/BureauHostService.cs:374-504` lists image-bearing catalogue
   packs with installed/indexed counts, opens the requisition catalogue, accepts
   the first ZIP from WebView2 AdditionalObjects, rejects invalid/busy/unknown or
   already-installed drops with WPF's messages, normalizes filename/name/id for
   matching, reports install progress, rebuilds the index, sends index progress
   and `pack-installed`, refreshes lockers, and clears the busy gate on failure.
   This needs the Bureau's actual page host and index, not another install API:
   Core already implements `InstallPackFromLocalZipAsync`.
2. **Chaos pack images in bubble/media effects.**
   `Services/Flash/FlashService.cs:3612` (`GetChaosImagePaths`) borrows the enabled
   disk/active-pack shuffle pools, selects distinct sources, decrypts selected
   pack images on demand and tracks cleanup. Live consumers include
   `Chaos/ChaosFlashOverlay.cs:336` and `Chaos/ChaosGifCascadeOverlay.cs:71`.
   The Avalonia `ChaosRunHost` explicitly lacks bubble spawning/pop/score and
   keeps FALL IN unavailable; the wash/cascade and bubble-host surfaces are
   absent. `CCP.Core/Chaos/ChaosImagePool.cs` is only a raw disk-list cache and
   cannot substitute for these consumers.
3. **EMI install moment after both successful install paths.**
   `Services/Content/ContentPackService.cs:588,680` fires
   `contentPackInstalled` after `PackDownloadCompleted`, with the lowercased pack
   name as `target`. `Services/EmiDesk/EmiDeskService.cs:661` publishes the moment
   before drawing, dispatches presentation to the UI thread, handles away/hold
   state and suppresses presentation-time chatter. Avalonia's `EmiDeskService`
   has summon/dismiss/hotkey ownership but explicitly lacks `Fire` and the moment
   presentation pipeline. Core's `EmiLineEngine` exists; drawing a line in Core
   alone would not reach the user or satisfy this behavior.

These are all live consumer dependencies, not missing standalone store APIs.
Searches of Core and Avalonia find no Bureau host/index, Chaos wash/cascade or
bubble host, or EMI `Fire` implementation. The generic `WebHost` now has a JSON
carrier and request-header seam; the older ledger's blanket claim that no bridge
exists is too broad. It still has no Bureau protocol/index or equivalent of the
raw AdditionalObjects file-drop channel.

## Why this unit is dependency-only

A WPF-faithful Bureau requires boot/auth/index progress, target resolution and
frame decoding, submission/inbox/admin protocol, window ownership, heartbeat
recovery and shutdown, plus the intake path. WPF's host and index are 614 + 233
lines before adapting platform operations and adding focused tests; its backing
`ChaosWebViewHost` is 1,904 lines. Those counts are scope evidence, not a claim
that importing every line is necessary. Even reusing Avalonia's generic carrier,
the missing Bureau lifecycle, file-drop seam, index/decode and behavior tests
leave insufficient room to complete Bureau alongside the other consumers.
Chaos's wash and cascade alone are 423 + 544 WPF lines, before bubble integration,
media selection and tests. EMI's 2,105-line WPF service includes a full speaking
pipeline absent from the head. An install-only button, unconsumed moment event,
or unused image-picker method would leave the same user paths incomplete.

Apply the requested pure-dependency rule: no code change; finish the Bureau
host/index/ZIP-drop, Chaos bubble/media effects and EMI Desk moment host in their
own units before reattempting this row. No P122 promotion: the live consumers
above disprove a wholly dead row.

## Dead WPF paths (no new UI enabled)

Pinned caller proof can be reproduced with:

```sh
git grep -n -E 'PacksSectionEnabled|RefreshBuckets\(|GetFullPackStatusAsync\(' 0a04ae5d3f630a54decf8766c1599ba583ac7e85 -- ConditioningControlPanel
git grep -n -E 'GetPackDownloadStatusAsync\(|GetExternalPackDownloadUrlAsync\(' 0a04ae5d3f630a54decf8766c1599ba583ac7e85 -- ConditioningControlPanel
```

- `MainWindow.xaml.cs:208` sets `PacksSectionEnabled = false`; the tab refresh at
  `MainWindow.TabNavigation.cs:577` is gated. Pack cards, Refresh/Delete,
  signed/external download, URL preview and their auth UI remain hidden.
- `GetFullPackStatusAsync` has only its own `GetPackDownloadStatusAsync`
  delegation, which has no caller. Its missing Discord-token fallback is dead.
- External-link merged-account recovery is absent in Core, but its WPF caller
  is the hidden pack-card path (`MainWindow.Assets.cs:2024`).
- `FocusGameService.RefreshBuckets` has only its declaration: pack-bucket
  enumeration has no live caller.

## Assumptions

- Preserve the existing honest stub row verbatim for this documentation-only
  dependency outcome; updating it would imply progress that did not occur.
- Shared engines and generic hosts do not count as a ported consumer until its
  WPF user path, timing, errors and cleanup can actually execute.

## Verification limits

- SDK: .NET 10.0.402. The prescribed channel installer encountered HTTP 403
  querying `ci.dot.net`; both the eventual channel fallback and an explicit
  published-version install from `builds.dotnet.microsoft.com` succeeded.
- `dotnet build <project> -c Release --nologo -v:q`: exit 0, zero errors for
  `CCP.Core`, `CCP.Avalonia`, and all eleven Linux test projects in
  `.github/workflows/build.yml`: Core.Tests, Core.Settings.Tests,
  Core.Content.Tests, Avalonia.Tests, Avalonia.Consent.Tests,
  Avalonia.Language.Tests, Avalonia.CatalogueStartup.Tests,
  Avalonia.Scheduler.Tests, Avalonia.Report.Tests,
  Avalonia.GeneralSettings.Tests and Avalonia.Snapshot.Tests.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed; 424 XAML
  references checked. An initial read-only-sandbox attempt could not create
  temporary files; the rerun with filesystem permission passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent on base.
  No row or tester coverage changed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release
  --nologo -v:q`: exit 1; 2,811 passed, five skipped, one failed out of 2,817.
  Failure: `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`.
  A diagnostic `--no-build` rerun filtered to that case plus
  ContentPackServiceTests, ContentPackStoreTests and PackMediaPoolTests passed
  all 30 tests (exit 0). The full-suite failure remains recorded; no test changed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --nologo -v:q --filter
  'FullyQualifiedName~ContentPacksWireTests|FullyQualifiedName~AssetsTabTests'`:
  exit 1; three passed, nine failed. A focused `--no-build` rerun of
  `AssetsTabTests.PackFilesSortByNameVideoFolderCheckFeedsThePoolAndDeactivateHidesThePack`
  confirms `Could not create glyphTypeface` for `Segoe UI Black`, during
  `PremiumSpark.BuildLabel` in shell construction, before pack-tree assertions.
  Headless visual behavior needs the supervisor's font-equipped GitHub runner.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release --nologo -v:q -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings,
  zero errors. The WPF application and test project compiled; neither ran here.
- No Windows execution or render-all proof; no P122 or verified claim. No live
  Bureau/webview, native drop, Chaos overlay or EMI speaking proof is available.
- `git diff --check`: exit 0. Only this analysis document is changed.
