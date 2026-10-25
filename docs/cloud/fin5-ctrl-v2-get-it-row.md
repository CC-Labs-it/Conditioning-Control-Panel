# fin5-ctrl-v2-get-it-row

- Finish the purchase row on both Flashes and Bubble Pop. Preserve the existing Core price/confirmation/buy/toast behavior; start the counter after signing in on an already-visible card and invalidate cached offers on identity changes.
- Add the missing Bubble Pop v2 host, ownership-gated FloatUp/Rain/Spiral In/Mix picker, retained box after purchase, and Core ambient motion playback. Reuse the existing shell and overlay surfaces; no new window type or panic registration is required.
- Restore ownership from provider validation, profile load and sync replies in addition to counter receipts. Publish grant changes so rows and the Bubble picker repaint on grants/revocations, ignoring other accounts and stale revisions; clear held grants on account changes.
- Add focused motion, host, sign-in and profile/sync feed tests; replace the duplicated historical ledger text with one wired row and add tester step 33.
- Read-only import source: `parity/trunk` pinned at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Adapted the pure `AmbientBubbleMotion`/`SpiralInPath` helpers (named `AmbientBubbleMotionRules`/`AmbientSpiralInPath` here to avoid WPF public-type collisions) and Bubble Pop picker methods from that snapshot; existing base files were merged, never overwritten. WPF behavior reference pinned at base `92bc73d5cdf6838c706acb0f5de16fa519c79f87`.

## Initial gap audit

The base already had the full counter purchase flow on Flashes: effective-visibility read, localized states, sign-in, displayed-price confirmation, guarded buy, refusal keys, receipt adoption, toast, and hide-on-ownership. Missing were: the Bubble Pop caller/box and its ownership-driven picker refresh; a provider/profile/sync ownership feed after restart; grant-change repaint outside a buy; and a read after signing in while the row remains visible. These are implemented here. Bubble Brain Drain, gaze interaction and other effects belong to `feat-bubble-pop`, not the purchase row, and are not promoted by this unit.

## Verification limits

.NET SDK 10.0.402. All builds used `dotnet build <project> -c Release`; the WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Every final build had zero errors (warnings remain).

| Project | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 |

With `DiffEngine_Disabled=true`:

- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 4,123 passed, 0 failed, 5 existing Vosk/environment skips, total 4,128. The five new `AmbientV2MotionTests` cases also passed in a focused run.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~V2GetItRowTests|FullyQualifiedName~Profile_load_and_sync_restore_and_revoke_prizes'`: exit 1; 2 passed, 3 failed during Inter glyphTypeface creation before window assertions. UI behavior is not validated here; the test project compiles with zero errors.
- `bash .github/scripts/core-guards.sh`: exit 0; all Core guards passed.
- `git diff --check`: exit 0.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this pinned base (exit 127, file absent); numbered tester step 33 explicitly covers this row.

No Windows desktop code or WPF tests were executed. No visual render proof was produced because of the font limitation. No tests were weakened, removed or newly skipped; the existing Vosk tests skip in this environment. Existing MessageDialog and BubbleOverlay hosts are reused. The initial WPF cross-build exposed a public type collision; renaming the portable helpers resolved it, and the final WPF test build passed.


The ownership sync regression was mutation-proven: disabling the sync prize sink produced 1 failed test (exit 1); restoring it produced 1 passed test (exit 0). The UI sign-in/revocation assertions cannot be fail-proven in this runtime because window layout fails first on Inter font creation.

## Assumptions

- This unit covers the purchase row and the Bubble host/motion choices needed to use its purchase, not every independent Bubble Pop effect.
- Ownership remains server-authoritative and in memory, as in WPF; restart refreshes it from existing account network paths rather than a new local cache.

## Changed files

- `CCP.Avalonia/Platform/AccountSeed.cs`
- `CCP.Avalonia/Platform/PrizeOwnership.cs`
- `CCP.Avalonia/Views/Controls/V2GetItRow.axaml.cs`
- `CCP.Avalonia/Views/Features/BubblePopFeatureControl.axaml`
- `CCP.Avalonia/Views/Features/BubblePopFeatureControl.axaml.cs`
- `CCP.Avalonia/Views/Features/FlashFeatureControl.axaml.cs`
- `CCP.Avalonia/Views/Overlays/BubbleOverlay.cs`
- `CCP.Core/Services/Account/SyncPush.cs`
- `CCP.Core/Services/Account/V2AuthService.cs`
- `CCP.Core/Services/AmbientBubbleMotionRules.cs`
- `CCP.Core/Services/AmbientBubbles.cs`
- `Tests/CCP.Avalonia.Tests/SyncPushTests.cs`
- `Tests/CCP.Avalonia.Tests/V2GetItRowTests.cs`
- `Tests/CCP.Core.Tests/AmbientV2MotionTests.cs`
- `docs/avalonia-parity.md`
- `docs/avalonia-test-plan.md`
- `docs/cloud/fin5-ctrl-v2-get-it-row.md`
