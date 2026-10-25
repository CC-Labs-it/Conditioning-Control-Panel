# fin4-shell-awareness: dependency analysis

- No runtime code or ledger promotion: `shell-awareness` remains `stub`. This is the
  dependency-only outcome authorized by the unit instructions, not completed parity.
- Base/reference pinned at `b6d389a871b8ef5ade86e49c4935b4c5b62d5798`.
- Inspected read-only `parity/trunk` at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported. Credit that revision
  for the reusable keyword engine/OCR/head split identified below.

## WPF inventory and remaining gaps

Reference: `ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs` at the
pinned base (all 1,206 lines), compared with the Avalonia tab's main partial,
preset partial, keyword drawer, and shell-awareness partial.

| WPF behavior | Base implementation / remaining dependency |
| --- | --- |
| Tab-open seed, premium gate, saved flags, cooldown stops and labels | Editors/gating exist. Runtime flags do not arm an engine. |
| Master ON starts keywords, keyboard hook, then OCR if enabled; OFF stops keywords/OCR and releases hook only when panic/leash permits (393–441) | Missing live keyword host, hook integration and OCR lifecycle. Existing panic hooks must be reused, never replaced. |
| Keyboard ON enables master; OFF preserves OCR/master and stops hook only when unused (480–500) | Master routing exists; source lifecycle is missing. |
| OCR access rejection, start only with master, stop otherwise, drawer sync (443–478) | Access/settings/drawer exist; screen capture and recognition service absent. |
| Ignore-own-UI, ignore-own-focus, loop protection, global/per-word cooldowns | Settings exist; no keyword runtime consumes them. Need own-window exclusion, app-scope evaluation and temporal suppression at match time. |
| Live fire subscription; newest five entries from twenty-entry ring, keyword/action snapshot chips, uppercase source, relative time and singular/plural total (133–368) | No fire producer/ring in this head; feed remains empty. WPF time thresholds are 10s/60s/60m/24h, then HH:mm; hidden tab ignores fire refresh. New action icons must use Fluent equivalents. |
| Live/Off dot and breathing status (370–391) | Static label/dot exists; live pulse cannot be truthfully driven before engine lifecycle exists. Use shared FX clock when ported. |
| App-scope mode/list editing, Enter/blur canonicalization (525–574, 640–694) | Ported through Core rules; runtime enforcement absent. |
| Recently focused app chips, excluding own/already-listed apps; click adds once, saves and rebuilds chips (575–638) | No keyword foreground resolver/ring; depends on same host. Manual app list remains usable. |
| Highlight on/off, swatches, valid hex, capture visibility (716–823) | Editors/drawer sync exist. No OCR word rectangles reach a highlight renderer; no live capture-affinity refresh. Overlay lifecycle/panic teardown required with host. |
| Preset cards, activate/deactivate/delete/create, AI badge, detail dialog, advanced link to installed preset or drawer (826–1203) | User paths already ported in AwarenessTabView.Presets.cs and main tab partial. WPF's shared-service PresetsChanged fan-out is not reproduced across separate service instances; current local mutation/dialog-close/tab-show paths refresh directly. Shared runtime/preset host should own that notification when introduced. |
| Tutorial and rescued keyword/OCR editors | UI exists; demo calls KeywordTriggerHead.Engine.FireDemo, whose base body does nothing. Requires real keyword engine. |
| Row's additional v2 observer/ledger gap | Core policies/data types exist, but observer/lifecycle/speech pipeline is not hosted. Legacy title observer and consent/AI routing are already implemented; they are not the OCR/keyword engine. |

## Why this cannot fit the unit

The base `CCP.Avalonia/Platform/KeywordTriggerHead.cs` is 14 lines and exposes only
an empty `FireDemo`. There is no Core `KeywordTriggerEngine`, Avalonia
`ScreenOcrService`, or live highlight host. The WPF implementations are 2,126
lines (keyword service), 336 (OCR), and 508 (highlight), before shell wiring/tests.
Reading the base first avoids overwriting the already ported editors and presets.

