# fin-views-companion-workshop-roster-cell

- Base: `38f465b8318e470abb4e26ef816d9e21c2b63793` on `origin/avalonia-port/cloud-base-wire-views-companion-workshop-roster-cell-b`.
- A successful roster card click now switches the existing tube as WPF does, including a click on the already-active companion. Personality and right-click paths keep their existing behavior.
- Reuses the existing static pose loader, title/level display and transforms. Maps companions to sets 3/4/5/6/7, persists `SelectedAvatarSet` and `ModAvatarSet` through shared `ModAvatarLooks`, and uses WPF's 200 ms fade-out/fade-in timing. A newer click cancels the old transition. Replaced pose bitmaps are disposed.
- Preserves a sole emote avatar using the shared shipped registry plus mod-local `resources/emotes/setN/emotes.json` folders. Another declared supported/custom set disables the pin, matching WPF. The persona can still switch while the look stays pinned.
- Uses the existing `tube-speech` panic registration (`Views/Windows/PanicSurfaces.cs`) to cancel the finite fade; close also cancels it and rejects later switch requests. No new window, surface or Core dependency was introduced.
- The base's mod-change label refresh was already implemented: `MainShellWindow.CompanionFx.InitializeCompanionFx` -> `CompanionRoomView.RefreshRoster` -> `WorkshopRosterCell.Refresh` -> `UpdatePromptLabels`. Removed the stale contrary comment and added label/tooltip refresh-and-clear assertions to the real shell test.
- Added focused headless coverage for all five mappings at player level one, persistence, rendered title/level/static art, rapid switches, dispatcher marshalling, panic/close cancellation, registry/local emote pinning and declared-other-set exceptions. Extended the existing real pointer-path test to assert tube coupling.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk files were imported or overwritten. Adapted WPF behavior from the pinned base listed below; no WPF or Core source was changed.

## WPF comparison and remaining gaps

References below are pinned to `38f465b8318e470abb4e26ef816d9e21c2b63793`:

| Reference | Behavior and finding |
| --- | --- |
| `Views/Controls/Companion/Runtime/WorkshopRosterCell.xaml` and `.xaml.cs` | Five cards and personality buttons forward to MainWindow. Existing Avalonia cards/picker already implement their text, ordering, left-click isolation and live state. |
| `MainWindow/MainWindow.CompanionTab.cs:319` | `CompanionCard_Click` first switches persona; on success repaints cards and calls the existing tube, without creating one. The tube call was missing and is now reachable from the real drawer. |
| `AvatarTube/AvatarTubeWindow.Avatar.cs:525,819` | Exact companion/set mapping and single-emote early return. Level gating is deliberately unconditional (`IsAvatarSetUnlocked`, line 205). Implemented. |
| `AvatarTube/AvatarTubeWindow.Avatar.cs:395,476` | Persist global/per-mod look, suppress stale completions, swap rendering and fade for 200 ms each way. Persistence, finite fades and static rendering are implemented. |
| `AvatarTube/AvatarTubeWindow.CirceEmotes.cs:203,235` | Union registry and valid mod-local emote folders; pin only when there is exactly one emote set and no other declared avatar. Implemented without importing the animated player. |
| `MainWindow/MainWindow.CompanionFx.cs:89` and `MainWindow/MainWindow.CompanionTab.cs:464` | Mod switch refreshes names/cards and assigned-prompt labels/tooltips on the dispatcher. Already reachable on the base; regression assertions added. |

The row remains **stub**, with one row for its id. **Missing:** WPF's GIF/emote renderer and emotive-portrait skin loading/crossfades in the existing `avatar-tube-window` dependency. This head's static pose fallback remains explicit; a roster click cannot claim full animated/portrait parity. These renderers are broader existing tube features, not a second implementation to introduce inside the roster. Static coupling was an independent user-visible gap at the base, so this is not a pure dependency NOOP. WPF has a live caller; P122 does not apply.

## Verification limits

- SDK installed with the requested .NET 10 installer: `dotnet --version` = `10.0.402`.
- `dotnet build ... -c Release`: Core, Avalonia, and every Linux test project named in `.github/workflows/build.yml` built with **0 errors**. Final focused Avalonia test compilation used `-m:1`.
- `bash .github/scripts/core-guards.sh`: exit 0; `All Core guards passed.`
- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; **2818 passed, 5 skipped, 0 failed**. The existing Vosk tests self-skip without the model; no tests were edited to skip. Model download is unavailable in this environment.
- Avalonia tests were **compiled but not run**, following the job's measured font-creation limitation. The new regression assertions therefore have no executed fail-before-fix or pass-after-fix proof here. Supervisor/rcheck should run `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~WorkshopRosterSwitchTests|FullyQualifiedName~WorkshopRosterTubeTests|FullyQualifiedName~WorkshopRosterPersonalityTests|FullyQualifiedName~WorkshopCellsTests|FullyQualifiedName~CompanionTabVisibilityTests'` with `DiffEngine_Disabled=true`.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; **508 warnings, 0 errors**. WPF app and tests were cross-compiled, not run on Linux.
- No real desktop, notification, user profile, or Windows runtime was exercised. Headless test setup uses the existing `CCP_USERDATA_DIR` sandbox. No new OS-specific code was added.
- No `--render-all` or live visual proof was attempted under the same font limitation; this row is not marked wired or verified.
- The original cloud checkout was clean but at a different commit, and its required fetch failed because `.git/FETCH_HEAD` was read-only. Used the explicitly authorized independent clone fallback and verified the exact required base before editing. Builds briefly exhausted the cloud disk while copying native dependencies; removed only generated test outputs and rebuilt projects sequentially.

## Assumptions

- Preserve the existing static renderer and its fallback until `avatar-tube-window` supplies animated/portrait rendering. Do not fake parity by relabeling static art as animated.
- Couple the tube only through successful roster clicks, including same-persona clicks, as the WPF handler does. Do not add a global Core switch listener that also changes the look for unrelated callers.
- Reuse the existing head default mod id when no mod service is attached, including its registry pin; tests select an explicit static fixture mod when testing static switching.
