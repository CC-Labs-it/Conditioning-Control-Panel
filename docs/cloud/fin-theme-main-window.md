- Finish the live main-window theme paths: share achievement card/filter, quest panel, and small/big emote themes; retain the quest tab's existing 30 fps shared ambient clock and WPF particle legs.
- Apply the WPF custom-emote ghost ink/italic styling, hide it while typing, preserve native last-successful-send hints, and use the live accent for focus in both emote entry surfaces.
- Add the implicit transparent, borderless TreeView with scrolling and selection intact.
- Restore SmallPinkButton's 80 ms press / 150 ms release with WPF BackEase amplitude 0.3, CollapsibleCard's 200 ms chevron and hover ink, and DarkComboBox's live arrow/open glow, 240 px popup scroller and finite slide. Card glow uses a sibling BoxShadow.
- Apply WPF's 60-second ordinary help-tooltip lifetime, cancel on close/detach, ignore stale queued expirations, and register the timed tooltips with panic stop. Interactive HelpPopover cards retain WPF hover/pin behavior.
- Add focused headless regression cases in `MainWindowThemeParityTests`; existing `QuestsTabFxTests`, `AchievementsTabGridTests`, and `RemoteControlShellTests` cover particle timings, reward cards, and successful-send hints.
- Source: frozen WPF at `1f5612c98f05b21a0cce4179e84506b3a5ac5af8`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing head files win throughout.

## Verification limits

- SDK: .NET 10.0.402 installed using the requested installer (exit 0).
- `dotnet build <project> -c Release --nologo -m:1 --disable-build-servers`: CCP.Core, CCP.Avalonia, and all 11 Linux test projects referenced by build.yml passed with 0 errors (exit 0 each). Final builds include the additional motion regressions. A transient reusable MSBuild worker crash was resolved by disabling build servers.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -m:1 --disable-build-servers`: passed, 0 errors (exit 0). Windows/WPF code compiled but was not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed` (including the final pass).
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build --nologo`: exit 1; 2810 passed, 1 failed, 5 existing Vosk skips. The model is unavailable under the supplied environment limit; no test source was changed to skip it.
- The failure is `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`, `Assert.NotNull(handle)` at line 80. An isolated retry with `--filter FullyQualifiedName~GoonClockAndRoundsTests` also exits 1 (11 passed, 1 failed). The unchanged MatchClock can reject a SafeFireAt timestamp if time advances below its minimum scheduling buffer. This remains outside the theme unit. `git diff --exit-code origin/avalonia-port/cloud-base-wire-theme-main-window-b -- CCP.Core Tests/CCP.Core.Tests` exits 0.
- Both offscreen render-all passes exited 0 with `224/224 views rendered`; the final pass includes the additional motion changes, reports no binding/construction errors, and uses an isolated CCP_USERDATA_DIR. Quest, Remote Control and Assets renders were inspected.
- Avalonia test execution and before-fix runtime failure proofs were **not run**, as directed by the supplied cloud font limitation. The supervisor should run `MainWindowThemeParityTests`, `QuestsTabFxTests`, `AchievementsTabGridTests`, `RemoteControlShellTests`, `RemoteControlTabTests`, `HelpPopoverTests`, `AssetsTabTests` and `PanicSurfacesTests` using a filtered CCP.Avalonia.Tests invocation with `DiffEngine_Disabled=true`. These classes compile with 0 errors. No tests were weakened, deleted, or skipped in source.
- No real desktop, user profile, remote relay, or Windows behavior was exercised. Existing build warnings remain.

## Baseline gap inventory and decisions

The last `theme-main-window` row was `stub`. The entire cited WPF dictionary was read, including its live callers.

| Baseline gap | Disposition |
| --- | --- |
| Implicit TreeView template (:502) | Implemented with WPF transparent/zero-border scrolling chrome. Assets remains its live path and keeps its explicit vertical visibility. |
| Achievement/art keys (:628-737) | Live card/filter themes moved out of AchievementsTabView; preserve hover/focus/selected chip states. `AchievementTile`, `FeatureImageBorder`, `RoundedFeatureImage` have no live WPF resource lookup. |
| HelpTooltipStyle and duration (:739-808) | Ordinary help-button timeout implemented. The rich tooltip builder's `Build` wrapper has no live caller; HelpPopover is the live rich-help surface and already carries the card chrome. |
| Shared animated gradient/tab templates (:810-1210) | Live standard/wide quest panels shared with unchanged WPF timing on the existing ambient clock. `AnimatedTabBackground` has no WPF caller. No new looping Animation or Effect. |
| Emote keys and converters (:1540-1723) | Share live button, badge, textbox themes. Native text glyphs replace the WPF image/fallback converter pair; native watermark plus the existing successful-send assignment replaces the WPF attached-property converter. No duplicate converters introduced. |
| Older interaction gaps (:122-186, :1296-1530) | Restored button timing/easing, card chevron motion/hover/glow, combo arrow/open glow, popup sizing/slide, and disabled cursor. |
| Session action/delete buttons (:233-278) | Dead WPF keys; the session rows use the local `SdRowAction` theme. |

## P122 evidence at the pinned WPF SHA

The following resource-lookup search yields only the internal SessionDeleteButton base-style reference and the dead HelpTooltipBuilder.Build wrapper's HelpTooltipStyle lookup:

```sh
git grep -n -E '(StaticResource|DynamicResource|FindResource|TryFind).*"?(AchievementTile|FeatureImageBorder|RoundedFeatureImage|HelpTooltipStyle|AnimatedTabBackground|SessionActionButton|SessionDeleteButton)' 1f5612c98f05b21a0cce4179e84506b3a5ac5af8 -- 'ConditioningControlPanel/*.cs' 'ConditioningControlPanel/*.xaml'
git grep -n 'HelpTooltipBuilder.Build(' 1f5612c98f05b21a0cce4179e84506b3a5ac5af8 -- 'ConditioningControlPanel/*.cs' 'ConditioningControlPanel/*.xaml'
```

The second search has no matches (exit 1). Documentation mentions and stale comments describing AchievementTile as in use are not live callers. Offscreen `--render-all` completed with exit 0: `224/224 views rendered`. No binding errors or construction failures were reported. Quest, Remote Control and Assets renders were inspected. This supplies the render-all half of P122; the row is `wired`, never `verified`.

## Assumptions

- Keep native Avalonia glyph and watermark rendering instead of importing WPF-only image and attached-property plumbing; this is the existing head convention and preserves the user path.
- Keep quest particles on their existing tab clock: this is their only WPF consumer, and moving the themes does not require a second clock abstraction.
- Use a finite 150 ms visual transition for the WPF popup slide, retaining Avalonia's popup placement and virtualization.
- Implement ordinary tooltip expiry separately from interactive HelpPopover pinning, matching WPF's two separate surfaces.
