# remote-core-a: Services/Remote pure rules move to CCP.Core (unit A)

Base: `avalonia-port/possession-core-a`. No Avalonia UI in this unit.

## What changed

- Inventory of `ConditioningControlPanel/Services/Remote` against Core before moving anything:
  - already in Core (earlier units): `RemoteHapticPlayer` / `RemoteHapticPlan` (git mv in port-remote) and
    the TimeProvider driver `CCP.Core/Services/RemoteControl/CoreRemoteHapticDriver.cs`;
  - not in Core, pure: `RemoteEasy.cs` (RemoteEasy, EasedOpacity), `RemoteActionLabels.cs`,
    `RemoteScreenState.cs` (RemoteScreenEvent, RemoteEventRing, RemoteScreenInputs, RemoteScreenState,
    RemoteControllerName);
  - not in Core, WPF-bound: `RemoteControlService.Screen.cs`, `RemoteControlService.V2.cs` (partials of the
    WPF service, read App/MainWindow) and `RemoteHapticDriver.cs` (DispatcherTimer).
- `git mv` of the three pure files to `CCP.Core/Services/RemoteControl/` (100% renames, no edits). They were
  already App-free and the Core project already references Newtonsoft.Json, so no seam, embedded resource
  or core-guards baseline entry was needed. Namespace stays `ConditioningControlPanel.Services.Remote`, so
  WPF (which references CCP.Core) compiles unchanged.
- Folder: `Services/RemoteControl/`, not a new `Services/Remote/`, because Core's existing remote files
  (RemoteRelay, RemoteCommands, RemoteHapticPlayer, CoreRemoteHapticDriver) already live there. One folder,
  one copy of each rule.
- Tests: `RemoteEasyTests` and `RemoteScreenStateTests` `git mv`'d to `Tests/CCP.Core.Tests` (namespace
  `CCP.Core.Tests`). The RemoteActionLabels test moved with them out of the WPF `RemoteHapticPlayerTests`.
  RemoteEasyTests' one source scan of a WPF file (`RemoteControlService.V2.cs` signal wiring) stays in the WPF
  suite, now in `RemoteHudRulesTests`, and resolves the file through `SourceRoots.ReadProductFile`.
- Trunk credit: `4bdfaafd8` (parity remote: RemoteEasy / RemoteActionLabels / RemoteControllerName in Core)
  and `73a50e3d6` (WPF copies dropped as byte-identical to Core). Our moved files are byte-identical to
  trunk's Core copies. Here they arrive as renames that keep their history, and RemoteScreenState comes with
  them (trunk left it in WPF).

## Next units

- remote-core-b: route the WPF `RemoteHapticDriver` (DispatcherTimer) to Core `CoreRemoteHapticDriver`
  (TimeProvider; marshal `Changed` back to the UI thread), so one driver remains.
- remote-core-c: `ConditioningControlPanel/Windows/RemoteHud/RemoteHudRules.cs` (pure, outside Services/Remote)
  to Core, with its WPF tests.
- remote-core-d: the pure halves of `RemoteControlService.V2.cs` / `.Screen.cs` (signal payload, screen
  input gathering behind a provider Func) and of `ConditioningControlPanel/Services/RemoteControlService.cs`
  that `RemoteRelay` does not yet own; then the Avalonia HUD / live preview (trunk `4bdfaafd8` head half,
  `d39969827`, `bc67fe306`).

## Verification limits

- `dotnet build CCP.Core/CCP.Core.csproj`: 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: 0 errors.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: 0 errors. This compiles the WPF head too, but
  the WPF tests and the WPF app were NOT run (Windows-only).
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1244 passed, 5 skipped, 2 failed. Both failures are unrelated and
  also fail on the unmodified base in this container:
  `LoopbackOAuthTests...(host: "[::1]")` (no IPv6 loopback here) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so the folder can be deleted).
- Moved tests: `--filter RemoteEasyTests|RemoteScreenStateTests`: 22/22 passed. Fail-proven: with
  `RemoteEasy.Floor` set to 0.2 and the `haptic_pattern` label changed, `Easy_halves_down...` and
  `Labels_name_the_new_commands...` both went red; the code was then restored.
- No Avalonia code changed, so no CCP.Avalonia.Tests class is affected and none was run.
