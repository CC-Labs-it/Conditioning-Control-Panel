# FIN4: Profile Privacy & Sharing

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; independent clean snapshot on local `work`, then the requested unit branch.
- Compared the panel markup/forwarders with live handlers in WPF `MainWindow.Browser.cs:1430,1500,1689,2020` and `MainWindow.Patreon.cs:809,843,913`, plus `ProfileSyncService.cs:1710`. Remaining gaps were: inert profile link-notice CTA/state; absent post-link achievement-sharing Ask/Inbox offer; own-card DM visibility/consent refresh/click; six-field sync parity for fresh-install and signed-out choices. Existing presence, local switches, dialog lifetime, offline login gate, and push retry/cooldown paths were reused.
- Wire the profile link CTA through the long-lived privacy panel to the existing account section flow; refresh its notice after account changes.
- Port the WPF achievement-sharing routing rule to Core. Successful Discord linking offers Yes/No, defers to one Inbox entry when a game/modal owns the screen or the panel is hidden/minimized, and re-asks on an explicit Inbox click. Save/repaint only on Yes; skip if already sharing. Register the offer for panic dismissal without consent.
- Paint own-card DM visibility immediately from consent and Discord id; open the stored Discord profile URL through the existing external-launch seam. Do not put an own DM action on another card.
- Restore all six saved privacy fields on every eligible sync. This supersedes the documented interim owned-field policy: fresh-install defaults and signed-out choices must reach the server after login/load, as WPF does. Keep the existing authentication/load, watermark, offline, cooldown, retry and in-flight revoke behavior.
- Add Core routing/payload regression tests and headless UI path tests; retain delivery assertions in the existing restart/cooldown/in-flight tests. The new payload regression failed before the fix (`Expected Boolean; Actual null`).
- Replace the duplicated historical prose with one `wired` ledger row, numbered tester steps, and a decision update. No claim of `verified`.
- Imports: no new trunk code imported. `git fetch origin parity/trunk` read-only reference resolved to `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Existing panel adaptation credit to `4f739180ca68a1e45f62c70a78d2bfbd0f1afcc0` remains. The new rule is adapted directly from WPF at the pinned base.

## Verification limits

- `dotnet build <project> -c Release`: Core, Avalonia, all 12 Linux CI test csprojs (Core, Core.Settings, Core.Content; Avalonia Consent, Language, CatalogueStartup, Migration, Scheduler, Report, GeneralSettings, Snapshot, and main Tests): exit 0, zero errors in every project; existing warnings remain.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3,961 passed, zero failed, five Vosk tests skipped because the model is unavailable. An earlier full `dotnet test ... -c Release` also passed before the final payload correction.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FreshInstallAndSignedOutChangesAreSentAfterLoginWithoutAnOwnedFieldMask`: exit 1 before the fix, omitted boolean; included and passing in the final full suite.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The initial read-only sandbox attempt could not create scratch files; rerun with workspace write permission passed.
- `git diff --cached --check`: exit 0. One ledger row; added-line total remains below 1,000.

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (554 existing warnings); compiled on Linux, not run. The first attempt caught a duplicated routing-rule type, fixed by placing the Core adaptation in `Services.Privacy`; no WPF file changed.
- Avalonia UI tests and render-all are not executed in this environment, per the task's measured font-creation limitation; all test projects must compile. Supervisor/GitHub must run `ProfilePrivacyCompletionTests`, `ProfilePrivacyPanelTests`, `AccountSettingsSectionTests`, `DiscordRichPresenceTests`, and `PanicSurfacesTests` with `DiffEngine_Disabled=true`.
- No live Discord OAuth, proxy mutation, browser launch, real desktop/profile, or Windows execution. WPF reference remains frozen.
- The coverage script `.github/scripts/test-plan-coverage.sh` is absent on this base (attempt returned 127); numbered WPF/Rows steps were supplied and the ledger id occurs once.

## Assumptions

- The task's instruction to finish WPF behavior supersedes the older interim payload decision; all local privacy values now replace server values after eligible login/load, including default false revokes.
- The existing modal closes before starting shell sign-in, as already documented on this head.
- Use the existing panic screen probes and startup-modal state; a hidden/minimized launcher owner is covered by the panel visibility check. No new launcher host is required.
- The Inbox offer uses text without adding an icon. Discord profile launch uses the cross-platform external opener rather than WPF's Windows-only launcher.
