- Finish `shell-account-shell` against WPF at `f4495678d66871436e08a4d5256e119fa5fda821`.
- Preserve WPF's synchronous UI dispatch for update-status notifications, including the pill's tag, localized caption and tooltip.
- Add focused headless coverage for worker-thread update notifications and both real language pickers reaching the restart banner; selecting the same language preserves an announcement.
- Connect account refreshes to the welcome banner, with WPF's trimmed unified-name / Patreon-name / Discord-name order, nameless signed-in copy, signed-out copy, and offline precedence.
- Reconcile the banner/notification gap in the ledger. `ApplyLanguageSelection` already writes the real `TxtBannerSecondary`; `MainShellWindow.Marquee.cs` rotates that beat every four seconds with a 500 ms fade. Desktop startup in `App.axaml.cs` already attaches `App.Notifications` to `NotificationHost` and wires `CoreProgram.NotifyProvider`.
- No trunk code imported. Read-only `parity/trunk` inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; existing base implementations win.

## WPF behavior audit

Gaps found and fixed: `ShowUpdateAvailableButton` lacked WPF's UI dispatch; live account refreshes did not repaint the welcome banner; its provider-name and nameless signed-in fallbacks were absent. The language restart banner and toast-host startup paths were already implemented on the base.

| WPF behavior | Avalonia user path |
| --- | --- |
| Account settings and legacy App Info account entry | `OpenAppSettingsSection("account")`; `ShowTab("patreon")` redirects there, as do the profile menu and tier gates. The actual App Info surface belongs to its own row. |
| Awareness / Exclusives navigation | Existing navigation doors and Home Vault tile reach `ShowTab`; the Vault tile calls `BtnPatreonExclusives_Click`. |
| Discord invite | Home Discord bubble calls `BtnDiscord_Click` and `ExternalOpener` with `DiscordLinks.Invite`. Sandbox profiles refuse public browser launches. |
| Rich Presence refusal, checkbox synchronization, persisted state and service update | Home quick switch and Profile privacy switch use `HasLinkedDiscord`, the refusal dialog and `App.ApplyRichPresence`; covered by `DiscordRichPresenceTests`. |
| Account welcome banner | Sign-in/restore/link/unlink/logout reach `UpdateQuickLoginUI`, which now repaints the existing banner. Offline name wins, then trimmed unified / Patreon / Discord names, then signed-in-no-name or signed-out copy, as WPF `MainWindow.Marquee.cs:125-154`. |
| Language list/order/current selection, reentrancy guard, saved language/live localization and restart banner | Header picker and Settings General picker share `ApplyLanguageSelection`; existing language suite covers persistence and flags, new focused test covers the banner and unchanged-language edge. First-run language uses its existing wizard path. |
| Update pill URL-first click, updater fallback, status tag/copy/tooltip | `BtnUpdateAvailable_Click`, `AppUpdater` and server-banner path; covered by `HomeMarqueeTests` / `AppUpdaterTests` and the new worker-notification test. |
| Visual-descendant helper | The intake-help outside-click exemption uses Avalonia visual ancestry in `SettingsTabView.axaml.cs`; it is not another account surface. |

No new window, overlay, effect, timer or other panic-managed surface is introduced; existing shell and dialog registrations remain sufficient.

## Dead WPF paths (P122)

Pinned reference: `f4495678d66871436e08a4d5256e119fa5fda821`.

```sh
git grep -n -E 'BtnQuick(Patreon|Discord)Login_Click|HandleQuickPatreonLoginAsync|HandleDiscordLoginAsync' f4495678d66871436e08a4d5256e119fa5fda821 -- ConditioningControlPanel
```

The only source matches are `MainWindow.AccountShell.cs:103,105,108,175,177,180`: the two handler declarations, their calls to private helpers, and the helper declarations. The extra match in `docs/behavior-design-audit.md:145` is historical prose, not a caller. There is no XAML binding or other caller. The old provider-specific OAuth/logout helpers are therefore dead paths. Their quick-UI wrappers have other live callers: their behavior is carried by `UpdateQuickLoginUI` and the reactive `AccountSettingsSection` provider cards; the underlying unified login/account features remain in their own rows.

```sh
git grep -n 'BtnCheckUpdates_Click' f4495678d66871436e08a4d5256e119fa5fda821 -- ConditioningControlPanel/MainWindow ConditioningControlPanel/Views/Controls/AppSettings
```

The shell method at `MainWindow.AccountShell.cs:458` has no caller. `UpdatesSettingsSection.xaml:57` binds to that control's own handler at `UpdatesSettingsSection.xaml.cs:45`, which already has its Avalonia counterpart. Its dormant shell-only "Checking…" affordance is not user-visible WPF behavior.

## Verification limits

SDK: .NET 10.0.402. Tests used `DiffEngine_Disabled=true` and isolated `CCP_USERDATA_DIR` profiles.

| Command / scope | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | Build succeeded; 0 errors. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` (repeated after final change) | 0 | Build succeeded; 0 errors; existing warnings. |
| `dotnet build` for all 11 Linux test projects listed in `.github/workflows/build.yml`, `-c Release` | 0 each | Build succeeded; 0 errors. Main Avalonia tests rebuilt after final change. |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 0 errors; existing warnings. WPF app and tests compiled, not run on Linux. |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed; 429 XAML references checked. |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0 | 2846 passed, 0 failed, 5 skipped (existing Vosk model-dependent tests; model unavailable in this environment). |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~ShellAccountShellTests` (final code) | 0 | 8 passed, 0 failed, 0 skipped; uses the app's existing bundled-font builder. |
| Same test project, filter `HomeMarqueeTests`, `DiscordRichPresenceTests`, `AppUpdaterTests`, `NotificationServiceTests`, `ShellSyncSevenTests` (each term prefixed `FullyQualifiedName~`) | 0 | 30 passed before the final welcome-banner change. |
| Final combined run of the new fixture and those related classes | 1 | 11 passed, 27 failed in the known cloud font-creation path (`Segoe UI Black`, `Could not create glyphTypeface`). No font infrastructure or tests were weakened; the new fixture passed separately. Final combined-suite confirmation belongs on the supervisor's GitHub runner. |
| `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --render-all <temporary-output>` (final code) | 0 | 225/225 views rendered; shell PNG inspected. |
| `git diff --check` | 0 | No whitespace errors. |

Linux test projects compiled: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, and the Avalonia `CatalogueStartup`, `Consent`, `GeneralSettings`, `Language`, `Report`, `Scheduler`, and `Snapshot` test projects. The isolated suites were compiled, not executed.

Failure proof: the worker-notification test failed before its fix with Avalonia's cross-thread access exception. All six account-banner cases failed before their fix because the previous account's announcement remained visible. An initial banner test asserted the first animated opacity frame; it now asserts the existing 500 ms fade's target value. One pre-fix build attempt exited 134 after generated WPF outputs exhausted disk space; deleting only completed generated outputs allowed the retry and final builds to succeed.

No Windows UI, installed updater, real OAuth/provider services or public browser launch was run. No Vosk model download was retried (the task records the environment's 403 restriction).

## Assumptions

- Reuse the existing welcome banner for account updates and the existing notification host for toasts; avoid adding a duplicate notification mechanism.
- Match the WPF account-shell file at the required base, retaining this head's established language pill and cross-platform updater behavior.
- Exclude the dead provider quick-login and shell check-button handlers under P122, rather than resurrecting removed controls.
