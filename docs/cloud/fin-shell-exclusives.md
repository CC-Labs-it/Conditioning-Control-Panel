# fin-shell-exclusives

- Base pinned to `cd812ba4a48d81683488ad2a1320b6a588827e97`; work is on `avalonia-port/cloud-fin-shell-exclusives`.
- Reviewed the ledger and the pinned WPF `MainWindow.Exclusives.cs`, `MainWindow.Exclusives.Flair.cs`, `Views/Tabs/ExclusivesTabView.xaml(.cs)`, `Services/FxTheme.cs`, `Services/MotionFx.cs`, and `Controls/VaultCardAura.cs`.
- Initial gaps: shared FX palette publication and mist opacity; hover lift/bloom; PlansMode and invite routing; hero/tier art; daily-free repaint check; entrance, sign sheens, owned-card aura/glint, click burst/press, and vault motes/zones; five game launch doors and Focus Gaze. The header Premium spark was already wired on the base, despite the older ledger note.
- Reused the existing vault for Settings > Account & Plans: its own localized header, tier plates, invites, and `plans_see_premium` link. Its content uses the outer Settings scroller; motion follows the selected Account section. The header invite ticket now opens that section.
- Published the four WPF FX Color/Brush slots at mod startup and on each mod switch. Ambient, sheen and padlock consumers share those values; mist opacity now reads the mod. Untiered accent behavior, free-day gold and fixed commerce livery remain intact.
- Restored the header's mod-specific vault art and tier plate images. Update the existing hero brush so its fade mask survives. Repainting checks the existing DailyFree service, whose 6-hour success/10-minute attempt gates still throttle requests.
- Added card hover lift and sibling glow, Full/Reduced entrance staggering, owned-card rim breath and glint, and masked group-sign sheens. Bursting card clicks press then wait 140ms; Reduced/Off route immediately. Navigation, unload and the registered `vault-card` panic surface cancel pending doors.
- Adapted the vault mote simulation to this head's DrawingContext renderer and shared frame clock. Full/Reduced/Off use the WPF caps/rates; visible card/sign zones carry Basic gold, Prime diamonds or free lilac, with owned cards/signs weighted twice. No mutable bitmap crosses threads and no infinite Avalonia animation or live Effect was added.
- Trunk credit: `073afa0aa8b7ecd4a1543c83825fce85d33b5be6` supplies `VaultMoteMath` and the mote simulation reference; the unused Core zone record was omitted. PlansMode and burst routing were adapted from `ExclusivesTabView.axaml.cs` at `4f7d56e399876695859aa0dc1484d60426555ed7`. Existing head files were merged by behavior, never replaced with trunk versions.

## Remaining dependencies

The row remains `stub`. These are live WPF paths at the pinned base, not dead members:

- `MainWindow.Exclusives.cs:198` routes Back Room, Breakout and Goon to their existing WPF launcher doors, and DtRH/Arcademy through `LaunchExclusiveGame`, including sign-in refusal. This head still lacks those hosts/launcher registrations; its Play wall disables the same doors.
- Head evidence: `GameWindow.Games` registers only Breakout demo, FYP, Just Drop, Piece by Piece and Racing Thoughts; `BreakoutEntitlementFor` publishes only demo access. Neither Core nor this head contains GazeFocusService or LauncherCatalogue.
- `MainWindow.Exclusives.cs:227` navigates to the Focus Gaze switch on Play. This head's `PlayTabView.ChkFocusGaze_Changed` explicitly lacks the gaze-focus consumer and consent/start wiring. Its existing unavailable card remains honest.
- `BuildComingSoonCard` has no live WPF caller: the reserved seats were removed with the grouped shelf. It is not a missing user behavior. P122 is not used to promote this row because the launch paths above are live.

## Verification limits

- Push is blocked by unavailable GitHub HTTPS credentials (`could not read Username`); the commit remains local and no PR was opened.
- `dotnet build -c Release` passed for CCP.Core, CCP.Avalonia and all 11 Linux test projects named by `.github/workflows/build.yml` (13 projects, exit 0, 0 errors). The final head test build includes all new window-based tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` passed: exit 0, 0 errors, 512 warnings. Windows app/tests were compiled, not executed.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.`
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0, 2825 passed, 5 optional Vosk tests skipped, 2830 total. The four new VaultMoteMath cases pass in this suite.
- The non-window `ExclusivesPalettePublicationTests` and `ExclusivesTimingTests` filter passed: exit 0, 2 passed, 0 skipped. Regression proof: remove only the new StartMods FX publication, run the palette test, and it fails (exit 1; expected `#FFE84393`, actual null); restore the publication and both tests pass.
- Window-based classes compiled but not executed: ExclusivesCompletionTests, ExclusivesAccentTests, ExclusivesVaultTests, ExclusivesVaultMotionTests, InvitePanelTests and PanicSurfacesTests. Render-all was not run; P122 is not invoked.
- Window-based Avalonia tests and render-all are compiled but not run in this worker: the task reports a known font-creation failure until cloud-test-fonts lands. The supervisor must run the focused vault, invite, accent and panic tests and inspect the Premium/Plans renders on GitHub.
- Windows behavior cannot run on this Linux worker. No new OS-specific code was introduced.
- The Vosk model download is known to return HTTP 403 here; optional model-dependent tests may report skips.
- The first multi-project build ran out of disk space copying native test assets. Generated outputs were removed and the required projects were rebuilt sequentially.

## Assumptions

- The smallest safe unit keeps unavailable game and Focus Gaze cards inert until their real destination gates/hosts exist; no new launch substitute was added.
- This head's shared 30fps frame clock and DrawingContext renderer are retained while applying WPF timings and motion gates. Decorative glows use sibling Borders; the glint uses the existing Fluent Sparkle glyph.
- PlansMode is configured once for the inline copy, matching its WPF use. The existing shell owns panic and the inline view adds only cancellation of queued launches.

## Changed files

- `CCP.Avalonia/App.axaml.cs`
- `CCP.Avalonia/Controls/AmbientFxCanvas.Vault.cs`
- `CCP.Avalonia/Controls/AmbientFxCanvas.cs`
- `CCP.Avalonia/Controls/FxTheme.cs`
- `CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml`
- `CCP.Avalonia/Views/Tabs/AppSettingsTabView.axaml.cs`
- `CCP.Avalonia/Views/Tabs/ExclusivesTabView.Flair.cs`
- `CCP.Avalonia/Views/Tabs/ExclusivesTabView.axaml`
- `CCP.Avalonia/Views/Tabs/ExclusivesTabView.axaml.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Exclusives.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.InviteTicket.cs`
- `CCP.Avalonia/Views/Windows/PanicSurfaces.cs`
- `CCP.Core/Fx/VaultMoteMath.cs`
- `Tests/CCP.Avalonia.Tests/ExclusivesAccentTests.cs`
- `Tests/CCP.Avalonia.Tests/ExclusivesCompletionTests.cs`
- `Tests/CCP.Avalonia.Tests/InvitePanelTests.cs`
- `Tests/CCP.Core.Tests/VaultMoteMathTests.cs`
- `docs/avalonia-parity.md`
- `docs/cloud/fin-shell-exclusives.md`
