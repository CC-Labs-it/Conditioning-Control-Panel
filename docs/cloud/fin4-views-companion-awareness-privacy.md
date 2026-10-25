# fin4 — companion awareness privacy dependency analysis

- Checked the clean snapshot and reset local `work` from its initial snapshot commit to the required base `c5de71e3b4ec06e9df272e443fa1b7c1d313244c`; created only `avalonia-port/cloud-fin4-views-companion-awareness-privacy` for delivery.
- No product code changed. The last (and only) `views-companion-awareness-privacy` row remains `stub`: its remaining user paths require unported runtime hosts, rather than additional settings UI. No ledger promotion, new surface, panic registration, or tester step is claimed.
- Read the WPF `AwarenessPrivacyView.xaml` and `.xaml.cs`, `Runtime/AwarenessPrivacyRuntimeVm.cs`, `AwarenessAppCandidates.cs`, and observer/startup dependencies at the pinned base below. Compared the Avalonia view, viewmodel, keyword stand-in and shared window-awareness/ledger seams.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported; no trunk implementation credit applies.

## Remaining WPF behaviours

All references in this section are at `c5de71e3b4ec06e9df272e443fa1b7c1d313244c`.

1. **Real frame production and wire summary.** WPF `Runtime/AwarenessPrivacyRuntimeVm.cs:395` reads the observer's actual last frame: closed before paused before legacy before idle; otherwise category, sanitised service/app, scrubbed optional title and duration. Adult frames expose only category/cluster/duration. Avalonia `AwarenessPrivacyViewModel.cs:221` always treats enabled awareness as legacy and has no real-frame summary path. A settings-derived reconstruction would falsely claim to be what was sent.
2. **Exact JSON disclosure.** WPF `BuildWireJson` at line 435 pretty-prints `AwarenessProjection.BuildCloudProjection` of the actual frame, falling back to raw JSON on parse failure; off, legacy and missing frames are empty. Avalonia `WireJson` is always empty. WPF's card displays the cloud projection; local projection helpers in Core do not themselves establish a live producer or a separate local-JSON card UI.
3. **Live styling/cursor lifecycle.** WPF `Sync` at lines 340–344 sets live only when on, unpaused and nonlegacy, with idle copy before the first frame. Code-behind observes that property and starts/stops the cursor as the view changes visibility. Avalonia has cursor and visibility machinery, but `IsWireLive` is permanently false, so no user can reach its live state.
4. **Working v2 warning/review path.** WPF computes legacy from `on && !AwarenessObserver.IsEnabled`, restores the accurate incognito/caption copy when v2 runs, and offers direct consent review (not the once-per-session prompt). Acceptance restarts awareness; decline changes no capability; the kill switch remains untouched. Avalonia deliberately over-warns while on and hides review (`CanReviewConsent = false`). Showing the action or removing the band before an observer exists would promise protections without the promised pipeline.
5. **Recent foreground-process chips.** WPF `RebuildSeen` at line 525 puts the current service first, then the keyword service's in-memory recent foreground ring, de-duplicates case-insensitively, sanitises, removes denied entries, caps at eight and binds one-click hide. Avalonia only has the current service source. WPF `KeywordTriggerService.cs:581–612` supplies a nonpersisted, eight-entry MRU process ring while that service runs, excluding the app's own process at capture.
6. **Picker's fourth candidate source.** WPF `AwarenessAppCandidates.Gather` merges current service, ledger transitions, persisted ids, then the same keyword ring; sanitises/de-duplicates, skips listed entries and caps at 40. Avalonia already has the first three sources and rules, but cannot include the missing ring.
7. **Erasure of the missing hosts' state.** The shared `AwarenessLive` already exposes reset/forget hooks, but no Avalonia observer/arbiter owns pending candidates, committed focus, media/churn state, or pacing state. WPF `App.xaml.cs:2904–2920` connects observer reset, memory, arbiter cooldown and scorer reset/forget to the real instances. That wiring must accompany the host port so wipe/forget cannot leave a pending reaction about erased activity. Existing ledger/frame erasure is already wired; no fake state is introduced here.

The existing dial, entitlement/consent gate, deny and allow editors, retained-app forget, retention, pause, two-step wipe, fine-tuning navigation and 1.5-second visible-only refresh are already wired on this base. The dormant shimmer is unreachable in the live WPF runtime (`IsDormant => false`), not a remaining user behaviour. The live observer and keyword ring do have WPF callers, so P122 does not apply.

## Dependency proof and size decision

Reproducible searches from the pinned base:

