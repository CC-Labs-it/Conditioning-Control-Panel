# wire-dialogs-username-picker (row `dialogs-username-picker`, stays **stub**)

- **Not wired, on purpose.** WPF never opens `UsernamePickerDialog`: its only caller,
  `AccountService.HandlePostAuthV2Async`, is reached solely from `MainWindow.AccountShell.cs`
  `BtnQuickPatreonLogin_Click` / `BtnQuickDiscordLogin_Click`, and no XAML or code subscribes either
  handler (re-checked on this base; WPF's pending-registration path `CheckPendingRegistrationAsync` ->
  `HandlePostAuthAsync` uses `DisplayNameDialog`, and the live registration prompt is LoginDialog's username
  panel on both heads). Wiring it in Avalonia would mean inventing a trigger WPF does not have.
- **Added** `Tests/CCP.Avalonia.Tests/UsernamePickerDialogTests.cs` (headless) pinning the already-ported
  dialog against the WPF code-behind:
  - `LocalRules_GateConfirm_WithWpfTextAndColours`: `ConfigureForNewUser` hides OG/suggestion panels; under 3
    chars -> `login_name_min_3_chars` Orange, over 30 -> `login_name_max_30_chars` Orange, blank ->
    `login_enter_unique_display_name` Gray; Confirm stays disabled and a click on it is a no-op.
  - `Cancel_ClosesFalse_WithNoName`: `ShowDialog<bool?>` returns false, `ChosenDisplayName` null.
- **Ledger** row `dialogs-username-picker`: evidence added, status kept `stub`, follow-up lane
  `wire-dialogs-username-picker-b` (blocked on an owner decision: WPF must gain a live caller first).
- No product code changed; no trunk commits imported (`origin/parity/trunk` carries the same dialog and the same
  "no caller" note, from 2721487c4 / 8a319aceb, already on this base). No panic surface added (none opened).

## Verification limits
- `dotnet test Tests/CCP.Avalonia.Tests --filter UsernamePickerDialogTests|IconGuard|PanicSurfaces`: 13/13 passed.
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 6 skipped, 2 failed, both environmental and untouched by
  this branch (`LoopbackOAuthTests ... host: "[::1]"`: no IPv6 loopback in the container;
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: running as root defeats the read-only setup).
- `dotnet build` OK: CCP.Core, CCP.Avalonia and every Linux test csproj in build.yml. `core-guards.sh`: passed.
- The debounced availability check (500 ms, hardcoded live endpoint) is not exercised by tests, to keep tests off
  the real server. WPF projects not built (Windows-only). Not visual: no `--render-all` run.
