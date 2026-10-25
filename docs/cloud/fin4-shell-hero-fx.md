# fin4-shell-hero-fx

- Base verified at `624a67a648bff22af6985815b7ffad948e242541`; initial cloud snapshot differed and was reset before implementation.
- Ported the remaining independent WPF hero moments: Start charge, exhale, ignition and running heartbeat; Save absorb; mod-aware rank thresholds; level-up right-edge burst, localized notification and stoppable audio. Reused existing visuals, shared BeatLoop, EventBurstLayer, CoreSettings, CoreMods and CoreAudio seams.
- Added panic registration for shell one-shots/audio, focused behavior tests and numbered manual tester steps. No new window or desktop surface.
- Condensed the duplicated ledger history into one honest `stub` row.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported in this unit; implementation adapted directly from the pinned WPF reference and existing head helpers.

## Gap audit

WPF references below are pinned at the base SHA above. Before this unit the live XP slice already covered odometer (700ms), fill/meniscus travel (600ms), meniscus pulse and chip pop (600ms).

| Previously missing | Result |
| --- | --- |
| Start charge | Mod-glow HSV family, 4.5s drift legs; static offsets when ambient motion parks |
| Start exhale | 4.2s cycle, 1.35s travel, 0.70 peak, 5px growth |
| Start ignition | Called before Start/Stop gates; 100ms dip, 300ms flash, 550/750ms rings with 80ms stagger |
| Running heartbeat | 0.6px growth, 2.4s legs; suppressed remotely and while press dip owns transform |
| Save absorb | Real Save click flushes current settings, 400ms tick, 520ms/14px ripple, 1600ms hold + 260ms fade |
| Rank title | Exact WPF 20/50/100 thresholds, Core mod wording, repaint on mod change |
| Level-up celebration | Existing bar flash and chip pop plus 110 sparks at track right edge before repaint |
| Level-up sound/notice | WPF path search, volume exponent 1.5 and multiplier 0.2625 (floor 0.01), replacement stop handle and localized notification |
| Bank hold/release + level-up abort | Still missing: depends on THE BANK pot/flight host, not an independent odometer delay |

WPF `MainWindow.BankFx.cs` is 923 lines: initialization, progression award/pot resolution, polling, token flight, counter landing and abort/release must come together. The head's matching file is a note-only stub. Adding a hold alone would leave XP stuck with no pot landing to release it. A complete host plus service/event plumbing and focused tests does not fit the remaining 1000-added-line budget of this unit. No hold was added and the row remains stub.

## Assumptions

- Follow WPF rank copy verbatim, then apply CoreMods.MakeModAware; it intentionally replaces the initial localized placeholder.
- Settings sections already update and save Current; Save flushes that model rather than creating a second UI-to-settings writer. Preset-offer behavior belongs to its separate row.
- Existing OsNotifications handles platform routing; Windows uses its existing in-app fallback.
- Keep the Bank hold atomic with the missing pot/flight host; prioritize independent user-visible moments within the line cap.

## Verification limits

- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- `dotnet build <project> -c Release` for all 12 Linux workflow test projects: exit 0, zero errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests and CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. Windows code compiled but was not run.
- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 4,120 passed, five skipped, zero failed. All five skipped cases are Vosk speech tests; the model is unavailable.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~HeroMomentsTests|FullyQualifiedName~HeroXpFxTests|FullyQualifiedName~HeaderLevelLiveTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0; 20 passed, zero failed/skipped. The reported font limitation did not reproduce here. Tests use the assembly's CCP_USERDATA_DIR sandbox; notifications/audio use existing sandbox guards and fake providers.
- `bash .github/scripts/core-guards.sh`: exit 0; “All Core guards passed.”
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent on this pinned base. Numbered WPF/Rows tester steps added manually.
- `git diff --check`: exit 0.
- .NET channel discovery returned 403; installed SDK 10.0.100 using the pinned supported download endpoint.
- Full Avalonia suite, render-all, real audio devices, real OS notifications and Windows runtime were not exercised. Bank pot/flight behavior remains unimplemented and untested here.

- Negative rank proof: temporarily restored the old localized placeholder and ran `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~HeroMomentsTests.RankUsesWpfThresholds`: exit 1, five assertions failed on “Basic Subject” vs the WPF ranks. Restored the implementation immediately afterward.
- Restored final focused command (same four-class filter, with build): exit 0; 20 passed, zero failed/skipped, 56 seconds.
