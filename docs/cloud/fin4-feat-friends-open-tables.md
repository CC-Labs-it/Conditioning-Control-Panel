- Ported WPF corner poke/request notices onto the existing Core FriendNoticeStack: newest-first cards, repeat folding with newest payload, three visible/12 retained, overflow and expiry callbacks, lifetime bars, hover pause, bottom-right work-area placement, and non-activating Windows styles.
- Connected Poke back, Accept, waiting-request summaries, request Inbox rows, body-to-drawer navigation, missed-poke Inbox fallback/reopening, and hold/panic/exit teardown. Drawer navigation opens the selected friend without toggling it shut.
- Added focused headless notice lifecycle/service-action tests and the safety-critical panic registry assertion; consolidated the parity row and added numbered WPF tester steps.
- Source: adapted frozen WPF reference at base `b9a2bfd07d94d2aadd180d8add49b802e9d07398`. Fetched `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files imported.

## Remaining WPF gaps

The row remains **stub**. The WPF behavior inventory absent before this unit was: Goon Join launching with the table code and ducking the panel; launcher playable-Goon-only count badge (hidden at zero/errors, refresh on show/every 60 seconds, stop hidden); lobby chip/dropdown (six tables, friends first, per-game gates, 10-second watch lease, seen-count dimming, Open lobby); corner poke/watch/request/waiting notices; Poke back/Watch/Try it/Accept/body actions; Inbox reopening; missed/hold/overflow fallback and exit cleanup.

This unit supplies the independent poke/request notice paths. Still missing:

- Goon host (`GoonHostService`, WPF 2,151 lines) and its real game/web bridge. Drawer Join keeps the existing unavailable message. The head's GoonTestWindow is explicitly a cockpit stub, not a playable host.
- Launcher Goon tile, count badge, lobby chip/dropdown and live lobby host integration. A badge on a nonexistent playable tile would not establish a real user path.
- Watch notice and Watch/Try it dispatch: WPF FriendsLanding routes Catalogue to Deeper, Flavour to source/consent-aware media selection and asset-pool invalidation, HT to the browser. Some individual head entry points exist; the receiving coordinator and full asset invalidation path do not. Watches remain Inbox-only.
- Proxy-avatar rendering, exact WPF relative-time typography/word coloring and arrival/repeat/dismiss motion; exact WPF sound choices/levels. Initials and immediate card removal are deliberate ponytail shortcuts, not parity claims.

The full Goon dependency plus launcher/lobby and watch work cannot fit the 1,000-added-line unit. No placeholder Join/Watch action reports success for a destination it cannot open. This is partial implementation, not P122: WPF has live callers, so no dead-code exception applies.

## Verification limits

- SDK: .NET 10. Channel discovery first returned 403; pinned install succeeded, and the channel install subsequently supplied 10.0.402 used by final builds.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release -m:1` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release -m:1`: exit 0, zero errors.
- All Linux workflow test projects built with `-c Release -m:1`, exit 0: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Migration.Tests, Report.Tests, Scheduler.Tests and Snapshot.Tests. Main Avalonia.Tests also compiled successfully: final build exit 0, 266 warnings, zero errors.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 warnings, zero errors; compiled only, not run.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” `git diff --check`: exit 0.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script is absent on the pinned base; numbered tester steps were added regardless.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3,950 passed, 5 skipped, 1 failed of 3,956. Failure: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected Loops, got Clips. No Core implementation or existing test was changed. The five speech skips are existing model-dependent tests; Vosk download is blocked by this environment.
- Initial overlapping builds hit an Avalonia generated-PDB lock, then generated test asset copies exhausted disk. Recovered by sequential builds with generated-bin cleanup between projects; all listed final builds succeeded. No source assets were removed.
- Earlier relevant headless run: exit 1, 28 passed / 15 failed of 43. Font failures include “Could not create glyphTypeface” for Fredoka / Segoe UI Black; an existing drawer test also found no floating word. UI behavior therefore requires the font-capable GitHub runner; no tests were weakened or skipped.
- Final head run with `DiffEngine_Disabled=true`, `-c Release --no-build --filter "FullyQualifiedName~FriendNoticesTests|FullyQualifiedName~FriendsLandingTests|FullyQualifiedName~FriendsDrawerTests|FullyQualifiedName~PanicSurfacesTests"`: exit 1, **35 passed / 9 failed of 44**. Failures are in existing window classes (font creation or an empty floating-word window collection); every new notice/navigation case passed.
- Isolated `--filter FullyQualifiedName~FriendNoticesTests`: exit 0, **8 passed / 0 failed / 0 skipped**, including real-service fake-wire Poke back and Accept. This proves actions and lifecycle rules, not the blocked font rendering or native Windows activation behavior.
- Final Core guards and diff whitespace check: exit 0. Windows passive/no-activate styles are compiled but cannot be exercised on this Linux worker. Tests use the repository's CCP_USERDATA_DIR module-initializer sandbox and fake friends HTTP transport; no real desktop/profile or live friend account is used.

## Assumptions

- Preserve the existing drawer polling and Core rules; port the independent poke/request surface first and retain an honest stub rather than claiming the Goon dependency is complete.
- Initials and immediate transitions are acceptable incremental behavior, explicitly retained as missing WPF polish in the ledger.
- The snapshot's .git is read-only: used the explicitly authorized independent fallback checkout, verified at the exact expected commit. No changes were made to the published snapshot or other branches.
