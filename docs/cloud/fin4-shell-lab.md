# fin4-shell-lab: dependency audit

- Audited `shell-lab` at pinned base `4702e2e55cd05db6c7d3208fa1ec372f4f106837` against `ConditioningControlPanel/MainWindow/MainWindow.Lab.cs` and its live launch targets. The existing single ledger row stays `stub`; no code, status promotion, new surface, or tester coverage claim is added.
- Dependency-only outcome: finishing this row requires the Bureau/Goon/Arcademy web-game hosts and the live Avalonia Possession host. These integrations together cannot fit the 1000-added-line unit cap. A button that merely opens an unimplemented bridge would not complete the WPF behavior.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported; no trunk credit is claimed for implementation.

## WPF behavior and remaining gaps

All paths below are repository-relative; source evidence is pinned to the base above. Historical prose in the ledger and generated stub comments is not proof that a behavior is absent today.

| WPF behavior | Base state / dependency |
| --- | --- |
| Lockdown premium gate, duration selection, double warning, activation; setup/active panels, clock masking, strict/panic greying, title badge navigation, secret exit | Present across `LockdownTabView`, `MainShellWindow.Lockdown.cs`, Core Lockdown and Win32 input. The warning deliberately omits Possession and dose promises because their live hosts are absent. System-key enforcement is Windows-only. |
| Crimson shell/card theme, level glow, 600 ms flash, restore and shutdown cleanup | Already implemented, covered by `LabLockdownThemeTests`; old ledger absence claims are superseded. |
| Possession initialization before lockdown service null-check; rung event subscription; Settle seed, localized rung text and five ember pips; dim/clear/unsubscribe on exit | Missing live Avalonia shell host/director binding. Core has `PossessionDirector`, contracts, deck and audio, but `MainShellWindow.Possession.cs` remains a stub and `LockdownTabView.ShowLockdownState` forcibly hides both controls. Four isolated head effects in `PossessionRuntime` do not supply the WPF target walk, full catalog, scenes, warden, pointer or director lifecycle. |
| First possession rules: defer through PresentOrInbox; spend intro flag only when opened; first-time feature popup plus immediate rules bark | No activation caller on this head. Must accompany the live Possession integration rather than promise effects that do not run. The reusable intro popup alone does not satisfy this lifecycle. |
| Emergency Exit sendback: restart clock and possession ladder, immediate page/badge repaint and readout reset to Settle | Clock/badge repaint exists. The ladder/readout has no Possession host; Emergency Exit games are a separate unported feature. `PreviewShowLockdownActivePanel` likewise has no live possession preview host. |
| Bureau launch/focus, failure feedback, authenticated live page, local frame decoding, packs/drop/catalogue, submit/inbox/stats/gold, heartbeat and exit watchdog, recover once | Missing Bureau head host. `BureauHostRules` and `BureauIndexService` exist in Core; no Bureau entry in `GameWindow.Games`. WPF `Services/Bureau/BureauHostService.cs` is 555 lines before adapting WebView2 raw additional-object file drops, frame decoding and lifecycle tests. |
| Arcademy launch, boot-failure retry question only when inactive, localized errors; host tier/audio-only gates and idempotency | Missing Arcademy head protocol. Core economy/meta/punch-card/sync helpers exist, but no game registry entry or init/reply/reward/boot/exit integration. WPF `Services/Arcademy/ArcademyHostService.cs` is 6140 lines; reusable Core logic reduces this, but a generic ready/init bridge cannot replace its game-specific protocol. |
| Back Room launch with localized failure feedback, free retry without boot-failed prompt | Also in the pinned WPF Lab.cs (lines 302 onward), despite omission from the historical ledger member list. Missing Back Room game host/registry entry. |
| Goon, Chaos and QuickStartChaos legacy member names | `BtnStartGoon_Click`, `BtnStartChaos_Click`, `BtnQuickStartChaos_Click` are absent from pinned WPF Lab.cs. They must not be resurrected as duplicate handlers. Current game routes belong to the launcher. Goon still lacks its complete head bridge (WPF Goon host: 2151 lines), while native `ChaosHubWindow`/`ChaosRunHost` and registered Dtrh `GameWindow` already exist. Their existence supersedes the blanket claim that every Chaos host is missing. |
| FYP premium refusal then focus/launch, failure feedback | Real route exists: `MainShellWindow.ShowTab("fyp")` -> `GameWindow.Launch(FypId)` with `FypGate`; do not add another bypassing entry point. Full feed parity belongs to its host row. |
| Quiz focus-existing, AI login gate, fullscreen/drone flags, report refresh; intake active focus, pass-aware gate/upsell, AI gate, first-ever ducking; pop quiz settings/test | Ownership moved to `GradedIntakeTabView`; quiz window/report refresh, pop-quiz editors/test, pass gates and an actual `IntakeHostWindow` already exist. Remaining source differences in that owning row include checking pass/AI before focusing an already-active intake (WPF focuses first) and the spent-pass CTA going to Account rather than the priced Tier 1 vault card. These are not missing shell event shims; do not duplicate their handlers here. |

## Reproducible source checks

```sh
rg '^\| shell-lab \|' docs/avalonia-parity.md
rg -n '^        (internal|private) ' ConditioningControlPanel/MainWindow/MainWindow.Lab.cs
rg -n 'BtnStart(Goon|Chaos)|BtnQuickStartChaos' ConditioningControlPanel/MainWindow/MainWindow.Lab.cs
rg -n 'PossessionDirector|AttachHost' CCP.Avalonia --glob '*.cs'
rg -n 'PossessionRung.IsVisible|PossessionPips.IsVisible' CCP.Avalonia/Views/Tabs/LockdownTabView.axaml.cs
wc -l ConditioningControlPanel/Services/{Bureau/BureauHostService,GoonGame/GoonHostService,Arcademy/ArcademyHostService}.cs
```

The legacy-member grep has no matches; the director grep finds comments, not a construction/attachment. This is not a P122 promotion: other live WPF behaviors remain unported, so no render-all result could justify `wired` for the row.

## Verification limits

- .NET SDK 10.0.402 installed. Channel discovery initially encountered HTTP 403; installing the explicit published version succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 48 warnings, 0 errors. `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (existing warnings).
- All 12 Linux test projects named in `.github/workflows/build.yml` built with `dotnet build <project> -c Release`: exit 0, 0 errors. CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings and Core.Tests.
- Initial sequential builds filled the fallback filesystem with duplicated test output; several processes aborted. Removed only generated test `bin` directories, then rebuilt each project separately, deleting its generated output after checking it. Results above are the completed retries.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3957 passed, 0 failed, 5 skipped, total 3962. The Vosk model is unavailable; speech integration coverage remains unproved.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~LabLockdownThemeTests|FullyQualifiedName~LabWebcamStatusTests'`: exit 0; 5 passed, 0 failed, 0 skipped. These focused tests did run successfully despite the historical font warning in the task.
- No behavior changed, so no new tests are introduced. No new panic registration is needed because there is no new surface. No real desktop, account, webcam or game server was exercised. Full Avalonia suite and render-all were not run; no visual or Windows runtime parity claim is made.

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors; compiled only, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0 (`All Core guards passed.`).
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent from this pinned base. No ledger row was added or promoted.
- `git diff --check`: exit 0. Documentation-only change remains below the 1000-added-line cap.

## Assumptions

- Apply the task's dependency-only exception to the full row: preserve the existing stub instead of adding isolated launch buttons or a readout whose actual subsystem is absent.
- Treat the pinned WPF source as authoritative over historical names and stale absence claims in the ledger. No P122 exception is used for the whole row.
