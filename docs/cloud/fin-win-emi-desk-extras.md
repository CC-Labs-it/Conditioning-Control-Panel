# fin-win-emi-desk-extras

- Match WPF presentation ownership of delayed FX: capture the presentation epoch in `After`, reject stale callbacks and ordinary FX during the show, and pass the presentation flag through both entrance delays. Closing before the smoke arrives no longer lets the old entrance repaint the restored widget; a pending dismiss no longer hides her during or after the show.
- Release an existing body drag/capture and its wobble when borrowed. Cancel the gentle-drop face timer, including a callback already posted to the dispatcher, so the stage owns her face. Keep WPF's Motion Off arrival-gate reset.
- Match WPF's presentation input gates: right-click cannot open the ring, hover cannot arm a pat, and gear/help commands cannot open panels. Resize and close remain available. No new surface: the existing `PanicSurfaces` entry `first-show` stops `FirstShowService`, whose Closing handler ends presentation.
- Add `EmiDeskPresentationTests`: 11 regression cases failed before their fixes (the drag case was separately rerun after correcting the handled-event recorder), plus a positive entrance-completion test. All use real headless windows in the assembly's temporary `CCP_USERDATA_DIR`; the new fixture registers the bundled Inter font.
- Keep exactly one ledger row, honestly `stub`: the remaining live gaps belong to shared, unported `win-emi-desk` services. This is not a dependency-only NOOP: the presentation lifetime and input gaps above were independently reachable.
- Source: WPF pinned at base `db079bf91474c77a5ea67ec306f6337a7de44b83`. Read-only `parity/trunk` fetched and inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no new trunk code imported. The presentation already on the base originated in trunk commit `9db631eb6bd2bd3f67da1e33ac5ba429f3fc921b`; its existing implementation was merged with the WPF lifecycle behaviour, not overwritten.

## WPF gap audit

Read the ledger's Presentation/Toss/Tube files, plus their live service, FX and input call sites at the pinned base. The base's claim that presentation was fully ported missed the independently fixed gaps above.

| Behaviour still absent | Exact dependency / disposition |
| --- | --- |
| Gentle land: `PUT DOWN` chain label, `celebration` body frame, then restoration after the full land chain (`^_^` 600 ms, rest 160 ms). Existing face-only feedback and squash remain. | `win-emi-desk`: `PlayChain` immediately calls its continuation, `ChainLive` is constant false, and `SetPose` only tracks a name over placeholder art. No duplicate chain player introduced. |
| Thrown landing: `DIZZY`, idle body, `@_@` 260 ms, `=_=` 260 ms, `@_@` 260 ms, `x_x` 800 ms, before the flight remark. | The same chain player, body art and bubble hooks; Core already owns the canonical chain table. |
| Pickup `dragged` moment; `dropped` after the land chain (immediately if another chain is live); `flung` only after dizzy. Monitor-third `zoneRow`, and accumulated fling thresholds `flings20` / `flings50`, must reach the line engine. | `win-emi-desk`: head `EmiDeskService.Fire` / moment bus, speech/bubble delivery and chain continuations are absent. The unconditional lift/bump/thud, fling counter and landing squash already run independently. |
| Actual wake-chain duration at the end of the presentation entrance; pixel glyph faces and visible pose swaps during stage frames. | Shared chain player, pixel face renderer and body image loading. Stage position/bob/turn and face/pose requests already reach the real widget. |
| Presentation entry cancels an active ask, channel and speech hold; stops idle/alive loops; refreshes the armed outfit. Exit restarts idle/alive behaviour. | `win-emi-desk`: ask/channel/voice/hold surfaces, alive/idle implementations and Arcademy outfit store are unported. Existing no-op stop/restart hooks cannot prove these behaviours. |
| Tube suspension stops idle, chains, pet/reactions/core hooks/voice and hides; resume shows/restarts core/idle. | Dead WPF entry point: no live caller of `SetTubeEmiVisible`; no new UI route invented. |

Pinned dead-caller proof (exit 0, exactly six matches):

```sh
git grep -n -E 'SetTubeEmiVisible|SuspendForTube|ResumeFromTube' db079bf91474c77a5ea67ec306f6337a7de44b83 -- 'ConditioningControlPanel/*.cs' 'ConditioningControlPanel/**/*.cs'
```

The matches are `Services/EmiDesk/EmiDeskService.Tube.cs:11` (definition), `:15` (UI-thread self-dispatch), `:24` / `:28` (suspend/resume), and `Windows/EmiDesk/EmiDeskWindow.Tube.cs:4` / `:15` (definitions). This justifies leaving the dead tube branch alone; it does not justify P122 promotion while live presentation/toss dependencies remain.

## Verification limits

All commands used .NET SDK `10.0.402`; builds used `-c Release`. Test commands exported `DiffEngine_Disabled=true`. Build copies used MSBuild's hardlink options; generated output directories were reclaimed between projects after an initial disk-full failure, then every affected build was retried successfully.

| Command / project | Result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj` | exit 0; 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj` | exit 0; 0 errors; existing warnings |
| `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | exit 0; 0 errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | exit 0; 0 errors; compiled Windows code, not run |
| `bash .github/scripts/core-guards.sh` | exit 0; all Core guards passed; 429 XAML references checked |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --no-build` | exit 0; 3,553 passed, 5 existing Vosk model-dependent skips, 0 failed |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --no-build --filter 'FullyQualifiedName~EmiDeskPresentationTests\|FullyQualifiedName~EmiDeskTossTests'` | exit 0; 13 passed, 0 skipped, 0 failed |
| Broader filter also including `FirstShowStageTests` and `EmiDeskSummonTests` | exit 1; 26 passed, 4 failed creating glyph typefaces in the existing shell tests |
| `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj --no-build -- --render-all <output-directory>` | exit 0; 252/252 views rendered in a temporary profile |
| `git diff --check` | exit 0 |

The four broader-test failures are `FirstShowStageTests.ShownOnceAfterTheFirstRunWizard`, `FirstShowStageTests.TheGuideWalksTheRealShell`, `EmiDeskSummonTests.DockMiniFaceMirrorsHerWhileOut`, and `EmiDeskSummonTests.SettingsSwitchOffDismissesHer`. They throw `Could not create glyphTypeface` for `Segoe UI Black` or `Consolas`, matching the task's documented cloud-font limit. No existing tests were weakened or removed. The new presentation fixture and the toss test ran successfully; the whole Avalonia suite was not run. The bundled Inter font enables the new fixture but does not supply these Windows font families.

Render-all includes `EmiDeskWindow` and `FirstShowStageWindow`; the desk image was inspected and still shows the base's placeholder body, consistent with the remaining art dependency. Render success is not WPF visual parity. The existing first-show panic/Esc cases passed in the broader selection. No real desktop, native stacking, Windows execution or hardware was exercised. The unavailable Vosk model prevents the five existing speech cases from running; no test skip logic changed.

## Assumptions

- Keep the existing Avalonia topmost raise as the platform equivalent of WPF's temporary stage ownership: a shown Avalonia window cannot be re-owned. Native stacking is not proved by headless tests.
- Shared chain/art/moment/wardrobe/idle work stays with `win-emi-desk`, as the base explicitly assigns it; duplicating those systems here would expand the unit and break the smallest-diff rule.
- The dead WPF tube API does not authorize inventing a new caller. The row stays `stub`, not `wired` or `verified`.
