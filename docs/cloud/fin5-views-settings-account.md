# fin5-views-settings-account

- Base verified at `8ab265b39dafcd52cb09f1a97ac7977fdecdb691`.
- Reused the existing Avalonia Premium view for the missing WPF Account & Plans host: localized compact header, tier plates, Premium door and invites in WPF order. Removes the nested scroller; hides the spotlight and shelf. The room canvas follows the checked Account section and Settings visibility; Premium's motion clock never starts for this mode.
- Invite ticket now opens Settings Account, keeping WPF's 300 ms delay, forced read and bring-into-view. Card reads repaint the ticket. Existing inline shell and owned InvitePanel dialog panic handling reused; no new top-level surface.
- Focused headless regression and tester instructions added; views-settings-account remains stub.
- No trunk code imported. Read-only parity/trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Implementation adapts the pinned base WPF reference and existing head.

## WPF comparison and remaining gaps

Read the row's cited AccountSettingsSection XAML and forwarding code, MainWindow.CloudBackup.cs, ProfileSyncService backup region, SettingsBackupBudget, and PlansMode/visibility/invite handlers. Provider/link/logout, Chaster, presence, tier refresh, backup metadata, GDPR export, privacy/provider URLs and sharing offers already have live head paths. Existing documented provider sign-out and Fluent icon deviations are retained.

The comparison found missing PlansView (WPF AccountSettingsSection.xaml:102); this unit ports its compact host. Full Premium art/flair/launch gaps remain tracked by shell-exclusives and views-tab-exclusives, outside this account host.

Remaining account behaviors, all part of the cloud backup dependency:

1. Backup now: ProfileSync availability guard, disable/backing-up label, forced upload, success/failure/error dialogs, successful metadata refresh, finally restore button/label (MainWindow.CloudBackup.cs:59-103).
2. Restore: service guard, session-lock refusal before confirmation, Yes/No confirmation, disable/restoring label, download/decompress/deserialize, null/error dialogs, local identity/progression/entitlement/API-key keep list plus PreserveLocalOnlyFields, RestoreFrom, whole Settings UI reload, completion dialog and finally restore button/label (:105-188).
3. ReloadSettingsUiAfterRestore (:43-57): load all settings under the loading guard, log failures, always clear the guard. Startup cloud restore and automatic backup are host dependencies tracked by shell-cloud-backup; not wired independently here.

These are live WPF callers: AccountSettingsSection forwards both buttons into MainWindow.CloudBackup; P122 does not apply. Avalonia still hides both buttons and has empty handlers. Core V2AuthService has metadata/export/recovery but no BackupSettingsAsync, RestoreSettingsFromCloudAsync, exclusion policy, budget encoder or local-field preservation. Core SettingsService/RestoreFrom already exists.

The 2026-10-09 rows-cloud-remember decision in docs/avalonia-decisions.md leaves the server contract and cross-OS asset-path translation unresolved. WPF ProfileSyncService:4556-4897 includes exclusion, local preservation, force/debounce/offline/daily-recovery guards, gzip/base64 upload, 413 suppression and restore; SettingsBackupBudget adds the 660,000-byte limit, ordered trimming and put-back behavior. Copying endpoints would not establish a correct Windows/Linux round trip for nonempty asset lists. The dependency is a defined cross-OS backup contract/client with behavior tests and whole-settings UI reload. This unit completes the independent visible host gap and retains the cloud gaps rather than inventing that contract.

## Verification limits

- Installed .NET SDK 10.0.402 from the official builds feed after the channel installer encountered a 403.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all eleven Linux workflow test projects passed with zero errors. CatalogueStartup initially hit MSB4166; retry with `-m:1 -p:UseSharedCompilation=false` passed (0 warnings, 0 errors).
- WPF test project built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings, 0 errors. Windows/WPF/native OAuth were compiled, not executed.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed (424 XAML references checked). The first sandboxed invocation could not create temp files; the permitted rerun passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 1, 2,822 passed / 1 failed / 5 skipped, total 2,828. The failure was BoardTilesTests.A_frame_fits_the_budget (29.11 ms while builds were active). Its isolated no-build recheck passed (1/1). The existing five Vosk skips reflect the unavailable model; no tests were weakened or newly skipped.
- Combined no-build filter `AccountSettingsSectionTests|InvitePanelTests|ExclusivesVault`: exit 1, 8 passed / 22 failed; failures report Segoe UI Black glyphTypeface creation. Those suites need the font-enabled supervisor environment for a combined run.
- The new Plans test failed on the base at Assert.NotNull(PlansView), then passed after the port (1/1). An intermediate Account-only run had 16 passed / 1 failed because the second navigation assertion did not wait for dispatcher settling. Both navigation transitions now run the dispatcher before assertions; the focused test passes with its existing bundled-font mappings.
- Final Account-only class: `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~AccountSettingsSectionTests`, with DiffEngine_Disabled=true: exit 0, 17 passed / 0 failed / 0 skipped (1 m 52 s).
- test-plan-coverage.sh is absent from this base (attempt exit 127); a numbered WPF/Rows tester step was added regardless. `git diff --check` passed; one ledger row remains for the id.
- No live cloud backup round trip, native desktop run or render-all was performed. Existing owned shell and InvitePanel panic handling is reused; no new window was added. Tests retain sandbox profiles, in-memory secrets and fake HTTP.

## Assumptions

- Use the pinned checkout's WPF PlansMode and reuse this head's tier art, InvitePanel and shared clock.
- ponytail: Retain the existing Premium invites card for compatibility; Account gains the WPF copy and the ticket opens Account.
- Retain the documented cloud path/server-contract boundary; metadata and GDPR export remain live.
