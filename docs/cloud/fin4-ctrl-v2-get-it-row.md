# fin4-ctrl-v2-get-it-row

- Reused Core OwnershipService for snapshot account/revision rules and change detection; removed the head's duplicate snapshot implementation.
- Subscribed attached Get it rows to ownership changes independently of counter service availability; detached rows unsubscribe.
- Rebuild the Flashes picker on grant and revoke, including snapshots not produced by this row's buy.
- Added snapshot lifecycle regression coverage and configured the existing test class to use bundled fonts. Retained all purchase tests.
- Replaced the accumulated historical ledger text with one honest stub row and added tester step 33.
- No trunk code imported. Read-only source fetched: parity/trunk at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038. WPF reference pinned at df5a59167c58ab3735c2a5295db3b3b7243af475.

## WPF gap inventory

The row already has the WPF copy, layout, automation IDs, effective-visibility counter read, sign-in path, checking/offer/short/unavailable/busy/failure states, Yes/No confirmation, confirmed-price buy, refusal mapping, receipt adoption and success toast. The Flashes card is a real caller.

Closed here: WPF OnLoaded subscribes to PrizeGrants.GrantsChanged even independently of the counter. The head previously listened only to counter and identity changes. Snapshot grant/revoke changes now repaint the row, and the motion picker follows both directions.

Remaining: svc-prize-ownership profile/provider/sync feed. WPF ProfileSyncService.PrizesFeed receives live server snapshots at login/profile/sync and clears on account transitions. The head's AccountSeed and SyncPush do not carry that complete feed. A fresh launch can therefore offer an already-owned prize until a counter reply carries ownership. WPF OwnershipService explicitly stores grants only in memory; local persistence is not the appropriate fix.

Remaining: RowGetBubblesV2 has a live WPF caller in Features/BubblePopFeatureControl.xaml and Configure in its code-behind. It is not dead under P122. The Avalonia Bubble Pop host lacks its v2 box, owned motion picker and Brain Drain control; Views/Overlays/BubbleOverlay.cs explicitly supports FloatUp only and lists Rain/Spiral In and Brain Drain as unported. A standalone offer would sell effects the head cannot play. Porting the host requires those effect implementations and their focused tests as well, beyond this row repair. The row remains stub; no claim of full parity or verification.

No new window, notification type or overlay surface was added. Existing confirmation and success toast paths are reused; no new panic registration is needed.

## Verification limits

- SDK: .NET 10.0.402.
- `dotnet build CCP.Core/CCP.Core.csproj` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: exit 0, zero errors.
- Built every Linux test csproj listed in `.github/workflows/build.yml`: CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia, Core.Content, Core.Settings and Core tests. All exit 0, zero errors.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (556 existing warnings). Windows code compiled but was not run.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests --no-build`: exit 0; 4,116 passed, 6 skipped, 0 failed. Five Vosk tests lack the model (download blocked in this environment); the other skip is the existing frame-budget test.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests --no-build --filter FullyQualifiedName~V2GetItRowTests`: exit 0; 4 passed, 0 failed, 0 skipped.
- Before the fix, the focused snapshot regression failed (exit 1) at the assertion that a snapshot grant hides the row. After the fix it passes. The first attempt hit missing-font creation instead; bundled font registration in this class resolved it.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed (429 XAML references checked).
- `git diff --check`: exit 0. Exactly one ledger row for ctrl-v2-get-it-row remains.
- Initial multi-project builds filled the disk with copied runtime assets and aborted. Retried every test build successfully, deleting generated test output between builds. No tracked assets were removed.
- `.github/scripts/test-plan-coverage.sh` does not exist on this base; not run.
- Full Avalonia suite, other test assemblies, render-all, real account/network purchases and Windows runtime checks were not run. Only the requested Core suite and relevant row class were executed; all workflow test projects were compiled.

## Assumptions

- Preserve the working Flashes purchase path and defer the Bubble Pop offer until its purchased effects can run.
- Treat server snapshot reload, rather than local grant persistence, as the WPF requirement.
