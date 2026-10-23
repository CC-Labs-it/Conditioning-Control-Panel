# fin-views-companion-room

- Restore the WPF Z2 dormant shimmer when the live chat state becomes Dormant after load. Observe State as well as IsThinking, on the Normal dispatcher queue; ignore work queued by a replaced or unloaded viewmodel.
- Match CmpShimmerSweepStoryboard: 250 ms delay, 1.4 s cubic ease in/out, top-left transform origin, one sweep per view lifetime. Continue using DoubleTransition on TranslateTransform; no Animation.RunAsync, new clock, surface, or panic registration is needed.
- Add CompanionRoomShimmerTests covering late entry, an unload before the queued callback followed by reload, the WPF timing/origin contract, and no replay on subsequent entries.
- Keep views-companion-room **stub**, with an explicit dependency list. Do not promote a partly reachable composition to wired.
- Imported trunk commits: none. Fetched and inspected parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no CommunityPromptService exists in its Core tree either. Existing head files were preserved.

## WPF comparison

Pinned reference/base: `89057bb49c6d44dd910a2c66cf4abaf625d20882`.

| Behaviour in the cited WPF reference | Avalonia disposition |
| --- | --- |
| Zone order, scroll container, 60/40 shelf, 1100/1160 hysteresis, invalid-width handling | Already composed; ApplyShelfLayout preserves the thresholds and column moves. |
| Claim navigator on load; clear it and detach chat turn log on unload; re-claim and sync on return | Already ported; CompanionRoomClaimTests covers the lifecycle and Engine re-sync. |
| Effective-visibility parking and resuming from current state | Already ported, including ancestor watches and shared BeatLoop minimisation parking; CompanionRoomThinkingTests. |
| Engine/Workshop reveal and deferred awareness focus | Legacy paths already ported. V2 awareness rehosting/navigation depends on the missing AwarenessTabView.Companion privacy/tuning host (see below). |
| Enter sends, Shift+Enter stays available, CanSend/empty/command guards | Already ported; draft sends through the live Core brain. |
| Follow thread collection changes and thinking starts; release property and collection subscriptions on unload | Already ported, with deferred scroll and observer identity checks. Turns is a stable ObservableCollection on this head, so WPF replacement-of-Turns handling has no producer here. |
| Thinking dots: three cloned pulses, 450 ms each way, 150 ms stagger; stop restores opacity | Already shared through CompanionThinkingPulse and BeatLoop; existing Core and head tests. |
| Dormant shimmer on load and on State notification, once per view lifetime | Load existed; late State notification and the missing 250 ms delay are restored by this change. |
| WPF wheel relay on the nested thread | Avalonia native ScrollViewer chaining supplies the edge relay, as already used by MemoryDiaryView and WorkshopLibraryCell; no WPF routed-event helper is imported. |
| ICompanionRoomVm swaps and hidden named compat passthroughs | No runtime Avalonia caller: each zone owns its concrete VM. Preview-only mock state swaps remain with the separately tracked preview row. |

## Remaining gaps and ownership

1. **Community prompts**: the live Z4 Community command is null and the readout uses the prompt id instead of the installed name. WPF routes the command to BtnBrowsePrompts_Click and resolves the name through App.CommunityPrompts.GetInstalledPrompt. CommunityPromptService and the browser/install workflow are WPF-only. This is the shared dependency of `views-companion-make-her-yours` and `views-companion-workshop-community-cell`; implementing another service/browser in this composition would duplicate their unit.
2. **V2 awareness host**: WPF CompanionRoomView.RevealWorkshop routes the awareness anchor to AwarenessTabView.RevealCompanionTuning, and FocusAwareness switches tabs before focusing the privacy view. WPF AwarenessTabView.Companion.cs reparents privacy and hosts the tuning cell. Avalonia lacks that host and method; moving only the navigation would leave focus/scroll pointed at the old room. The existing legacy room targets remain until that feature is ported.
3. **Preview variants**: ShowVariant changes the strip label without replacing every zone VM. This belongs to `views-companion-room-preview`, and is dead in normal WPF runtime. No P122 promotion is claimed for the room: the other two gaps are live dependencies, and render-all is not proven here.
4. The independently tracked child-zone rows retain their own gaps (for example Engine live actions and Workshop actions). This change does not claim those zones are fully wired.

Pinned grep evidence for the preview (search only production WPF source):

```sh
git grep -n -E 'CompanionRoomPreview\.(MaybeShow|IsRequested)|CompanionRoomPreviewWindow\.Launch|new CompanionRoomPreviewWindow' 89057bb49c6d44dd910a2c66cf4abaf625d20882 -- 'ConditioningControlPanel/*.cs' 'ConditioningControlPanel/**/*.cs'
```

The only matches are in CompanionRoomPreview.cs (documentation plus the two internal Launch calls) and CompanionRoomPreviewWindow.xaml.cs (Launch constructing its own window). There is no startup, shell, or other production caller. MaybeShow is inert outside DEBUG. Mock state swaps have no live WPF user path.

## Verification limits

All commands used .NET SDK 10.0.402 and Release configuration.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | 48 warnings, 0 errors. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | 0 warnings, 0 errors (incremental). |
| `dotnet build Tests/<project>/<project>.csproj -c Release`, for every Linux test project in build.yml | 0 each | All eleven compiled with 0 errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests. |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed; 424 XAML references checked. |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` | 0 | Failed 0, passed 2824, skipped 5, total 2829. Existing Vosk cases skip without the unavailable model; no tests changed to skip. |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 512 warnings, 0 errors; compiled but not run on Linux. |
| `git diff --cached --check` | 0 | No whitespace errors. |

The first guard attempt could not write its temporary files in the read-only sandbox; the rerun with write access passed. The build sweep initially exhausted disk space copying native test assets (exit 134). Generated outputs were removed between sequential builds. CatalogueStartup first passed, then hit an MSBuild child-node failure on the post-exhaustion retry (exit 1); a single-node retry with `--no-restore -m:1 -nr:false -p:BuildProjectReferences=false` passed with 0 warnings/errors. These were infrastructure failures; no source or tests were weakened.

Pending supervisor headless command (not run here):

```sh
DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~CompanionRoomShimmerTests|FullyQualifiedName~CompanionRoomThinkingTests|FullyQualifiedName~CompanionRoomClaimTests|FullyQualifiedName~HerRoomChatMemoryTests'
```

The supplied cloud limitation says Avalonia headless tests fail during font creation until cloud-test-fonts lands. That fix is absent from this base. The new regression cases and existing relevant head tests are compiled, but are not executed in this environment; no before-fix failing behavioural run is claimed. They require the supervisor GitHub rcheck. No tests are weakened or skipped in code. Windows/WPF runtime behaviour cannot be exercised on Linux. No real desktop, notification, account, or user profile is used by the new tests (the assembly sets CCP_USERDATA_DIR and uses a memory-only SecretStore).

## Assumptions

- Preserve direct zone composition instead of introducing the WPF-only room interface and eight zone interfaces for a debug harness.
- Treat the V2 privacy/tuning host as one dependency: redirecting only a tab switch would break the WPF scroll/focus order.
- Keep preview work in its separately tracked row and keep this composition stub while live dependencies remain.
