# Workshop behavior cell: dependency analysis

- Audited `views-companion-workshop-behavior-cell` at base
  `50faccb5ef5de7a4a19968ccb67489cb4905c81c`; its single ledger row remains `stub`.
- Applied the unit's dependency-only rule: no implementation or ledger change.
  The remaining behavior needs the shared shortcut route, tube idle-bark scheduler,
  and live Arcademy host described below. No new surface needs panic registration.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  No code was imported; there are no imported trunk commits to credit.

## WPF comparison

References below are pinned to the base SHA above. Read both
`ConditioningControlPanel/Views/Controls/Companion/Runtime/WorkshopBehaviorCell.xaml`
and its code-behind, then traced every forward into the WPF shell/tube.

The existing Avalonia path is Companion -> Workshop -> Behavior. It already seeds
and saves the idle interval (20–300 seconds), bubble duration (1–10 seconds),
whisper mute and spoken-voice mute separately from their enable flags. Browser
pause/resume reaches the live Home browser without persisting the pause flag,
preserves the saved browser mute, and mirrors the tube menu. Midnight glass reads
the persisted wallet, disables/unchecks the row when unowned, and refreshes the
tube after a change. Chat capture saves/reset-to-default and reapplies the local
shell/tube binding. These are existing behaviors, not changes in this branch.

Every remaining gap for this row:

| Missing behavior | Live WPF reference | Unported dependency on this head |
| --- | --- | --- |
| Camera pill capture/cancel/reset, saved-combo label, immediate shell binding and global rearm | `MainWindow/MainWindow.SessionIO.cs:1274` (`ApplyCameraShortcutTo`), `:1381` (`RefreshCameraShortcutLabel`), `:1403` (`BtnCameraShortcut_Click`) | Shared camera shortcut route to `ToggleWebcamFromHotkey`; `WorkshopAccordion` leaves `CameraShortcutRequested` unsubscribed. `Platform/WebcamTracker.cs` exists, but the route does not. The Devices settings twin records the same dependency. |
| Global chat shortcut, including unregister when disabled, silent OS-conflict fallback, restore/foreground/show tube/open chat; camera global shortcut, including restoring the owner for consent before starting, and stopping a running tracker | `MainWindow/MainWindow.SessionIO.cs:1160` (`ApplyGlobalChatHotkey`), `:1202` (`BringToForegroundAndOpenChat`), `:1324` (`ApplyGlobalCameraHotkey`); `MainWindow/MainWindow.BlinkTrainer.cs:350` | `GlobalHotkeyService` chat/camera registration and shell action routes; tracked by `shell-session-io`. Existing panic and EMI summon hooks do not register these actions. Saving `ChatShortcutGlobal` does not implement global activation. |
| Immediately restart the running idle timer when its slider changes, with UI dispatch and the new cadence; tick rereads the setting, checks speech readiness and dispatches idle barks/fallback phrases | `MainWindow/MainWindow.Patreon.cs:1374`; `AvatarTube/AvatarTubeWindow.Speech.cs:1534–1599`; constructor calls `StartIdleTimer` at `AvatarTubeWindow.xaml.cs:356` | Tube idle-giggle scheduler / idle bark source (`avatar-tube-window`, `feat-companion-barks`). Avalonia saves the interval but has no `StartIdleTimer`, `RestartIdleTimer` or tube `OnIdleTick` to restart. |
| Midnight-glass reveal uses the current live purchase before it is flushed, falling back to disk when the school is closed and failing closed on unreadable ownership | `WorkshopBehaviorCell.xaml.cs:28` (`SyncTubeMidnightRow`); `Services/Arcademy/ArcademyHostService.cs:852` (`WalletOwnsSku`) | Live Arcademy host and its `_meta` store. Core provides `ArcademyMetaStore` and the persisted-wallet reader, but Avalonia has no host that owns a live purchase store. |

WPF callers are live, so P122 does **not** apply and the row cannot be promoted to
`wired`. Native-browser and Windows execution are verification limits, not another
missing cell implementation.

## Reproducible source evidence

Run against the pinned base, not a moving branch:

