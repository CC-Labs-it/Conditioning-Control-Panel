# fin-views-companion-v2-conversation

- Host the existing live CompanionPickerCard above Personality presets; refresh it on each visit. Its personality gate, samples, on/fit and perk controls are reused. The advanced link opens the existing prompt editor and re-syncs presets afterward.
- Adapt WPF KnowledgeLinksEditor into one control shared by Companion > Links and CompanionPromptEditorDialog. Preserve add-dialog validation, cancellation, selected removal, the no-selection notice, empty-state copy, ordering, page reloads and immediate page saves. Prompt-dialog edits remain staged until Save and are discarded by Cancel.
- Add the portrait DustField with WPF intensity 0.8, density 0.65 and RGB(244,134,211), using AmbientFxCanvas and its shared clock. Park on tab hide, detach, window close and panic. Register the knowledge-link and perk dialogs with panic as well.
- Add focused behavioral headless tests in CompanionConversationFinishTests; retain the existing conversation, page, picker and Core rule tests.
- Keep the ledger row `stub`: the dependencies and remaining differences below are explicit. Remove the superseded claim that the AI page lacks the preferred-name and recap hosts.
- Reference source fetched read-only from `origin/parity/trunk`, inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Adapted behavior credits: `5681c132a62988f57dbf54fad6f4fdf76688bec8` (page picker and shared knowledge links), `48f5872004ca1fedfd2cba1d9d37e2d5c0484ad8` (conversation page/AI redirects), `9db154ab658b63608aa19f692b1d5f0a7883d8d8` (Personality page after the AI move). Existing head files were merged, not replaced with trunk versions. No Core or WPF implementation files changed.

## WPF gap inventory

Compared the ledger's cited ConversationPage XAML/code-behind, ConversationPageVm, ConversationRecap, PreferredNameEditor and PerkPicker, plus their live Personality/Links page callers and KnowledgeLinksEditor, against base `888cdbffe4d2ecf3967cee4a4334557ee533e54d`.

| Gap at base | Result in this unit |
| --- | --- |
| PersonalityPage lacks PickerHost and the live picker/perk entry | Host added in WPF order; retain and refresh on return. Existing picker behavior is reused. |
| More personality options should open detached PersonalityStudio and sync | Existing full prompt editor used as an explicit ponytail fallback; detached draft/fork semantics remain missing. |
| LinksPage lacks the second knowledge-links card, reload and immediate saves | Added; one editor shared with the prompt dialog, matching WPF's save modes. |
| Prompt dialog has its own inline links implementation instead of the shared editor | Extracted without changing its staged Save/Discard contract. |
| Portrait lacks StageDust | Added with WPF tuning and shared-clock lifecycle. |
| PerkPicker cannot restart the XP-drain timer as WPF SetPerk does | Dependency: the head lacks the CompanionService XP lifecycle. Choice persistence remains as on the base. |
| Picker's avatar-set switch/emote portrait, portrait fade and single-set hint | Existing dependency tracked by `views-companion-picker-card` and avatar-tube switching; not changed here. |
| DailyLimit does not call ConversionSignals.NoteCompanionCap for a later Inbox row | Dependency: ConversionSignals and the corresponding Inbox flow have no port on this base. Immediate failure text/retry rules already match Core ConversationRules. |
| Preview fallback sheet: asks toggle, familiarity, allowance, borrowed zones/library, sheet resize, Escape, focus restoration and account/hide cleanup | Not on the production WPF path, as shown below. Remains unported; no P122 promotion or render-all claim. |
| AI PreferredNameEditor + ConversationRecap hosts allegedly absent | Stale ledger text: both hosts already exist in AiPage.OnShown on this base. No duplicate controls added. |

The existing shared-session projection, suffix reconciliation, watch/activity/ask handling, start/sign-in, reply delivery, moderation refusal, cancellation/retry, identity clearing, composer and confirmed new-thread behavior were read and retained. Sign-in continues to the account tab when this head's engine LoginCommand is null.

## Pinned WPF caller evidence

At `888cdbffe4d2ecf3967cee4a4334557ee533e54d`:

- `ConditioningControlPanel/Views/Tabs/CompanionTabView.xaml.cs:39-40` collapses Room and mounts ConversationPage as its sibling.
- `ConditioningControlPanel/Views/Controls/Companion/V2/ConversationPage.xaml.cs:110` redirects OpenSheet to a registered page before constructing the fallback sheet.
- `ConditioningControlPanel/MainWindow/MainWindow.CompanionTabs.cs:14-20` registers personality, permissions, companionlinks and companionai.
- Evidence commands: `git grep -n -E 'IsRegisteredNavTab|PageHost.Children.Add|Room.Visibility' 888cdbffe4d2ecf3967cee4a4334557ee533e54d -- ConditioningControlPanel/Views/Controls/Companion/V2/ConversationPage.xaml.cs ConditioningControlPanel/Views/Tabs/CompanionTabView.xaml.cs` and `git grep -n 'RegisterNavTab' 888cdbffe4d2ecf3967cee4a4334557ee533e54d -- ConditioningControlPanel/MainWindow/MainWindow.CompanionTabs.cs`.

## Verification limits

- SDK: .NET 10.0.402. The provided snapshot was read-only and at the wrong commit; used the authorized fresh-clone fallback and verified the exact expected base before branching.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- Every Linux test project in `.github/workflows/build.yml` built in Release with exit 0 and zero errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 warnings, zero errors. Windows/WPF code compiled; no Windows execution was possible here.
- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release`: exit 0, 242 warnings, zero errors, including the new tests. Earlier compilation caught and corrected a missing XAML namespace. The first all-project sweep exhausted disk while copying native runtimes; removing generated outputs between builds allowed the complete sweep to pass.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed; 429 XAML references checked. Its initial read-only invocation could not create temporary files; reran with workspace write access.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: first run exit 1 (3,544 passed, one DtrhRemoteMediaTests failure, five skipped); repeat exit 0 (3,545 passed, zero failed, five skipped, 3,550 total). The initial media failure expected Loops but received Clips. Its isolated rerun passed (one test, exit 0); `git diff --exit-code` confirms Core and Core tests are identical to the base. No tests were weakened or skipped by this unit.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~ConversationRulesTests`: exit 0, 24 passed. The five existing Vosk tests skip because the model is unavailable in this environment.
- Avalonia headless behavior tests were **compiled but not run**, as required by the supplied cloud font limitation. The cloud-test-fonts lane is absent from this base. No render-all or before-fix runtime failure proof is claimed. GitHub/rcheck must run `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~CompanionConversationFinishTests|FullyQualifiedName~ConversationPageTests|FullyQualifiedName~CompanionPagesTests|FullyQualifiedName~CompanionPickerCardTests|FullyQualifiedName~PanicSurfacesTests'` with `DiffEngine_Disabled=true`.
- The headless tests inherit the assembly's CCP_USERDATA_DIR sandbox, memory-only SecretStore and disabled real foreground-window probe. No real desktop or user profile was used.
- No release, deployment or PR was created.

## Assumptions

- Reuse this head's full prompt editor for the hosted picker's advanced link until PersonalityStudio is ported; this follows the existing presets zone's fallback. It does not claim WPF's detached draft/fork semantics.
- Companion-picker-host's ModManager cleanup/reference-render work belongs to the picker unit. This unit adds only the Personality host named in this row.
- The preview fallback sheet is outside the real user's registered-page path. Keep it documented and keep `stub`; the required render-all evidence for P122 is unavailable in this cloud environment.
- Do not fabricate the missing CompanionService XP timer or ConversionSignals/Inbox flow in this view unit.