Trunk offers a smaller adapted split, but these four files alone total **1,309
lines**, before the screen reader, key listener, highlight surface, shell wiring,
panic registration and behavior tests:

- `CCP.Core/Services/KeywordTriggers/KeywordTriggerEngine.cs`: 452.
- `CCP.Core/Services/KeywordTriggers/KeywordTriggerEngine.Ocr.cs`: 204.
- `CCP.Avalonia/Platform/KeywordTriggerHead.cs`: 457 (actions and platform input).
- `CCP.Avalonia/Platform/ScreenOcrService.cs`: 196 (timer and confirmation scans).

The OCR contract includes 200ms confirmations (maximum three quick scans),
non-overlap, stop/dispose safety and own-window exclusion. A reduced matcher or
fake feed would omit user-visible behavior and privacy safeguards. Even importing
these four intact exceeds 1,000 additions. Separately, trunk's v2
`AwarenessObserver` is 1,013 lines and `AwarenessLive` is 228 before its host,
platform probes and tests. Importing either feature safely needs its own unit(s).

This is **not P122**: the WPF handlers are live. Pinned grep proof:
`Views/Tabs/AwarenessTabView.xaml.cs:77–80` forwards the master handler;
`MainWindow/MainWindow.TabNavigation.cs:599` calls `SyncAwarenessTabUI`;
`MainWindow/MainWindow.Patreon.cs:220` refreshes it on entitlement change.
These paths prevent declaring dead-code parity or promoting to wired.

## Assumptions

- Treat the missing OCR/keyword runtime (including sources, dispatch and overlays)
  and the missing v2 observer host as unported feature dependencies. Preserve all
  existing settings/preset/title-observer behavior and the honest stub ledger.
- Use the prescribed fallback clone after read-only FETCH_HEAD failure. No local
  requester changes are present; the fallback was clean at the exact pinned base.
- No new surface/behavior: no new panic registration or test-plan row is warranted.

## Verification limits

Verification results follow. Windows behavior cannot
be run on this Linux worker. No live screen capture, desktop input observation,
AI service or real user profile is used. No render-all claim or P122 promotion.

- SDK: .NET 10.0.402 installed with dotnet-install (channel lookup initially
  returned 403; the SDK download succeeded).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. Initial
  read-only sandbox attempt could not create temporary files; rerun with write
  access succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and
  `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors
  (48 and 192 warnings respectively). Build invocations also passed
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj
  -c Release`: first run invalidated by disk exhaustion (95 failed). After freeing
  generated outputs, rerun with `--no-build`: exit 1, **3,959 passed, 2 failed,
  5 skipped** of 3,966. Failures: DtrhRemoteMediaTests cold-cache test expected
  Loops, got Clips; BoardTilesTests frame budget measured 21.75ms. No runtime/test
  source differs from the pinned base. The five Vosk tests skipped for absent model;
  no skip or test was added/changed by this unit.
- Sequential builds reclaim each completed test project's generated binaries to
  fit worker storage. The initial GeneralSettings build hit disk exhaustion;
  its retry succeeded with zero errors. No source files were removed.
- Test-plan coverage script is absent on this base; no ledger row changed.
- Workflow Linux test projects all compile with zero errors (exit 0):
  CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests,
  CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests,
  CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests,
  CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests,
  CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests,
  CCP.Avalonia.Snapshot.Tests. Each was built with `dotnet build <project> -c
  Release`; the batch used the Windows-targeting/validation properties above.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj
  -c Release --no-build --filter 'FullyQualifiedName~Awareness'`: exit 1,
  **4 passed, 10 failed** of 14. Every failure is the known worker font creation
  issue (`Could not create glyphTypeface`, Inter or Segoe UI Black). Tests were
  attempted, not claimed green; visual/live behavior remains unverified.
- `git diff --check`: exit 0.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings,
  zero errors. WPF/Windows code compiled only; no Windows tests executed.
- No full Avalonia suite, live desktop run, render-all, real OCR or AI verification.
