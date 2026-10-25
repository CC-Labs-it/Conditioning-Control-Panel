# fin4-win-emi-book: dependency analysis (PR-body draft)

- Audited the single `win-emi-book` stub row against its cited WPF book markup and code, its router, and the live desk speech/offer/moment callers at base `57b9d757349b1aa1ce07eaf98895ee09acfce697`.
- No product code, tests, panic surfaces, or ledger status changed. The remaining work requires the unported `win-emi-desk` offer/channel/chain host, which cannot be completed with behavioral tests inside this unit's 1,000-added-line budget.
- Read-only import reference: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; existing base implementations take precedence.
- Retained `stub`: there are live WPF callers, so P122 cannot justify `wired`.

## Remaining WPF behavior

All base file locations below are pinned to the base SHA above.

| Gap | WPF reference and behavior | Base head evidence / required host |
| --- | --- | --- |
| Offer ownership and margin parking | `Windows/EmiDesk/EmiBookWindow.xaml.cs:967` sends a priority-2 `book.margin.<id>` draw to `SpeakLine`. `Windows/EmiDesk/EmiDeskWindow.Bubble.cs:503` parks the latest line while `AskLive`, without acknowledging it or replacing the question. | Avalonia `EmiBookWindow.axaml.cs:1032` already makes the identical draw, but `EmiDeskWindow.Bubble.cs:28` speaks immediately. No ask owner or chips exist on this base. Requires a real ask host, not an unused parked slot. |
| Release ordering and timing | WPF Bubble `AnswerAsk:762` clears ask/chips, records answer, fires `askAnswered`, speaks reply, runs effect, then releases parked speech. `ReleaseParked:849` clears the slot and schedules it after 900 ms. Ignoring an ask runs the reaction/thinking beats before release; teardown clears parked speech. | Requires yes/no effects, Esc/hover-close/fullscreen cancellation, timers and teardown in the missing offer host. A second parked line replaces the first (the implementation wins over the stale field comment). |
| Hold/channel ownership | WPF `SpeakLine:517` cancels a face hold and declines/closes an active channel before acknowledging speech. `HoldFace:546` cancels channel/chain, acknowledges silently, holds a face, and restores idle. | Avalonia's speech adapter has no hold timer, channel ownership, or `HoldFace`. Core hold decisions already exist in `EmiLineEngine`; the absent host must consume them and own cancellation. |
| Chain/body-pose integration | WPF Bubble `SpeakLine:527` plays wordless chains. WPF window `Say:1071` uses the real chain player with face/body/FX/move hooks, idle restoration and interruption. | Avalonia `PlayChain:895` only calls its continuation; `SetPose:830` tracks a string without loading art. Existing speech cadence/voice is real, but full host chain/pose behavior is absent. Requires the player and art/hooks, not another speech-only clock. |
| Book navigation moment effects | WPF book `Go:797` calls target Open then fires `effectFired` with `channel=bookGo,target`; the tour branch closes book, starts tutorial, then fires `channel=bookTour,tour`. | Avalonia Go already performs navigation/tutorials, but its comment at `:905` identifies the missing desk moment bus. WPF service `Fire:661` broadcasts even while away, arms safety holds, marshals to UI, and resolves line/ask/hold through `Speak:727`. Merely adding a no-op event would leave the WPF reactions unreachable. |
| Offer-driven book entry | WPF `Services/EmiDesk/EmiBook.cs` defines `bookOffer` / `book:open`; WPF `EmiOffers.cs:210-244` tests content/out/presentation and opens the book after accepting the offer. | Core `EmiLineEngine.EffectFeasibleProbe` exists but no head offer dispatcher wires it; no live ask path can select this effect. Existing question-mark-chip/ring book entry and bookmark are already implemented. |

WPF paths in this table are relative to `ConditioningControlPanel/`; Avalonia paths are relative to `CCP.Avalonia/Views/Windows/EmiDesk/`.

The row's native Wayland caveat is a platform limitation, not an additional live book path: `CCP.Avalonia/Program.cs:240-256` explicitly uses X11 on Linux, including XWayland in a Wayland session, because the shipped Avalonia backend has no native Wayland support. `Platform/X11Overlay.cs` already implements passive X11/Win32 windows. A future native Wayland/layer-shell host is outside this book unit; no unsupported protocol shim was added.

## Dependency scope and cap