```sh
rg -n 'class AwarenessObserver|class ReactionArbiter|class AwarenessV2Routing|class KeywordTriggerService|class KeywordTriggerEngine' CCP.Core CCP.Avalonia
rg -n 'AwarenessLive.Publish\(' CCP.Core CCP.Avalonia
rg -n 'CanReviewConsent|IsWireLive|WireJson|_isLegacyPipeline = on' CCP.Avalonia/Views/Controls/Companion/AwarenessPrivacyViewModel.cs
rg -n 'GetRecentForegroundApps|AwarenessLive.Publish|PublishFrame\(frame' ConditioningControlPanel/Services/Awareness/AwarenessObserver.cs ConditioningControlPanel/Services/KeywordTriggerService.cs ConditioningControlPanel/Services/Awareness/AwarenessAppCandidates.cs
```

The first search finds only `AwarenessObserverPolicy`, not an observer host; the second has no production matches. `CCP.Avalonia/Platform/KeywordTriggerHead.cs` explicitly contains only a no-op demo stub. `MainShellWindow.KeywordTriggers.cs` documents the missing keyboard/OCR/dispatch runtime. Core already owns policy, signals, projection, ledger, scorer and cooldown primitives; these were checked before considering imports.

The required host is **Awareness v2 observer + platform probes + shared reaction arbiter/routing and startup/erasure lifecycle**, with **KeywordTriggerService foreground-ring capture** as the other missing feature. At this base, `wc -l` reports 1,017 observer lines, 498 probe lines, 345 arbiter lines and 293 routing lines (2,153 total). This is a scope indicator, not a claim that comments must be copied. The implementation also needs media capture (`SmtcMediaWatcher`), platform adaptation and focused tests. Even reusing the existing Core policy/projection/scorer leaves a substantial stateful poll pipeline: 20-second dwell gating, AFK accrual/wake handling, fullscreen exits, rapid cycling, milestone/media selection, DND, cancellation, scoring and single-mouth delivery. Porting just a publisher or a second foreground ring would bypass the reference lifecycle and violate the card's exact-wire promise.

Decision: use the task's pure-dependency NOOP exception. A faithful host port with platform seams and per-behaviour tests cannot responsibly fit the 1,000-added-line unit. No speculative interface, synthetic frame or partial host is added. The follow-up must port these named hosts before wiring the seven behaviours above and promoting the row to `wired`.

## Verification limits

This is a documentation-only dependency analysis; no new behaviour test or render proof is claimed. Existing tests cannot prove the absent WPF pipeline on this head. Windows runtime behaviour cannot be exercised on Linux.

- SDK: .NET `10.0.402`. The requested channel installer initially received a discovery-feed HTTP 403, then completed successfully from the stable builds feed; a stable `10.0.100` fallback also installed. No credentials or secret files inspected.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed (3 existing baselined APIs; 424 XAML references checked). The first read-only-sandbox attempt could not create temporary files; rerun with writable temporary storage passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (exit 127); no row added/promoted.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 2,853 passed, 1 failed, 5 skipped (2,859 total). Failure: `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`, `Assert.NotNull` at line 80. This unchanged test also fails alone: exit 1, 0 passed/1 failed. No tests weakened or skipped by this change. The five existing speech skips need the unavailable Vosk model.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter 'FullyQualifiedName~Awareness|FullyQualifiedName~WindowAwarenessService'`: exit 0; 155 passed, 0 failed, 0 skipped.
- Relevant Avalonia classes (`AwarenessPrivacyCardTests`, `AwarenessLedgerPrivacyTests`, `PanicSurfacesTests`) compiled with the full test project; UI tests not run under the task's measured font-creation limitation. Their headless setup lacks the font configuration, and no font-lane fix is included here. No real desktop or real profile was used for UI testing.
- WPF runtime and render-all not run; no P122/render completion or `wired` claim is made.

All 14 requested builds returned exit 0 and `0 Error(s)` using `dotnet build <project> -c Release`:

| Project | Result |
| --- | --- |
| `CCP.Core` | 48 warnings, 0 errors |
| `CCP.Avalonia` | 172 warnings, 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests` | 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests` | 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests` | 0 errors |
| `Tests/CCP.Avalonia.Language.Tests` | 0 errors |
| `Tests/CCP.Avalonia.Report.Tests` | 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests` | 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests` | 0 errors |
| `Tests/CCP.Avalonia.Tests` | 50 warnings, 0 errors |
| `Tests/CCP.Core.Content.Tests` | 0 errors |
| `Tests/CCP.Core.Settings.Tests` | 0 errors |
| `Tests/CCP.Core.Tests` | 0 errors (incremental; test command compiled with existing warnings) |
| `Tests/ConditioningControlPanel.Tests` | 512 warnings, 0 errors; cross-build only |

The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; its referenced WPF application compiled, but neither was run. Other test assemblies were built, not executed, except the Core commands listed above. No Vosk download attempted because the task identifies it as blocked. `git diff --cached --check` passed. The content privacy scan found no local user paths or email addresses.

## Assumptions

- The explicit pure-dependency NOOP rule takes precedence over the general request to finish the row when faithful dependency ports exceed the unit cap.
- Keep the existing privacy-conservative warning, empty wire and hidden review action until the real pipeline exists; preserve the base's documented retention deviation.
