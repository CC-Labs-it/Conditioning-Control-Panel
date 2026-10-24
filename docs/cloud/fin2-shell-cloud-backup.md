# fin2-shell-cloud-backup: dependency analysis

- Documentation only; no application code, test, panic surface or parity status change.
- Base/reference: `a65dbbe2f4935a4797b32efd87513054dd39e1d2`.
- `shell-cloud-backup` remains `stub`. All remaining paths require the unported
  **Core ProfileSync settings backup/restore/GDPR-export transport**. Account identity
  is available, but is not a replacement for that transport.
- No trunk code imported. `parity/trunk` was fetched read-only; no imported commit
  credits apply.

## Remaining WPF behaviours

Pinned reference: `ConditioningControlPanel/MainWindow/MainWindow.CloudBackup.cs`.

- Backup now: absent ProfileSync is a no-op; disable button, show backing-up label,
  force upload, show localized success/failure/exception dialog, refresh status only
  on success, always re-enable and restore the label.
- Restore: absent ProfileSync is a no-op; refuse a locked session before confirmation;
  require Yes, disable button and show restoring label; null result warns and returns.
  Preserve UnifiedId, PlayerLevel, PlayerXP, SkillPoints, UnlockedSkills,
  HighestLevelEver, IsSeason0Og, CurrentSeason, PendingSkillsResetAck,
  UserDisplayName, PatreonTier, PatreonPremiumValidUntil, PatreonLabValidUntil,
  InviteGrantUntil, LastPatreonVerification and OpenRouterApiKey from current settings;
  then PreserveLocalOnlyFields, RestoreFrom, reload controls under the loading guard,
  show success, and always reset the button. Exceptions show the restore-error dialog.
- Export: disable button and show exporting label; fetch server GDPR JSON, surface
  returned errors/null data, then offer a JSON save picker with dated
  `my-data-export-yyyy-MM-dd.json` name. Cancellation writes nothing; saving shows
  the destination in the success dialog. Exceptions warn; finally reset the button.
- Status: require ProfileSync and cloud identity; display local backup date in
  `MMM d, yyyy h:mm tt` format plus app version, no-backup copy, or lookup-error copy.
- ReloadSettingsUiAfterRestore: reseed all settings controls, log failures and clear
  the loading flag in finally. Needed after manual and startup restore.
- Startup (`ConditioningControlPanel/App.xaml.cs`): missing-settings installs only;
  consume factory-reset marker and suppress the offer; hook ProfileLoaded before a
  five-second delay and try again on profile arrival. Serialize offers with a
  two-minute gate and claim once per launch; skip unattended rigs and absent identity.
  Fetch metadata AND deserialize the backup before offering its restore row. Build
  welcome-back restore/flavour/season/patch-note content, queue at startup priority 30
  (dispatcher fallback without ladder), apply only the user's selected restore and
  preserve local identity/progression/entitlements and machine-local settings.
- Automatic backup on exit and the service's existing automatic upload callers.
  WPF seeds CoreSettingsHooks.CloudBackup in App.xaml.cs:683; the shared settings
  save path invokes it with a 30-second attempt gate. Avalonia does not seed it.
  The transport includes offline/identity guards, five-minute debounce, forced
  bypass, daily-recovery suppression, payload exclusions/budget trimming, oversized
  response suppression, authenticated requests and recovery/error handling.

## Dependency evidence

At the pinned SHA, AccountSettingsSection.axaml.cs:472-480 contains empty backup,
restore and export handlers; its cloud section/export button remain hidden.
Privacy Policy already calls ExternalOpener with the WPF URL (line 485), so it is
not missing. DataSettingsSection's live PhraseBackupService export/import is local
phrase data, not the server GDPR export. MainShellWindow.CloudBackup.cs explicitly
redirects ownership to AccountSettingsSection rather than duplicating handlers.

`ProfileSyncService`, `SettingsBackupBudget`, `ExcludedBackupProperties` and
`PreserveLocalOnlyFields` remain in the WPF head. The local-wins helper also keeps
content-folder/webhook/presence/timestamp/conversion/mod state, Chaster and audio
playlist properties, and local asset lists when the backup omits them for budget.
A picker, dialog or settings refresh alone cannot reach any missing cloud behaviour.

Reproduce source evidence at the pinned SHA:

```sh
git grep -n -E 'BackupSettingsAsync|RestoreSettingsFromCloudAsync|GetSettingsBackupInfoAsync|ExportDataAsync|PreserveLocalOnlyFields' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- CCP.Core CCP.Avalonia ConditioningControlPanel/MainWindow/MainWindow.CloudBackup.cs ConditioningControlPanel/Services/Settings/ProfileSyncService.cs
git grep -n -E 'CheckCloudSettingsRestoreAsync|TryOfferWelcomeBackAsync|BackupSettingsAsync' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- ConditioningControlPanel/App.xaml.cs
```

These are live WPF callers; P122 (dead reference behaviour) does not apply.
`docs/avalonia-decisions.md:676-687` records the unresolved server contract and
cross-OS path translation and explicitly rejects export-only wiring. This is
supporting context; the task's dependency-only rule is the reason for this NOOP.
A follow-up must port the transport and resolve those payload semantics before
exposing the controls. The ledger is deliberately unchanged, with its missing list.

## Assumptions

- Treat the WPF-only ProfileSync backup/export transport as the named unported
  dependency under the task's NOOP rule; do not introduce partial buttons or a
  second shell handler copy.
- Preserve the existing stub row verbatim for this dependency-only unit.

## Verification limits

- SDK: .NET 10.0.402. Channel discovery initially encountered HTTP 403; a fixed
  10.0.100 install succeeded and channel installation subsequently supplied 10.0.402.
- `dotnet build -c Release`: CCP.Core and CCP.Avalonia passed (exit 0, zero errors).
- All Linux CI test projects built (exit 0, zero errors): CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests and the
  separate CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report,
  Scheduler and Snapshot test projects. Existing compiler warnings remain.
- `bash .github/scripts/core-guards.sh`: passed (exit 0).
- `git diff --check`: passed (exit 0).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`:
  passed (exit 0), 3,953 passed, zero failed, five skipped, 3,958 total, 36 seconds.
- Relevant headless class: AccountSettingsSectionTests. Compiled, not executed:
  this checkout's account test builder has no Inter font setup; the supplied cloud
  font-creation limitation applies. Supervisor/GitHub execution remains necessary.
- Windows test build attempted with EnableWindowsTargeting=true and
  ValidateExecutableReferencesMatchSelfContained=false: exit 1, 64 errors from
  asset inputs omitted by the sparse checkout (including february.png and logo.png).
  Windows code was not successfully compiled or executed here.
- Initial Linux attempts failed for sparse asset inputs; restored required
  resources/fixtures and repeated. Disk exhaustion (exit 134) while copying native
  dependencies was resolved by deleting only generated test bin directories and
  building remaining projects one at a time. No tests were weakened or changed.
- No Vosk model download attempted; the existing five Vosk skips remain.
- No new behaviour means no new behavioural tests or panic registration. No
  render-all claim: P122 is not invoked. No production server requests were made.
