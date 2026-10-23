- Wire startup What's New through an awaited modal queue using the existing Core ordering and quiet-window rules: priority 30, 750 ms settle, 500 ms polls, five-minute wait budget, and no budget consumption while a game or the launcher owns the screen. Fresh installs stamp silently; upgrades stamp after dismissal. The existing upgrade tour runs after the modal unwinds; an outstanding age confirmation holds the notes until accepted.
- Read accepted heartbeat tier data through SyncPush into the existing entitlement rise handler. Ignore rejected responses and account changes while a request is in flight. Preserve the existing rise-only entitlement rule.
- Restore the premium rise's gold/cyan profile glow and 60-particle burst through the existing EventBurstLayer; subscribe the existing PremiumSpark to the rise event and unsubscribe on detach. Gate immediate celebrations using existing game ownership.
- Raise the guaranteed possession-rules bark even when its card is parked in the Inbox. Connect empty flashes and the no-videos prompt to one shared remote-media intro offer, with startup-modal retry and budget claimed at open time. Register startup modals and intro cards for panic cleanup.
- Add focused Core and headless tests; keep `win-feature-intro` **stub**, with the dependency gaps below explicitly recorded.
- Import source fetched read-only: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No new trunk code imported: the existing base files and shared helpers win. WPF behavior was adapted from the pinned base `9cfbf28b284227fcd73f23e57a5b31e4f8dd5bce`.

## WPF gap audit

The ledger row and every direct FeatureIntroPopup caller were inspected at the pinned base.

| Behavior absent on the base head | Result |
| --- | --- |
| Automatic What's New caller, modal ordering, flag ownership, version stamp, upgrade tour | Implemented using existing dialog/tour/Core queue. |
| Automatic season recap | Dead UI path in pinned WPF: TryPresentSeasonRecap only performs silent monthly bookkeeping. No new modal added. |
| Descent-vat explainer after an armed, on-screen jar | Still blocked by `shell-profile-vat` / missing DescentService. No fabricated jar or eager flag spending. |
| Heartbeat entitlement reading | Implemented; response and account guards tested. |
| Tier-rise profile bubble refresh, gold/cyan glow, burst, header PremiumSpark burst | Implemented with existing visual helpers. |
| Possession rules' guaranteed bark | Implemented through the existing BarkEngine. |
| Celebration/game-host quiet gate | Live game and launcher ownership wired. Exact LauncherHost.AwaitingGame acknowledgement/handoff remains part of `shell-launcher`. |
| App-wide remote-media coaching (additional gap found beyond ledger text) | Existing empty-flash and no-video paths implemented; wallpaper and legacy first-run-assets-prompt callers still depend on those unported head paths. Manual media-picker intro already exists. |
| What's New's EMI afterUpdate moment | Still depends on EmiDeskService's unported moment bus. |

Pinned grep proof for the retired automatic recap:

```sh
git grep -n 'TryPresentSeasonRecap' 9cfbf28b284227fcd73f23e57a5b31e4f8dd5bce -- ConditioningControlPanel/MainWindow/MainWindow.Marquee.cs
# :265: public void TryPresentSeasonRecap()
git grep -n 'new SeasonRecapWindow' 9cfbf28b284227fcd73f23e57a5b31e4f8dd5bce -- 'ConditioningControlPanel/**/*.cs'
# no matches (exit 1)
```

The method at lines 265–288 contains bookkeeping and no ShowDialog/EnqueueStartupModal call. The manual recap review/export surfaces are outside the automatic-startup gap. P122's dead-caller evidence does not make this whole row wired: the other gaps remain.

## Verification limits

- Checkout: the supplied snapshot had the wrong HEAD and read-only Git metadata. Used the authorized fallback clone and verified exact base HEAD and a clean sparse worktree before implementation. Large assets were restored as builds/tests required them.
- .NET SDK: 10.0.402. All builds use Release configuration.
- Heartbeat regression before implementation: **exit 1**, accepted tier 2 expected, actual null; two rejected-response cases passed. After implementation, V2AuthServiceWireTests: **exit 0**, 17 passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build` with `DiffEngine_Disabled=true`: **exit 0**, 2,816 passed, five existing Vosk model-dependent cases skipped. No tests weakened, disabled, or deleted. The model download is unavailable in this environment.
- `bash .github/scripts/core-guards.sh`: **exit 0**, all guards passed.
- Build sweep: Core, Avalonia, and all 11 Linux-compatible workflow-listed test projects: **exit 0**, zero errors. Generated test outputs were removed between builds to fit disk capacity. The focused head tests were recompiled after the final caller and fixture changes.
- Relevant headless classes: FeatureIntroStartupTests, FeatureIntroWiringTests, PremiumCelebrationTests, PremiumSparkTests, CompanionBarkHeadTests, and PanicSurfacesTests. Compiled; not executed locally per the supplied cloud font limitation. Supervisor/GitHub rcheck must execute these classes. Head tests are not claimed fail-proven here.
- `--render-all`: **exit 0**, 224/224 views rendered; FeatureIntroPopup and WhatsNewDialog images inspected. The final repeat after all caller changes also passed 224/224.
- `--nav-check`: **exit 1**, two Devices navigation assertions fail. Pinned-base build succeeded and nav-check reproduces exactly the same two failures; all unit sources were restored afterward.
- WPF test project cross-target build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: **exit 0**, zero errors (existing warnings). Additional real resources were restored for the sparse checkout. Windows desktop behavior was compiled but cannot run on Linux.

Build command results (the WPF row additionally used both Windows-targeting flags above):

| Command | Exit | Tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | zero errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` | 0 | zero errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release` | 0 | zero errors |

## Assumptions

- Use the pinned WPF behavior and copy; keep the existing Fluent icon mapping.
- Existing panic game ownership and launcher visibility provide this head's live parking inputs; do not invent an AwaitingGame protocol or a DescentService inside this unit.
- Keep the ledger stub until descent-vat, EMI moments, and the remaining dependent caller/handoff paths are real user paths.

## Changed files

- `CCP.Avalonia/App.axaml.cs`
- `CCP.Avalonia/Controls/PremiumSpark.cs`
- `CCP.Avalonia/Platform/EntitlementTierSync.cs`
- `CCP.Avalonia/Platform/StartupLadder.Modals.cs`
- `CCP.Avalonia/Platform/StartupLadder.cs`
- `CCP.Avalonia/Views/Overlays/FlashOverlay.cs`
- `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs`
- `CCP.Avalonia/Views/Windows/FeatureIntroPopup.axaml.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.FeatureIntroStartup.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.FirstRun.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Lockdown.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Marquee.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Patreon.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.ProfileBubble.cs`
- `CCP.Avalonia/Views/Windows/PanicSurfaces.cs`
- `CCP.Core/Services/Account/SyncPush.cs`
- `CCP.Core/Services/Account/V2AuthService.cs`
- `Tests/CCP.Avalonia.Tests/CompanionBarkHeadTests.cs`
- `Tests/CCP.Avalonia.Tests/FeatureIntroStartupTests.cs`
- `Tests/CCP.Avalonia.Tests/PremiumCelebrationTests.cs`
- `Tests/CCP.Core.Tests/V2AuthServiceWireTests.cs`
- `docs/avalonia-parity.md`
- `docs/cloud/fin-win-feature-intro.md`