The actual WPF host contains 944 lines of bubble/ask/channel logic, 710 lines of offer feasibility/effects, and 598 lines of chain data/player logic, before the moment service and adapted head hooks/tests. This is a connected host: offers dispatch effects and moments; moments select offers, lines and holds; chains own pose/FX/idle cancellation.

The pinned trunk has an adapted implementation, but it is not a small independent book patch:

| Trunk file under `CCP.Avalonia/Views/Windows/EmiDesk/` | Lines |
| --- | ---: |
| `EmiDeskWindow.Ask.cs` | 412 |
| `EmiDeskService.Moments.cs` | 255 |
| `EmiDeskService.Offers.cs` | 202 |
| `EmiChains.cs` | 692 |
| `EmiDeskWindow.Chains.cs` | 454 |
| Total | 2015 |

These are scope measurements, not a proposed blind import: Core already owns `EmiSpeech`, `EmiVox`, `EmiState` and line-engine behavior and must keep owning shared logic. Even omitting duplicate speech pieces, a full offer/channel/chain host plus base integration and behavior tests exceeds this unit. Porting only a book event or parking without a live ask would not finish any remaining user-visible path. Per the dependency-only rule, this branch records the analysis instead.

Reproduce the live-caller proof at the pinned base:

```sh
git grep -n -E 'SpeakMargin|bookGo|bookTour' 57b9d757349b1aa1ce07eaf98895ee09acfce697 -- ConditioningControlPanel/Windows/EmiDesk/EmiBookWindow.xaml.cs
git grep -n -E 'AskLive|ReleaseParked|EmiOffers.Run' 57b9d757349b1aa1ce07eaf98895ee09acfce697 -- ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Bubble.cs
git grep -n -E 'ShowAsk|HoldFace|DrawAsk' 57b9d757349b1aa1ce07eaf98895ee09acfce697 -- ConditioningControlPanel/Services/EmiDesk/EmiDeskService.cs
```

## Verification limits

Commands used .NET SDK 10.0.402 and Release configuration. `DiffEngine_Disabled=true` was exported for the Core test run.

- Checkout verification: original snapshot was clean but at the wrong SHA; fetch failed with read-only `FETCH_HEAD`. Authorized fallback clone was clean at the exact required base SHA on local `work`; unit branch created from that commit. The origin base ref also equals that SHA.
- `git fetch origin parity/trunk`: exit 0. Pinned live-caller `git grep` commands above: exit 0 with active `SpeakMargin`, `bookGo`, `bookTour`, `ReleaseParked`, `ShowAsk` and `HoldFace` sites.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors (48 existing warnings).
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (172 existing warnings).
- `dotnet build <test-project> -c Release` for every Linux-buildable test csproj named in `.github/workflows/build.yml`: all 11 exit 0, 0 errors: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `dotnet test Tests/CCP.Core.Tests -c Release`: clean rerun exit 0, “Failed: 0, Passed: 2839, Skipped: 5, Total: 2844.” Existing Vosk tests self-skip without the blocked model; no test or skip policy changed.
- Initial batch builds and the first Core run exhausted the temporary disk while copying large native binaries. Generated test outputs were removed; sequential builds with cleanup and the full Core rerun above passed. Initial Core result was 56 failed / 2783 passed / 5 skipped during disk exhaustion; superseded by the clean rerun.
- Avalonia runtime tests (`EmiBookDemoTests`, `EmiBookNavigationTests`, `EmiBookSpeechTests`, `EmiRingWiringTests`) and render-all were not run, following the supplied cloud font-creation limit. The existing head test assembly compiled successfully; no head runtime or visual validation is claimed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors, 360 warnings. Initial attempts hit disk exhaustion and then MSB4166 (child node exited prematurely); serial retry passed. WPF compiled but was not run; Windows desktop/native passive-window behavior cannot run here.
- `git diff --check`: exit 0. No test-plan coverage script exists on this base.

 No new behavior or surface was introduced, so no new behavioral tests or panic registration is applicable. The existing row remains unchanged; this base has no `docs/avalonia-test-plan.md` or test-plan coverage script.

## Assumptions

- The explicit infrastructure fallback overrides the earlier stop-on-fetch-failure instruction when the snapshot is read-only; the independent clone was verified at the required commit before inspection or changes.
- The explicit dependency-only outcome applies to the interconnected desk offer/channel/chain host, rather than shipping partial hooks that cannot exercise the WPF behavior.
- Native Wayland is a future backend dependency; the current supported Linux path remains X11/XWayland.

NOOP: unported win-emi-desk offer/channel/chain/moment host exceeds the unit cap.
