# sync7-port-account-modal-startup

- Adapted WPF commit `bc0bb187ef2c95194be937e96c85f507fea2311c`: required display-name validation and suggestions live in Core; Avalonia adds the required dialog mode, inline save failures, dismiss/retry behaviour, and panic dismissal.
- Sign-in now carries the server flag and awaits the name prompt before later sign-in notices. The unauthenticated availability check precedes rename, avoiding the taken-name cooldown; a failed check leaves the change route authoritative. Account changes during a request cannot write a name into the replacement account.
- Blank names are skipped in settings, the own-profile card and the banner; signed-in unnamed accounts use the existing localized no-name copy.
- Read-only source branch `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No additional trunk implementation imported; adapted source credited above. WPF and sync ledgers are unchanged.

## Commit disposition

`bc0bb187e`: **ported in part; blocked-by-host for restored-session startup scheduling**.

- Ported: `CCP.Core/Services/Account/NeedsDisplayNameRule.cs:15`, `CCP.Core/Services/Account/V2AuthService.cs:761`; tests `NeedsDisplayNameRuleTests` and `DisplayNameAvailabilityTests` cover ask eligibility, shape, blank-name selection, suggestion cleaning, URI encoding and unavailable checks.
- Ported: `CCP.Avalonia/Views/Dialogs/DisplayNameDialog.axaml.cs:40`, `CCP.Avalonia/Views/Windows/MainShellWindow.DisplayNameAsk.cs:15`, `CCP.Avalonia/Views/Windows/MainShellWindow.Login.cs:75`, `CCP.Avalonia/Views/Dialogs/LoginDialog.axaml.cs:145`; `RequiredDisplayNameTests` covers failed-save retry, pending-save controls, panic and cooldown precheck ordering. `PanicSurfaces.cs:37` registers the prompt; the registry contract includes its id.
- Ported: `MainShellWindow.Login.cs:37`, `MainShellWindow.Marquee.cs:180`, `Views/Tabs/DiscordTabView.axaml.cs:167` (all under `CCP.Avalonia/Views`): named fallbacks and signed-in banner copy. Numbered manual steps are in `docs/avalonia-test-plan.md`.
- Already covered on base: `CCP.Core/Services/Account/V2AuthService.cs:179` and `V2DeviceCodeService.cs:85,233` deserialize the nullable server flag. `CCP.Core/Localization/Languages/en.json:4389` and the other nine language files already carry the five required strings.
- Blocked-by-host: `CCP.Avalonia/Platform/StartupLadder.cs:20` explicitly has no modal ladder, launcher hold or game-host park. WPF's restored-session priority-40 `EnqueueModal` cannot be wired faithfully without that shared modal pump. The profile-loaded event exists, but opening a dialog directly from it would bypass arbitration. No new modal infrastructure is invented in this unit.

## Assumptions

- Follow the explicitly requested WPF commit's rule: a blank name asks even when an older server sends false; later sibling changes to this rule are outside this unit.
- Place the shared rule in `ConditioningControlPanel.Services.Account` so the frozen WPF rule and its tests retain unambiguous type names.
- Keep anonymous fallback labels and existing welcome/rename/delete modes intact. Use the existing title-bar multiplication-sign text for the required close control, matching WPF (no new icon asset).
- Startup prompting remains blocked rather than approximating modal timing with the passive Inbox.

## Verification limits

All commands used SDK 10.0.100 and Release configuration; tests used `DiffEngine_Disabled=true`.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | 0 errors |
| `dotnet build <test csproj> -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` for all 13 test projects named in `.github/workflows/build.yml` | 0 each | 0 errors, including `ConditioningControlPanel.Tests` |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed |
| `bash .github/scripts/test-plan-coverage.sh` | 127 | Script absent on this base; numbered tester steps added |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` | 0 | 3981 passed, 5 skipped, 0 failed |
| Focused Core rule/availability tests | 0 | 28 passed |
| Temporarily restore the old two-character minimum; run `NeedsDisplayNameRuleTests` | 1 (expected) | 2 failed, 21 passed; production minimum restored to 3, focused suite passed again |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~RequiredDisplayNameTests|FullyQualifiedName~LoginDeviceCodeTests|FullyQualifiedName~PanicSurfacesTests'` | 1 | Final combined run: 10 font-creation failures, 5 passed; no assertion failures |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~RequiredDisplayNameTests\|FullyQualifiedName~PanicSurfacesTests'` | 0 | 12 passed, 0 failed |
| `git diff --cached --check` | 0 | No whitespace errors |

The new test class uses the shipped font registration, matching the existing panic tests. A combined 14-test run passed before the account-switch case was added; a later combined run hit the documented process-first font setup limitation (`Inter` / `Segoe UI Black` glyphTypeface creation). The final required-name and panic-only run passed all 12 tests. Existing login tests can initialize the process without that font setup; no existing tests were modified to hide this environment limitation.

The final builds use both Windows-targeting flags throughout. Existing compiler warnings remain. An initial full Core run had one `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` failure (expected Loops, received Clips); it passed on the final full run without changes to that area. No tests were weakened or skipped by this unit. The five existing speech tests skip because the Vosk model is unavailable. The headless test profile is sandboxed through `CCP_USERDATA_DIR`; no real desktop or user profile was used. Windows code was compiled but could not be run on Linux; the full Avalonia suite and manual interactive steps were not run. Restored-session modal timing cannot be tested until its host exists.

## Changed files

- `CCP.Avalonia/Views/Dialogs/DisplayNameDialog.axaml`
- `CCP.Avalonia/Views/Dialogs/DisplayNameDialog.axaml.cs`
- `CCP.Avalonia/Views/Dialogs/LoginDialog.axaml.cs`
- `CCP.Avalonia/Views/Tabs/DiscordTabView.axaml.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.DisplayNameAsk.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Login.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Marquee.cs`
- `CCP.Avalonia/Views/Windows/PanicSurfaces.cs`
- `CCP.Core/Services/Account/NeedsDisplayNameRule.cs`
- `CCP.Core/Services/Account/V2AuthService.cs`
- `Tests/CCP.Avalonia.Tests/PanicSurfacesTests.cs`
- `Tests/CCP.Avalonia.Tests/RequiredDisplayNameTests.cs`
- `Tests/CCP.Core.Tests/DisplayNameAvailabilityTests.cs`
- `Tests/CCP.Core.Tests/NeedsDisplayNameRuleTests.cs`
- `docs/avalonia-test-plan.md`
- `docs/cloud/sync7-port-account-modal-startup.md`