```sh
git grep -n -E 'BtnChatShortcut_Click|BtnCameraShortcut_Click|RestartIdleTimer|WalletOwnsSku' 50faccb5ef5de7a4a19968ccb67489cb4905c81c -- ConditioningControlPanel/Views/Controls/Companion/Runtime/WorkshopBehaviorCell.xaml.cs ConditioningControlPanel/MainWindow/MainWindow.Patreon.cs ConditioningControlPanel/MainWindow/MainWindow.SessionIO.cs
git grep -n -E 'StartIdleTimer|RestartIdleTimer|OnIdleTick' 50faccb5ef5de7a4a19968ccb67489cb4905c81c -- CCP.Avalonia/Views/AvatarTube
git grep -n -E 'CameraShortcutRequested|ApplyGlobalChatHotkey|ApplyGlobalCameraHotkey|ToggleWebcamFromHotkey|WalletOwnsSku' 50faccb5ef5de7a4a19968ccb67489cb4905c81c -- CCP.Avalonia
```

The first command finds the actual WPF forwards/handlers. The second returns no
matches (exit 1). The third finds only camera event declaration/emission and
comments about the missing routes; it finds no subscription, action implementation
or live-wallet call. Read `WorkshopAccordion.axaml.cs:36–109`,
`DevicesSettingsSection.axaml.cs:811–818`, and `MainShellWindow.SessionIO.cs:48`.
The old Workshop comment saying no webcam engine exists is stale; the absent
shortcut route, not the tracker itself, is the dependency.

## Verification limits

- SDK: .NET `10.0.402`. Required installer succeeded after its initial
  `ci.dot.net` 403 retries, using the supported builds endpoint.
- `dotnet build <project> -c Release`: exit 0 and **0 errors** for `CCP.Core`,
  `CCP.Avalonia`, and all 11 Linux test projects named in `.github/workflows/build.yml`:
  `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`,
  `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`,
  `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`,
  `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and
  `CCP.Avalonia.Snapshot.Tests`. Existing compiler/analyzer warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0; both guards passed, including
  424 XAML type references. Core's existing three baseline entries remain.
- With `DiffEngine_Disabled=true`,
  `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`:
  exit 0; **2,812 passed, 0 failed, 5 skipped, 2,817 total**, duration 59 seconds.
  The five existing speech tests skip without the unavailable Vosk model; tests
  were not changed and `CCP_REQUIRE_VOSK` was not enabled.
- Initial later-project builds and the first Core test attempt exhausted the
  workspace disk (exit 134). Removed generated Avalonia test binaries only and
  reran affected builds sequentially, removing each generated test output after
  its successful compilation; all required Linux builds and Core tests then passed.
- `WorkshopBehaviorBrowserTests` and other Avalonia headless classes were compiled
  but **not run**, per the supplied cloud font-creation limitation. No
  `cloud-test-fonts` change was found in this base's history. The supervisor's
  GitHub rcheck must run the relevant UI classes. Render-all was not run for the
  same environment limitation; this branch makes no visual or P122 claim.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0,
  **0 errors**, 512 existing warnings, elapsed 5 minutes 48 seconds.
  WPF application and test assemblies compiled; Windows code was **not run**.
- Windows runtime, global OS shortcuts, real camera/native browser, and live
  Arcademy purchases were not run. No real desktop or user profile was touched.
- Pinned source checks: WPF forwards found (exit 0, nine lines); Avalonia tube
  idle methods absent (exit 1); eight Avalonia dependency references are event
  declarations/emission or comments, with no live implementation (exit 0).
- `git diff --check`: exit 0. Only this analysis document is added.

## Assumptions

- The explicit dependency-only rule takes precedence over the general instruction
  to implement gaps: importing three other features into this cell would expand
  the unit and bypass their shared callers.
- Retain the single existing ledger row verbatim and `stub`; this analysis does
  not establish complete WPF behavior or visual verification.
- No new behavior means no new tests, fail-before-fix claim, or panic surface.

NOOP: shared global chat/camera shortcut route, tube idle-bark scheduler, and live Arcademy host.
