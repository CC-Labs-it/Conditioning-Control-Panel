# Main sync #9 delta ledger (2026-10-30)

Branch `avalonia-port/main-20261030` (merge) + `avalonia-port/main-20261030-compat` (build fixes), from
`review/mich-1025-merge` (`950aedaa1`: Mich's frozen tip `a3d9e984f` + `port/7.2` + the port-review fixes #3099-#3125).
Pin: `origin/main` = `7e01ec326` (Merge PR #3132 rt-prime-only, 2026-10-29), then follow-up pin `a11ada22e`
(Merge PR #3138 garage-owned-bodies, 2026-10-29; rows 590-591, merge `a9605b2cc`), then `ddc43afeb`
(Merge PR #3146 rain-wardrobe page 4, 2026-10-30; rows 592-594, merge `047449d1f`).
Last sync pin: `f81ea01fb` (main sync #8, [`avalonia-main-sync-20261024.md`](avalonia-main-sync-20261024.md)).
Delta: 594 non-merge commits (589 + 2 + 3 follow-up), 1562 + 22 + 12 files. Almost all of it is the Racing Thoughts 3D (Glassroad) web tree, which now
lives in `Assets/web/glassroad` (moved in the merge, same rule git applied to the other web trees). The rest is the 7.1.15
release, Sparkles by place (B1), Lobby race rows and the friends race invite (A3), and a few bug fixes.

Every non-merge commit has one row. Status legend as in `avalonia-main-sync-20261005.md`:

- **ported-by-merge**: the change landed in a file both heads use (`Assets/web`, `CCP.Core`, the language files).
- **needs-port (`lane`)**: WPF-only behaviour the Avalonia head lacks; the lane owns it.
- **n/a**: no Avalonia counterpart by design, reason given.

Counts: 562 ported-by-merge, 26 needs-port, 6 n/a (total 594)

Under the 7.2 plan these lanes are A5 work and go as PRs of 600 lines or less against `port/7.2` after A1 lands.
Sparkles waits for the B1 DECISIONS file; Lobby/invite waits for the A3 server half.

## needs-port lanes

| lane | commits | rows |
|---|---|---|
| `sync9-fixes` | 4 | `97eca18de`, `08bb30ba7`, `81bc9fa21`, `cdda883ac` |
| `sync9-glassroad` | 4 | `5121933ee`, `099607382`, `c7af815a0`, `9718fd3f4` |
| `sync9-lobby-invite` | 3 | `5a353e6b0`, `d6a39c7ac`, `cc6ad7a39` |
| `sync9-sparkles` | 15 | `3930199ac`, `9d7d5406e`, `28c334234`, `21873bc05`, `48d232dd9`, `a568665e0`, `de0947d9f`, `b96b5931c`, `cc1bdc4b0`, `b2487a365`, `7c086f322`, `70c3d96e6`, `d7348e6a0`, `ba537a09b`, `e0356be6e` |

What each lane is:
- `sync9-glassroad`: the Avalonia door (`GameWindow.Glassroad.cs`, still behind `CCP_GLASSROAD=1`) opens for everyone,
  Prime only, and the 2D race door is retired (WPF `LauncherCatalogue` "race", `GameSurfaces`, Play tab). Panic/Escape
  rules from `5121933ee` are already in `PanicPolicy.KeptEscapePage.Glassroad`; recheck against `c7af815a0`.
- `sync9-glassroad` also owns row 590 (`9718fd3f4`): the Avalonia `GlassroadHostRules.InitFrame` must carry
  `identity.grants` (only `cos.kart.*`, sorted, at most 64, WPF `GlassroadRules.KartGrants`) and `GameWindow.Glassroad.cs`
  must post `{type:'owned', grants}` on Core `OwnershipService.OwnershipChanged` while the window is up, unsubscribing on close.
  Without it the Linux garage offers no owned bodies (the page falls back to the free ones). Test to port: `GlassroadHostTests.Init_CarriesOnlyKartGrants_AndTheOwnedPushMatches`.
- `sync9-sparkles`: claim service, per-game claims (Arcademy, DtRH, Caucus, Intake, chess, Goon, Breakout, Glassroad) and the
  results slip, host side. The web halves (`sparkles-slip.js` in every tree) merged. WPF-only today (`ConditioningControlPanel/Services/Sparkles`).
- `sync9-lobby-invite`: open race rows in the Lobby (Core `LobbyModel`/`RaceOpenApi`, now in Core), the in-app `?join=` door
  and the friends race invite.
- `sync9-fixes`: JustDrop refused handoff (#1499), subliminal font pick on the Avalonia card (#1493; the Core layer now takes
  the face through `SubliminalLayer.BundledFaceStream`), browser fullscreen diag/software target (#1492, WebView2-only parts are n/a).

## Rows

| # | sha | subject | where | status |
|---|---|---|---|---|
| 1 | `409a708f3` | feat(glassroad): vendor three.js r186 (webgpu, tsl, bloom addon) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 2 | `4b2330d01` | feat(glassroad): contracts, feel sheet data, Threshold greybox track, schema | ConditioningControlPanel/Resources/web | ported-by-merge |
| 3 | `f62eac1eb` | feat(glassroad): track spline, frames, ground query, meshes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 4 | `7ebda455e` | feat(glassroad): clock, input and kart sim (drift, boost, air, rescue) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 5 | `3883954a4` | feat(glassroad): renderer, TSL materials, camera, bloom, particles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 6 | `0bcb5d2a4` | feat(glassroad): juice registry, photosafe, gentle, synthesised audio | ConditioningControlPanel/Resources/web | ported-by-merge |
| 7 | `e21023441` | feat(glassroad): hud, feel sheet, host bridge v2, escape pauses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 8 | `0bb681d4a` | feat(glassroad): boot loop, local server, headless boot check | ConditioningControlPanel/Resources/web | ported-by-merge |
| 9 | `18edf08b2` | feat(glassroad): vendor three r186.1 glTF, KTX2, meshopt and basis loaders | ConditioningControlPanel/Resources/web | ported-by-merge |
| 10 | `e5d9161e0` | feat(glassroad): Astra's wave 1 art (road, sky, EMI kart, props, rings, portal) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 11 | `e0424465c` | feat(glassroad): wire wave 1 art (road, rails, pads, sky, EMI kart, rings, arch, scatter) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 12 | `2547c42a2` | feat(glassroad): props read as landmarks (about 3x size, 90 on high, fewer per tier) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 13 | `04f645371` | Add Glassroad particle atlas and cell map | ConditioningControlPanel/Resources/web | ported-by-merge |
| 14 | `ed876c985` | Add Glassroad screen effect masks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 15 | `054d42398` | Add Glassroad Wave 2 item carriers | ConditioningControlPanel/Resources/web | ported-by-merge |
| 16 | `a08933f9e` | Add Glassroad Wave 2 rival colourways and accessories | ConditioningControlPanel/Resources/web | ported-by-merge |
| 17 | `c3649f10c` | Add Glassroad Wave 2 paper and ink HUD kit | ConditioningControlPanel/Resources/web | ported-by-merge |
| 18 | `54d9d1995` | Refine Glassroad galaxies and finish portal for Wave 2 | ConditioningControlPanel/Resources/web | ported-by-merge |
| 19 | `75eead6c0` | Clarify finish mask transparency binding | ConditioningControlPanel/Resources/web | ported-by-merge |
| 20 | `5ec394b1a` | feat(glassroad): B1 contracts, item/ai/contact feel, item boxes on Threshold, effect icons | ConditioningControlPanel/Resources/web | ported-by-merge |
| 21 | `01b3d1213` | feat(glassroad): 8 racers, contact, slipstream, standings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 22 | `57af5ad81` | feat(glassroad): AI racers (lines, braking, drift boosts, items, rubber band) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 23 | `b0c974912` | feat(glassroad): effect-bubble items, mercy, screen effect state | ConditioningControlPanel/Resources/web | ported-by-merge |
| 24 | `8d003375b` | feat(glassroad): screen effects and living item bubbles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 25 | `4cd2d4ecf` | feat(glassroad): juice pass (8 karts, lean, particles, motes, wakes, road sheen) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 26 | `853ec1d51` | feat(glassroad): menu, pause, options, results, race HUD, minimap | ConditioningControlPanel/Resources/web | ported-by-merge |
| 27 | `5687dc9ee` | feat(glassroad): item and rival audio, mix, music player for chart tracks and local files | ConditioningControlPanel/Resources/web | ported-by-merge |
| 28 | `76575e208` | feat(glassroad): B1 boot flow (menu to results), item input, pad card navigation, smoke checks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 29 | `ce8ecba4b` | chore(glassroad): one manifest for waves 1 and 2 (78 rows) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 30 | `38458f821` | fix(glassroad): bot item view once per tick, leader fires Blackout, finished karts never targeted, Gentle Flas | ConditioningControlPanel/Resources/web | ported-by-merge |
| 31 | `508a1bbd5` | feat(glassroad): particle atlas and screen-effect masks (frost, melt flow, crack, iris, noise dither) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 32 | `66d2f4dd6` | feat(glassroad): item cards, glass bubble shells, melt puddle, rival colourways and head accessories, finish m | ConditioningControlPanel/Resources/web | ported-by-merge |
| 33 | `ec9df63b4` | feat(glassroad): SVG HUD kit, pad layouts for non-standard pads, RB item, pause on pad loss, device-aware labe | ConditioningControlPanel/Resources/web | ported-by-merge |
| 34 | `b34245e31` | feat(glassroad): polish 1 integration (look numbers, near-camera bubble cap, hit sprays, boot-check shots) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 35 | `a1eddd8c1` | Add Glassroad Wave 3 EMI screen expressions | ConditioningControlPanel/Resources/web | ported-by-merge |
| 36 | `7b8b8abe9` | Add Glassroad Wave 3 glyphs art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 37 | `099938c58` | Add Glassroad Wave 3 cards art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 38 | `93cfcd4bb` | Add Glassroad Wave 3 threshold art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 39 | `3f92d1f16` | Add Glassroad Wave 3 horizon art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 40 | `2edff0b3b` | Add Glassroad Wave 3 stage art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 41 | `e6740ee1f` | Update Racing Thoughts 3D launcher artwork | Assets/features/race.png | ported-by-merge |
| 42 | `7b0393537` | Add Glassroad Wave 3 road art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 43 | `a4852ac78` | feat(glassroad): calmer speed-sensitive steering, key ramp and stick curve, steering sensitivity, snappier hop | ConditioningControlPanel/Resources/web | ported-by-merge |
| 44 | `0bcf4c9b7` | feat(glassroad): recorded hover engine loops and sound effects (no more synth whine), synth as fallback | ConditioningControlPanel/Resources/web | ported-by-merge |
| 45 | `601528646` | feat(glassroad): frame cap, auto render scale, quality and fps options, high-performance GPU, material warm-up | ConditioningControlPanel/Resources/web | ported-by-merge |
| 46 | `d887c2255` | feat(glassroad): colossi leaning over the track, soft lamp shadows, bigger scatter, glassier road, rails and s | ConditioningControlPanel/Resources/web | ported-by-merge |
| 47 | `47c160b4a` | feat(glassroad): kart reactions (squash, head lag, bumps, effect hits, finish) and 16 EMI screen faces | ConditioningControlPanel/Resources/web | ported-by-merge |
| 48 | `7a02abfcb` | feat(glassroad): power-up and screen effect polish (trails, impacts, confetti boxes, slot orbit, richer effect | ConditioningControlPanel/Resources/web | ported-by-merge |
| 49 | `a0a12ed1f` | feat(glassroad): polish 2 integration (options wiring, render-time pacing, shadows, reactions, boot-check shot | ConditioningControlPanel/Resources/web | ported-by-merge |
| 50 | `860c00645` | Add Glassroad Wave 4 flipbooks art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 51 | `0475f0b55` | Add Glassroad Wave 4 trails art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 52 | `f1744f1f4` | Add Glassroad Wave 4 details art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 53 | `9b38bfcf0` | Add Glassroad Wave 4 colossi art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 54 | `f3f56b427` | Add Glassroad Wave 4 sky art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 55 | `814a420bb` | Add Glassroad Wave 4 reactions art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 56 | `7eee74aff` | Clarify selected-tier texture residency | ConditioningControlPanel/Resources/web | ported-by-merge |
| 57 | `ab69ac7e2` | Clarify selected-tier texture residency | ConditioningControlPanel/Resources/web | ported-by-merge |
| 58 | `5d91e05f4` | feat(glassroad): full-spectrum colour system, road lanes that stream along the track, per-section patterns, sp | ConditioningControlPanel/Resources/web | ported-by-merge |
| 59 | `199ff6f63` | feat(glassroad): visible floating world (70/45/25 props in the camera corridor, saturated colours, less haze), | ConditioningControlPanel/Resources/web | ported-by-merge |
| 60 | `a1b1e6310` | feat(glassroad): Loom spiral gates materialised as glitter particles that part as karts pass through | ConditioningControlPanel/Resources/web | ported-by-merge |
| 61 | `da5d54202` | feat(glassroad): media slots with animated placeholders (billboards, sky-halo ring, screen tunnel), no remote  | ConditioningControlPanel/Resources/web | ported-by-merge |
| 62 | `c1a8ebf9f` | feat(glassroad): bigger, clearer juice (speed lines, boost, drift, bumps, landings, reactions, camera kick) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 63 | `cced3792c` | feat(glassroad): polish 3 integration (gates, media, spectrum wiring, art status in the fps readout, muted hea | ConditioningControlPanel/Resources/web | ported-by-merge |
| 64 | `019c4d9ae` | feat(glassroad): progressive steering, brake power slide with a boost after every drift, timed trick boost | ConditioningControlPanel/Resources/web | ported-by-merge |
| 65 | `6aa00cca7` | feat(glassroad): drop shortcut from the helix top onto the lower straight (kerb gap, catch zone, safe lap cred | ConditioningControlPanel/Resources/web | ported-by-merge |
| 66 | `3026e1f14` | feat(glassroad): dense glowing Loom gates with an aura, the gate's Loom plays on screen, Loom band maths impor | Assets/web/arcademy, ConditioningControlPanel/Resources/web | ported-by-merge |
| 67 | `2092f97df` | feat(glassroad): real hit reactions (spins, stomps, flips, dizzy faces, stars), louder power-ups with auras an | ConditioningControlPanel/Resources/web | ported-by-merge |
| 68 | `6f2fa18f2` | feat(glassroad): depth speed field, glowing skid marks, tier sparks, mini-turbo and trick bursts, a lens rule  | ConditioningControlPanel/Resources/web | ported-by-merge |
| 69 | `f378b892a` | feat(glassroad): polish 4 integration (pause freezes the floor and effects, Firefox on WebGL2, governor follow | ConditioningControlPanel/Resources/web | ported-by-merge |
| 70 | `6c8d4aeb3` | feat(glassroad): colossal hero props (chess, slots, cards, watches, discs 30 to 55 m tall beside the road, nea | ConditioningControlPanel/Resources/web | ported-by-merge |
| 71 | `9d63f5963` | feat(glassroad): music road + word bubbles (the road plays the song, each player pops their own words) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 72 | `9c1b49d82` | art(glassroad): refine EMI teacup and add race reactions | ConditioningControlPanel/Resources/web | ported-by-merge |
| 73 | `830257dc3` | art(glassroad): seat EMI and stagger cup assembly reactions | ConditioningControlPanel/Resources/web | ported-by-merge |
| 74 | `d81e4e6db` | art(glassroad): label takeoff endpoints as airborne joins | ConditioningControlPanel/Resources/web | ported-by-merge |
| 75 | `28e2df5d7` | art(glassroad): align reaction handoff with engine polish four | ConditioningControlPanel/Resources/web | ported-by-merge |
| 76 | `dfdc834e1` | art(glassroad): add opposing cup and saucer wobble | ConditioningControlPanel/Resources/web | ported-by-merge |
| 77 | `809c0ed7b` | art(glassroad): strengthen teacup layer motion | ConditioningControlPanel/Resources/web | ported-by-merge |
| 78 | `f65bebe58` | feat(glassroad): music road pass 2 - every word in a bubble, pop juice, visible power-up status, no boot stall | ConditioningControlPanel/Resources/web | ported-by-merge |
| 79 | `6083dc982` | fix(glassroad): no freeze at the first spiral gate, a spread-out field, song state in the dev beacon | ConditioningControlPanel/Resources/web | ported-by-merge |
| 80 | `4cdf77c63` | art(glassroad): restore classic Back Room EMI expressions | ConditioningControlPanel/Resources/web | ported-by-merge |
| 81 | `ec9ee366b` | art(glassroad): replace melty EMI states with classic idle face | ConditioningControlPanel/Resources/web | ported-by-merge |
| 82 | `76da33662` | fix(glassroad): pass 3 - no mid-race tier relinks, an honest loading card, bubbles on the voice, no white cres | ConditioningControlPanel/Resources/web | ported-by-merge |
| 83 | `6bde90922` | feat(glassroad): music road pass 4 - words painted on the road, fewer shiny bubbles, the word rush, steer assi | ConditioningControlPanel/Resources/web | ported-by-merge |
| 84 | `848442ca2` | art(glassroad): add eight modular EMI bot outfits | ConditioningControlPanel/Resources/web | ported-by-merge |
| 85 | `b2150937e` | art(glassroad): give every EMI outfit a distinct headpiece | ConditioningControlPanel/Resources/web | ported-by-merge |
| 86 | `d64a65a85` | art(glassroad): add fitted Petal and Trinket vehicle variants | ConditioningControlPanel/Resources/web | ported-by-merge |
| 87 | `a2e113cae` | feat(glassroad): Astra R4 seated EMI rig, classic faces, eight bot outfits and two vehicles wired in | ConditioningControlPanel/Resources/web | ported-by-merge |
| 88 | `99dc9cb2e` | feat(glassroad): readable road edges and a drop shortcut people make | ConditioningControlPanel/Resources/web | ported-by-merge |
| 89 | `525d1d9bd` | feat(glassroad): B2 net contracts - wire format, messages, ticket spec, shared vectors | ConditioningControlPanel/Resources/web | ported-by-merge |
| 90 | `5b7118219` | feat(glassroad): B2 client net - room session, server clock, interpolation, remote karts, bot keeper | ConditioningControlPanel/Resources/web | ported-by-merge |
| 91 | `ba8b5e49f` | feat(glassroad): B2 lobby card - Race online door, open tables, quick match, seats, looks, preset chat | ConditioningControlPanel/Resources/web | ported-by-merge |
| 92 | `cdade1a3f` | feat(glassroad): B2 integration - online flow in boot, seat looks in the scene, local 4-seat e2e | ConditioningControlPanel/Resources/web | ported-by-merge |
| 93 | `1747e84a3` | feat(glassroad): boost camera ladder - fov kick, speed lines and shake by boost tier (polish 5 J1) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 94 | `5faffd46f` | feat(glassroad): screen effects keep a clear lane, gate glitter fades at the lens, particles fade in (polish 5 | ConditioningControlPanel/Resources/web | ported-by-merge |
| 95 | `464c4ba74` | fix(glassroad): road stair bands gone - shadow depth range, line antialiasing, anisotropy, pad and sky seams ( | ConditioningControlPanel/Resources/web | ported-by-merge |
| 96 | `6e1eb6e3f` | feat(glassroad): countdown that punches, Go on the song downbeat, menu and lobby sounds, dev hook (polish 5 J2 | ConditioningControlPanel/Resources/web | ported-by-merge |
| 97 | `d4b518c0e` | feat(glassroad): Go bursts at the player's kart (polish 5 J2 follow-up) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 98 | `7b74c3382` | feat(glassroad): item slot that reads, and moments that answer (polish 6 JUICE) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 99 | `008f99c60` | feat(glassroad): beat shake that phases in with the song's rise (polish 6 BEAT) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 100 | `1398672bc` | feat(glassroad): add validated cavity shading and material detail | ConditioningControlPanel/Resources/web | ported-by-merge |
| 101 | `c3608ff9a` | feat(glassroad): frame the road with layered construction and contrast | ConditioningControlPanel/Resources/web | ported-by-merge |
| 102 | `0d891f5b5` | feat(glassroad): sharpen deliberate keyboard counter-steering | ConditioningControlPanel/Resources/web | ported-by-merge |
| 103 | `71645462c` | fix(glassroad): keep material wear fixed to instanced geometry | ConditioningControlPanel/Resources/web | ported-by-merge |
| 104 | `0537edaeb` | chore(glassroad): freeze P6 course snapshot for isolated art work | ConditioningControlPanel/Resources/web | ported-by-merge |
| 105 | `3e577e444` | feat(glassroad): Threshold rebuilt as a stacked course with loops and a dare (polish 6 GEO + SIM) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 106 | `b4f3f63cf` | feat(glassroad): closer, lower chase camera, smaller karts, roadside scale cues (polish 6 VIEW) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 107 | `b47375541` | perf(glassroad): Firefox hitches - Echo ghosts linked at load, word rush without CSS filters, less garbage (po | ConditioningControlPanel/Resources/web | ported-by-merge |
| 108 | `9e6c9523d` | feat(glassroad): add raised arcade terraces beside the P6 starting straight | ConditioningControlPanel/Resources/web | ported-by-merge |
| 109 | `eb5a05243` | feat(glassroad): add fixed glazed tiles driven by the song clock | ConditioningControlPanel/Resources/web | ported-by-merge |
| 110 | `c5a1de703` | fix(glassroad): owed polish 6 leftovers - bump shake ducks the beat, engine layers reuse a record, item visual | ConditioningControlPanel/Resources/web | ported-by-merge |
| 111 | `1af472815` | fix(glassroad): bring the chase closer and enlarge every racer | ConditioningControlPanel/Resources/web | ported-by-merge |
| 112 | `7df00e32c` | feat(glassroad): race a friend through a link - Invite link for friends-room hosts, ?r=<token> claim, closed p | ConditioningControlPanel/Resources/web | ported-by-merge |
| 113 | `7395f46a5` | fix(glassroad): hold the racer in the reference chase framing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 114 | `eb943dac2` | feat(glassroad): widen P6 racing lanes while preserving technical gaps | ConditioningControlPanel/Resources/web | ported-by-merge |
| 115 | `5121933ee` | feat(glassroad): desktop host behind CCP_GLASSROAD=1 - WebView2 window, /v2/race/ relay, panic + Escape rules | WPF: App.xaml.cs, GameSurfaces.cs, GlassroadHostService.cs, GlassroadRules.cs, LauncherCatalogue.cs, MainWindow.xaml.cs | needs-port (`sync9-glassroad`) |
| 116 | `b245fda29` | feat(glassroad): songs from the host (caps.songs), bigger racers and a closer camera (owner call), invite-link | ConditioningControlPanel/Resources/web | ported-by-merge |
| 117 | `6cdb7a37f` | revert(glassroad): back out the duplicate size/camera change - Codex owns the owner's framing call (codex/glas | ConditioningControlPanel/Resources/web | ported-by-merge |
| 118 | `7d0100677` | feat(glassroad): add clear song-beat waves across glass and tiles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 119 | `4e5add5ff` | release: 7.1.15 "Settle Down" (night-shift fixes + quiet sensing lines) | CCP.Core/Localization/Languages | ported-by-merge |
| 120 | `6b0e282e4` | feat(glassroad): songs from the lobby when the host names none - signed room-origin chart urls (lobbyApi.songs | ConditioningControlPanel/Resources/web | ported-by-merge |
| 121 | `31b275ca6` | perf(glassroad): the open-table list stops polling in a hidden tab and backs off 15 -> 60 s while nothing chan | ConditioningControlPanel/Resources/web | ported-by-merge |
| 122 | `38269e22d` | feat(glassroad): add Aperture Court geometry and physical openings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 123 | `73fbf1600` | feat(glassroad): guide racers safely through court routes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 124 | `f8b6df42f` | feat(glassroad): dress Aperture Court with musical tiles and open shafts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 125 | `c38a9ee70` | Add Racing Thoughts wave 3 water art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 126 | `5f0893e9a` | Add Racing Thoughts wave 3 stones art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 127 | `c31e25a58` | Add Racing Thoughts wave 3 glide art concepts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 128 | `3e82f8284` | Add Racing Thoughts wave 3 spouts art | ConditioningControlPanel/Resources/web | ported-by-merge |
| 129 | `332a9430e` | feat(glassroad): water wave 1, track: stream and pool surfaces, flow per node, chute and pool in the lap | ConditioningControlPanel/Resources/web | ported-by-merge |
| 130 | `80c2efc3a` | feat(glassroad): water wave 1, sim: the kart drives relative to the current, chutes and pool landings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 131 | `33031598a` | feat(glassroad): water wave 1, render: TSL water on stream and pool nodes, the waterfall sheet, wakes on water | ConditioningControlPanel/Resources/web | ported-by-merge |
| 132 | `a1e847393` | feat(glassroad): water wave 1, bots: read the current, take the inside line on a stream, no drift on water | ConditioningControlPanel/Resources/web | ported-by-merge |
| 133 | `9cb768f8f` | Glassroad art W4: six fitted propeller beanies | ConditioningControlPanel/Resources/web | ported-by-merge |
| 134 | `7d7de51a3` | Glassroad art W4: six unlockable umbrella alternates | ConditioningControlPanel/Resources/web | ported-by-merge |
| 135 | `d7a8f2230` | Glassroad art W4: pawn stamp and portcullis hazards | ConditioningControlPanel/Resources/web | ported-by-merge |
| 136 | `2ea7e8221` | Glassroad art W4: Threshold stream bank trims | ConditioningControlPanel/Resources/web | ported-by-merge |
| 137 | `2e6d5d75d` | Glassroad art W4: The Keep road, sky and dressing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 138 | `96bba73e2` | feat(glassroad): stones wave 2, track: a stepping-stone field on the bridge, in the lap | ConditioningControlPanel/Resources/web | ported-by-merge |
| 139 | `cf12caed6` | feat(glassroad): stones wave 2, sim: the lip kick, the void fall, rescue clear of the field, damped shoves | ConditioningControlPanel/Resources/web | ported-by-merge |
| 140 | `890f20f62` | feat(glassroad): stones wave 2, bots: a field plan, one safe lane picked late and held, a damped approach | ConditioningControlPanel/Resources/web | ported-by-merge |
| 141 | `261974a68` | feat(glassroad): stones wave 2, render: Astra's platforms per 5 m tile, a slab fallback, stone and void cues | ConditioningControlPanel/Resources/web | ported-by-merge |
| 142 | `b746a78b3` | feat(glassroad): ship polished garage wardrobe and vehicle catalog | ConditioningControlPanel/Resources/web | ported-by-merge |
| 143 | `a8a313197` | feat(glassroad): integrate the animated racer garage | ConditioningControlPanel/Resources/web | ported-by-merge |
| 144 | `ac14dfb67` | build(glassroad): keep the Racing Thoughts 3D tree out of the WPF build and installer (no desktop door yet; it | WPF build/release only (version is Version.props here; the web tree ships from Assets/web): ConditioningControlPanel.csproj | n/a |
| 145 | `4174e8cd4` | docs(glassroad): record the garage appearance wire | ConditioningControlPanel/Resources/web | ported-by-merge |
| 146 | `a18eeadb9` | feat(glassroad): author Mainframe course and timed scan rules | ConditioningControlPanel/Resources/web | ported-by-merge |
| 147 | `00db018e6` | feat(glassroad): make Mainframe playable with its circuit world | ConditioningControlPanel/Resources/web | ported-by-merge |
| 148 | `90f15d141` | fix(glassroad): keep blank Mainframe song choices silent | ConditioningControlPanel/Resources/web | ported-by-merge |
| 149 | `d7bb9cbdf` | fix(glassroad): fit garage skin masks within WebGPU buffer limits | ConditioningControlPanel/Resources/web | ported-by-merge |
| 150 | `91481d2f4` | feat(glassroad): author the Keep course and deterministic gate simulation | ConditioningControlPanel/Resources/web | ported-by-merge |
| 151 | `747864f5d` | feat(glassroad): dress the Keep with delivered fortress art and moving gates | ConditioningControlPanel/Resources/web | ported-by-merge |
| 152 | `f817398b5` | feat(glassroad): make the Keep selectable for complete solo races | ConditioningControlPanel/Resources/web | ported-by-merge |
| 153 | `f4006bfc0` | fix(glassroad): keep garage skin within WebGPU buffer limit | ConditioningControlPanel/Resources/web | ported-by-merge |
| 154 | `7612d98e6` | Rebuild Keep as a two-turn tower circuit with chain traps | ConditioningControlPanel/Resources/web | ported-by-merge |
| 155 | `b0125806c` | Build solid Keep halls, battlements and spiral tower scenery | ConditioningControlPanel/Resources/web | ported-by-merge |
| 156 | `c5170e04e` | Restore shared moving media and Loom panels for Keep | ConditioningControlPanel/Resources/web | ported-by-merge |
| 157 | `f33ee3254` | Connect Keep panels to the saved media feed on Race | ConditioningControlPanel/Resources/web | ported-by-merge |
| 158 | `abf28e07f` | Keep fortress foundations below the curved driving surface | ConditioningControlPanel/Resources/web | ported-by-merge |
| 159 | `16c2db999` | feat(glassroad): support driving on curved canal banks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 160 | `3a0cd2e22` | feat(glassroad): extend Mainframe through a reactor and coolant circuit | ConditioningControlPanel/Resources/web | ported-by-merge |
| 161 | `017432ad0` | feat(glassroad): enclose Mainframe and flood the coolant reservoir | ConditioningControlPanel/Resources/web | ported-by-merge |
| 162 | `0a1d8ce03` | fix(glassroad): share and prewarm animated world media | ConditioningControlPanel/Resources/web | ported-by-merge |
| 163 | `cd14a34d1` | feat(glassroad): bind Mainframe panels to the selected cached feed | ConditioningControlPanel/Resources/web | ported-by-merge |
| 164 | `77c785ee8` | fix(glassroad): expose Mainframe media through framed display bays | ConditioningControlPanel/Resources/web | ported-by-merge |
| 165 | `d562c36dd` | Extend The Keep around a ruined prison with broken crossings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 166 | `9d2872bb8` | Build the ruined prison castle around The Keep | ConditioningControlPanel/Resources/web | ported-by-merge |
| 167 | `283ac0b40` | Give The Keep an overcast ruined skyline and mounted media | ConditioningControlPanel/Resources/web | ported-by-merge |
| 168 | `d60d473da` | Keep mounted screens clear of prison bars and roof ribs | ConditioningControlPanel/Resources/web | ported-by-merge |
| 169 | `4761311fc` | Make the ruined Keep playable with a raised drawbridge and stone crossing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 170 | `0f28309f8` | Record castle race and Firefox verification | ConditioningControlPanel/Resources/web | ported-by-merge |
| 171 | `8132dfe09` | chore(glassroad): pair authored Mainframe node rows for course review | ConditioningControlPanel/Resources/web | ported-by-merge |
| 172 | `44b135a45` | feat(glassroad): support broad hex platforms and overlapping decks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 173 | `d9d86de56` | feat(glassroad): give coolant depth, overflow and contact spray | ConditioningControlPanel/Resources/web | ported-by-merge |
| 174 | `dcf742302` | feat(glassroad): fit Mainframe machinery and smooth the reservoir exit | ConditioningControlPanel/Resources/web | ported-by-merge |
| 175 | `250896b29` | feat(glassroad): expand Mainframe with five hex paths and switchbacks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 176 | `77da25d1d` | fix(glassroad): keep the chase camera above rising roads | ConditioningControlPanel/Resources/web | ported-by-merge |
| 177 | `ca3610f57` | feat(glassroad): widen Keep platforms and add propeller flight | ConditioningControlPanel/Resources/web | ported-by-merge |
| 178 | `e72c6d64a` | feat(glassroad): add worn Keep interiors and stronger masonry contrast | ConditioningControlPanel/Resources/web | ported-by-merge |
| 179 | `56f973600` | fix(glassroad): display shared video frames on WebGL2 | ConditioningControlPanel/Resources/web | ported-by-merge |
| 180 | `7f6ae0085` | Expand Keep into stacked tower routes with drop shortcuts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 181 | `090cb0ab0` | Dress Keep with forged pendulums and colossal lock passages | ConditioningControlPanel/Resources/web | ported-by-merge |
| 182 | `2e7ed8a8b` | Give shared water depth, rolling crests and stronger contact spray | ConditioningControlPanel/Resources/web | ported-by-merge |
| 183 | `a41cfabd1` | Complete Mainframe layered routes, hovering islands and shared bubble ram | ConditioningControlPanel/Resources/web | ported-by-merge |
| 184 | `5dae348fe` | Add Boudoir course with layered routes, perfume courts and shared water | ConditioningControlPanel/Resources/web | ported-by-merge |
| 185 | `7c786185b` | feat(glassroad): articulate the eight kart designs with mechanism metadata | ConditioningControlPanel/Resources/web | ported-by-merge |
| 186 | `8ea23b01b` | feat(glassroad): animate rigid kart mechanisms on the shared rig | ConditioningControlPanel/Resources/web | ported-by-merge |
| 187 | `f38ccd12b` | feat(glassroad): bring EMI and the garage to life | ConditioningControlPanel/Resources/web | ported-by-merge |
| 188 | `733a9d813` | Add Mainframe industrial rooms and traps with shared spiral clouds | ConditioningControlPanel/Resources/web | ported-by-merge |
| 189 | `0fe757bb7` | docs(glassroad): record liveness contracts and review evidence | docs/tests/release only: GLASSROAD-KART-LIFE.md | n/a |
| 190 | `9f46349c0` | Polish Mainframe materials and spirals with musical hex landings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 191 | `251098d3c` | Keep hex landing highlights smooth when racers arrive together | ConditioningControlPanel/Resources/web | ported-by-merge |
| 192 | `623bd4d72` | Stabilize distant screens and enrich Boudoir scenery | ConditioningControlPanel/Resources/web | ported-by-merge |
| 193 | `f1f80a430` | Add timed Boudoir traps and reversible ribbon lanes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 194 | `8b3acb19a` | feat(glassroad): unify the garage menu and conceal shader warmup | ConditioningControlPanel/Resources/web | ported-by-merge |
| 195 | `384f1462f` | fix(glassroad): keep accent cores visible and calm menu motion | ConditioningControlPanel/Resources/web | ported-by-merge |
| 196 | `e7989596b` | Add live Boudoir mirror hall and integrate course traps | ConditioningControlPanel/Resources/web | ported-by-merge |
| 197 | `67df2c8d2` | Vary Boudoir galleries with crests and tapered S-bends | ConditioningControlPanel/Resources/web | ported-by-merge |
| 198 | `d5a753279` | Split Boudoir sweepers into independent pendulum groups | ConditioningControlPanel/Resources/web | ported-by-merge |
| 199 | `583a27efa` | Give Boudoir distinct textured room architecture | ConditioningControlPanel/Resources/web | ported-by-merge |
| 200 | `14ba8b151` | Give Boudoir distinct road finishes and a teal bath palette | ConditioningControlPanel/Resources/web | ported-by-merge |
| 201 | `eeeda8b95` | feat(glassroad): add cartoon garage and smooth centered customization | ConditioningControlPanel/Resources/web | ported-by-merge |
| 202 | `7df9b0cc3` | feat(glassroad): simulate persistent contact-driven water waves | ConditioningControlPanel/Resources/web | ported-by-merge |
| 203 | `2e28f3bcf` | feat(glassroad): animate physical water spray and bank impacts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 204 | `73f0c6ceb` | feat(glassroad): render shared reactive water across courses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 205 | `733ba8505` | Support physical branch roads with continuous race progress | ConditioningControlPanel/Resources/web | ported-by-merge |
| 206 | `0dfcff077` | Add three distinct Boudoir split routes and shortcuts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 207 | `2d7286a12` | Keep assistance and item interactions on the selected path | ConditioningControlPanel/Resources/web | ported-by-merge |
| 208 | `0fe02da0e` | Render signed branch junctions with clean shared aprons | ConditioningControlPanel/Resources/web | ported-by-merge |
| 209 | `0e2ce99c5` | Add shared underwater audio and folding kart propellers | ConditioningControlPanel/Resources/web | ported-by-merge |
| 210 | `9410bbdbd` | Add clear reflective water and dimensional wake and spill foam | ConditioningControlPanel/Resources/web | ported-by-merge |
| 211 | `4c70922e9` | Connect water optics and underwater effects to track depth | ConditioningControlPanel/Resources/web | ported-by-merge |
| 212 | `d6bae277b` | feat(glassroad): choose individual courses from the garage menu | ConditioningControlPanel/Resources/web | ported-by-merge |
| 213 | `8bce49eea` | Build GOONWORKS folded funhouse course and polish its playable attractions | ConditioningControlPanel/Resources/web | ported-by-merge |
| 214 | `86c40dfdf` | Make shield shells smaller and smooth their colour transitions | ConditioningControlPanel/Resources/web | ported-by-merge |
| 215 | `e5446a19a` | Stabilize masonry projection on diagonal Keep walls | ConditioningControlPanel/Resources/web | ported-by-merge |
| 216 | `1f71d7e82` | Make item flight steer visibly and enlarge circular trap contacts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 217 | `3a5d8a814` | Give projectiles larger shells and trails and readable trap footprints | ConditioningControlPanel/Resources/web | ported-by-merge |
| 218 | `eae2fed47` | fix(glassroad): give water visible body and clear its submerged bed | ConditioningControlPanel/Resources/web | ported-by-merge |
| 219 | `9bd3b594e` | feat(glassroad): trail propeller bubbles through moving underwater light | ConditioningControlPanel/Resources/web | ported-by-merge |
| 220 | `019584e15` | Polish GOONWORKS sightlines, landmarks and linked boost runs | ConditioningControlPanel/Resources/web | ported-by-merge |
| 221 | `4dc974835` | Obscure the race with live media cards drifting fog and blackout tunnels | ConditioningControlPanel/Resources/web | ported-by-merge |
| 222 | `6d9cdaf73` | Add deliberate triple boost runs across Boudoir | ConditioningControlPanel/Resources/web | ported-by-merge |
| 223 | `eff514367` | feat(race): share measured music energy across floor and water materials | ConditioningControlPanel/Resources/web | ported-by-merge |
| 224 | `e8b1a1873` | feat(race): fill every course with fitted shared-media screen clusters | ConditioningControlPanel/Resources/web | ported-by-merge |
| 225 | `a3a3935f6` | Keep distant GOONWORKS surfaces stable with verified face culling | ConditioningControlPanel/Resources/web | ported-by-merge |
| 226 | `83cd3006a` | Cover item effects across the combined Boudoir fork routes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 227 | `20f49ea1e` | Polish shared water with crossing swells and stronger wakes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 228 | `c6fd106c1` | feat(race): author dense boost trains with verified safe rollouts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 229 | `388310ce7` | Fit curved boost pads to rendered canal triangles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 230 | `2a1728cf2` | Keep projectile trails finite and lob warnings at their committed landing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 231 | `e6f3342de` | Burst tilted subliminal cards across the Brain Drain victim view | ConditioningControlPanel/Resources/web | ported-by-merge |
| 232 | `c98015f21` | Keep boost arrows visible over transparent roads | ConditioningControlPanel/Resources/web | ported-by-merge |
| 233 | `f239f1d6b` | Add themed pass-through phrase gates across the five courses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 234 | `eeece2d13` | Refine GOONWORKS colour, stable painted faces and 60 percent media | ConditioningControlPanel/Resources/web | ported-by-merge |
| 235 | `067ac5b6f` | Keep wrapped canal boost decals complete at Float32 road boundaries | ConditioningControlPanel/Resources/web | ported-by-merge |
| 236 | `4f4640439` | Polish phrase gates with particles, crossing pulses and animated text | ConditioningControlPanel/Resources/web | ported-by-merge |
| 237 | `3e90617dd` | Give each GOONWORKS section a stronger colour identity | ConditioningControlPanel/Resources/web | ported-by-merge |
| 238 | `933c1accc` | Make subliminals larger unboxed level-colored words with drifting smoke | ConditioningControlPanel/Resources/web | ported-by-merge |
| 239 | `674e1aedf` | Sound phrase gate crossings with track-themed pentatonic cues | ConditioningControlPanel/Resources/web | ported-by-merge |
| 240 | `cce423c07` | Add water travel wake, spray and underwater bubbles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 241 | `9f488690b` | Register phrase gate notifications without duplicating their visual pulse | ConditioningControlPanel/Resources/web | ported-by-merge |
| 242 | `753fcea1f` | Stagger subliminal words with independent fades and clearer spacing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 243 | `b76b7fe52` | fix(glassroad): identify loaded course data when joining online rooms | ConditioningControlPanel/Resources/web | ported-by-merge |
| 244 | `232d55b78` | feat(glassroad): race every course online with shared hazards and branch paths | ConditioningControlPanel/Resources/web | ported-by-merge |
| 245 | `8315b1d9a` | test(glassroad): cover course filtering and wait for long races | ConditioningControlPanel/Resources/web | ported-by-merge |
| 246 | `fcc6b4a4c` | fix(glassroad): keep shared online stone motion gentle | ConditioningControlPanel/Resources/web | ported-by-merge |
| 247 | `8455f4ece` | Improve Threshold song continuity, course pacing and driving spray | ConditioningControlPanel/Resources/web | ported-by-merge |
| 248 | `d6db721c0` | Regenerate the extended six-section Threshold course | ConditioningControlPanel/Resources/web | ported-by-merge |
| 249 | `0ff80db73` | Polish course textures and add shared boost media and malus Loom underlays | ConditioningControlPanel/Resources/web | ported-by-merge |
| 250 | `c76d5d128` | Improve driving control, course launch, audio and floor finish | ConditioningControlPanel/Resources/web | ported-by-merge |
| 251 | `b332d6e0f` | Regenerate Threshold with wider ordinary road sections | ConditioningControlPanel/Resources/web | ported-by-merge |
| 252 | `4e0b37f85` | Reduce course loading and avoid duplicate graphics preparation | ConditioningControlPanel/Resources/web | ported-by-merge |
| 253 | `49b8b8abf` | Retain shared race art through renderer-owned leases | ConditioningControlPanel/Resources/web | ported-by-merge |
| 254 | `18d145233` | Reuse garage racers and graphics across course worlds | ConditioningControlPanel/Resources/web | ported-by-merge |
| 255 | `8c32651b7` | Release course shadow targets through their light owner | ConditioningControlPanel/Resources/web | ported-by-merge |
| 256 | `703622676` | Retire old course callbacks while preserving audio and room tickets | ConditioningControlPanel/Resources/web | ported-by-merge |
| 257 | `95a10d644` | Switch race courses inside one retained application shell | ConditioningControlPanel/Resources/web | ported-by-merge |
| 258 | `fe2abf055` | Remove glancing camera hops and warm first-water cull variants | ConditioningControlPanel/Resources/web | ported-by-merge |
| 259 | `716608c45` | Keep the paused camera still and start race music at GO | ConditioningControlPanel/Resources/web | ported-by-merge |
| 260 | `fb591e6f1` | Adapt private Mainframe verse timing for local race previews | ConditioningControlPanel/Resources/web | ported-by-merge |
| 261 | `3e2bc8d9e` | Add cinematic course intros before solo race countdowns | ConditioningControlPanel/Resources/web | ported-by-merge |
| 262 | `424f2f591` | Polish Racing Thoughts reactive surfaces, media cards and course lettering | ConditioningControlPanel/Resources/web | ported-by-merge |
| 263 | `cec828a57` | Lengthen each course intro shot by one second | ConditioningControlPanel/Resources/web | ported-by-merge |
| 264 | `dd0291b38` | fix(race): make shortcuts forgiving and show wrong-way guidance | ConditioningControlPanel/Resources/web | ported-by-merge |
| 265 | `56a55c03f` | feat(race): align phrase and drop gates with recorded songs | ConditioningControlPanel/Resources/web | ported-by-merge |
| 266 | `697287d83` | Enlarge platform challenges with real jump gaps and landing feel | ConditioningControlPanel/Resources/web | ported-by-merge |
| 267 | `0af119b4f` | Polish boost pads, song-lit Keep tiles and driver view | ConditioningControlPanel/Resources/web | ported-by-merge |
| 268 | `6ba07bdee` | feat(race): add the selected GOONWORKS recording | ConditioningControlPanel/Resources/web | ported-by-merge |
| 269 | `2341f178e` | feat(race): vary Loom spirals across world and HUD | ConditioningControlPanel/Resources/web | ported-by-merge |
| 270 | `6dc3b969c` | feat(race): synchronize solo machinery with musical beats | ConditioningControlPanel/Resources/web | ported-by-merge |
| 271 | `fd2ccb4a7` | feat(race): connect heard beats and nearby press impacts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 272 | `40729cbd2` | feat(glassroad): add physical sung-word flight mechanics | ConditioningControlPanel/Resources/web | ported-by-merge |
| 273 | `75cb7ac7a` | feat(glassroad): add two long aerial crossings to every course | ConditioningControlPanel/Resources/web | ported-by-merge |
| 274 | `549d74297` | fix(race): keep first-beat machinery transitions continuous | ConditioningControlPanel/Resources/web | ported-by-merge |
| 275 | `e64b21f16` | feat(glassroad): materialize sung words as aerial platforms | ConditioningControlPanel/Resources/web | ported-by-merge |
| 276 | `194c9d465` | fix(race): resolve root-relative song audio paths | ConditioningControlPanel/Resources/web | ported-by-merge |
| 277 | `f538747a9` | test(glassroad): reuse vectors in the castle geometry audit | ConditioningControlPanel/Resources/web | ported-by-merge |
| 278 | `f7ad82fff` | feat(glassroad): sync Boudoir to Think Pink v5 | ConditioningControlPanel/Resources/web | ported-by-merge |
| 279 | `ba4dce20b` | feat(race): add music-reactive screen edges across courses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 280 | `910aa4c11` | feat(race): shape word-flight ramps and add musical jump cues | ConditioningControlPanel/Resources/web | ported-by-merge |
| 281 | `b2eb2cc6a` | feat(race): build solid themed letters for aerial catches | ConditioningControlPanel/Resources/web | ported-by-merge |
| 282 | `748ea7df9` | feat(race): carry players automatically across word platforms | ConditioningControlPanel/Resources/web | ported-by-merge |
| 283 | `996391a29` | fix(race): preserve scenery and authored audits around raised ramps | ConditioningControlPanel/Resources/web | ported-by-merge |
| 284 | `66500685e` | fix(race): keep reactor rings behind screen approach sightlines | ConditioningControlPanel/Resources/web | ported-by-merge |
| 285 | `0e15fa419` | feat(glassroad): add anchored cosmetic song cue transport | ConditioningControlPanel/Resources/web | ported-by-merge |
| 286 | `b455ebe94` | feat(glassroad): add pooled song veils and cosmetic flash bursts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 287 | `da790da6a` | Add pooled song marquees and hanging-screen motion | ConditioningControlPanel/Resources/web | ported-by-merge |
| 288 | `2d416d5fb` | feat(glassroad): select Locked v5 for The Keep preview | ConditioningControlPanel/Resources/web | ported-by-merge |
| 289 | `b13bc24e0` | feat(glassroad): toys item kit - Pendulum, Disc, Tether, Snap, Big Spiral, bigger Melt | ConditioningControlPanel/Resources/web | ported-by-merge |
| 290 | `449352721` | feat(glassroad): payoff words and flashes, Tether chain | ConditioningControlPanel/Resources/web | ported-by-merge |
| 291 | `897477d4a` | fix(glassroad): preserve beneficial scans and physical tether boundaries | ConditioningControlPanel/Resources/web | ported-by-merge |
| 292 | `f92466a7c` | fix(glassroad): preserve beneficial scans and physical tether boundaries | ConditioningControlPanel/Resources/web | ported-by-merge |
| 293 | `8474d53f5` | art(glassroad): distinct toy icons and measured physical props | ConditioningControlPanel/Resources/web | ported-by-merge |
| 294 | `d534f2fa0` | feat(glassroad): physical Tether chain and pass break | ConditioningControlPanel/Resources/web | ported-by-merge |
| 295 | `d918a5b64` | feat(glassroad): tactile toy flights and host-controlled hit looks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 296 | `a1df7d753` | fix(glassroad): restore screen effects module parsing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 297 | `fe48a7309` | fix(glassroad): song cues sit on fixed road spots and lock to the beat | ConditioningControlPanel/Resources/web | ported-by-merge |
| 298 | `edb53f8bd` | test(glassroad): the intro harness mirrors boot's DEV flag | ConditioningControlPanel/Resources/web | ported-by-merge |
| 299 | `738d898e2` | feat(glassroad): song cue SHIFT, count arches, install bar and world bursts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 300 | `40f5ecb96` | feat(glassroad): song cue swarm, giants and course props | ConditioningControlPanel/Resources/web | ported-by-merge |
| 301 | `d49461eb8` | Polish Glassroad materials and make toy flights readable | ConditioningControlPanel/Resources/web | ported-by-merge |
| 302 | `11f6d0ef7` | Polish race arches, rhythmic Keep tiles and water spray | ConditioningControlPanel/Resources/web | ported-by-merge |
| 303 | `9c344a195` | Refine aerial word scale, fall reveals and soft catches | ConditioningControlPanel/Resources/web | ported-by-merge |
| 304 | `a4a757977` | Build solo speed from clean driving and the heard song | ConditioningControlPanel/Resources/web | ported-by-merge |
| 305 | `b9338a0d6` | Align feel reference and spray expectations with the polish defaults | ConditioningControlPanel/Resources/web | ported-by-merge |
| 306 | `0f4c9cfcf` | Fit Keep arch grain to UV-less models and retain floor contrast | ConditioningControlPanel/Resources/web | ported-by-merge |
| 307 | `aa411ac2a` | Give road slabs clearance-aware structural depth | ConditioningControlPanel/Resources/web | ported-by-merge |
| 308 | `e98e09db8` | Build substantial themed bodies beneath jump platforms | ConditioningControlPanel/Resources/web | ported-by-merge |
| 309 | `1c366afe3` | Ease water boundaries through shallows puddles drains and spills | ConditioningControlPanel/Resources/web | ported-by-merge |
| 310 | `25497092e` | Keep liquid graph checks scoped around static drainage groups | ConditioningControlPanel/Resources/web | ported-by-merge |
| 311 | `b3c4ffdb5` | Verify tapered water heights and nested drain geometry | ConditioningControlPanel/Resources/web | ported-by-merge |
| 312 | `cc5e0a13c` | Give every course distinct outlined road lettering and semantic motion | ConditioningControlPanel/Resources/web | ported-by-merge |
| 313 | `a1f0c5ef1` | Enlarge road lettering and keep complete glyphs inside moving lanes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 314 | `0dc95bd2c` | Carry gaps with two long phrase jumps and a steady helicopter glide | ConditioningControlPanel/Resources/web | ported-by-merge |
| 315 | `01a66e6cc` | Give Goonworks four beat-driven openings and solid press contacts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 316 | `968336a21` | Animate Goonworks enamel with three song-driven floor patterns | ConditioningControlPanel/Resources/web | ported-by-merge |
| 317 | `9a402584c` | Add phrase landing spring, steering and delayed helicopter flight | ConditioningControlPanel/Resources/web | ported-by-merge |
| 318 | `edb4d9726` | Place dropping screens farther ahead and remove card face depth fighting | ConditioningControlPanel/Resources/web | ported-by-merge |
| 319 | `703b59f2d` | Give Boudoir mirrors live reflections and beat-driven escape openings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 320 | `500f14789` | Keep ground impacts level and spring continuously from outlined phrases | ConditioningControlPanel/Resources/web | ported-by-merge |
| 321 | `646b2e291` | Skip unseen Mainframe display redraws while preserving music phase | ConditioningControlPanel/Resources/web | ported-by-merge |
| 322 | `0bc7a6680` | Fix Firefox black scenes caused by forced Loom context loss | Assets/web/arcademy, ConditioningControlPanel/Resources/web | ported-by-merge |
| 323 | `d46c6c6a7` | Make late phrase catches spring into a three-piece kart rebound | ConditioningControlPanel/Resources/web | ported-by-merge |
| 324 | `74b87c353` | Keep banked road contact solid through rail bumps and side hops | ConditioningControlPanel/Resources/web | ported-by-merge |
| 325 | `f328b563a` | Give image screens solid frames and speed up their placement | ConditioningControlPanel/Resources/web | ported-by-merge |
| 326 | `5faa367ee` | Add directional material relief across all five courses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 327 | `47d1a1fa6` | Reduce race loading work while warming every visible variant | ConditioningControlPanel/Resources/web | ported-by-merge |
| 328 | `b8d16737b` | Keep the late falling rescue within the steady chase view | ConditioningControlPanel/Resources/web | ported-by-merge |
| 329 | `4e82a8d15` | Enlarge all course media screens with visible cleared placements | ConditioningControlPanel/Resources/web | ported-by-merge |
| 330 | `957bd9c67` | Keep the rebound assembly visible and skip inactive pose work | ConditioningControlPanel/Resources/web | ported-by-merge |
| 331 | `c03d128de` | Keep Loom cleanup compatible with older shared web helpers | ConditioningControlPanel/Resources/web | ported-by-merge |
| 332 | `a5fc31c6b` | Fit enlarged media using complete cabinet bounds | ConditioningControlPanel/Resources/web | ported-by-merge |
| 333 | `6b7283f2d` | fix(glassroad): preserve shortcut flow and catch curved-road jumps | ConditioningControlPanel/Resources/web | ported-by-merge |
| 334 | `ed6f8d400` | fix(glassroad): carry fast shortcuts through their landing runout | ConditioningControlPanel/Resources/web | ported-by-merge |
| 335 | `838e51f3e` | feat(glassroad): enlarge thrown toys and strengthen launch feedback | ConditioningControlPanel/Resources/web | ported-by-merge |
| 336 | `60d9c122a` | feat(glassroad): give phrase rebounds more height and pace | ConditioningControlPanel/Resources/web | ported-by-merge |
| 337 | `441993556` | feat(glassroad): add beat-driven traps to Keep spiral drives | ConditioningControlPanel/Resources/web | ported-by-merge |
| 338 | `f372f5c59` | Make four Glassroad floors respond with distinct musical shapes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 339 | `c332874a6` | Raise the chase view slightly for readable floor lettering | ConditioningControlPanel/Resources/web | ported-by-merge |
| 340 | `599268228` | Make racing media flashes scatter in shared view-blocking bursts | ConditioningControlPanel/Resources/web | ported-by-merge |
| 341 | `0c19018d0` | Retire pending flash cards after multiple effect replacements | ConditioningControlPanel/Resources/web | ported-by-merge |
| 342 | `7edea2f90` | fix(glassroad): release stuck controls and bound pickup height | ConditioningControlPanel/Resources/web | ported-by-merge |
| 343 | `5537b7407` | Add racing camera views and authored mirror close-up | ConditioningControlPanel/Resources/web | ported-by-merge |
| 344 | `81fecccf1` | Explain camera controls in How to Play | ConditioningControlPanel/Resources/web | ported-by-merge |
| 345 | `4c5f6e845` | fix(glassroad): keep the full phrase rebound rig in view | ConditioningControlPanel/Resources/web | ported-by-merge |
| 346 | `dabdfc698` | fix(glassroad): correct controller, reconnect and launch handling | ConditioningControlPanel/Resources/web | ported-by-merge |
| 347 | `0d6a44a69` | Keep the actual GO frame eligible for a launch boost | ConditioningControlPanel/Resources/web | ported-by-merge |
| 348 | `3351b2b23` | Restore visible GOONWORKS screens and fit complete cabinets to tube walls | ConditioningControlPanel/Resources/web | ported-by-merge |
| 349 | `b09fc4407` | Make course floors readable and keep screens with their frames | ConditioningControlPanel/Resources/web | ported-by-merge |
| 350 | `1c21a6f37` | Speed solo machinery and add shared breakable mirrors | ConditioningControlPanel/Resources/web | ported-by-merge |
| 351 | `30a8a679c` | feat(glassroad): restore bubble shield and course fog | ConditioningControlPanel/Resources/web | ported-by-merge |
| 352 | `168a04c25` | Give Threshold wider lanes and a living pink sky | ConditioningControlPanel/Resources/web | ported-by-merge |
| 353 | `06d883280` | Open Threshold galleries and keep stacked road clearance | ConditioningControlPanel/Resources/web | ported-by-merge |
| 354 | `63d893518` | Shorten platform fields and add worn Keep openings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 355 | `bd989d03e` | Align integration checks with shorter fields and faster machinery | ConditioningControlPanel/Resources/web | ported-by-merge |
| 356 | `3996f0878` | Keep pink clouds bright and soap bubbles readable | ConditioningControlPanel/Resources/web | ported-by-merge |
| 357 | `725c55765` | Keep pink clouds bright and soap bubbles readable | ConditioningControlPanel/Resources/web | ported-by-merge |
| 358 | `4cda1e6e5` | polish(glassroad): name restored items and match Keep fog | ConditioningControlPanel/Resources/web | ported-by-merge |
| 359 | `74e6ae06b` | Preserve floor support and gate clearance on widened Threshold | ConditioningControlPanel/Resources/web | ported-by-merge |
| 360 | `1f2a86595` | Make Racing Thoughts hazards strike on short beat cycles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 361 | `73ab35562` | Author moving trap stations and bridge shortcuts on all five courses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 362 | `b2d01c617` | Drive beat-locked horizontal pistons and timed forming bridges | ConditioningControlPanel/Resources/web | ported-by-merge |
| 363 | `bb52f6994` | Keep thuds and contact checks aligned with faster machinery | ConditioningControlPanel/Resources/web | ported-by-merge |
| 364 | `63b0200d5` | Give Boudoir its own timed powder bridge and a broad trap approach | ConditioningControlPanel/Resources/web | ported-by-merge |
| 365 | `e515240b2` | Keep piston bypasses clear of holes and plan boosted approaches early | ConditioningControlPanel/Resources/web | ported-by-merge |
| 366 | `9900008c4` | Place Keep ironwork clear of the complete missing-slab intervals | ConditioningControlPanel/Resources/web | ported-by-merge |
| 367 | `bf67b395a` | fix(glassroad): retain support on moving bridge slabs | ConditioningControlPanel/Resources/web | ported-by-merge |
| 368 | `0d50cdcdd` | Animate liquid DNA on the heard music grid and course colors | ConditioningControlPanel/Resources/web | ported-by-merge |
| 369 | `dfe33759c` | Lengthen horizontal trap heads and refine themed materials | ConditioningControlPanel/Resources/web | ported-by-merge |
| 370 | `a409723fc` | Replace menu engine noise with a soft instrumental melody | ConditioningControlPanel/Resources/web | ported-by-merge |
| 371 | `c303a465d` | feat(glassroad): logbook contract, record v1 store and medal table | ConditioningControlPanel/Resources/web | ported-by-merge |
| 372 | `c70d2f64d` | Keep the Goonworks horizontal trap above its driving surface | ConditioningControlPanel/Resources/web | ported-by-merge |
| 373 | `91c0c308c` | Build a layered bubblegum garage and upbeat menu instrumental | ConditioningControlPanel/Resources/web | ported-by-merge |
| 374 | `6ed74e784` | feat(glassroad): logbook Record panel, achievement wall and unlock-aware garage locks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 375 | `fa6428825` | feat(glassroad): account record sync for the Logbook (meta/profileSync.js) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 376 | `d162e71d5` | Keep racing effects out of the camera and fix controller menu navigation | ConditioningControlPanel/Resources/web | ported-by-merge |
| 377 | `f4a6694d7` | feat(glassroad): Time Trial with a best-run ghost and medal times | ConditioningControlPanel/Resources/web | ported-by-merge |
| 378 | `48bcb6790` | feat(glassroad): logbook META lane - run recorder, applyRun, achievements, unlocks, recap | ConditioningControlPanel/Resources/web | ported-by-merge |
| 379 | `6e2f0b094` | Pin the steady camera contract in the legacy boost regression | ConditioningControlPanel/Resources/web | ported-by-merge |
| 380 | `18daa506a` | docs(glassroad): medal times cross-checked against 8-kart races | ConditioningControlPanel/Resources/web | ported-by-merge |
| 381 | `1292797d6` | feat(glassroad): logbook recap pages on the results card | ConditioningControlPanel/Resources/web | ported-by-merge |
| 382 | `50029209e` | docs(glassroad): boot-check --finish timing for the long courses, trial flag | ConditioningControlPanel/Resources/web | ported-by-merge |
| 383 | `f67e9ee11` | feat(glassroad): toys kit online - Disc, Tether, Snap, Pendulum, Big Spiral on the room wire | ConditioningControlPanel/Resources/web | ported-by-merge |
| 384 | `eb947f53c` | fix(glassroad): one onlineCourseHash for the page, netbots and the room card generator | ConditioningControlPanel/Resources/web | ported-by-merge |
| 385 | `415caf0fd` | fix(glassroad): garage menu fits phones held sideways, 720p and portrait | ConditioningControlPanel/Resources/web | ported-by-merge |
| 386 | `aed182501` | fix(glassroad): chase follow runs on the drawn karts' clock, no pitch bounce at phone frame rates | ConditioningControlPanel/Resources/web | ported-by-merge |
| 387 | `97eca18de` | fix(justdrop): a refused sign-in handoff opens the signed-out page, not raw JSON (ccp-bugs #1499) | WPF: JustDropHostService.cs | needs-port (`sync9-fixes`) |
| 388 | `08bb30ba7` | fix(subliminal): the compositor card honours the Studio font pick (ccp-bugs #1493) | WPF: SubliminalService.cs | needs-port (`sync9-fixes`) |
| 389 | `81bc9fa21` | diag(browser): log why a web takeover's fullscreen request is refused (ccp-bugs #1492) | WPF: MainWindow.Browser.cs | needs-port (`sync9-fixes`) |
| 390 | `77d1e929e` | fix(chaster): a lock that ended or ran out no longer hides the credit behind a raffle day slip-up (ccp-bugs #1 | CCP.Core/Services/Chaster | ported-by-merge |
| 391 | `cdda883ac` | fix(browser): fullscreen browser hosts use a software target, blink preview decodes off the UI thread (ccp-bug | WPF: App.xaml.cs, BenignTaskFault.cs, BrowserHostSurface.cs, EnhancementPlayerWindow.xaml.cs, MainWindow.BlinkTrainer.cs, MainWindow.Browser.cs | needs-port (`sync9-fixes`) |
| 392 | `50339c51d` | feat(glassroad): web Scrolller pictures in the flash bursts, Online pictures switch | ConditioningControlPanel/Resources/web | ported-by-merge |
| 393 | `b315a742a` | fix(goon): a refused game card comes back with a reason (ccp-bugs #1507) | Assets/web/goon | ported-by-merge |
| 394 | `9f6a6b04d` | docs(leash): unseen assignments move to the day the leashed side comes back (ccp-bugs #1496) | CCP.Core/Services/Leash | ported-by-merge |
| 395 | `09fd6a825` | fix(glassroad): phones race in real time, the sim catches up a whole capped frame | ConditioningControlPanel/Resources/web | ported-by-merge |
| 396 | `26ba26f1b` | chore(glassroad): __glassroad.scrolller readout (consent, pictures, ready) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 397 | `c129d4c37` | perf(glassroad): water field steps live runs only, from static neighbour taps | ConditioningControlPanel/Resources/web | ported-by-merge |
| 398 | `50eabbbec` | perf(glassroad): minimap reads its box only after a resize | ConditioningControlPanel/Resources/web | ported-by-merge |
| 399 | `a4f00f4a6` | perf(glassroad): no reflection probe warm-up on a course without raised water | ConditioningControlPanel/Resources/web | ported-by-merge |
| 400 | `0c8de4ced` | feat(glassroad): Scrolller niches per track, pictures on by default | ConditioningControlPanel/Resources/web | ported-by-merge |
| 401 | `0cbf8107e` | fix(glassroad): a Quality change never relinks every program in view | ConditioningControlPanel/Resources/web | ported-by-merge |
| 402 | `182b6d59c` | perf(glassroad): phone tier water drops the live scene probe for a palette sheen | ConditioningControlPanel/Resources/web | ported-by-merge |
| 403 | `4e0cf4e18` | fix(glassroad): honest boot failures, WebGPU to WebGL2 fallback, dead-world recovery | ConditioningControlPanel/Resources/web | ported-by-merge |
| 404 | `4b8fb892c` | fix(glassroad): the stall watchdog forgives a late tick (one long task is not a dead loop) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 405 | `75ed7b60f` | fix(glassroad): a course switch no longer keeps the departed course in memory | ConditioningControlPanel/Resources/web | ported-by-merge |
| 406 | `ace80d971` | feat(glassroad): hit cam inset and item fire/hit feel | ConditioningControlPanel/Resources/web | ported-by-merge |
| 407 | `2a2aa3c7c` | feat(glassroad): staged shader preparation on the phone tier | ConditioningControlPanel/Resources/web | ported-by-merge |
| 408 | `a74caa54b` | feat(glassroad): staged warm pins far less to the cover | ConditioningControlPanel/Resources/web | ported-by-merge |
| 409 | `bdf559899` | fix(glassroad): dropping screens land whole in the chase view, on the kart's line | ConditioningControlPanel/Resources/web | ported-by-merge |
| 410 | `739ac6b08` | fix(glassroad): every dropped screen shows its own picture and never falls through a deck | ConditioningControlPanel/Resources/web | ported-by-merge |
| 411 | `3c81c80d0` | Polish Threshold cotton candy clouds and glass bubbles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 412 | `60ccb2021` | Add Mainframe green code hexagons and Matrix rain | ConditioningControlPanel/Resources/web | ported-by-merge |
| 413 | `abe1bf87d` | Add Keep dream clouds and bright floating padlocks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 414 | `2e585dcf3` | Dress Boudoir with satin bows lace pearls and cosmetics | ConditioningControlPanel/Resources/web | ported-by-merge |
| 415 | `14fa377e8` | Fill GOONWORKS sky with a morphing spiral and course keepsakes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 416 | `d008769bb` | Connect five course atmospheres to race lifetime and motion settings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 417 | `129fd85be` | feat(glassroad): Mainframe plays song v5 (drain pop burst removed) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 418 | `9f7687bbd` | Fix Glassroad web media, course music and intro hitches | ConditioningControlPanel/Resources/web | ported-by-merge |
| 419 | `aff5d48a0` | Update fallback assertion for transparent world screens | ConditioningControlPanel/Resources/web | ported-by-merge |
| 420 | `9ae707613` | perf(glassroad): one material per look, one vertex program per instanced material | ConditioningControlPanel/Resources/web | ported-by-merge |
| 421 | `8f7783383` | perf(glassroad): the water probe sees only the reflection layer | ConditioningControlPanel/Resources/web | ported-by-merge |
| 422 | `ec2f4bf8f` | fix(glassroad): items never move the chase camera; edge flash asks photosafe; fire tick keeps the confirm rumb | ConditioningControlPanel/Resources/web | ported-by-merge |
| 423 | `0a6b3173e` | perf(glassroad): shareInstancePrograms in its own module | ConditioningControlPanel/Resources/web | ported-by-merge |
| 424 | `e772ff609` | feat(glassroad): logo on the title card, course shots in the chooser | ConditioningControlPanel/Resources/web | ported-by-merge |
| 425 | `dc941f1dc` | fix(glassroad): new logo on the loading screen; course mode label readable on every shot | ConditioningControlPanel/Resources/web | ported-by-merge |
| 426 | `80228c5b1` | fix(glassroad): web Scrolller screens play the GIF-style clips | ConditioningControlPanel/Resources/web | ported-by-merge |
| 427 | `15933968a` | Fix unsafe boost approaches, tall rail contact and chase camera angle | ConditioningControlPanel/Resources/web | ported-by-merge |
| 428 | `0addaa449` | Keep song screens and framing checks aligned with the new chase angle | ConditioningControlPanel/Resources/web | ported-by-merge |
| 429 | `078ef7f38` | feat(pbp): host intent mode 'quick' starts Quick match at once | Assets/web/piecebypiece | ported-by-merge |
| 430 | `621dfdbe4` | Slow solo race traps and arrange closures around the song | ConditioningControlPanel/Resources/web | ported-by-merge |
| 431 | `1739578d0` | feat(glassroad): garage first, the course's lights warm behind the menu | ConditioningControlPanel/Resources/web | ported-by-merge |
| 432 | `7dcd1e79b` | feat(glassroad): camera - look-point hints and rotational trauma on the drawn copy | ConditioningControlPanel/Resources/web | ported-by-merge |
| 433 | `68283421d` | feat(glassroad): world - permanence, final-lap wake, speed sense, cascades, rival glances | ConditioningControlPanel/Resources/web | ported-by-merge |
| 434 | `cdaf8cdc6` | feat(glassroad): moments - rocket start ladder, hitter-hit, lap final flag, HUD pulses, Motion option | ConditioningControlPanel/Resources/web | ported-by-merge |
| 435 | `aa69e156f` | feat(glassroad): audio - sub thump, drift squeal, final lap, rocket start, hitter chime, wind | ConditioningControlPanel/Resources/web | ported-by-merge |
| 436 | `2ca016798` | feat(glassroad): trauma - one trauma value, render-only hit-stop and finish slow frame | ConditioningControlPanel/Resources/web | ported-by-merge |
| 437 | `7f1a3798c` | feat(glassroad): scene wiring for the world feel lane (scorch, debris, final-lap wake, rival glances, motion l | ConditioningControlPanel/Resources/web | ported-by-merge |
| 438 | `d96bdced7` | feat(glassroad): feel wave integration hooks (hud racers, drift charge, motion to world and garage, kart wobbl | ConditioningControlPanel/Resources/web | ported-by-merge |
| 439 | `0d237403d` | perf(glassroad): draw every kart's marine drive as one fleet, let pickup cards cull | ConditioningControlPanel/Resources/web | ported-by-merge |
| 440 | `8d29e905b` | perf(glassroad): one cheap matrix walk per frame instead of stock updateMatrixWorld per render | ConditioningControlPanel/Resources/web | ported-by-merge |
| 441 | `28ae87f2c` | perf(glassroad): cut per-frame instance and upload work (Firefox frame cost, lane 2) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 442 | `397e42b3a` | perf(glassroad): upload world video frames straight from the <video> | ConditioningControlPanel/Resources/web | ported-by-merge |
| 443 | `5f4b2a16d` | Keep shadow catchers out of shadowless inset warmup | ConditioningControlPanel/Resources/web | ported-by-merge |
| 444 | `4f13be621` | Clear GOONWORKS carousel horses and tube particle clutter | ConditioningControlPanel/Resources/web | ported-by-merge |
| 445 | `c490ba4ce` | Keep song helix geometry outside the GOONWORKS tube | ConditioningControlPanel/Resources/web | ported-by-merge |
| 446 | `947888adb` | feat(glassroad): chess piston heads and staggered Boudoir thumpers | ConditioningControlPanel/Resources/web | ported-by-merge |
| 447 | `77e1d2eab` | feat(glassroad): trap telegraph with headroom, no repeated flash-burst pictures | ConditioningControlPanel/Resources/web | ported-by-merge |
| 448 | `7dde57a1a` | feat(glassroad): vivid beat-driven floor colour across three courses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 449 | `2dd415b60` | fix(glassroad): keep vivid floor within shader graph budget | ConditioningControlPanel/Resources/web | ported-by-merge |
| 450 | `c41247cb0` | perf(glassroad): living icons re-upload only their redrawn cells; dry road words skip per-frame rewrites | ConditioningControlPanel/Resources/web | ported-by-merge |
| 451 | `5848b249f` | test(glassroad): verify distinct Boudoir boost sequences | ConditioningControlPanel/Resources/web | ported-by-merge |
| 452 | `3892e82c2` | test(glassroad): require boost use on each completed lap | ConditioningControlPanel/Resources/web | ported-by-merge |
| 453 | `b8f35668e` | perf(glassroad): merge static GOONWORKS toy parts and share ink hulls (fewer draws) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 454 | `a2e18f9be` | ci(glassroad): verify game suite on feature pull requests | docs/tests/release only: build.yml | n/a |
| 455 | `bc0b02d7a` | fix(ci): keep Windows test arguments below path limit | docs/tests/release only: build.yml | n/a |
| 456 | `783ea01f8` | fix(glassroad): Firefox WebGPU draws soft props, shadow catchers and clips | ConditioningControlPanel/Resources/web | ported-by-merge |
| 457 | `55b430d71` | perf(glassroad): cheaper per-draw uniform, node and attribute bookkeeping on WebGL2 | ConditioningControlPanel/Resources/web | ported-by-merge |
| 458 | `066d1bdfa` | perf(glassroad): cut the shadow pass on high (FF60 shadows lane) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 459 | `eddf0fb9b` | perf(glassroad): frustum cull rigid kart parts (mechanisms, accents) per camera | ConditioningControlPanel/Resources/web | ported-by-merge |
| 460 | `ec6fea9d7` | perf(glassroad): cube warm shows every LOD level; music edge wash on a one-row canvas; dev link log | ConditioningControlPanel/Resources/web | ported-by-merge |
| 461 | `2063cbc6c` | perf(glassroad): cull racer skinned meshes on a fixed kart sphere | ConditioningControlPanel/Resources/web | ported-by-merge |
| 462 | `480312063` | perf(glassroad): one walk record per node, shared group versions kept on the binding | ConditioningControlPanel/Resources/web | ported-by-merge |
| 463 | `125c454ba` | fix(glassroad): no shadow map render while shadows are switched off (Threshold 3 s stall) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 464 | `d27ea326e` | perf(glassroad): warm the rival name-tag sprite program behind the cover | ConditioningControlPanel/Resources/web | ported-by-merge |
| 465 | `5c865719d` | perf(glassroad): WebGL2 uploads send only changed bytes (Firefox command channel) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 466 | `2e1b50efb` | feat(glassroad): share live waveform, spectrum and musical attack data | ConditioningControlPanel/Resources/web | ported-by-merge |
| 467 | `e82437e10` | feat(glassroad): phosphor instruments and musical slate mosaics | ConditioningControlPanel/Resources/web | ported-by-merge |
| 468 | `7a9e947b1` | feat(glassroad): silk spectra and comic soundstage visualizers | ConditioningControlPanel/Resources/web | ported-by-merge |
| 469 | `816ea04e3` | feat(glassroad): draw live sound and struck caustics through water | ConditioningControlPanel/Resources/web | ported-by-merge |
| 470 | `90c3e5745` | fix(glassroad): cull distant gate mist and update shared video bindings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 471 | `c83a04629` | fix(glassroad): keep musical slate clear of broad artwork masks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 472 | `c2be0101c` | fix(glassroad): retain per-copy effect materials to avoid per-draw state churn | ConditioningControlPanel/Resources/web | ported-by-merge |
| 473 | `24553801b` | fix(glassroad): distinguish musical slate frequency banks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 474 | `7564e765a` | Revert "fix(glassroad): retain per-copy effect materials to avoid per-draw state churn" | ConditioningControlPanel/Resources/web | ported-by-merge |
| 475 | `3dfe6f6dc` | fix(glassroad): scope attribute program sharing to instanced mesh builders | ConditioningControlPanel/Resources/web | ported-by-merge |
| 476 | `2e526ea73` | fix(glassroad): keep one fixed sampled image per warmed video owner | ConditioningControlPanel/Resources/web | ported-by-merge |
| 477 | `6d3c511f6` | test(glassroad): include scoped instance builder in course lifecycle fixture | ConditioningControlPanel/Resources/web | ported-by-merge |
| 478 | `8adb76d9f` | fix(glassroad): prepare shared five-course shape infrastructure | ConditioningControlPanel/Resources/web | ported-by-merge |
| 479 | `5e1862138` | fix(glassroad): align scenery and source authoring with reshaped courses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 480 | `a94ce8ff8` | fix(glassroad): activate broad bends and actual-speed boost corridors | ConditioningControlPanel/Resources/web | ported-by-merge |
| 481 | `735aac49a` | fix(glassroad): verify every course and live boost run | ConditioningControlPanel/Resources/web | ported-by-merge |
| 482 | `a9e68ed28` | perf(glassroad): reuse identical road samples in boost safety checks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 483 | `00a162a83` | feat(glassroad): give musical attacks independent visual clocks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 484 | `81a65bb20` | feat(glassroad): play slate and phosphor as separate musical instruments | ConditioningControlPanel/Resources/web | ported-by-merge |
| 485 | `64133650e` | feat(glassroad): separate silk and comic light by musical role | ConditioningControlPanel/Resources/web | ported-by-merge |
| 486 | `4d269db83` | feat(glassroad): separate bass, snare and high water light | ConditioningControlPanel/Resources/web | ported-by-merge |
| 487 | `500e0ee82` | fix(glassroad): keep quiet melodic slabs visibly distinct | ConditioningControlPanel/Resources/web | ported-by-merge |
| 488 | `95557dd98` | fix(glassroad): cap the comic road lookup across long courses | ConditioningControlPanel/Resources/web | ported-by-merge |
| 489 | `a02de8e9e` | fix(glassroad): expose quiet music shapes in water | ConditioningControlPanel/Resources/web | ported-by-merge |
| 490 | `466cc1da5` | fix(glassroad): clear GOONWORKS scenery and stabilize Boudoir mirrors | ConditioningControlPanel/Resources/web | ported-by-merge |
| 491 | `fe7452363` | fix(glassroad): omit GOONWORKS pictures hidden behind the road | ConditioningControlPanel/Resources/web | ported-by-merge |
| 492 | `76337fa18` | fix(glassroad): preserve reflection camera across nested draws | ConditioningControlPanel/Resources/web | ported-by-merge |
| 493 | `f8487fcff` | fix(glassroad): phones lift the cover after the first pictures; a stalled load fails with Retry | ConditioningControlPanel/Resources/web | ported-by-merge |
| 494 | `6c20a7675` | fix(glassroad): phones decode the race song at half rate; touch throw button sits bottom right | ConditioningControlPanel/Resources/web | ported-by-merge |
| 495 | `a069c5edd` | fix(glassroad): STOMP heel reads as a stiletto pump | ConditioningControlPanel/Resources/web | ported-by-merge |
| 496 | `171e827d2` | fix(glassroad): shortcuts catch at speed - route commit assist, earlier lead chevrons | ConditioningControlPanel/Resources/web | ported-by-merge |
| 497 | `7e7270cdb` | fix(glassroad): online seats survive engine rounding; a refused seat says why | ConditioningControlPanel/Resources/web | ported-by-merge |
| 498 | `a8b8a5ab7` | feat(glassroad): ?join=<roomId> seats you in that room (CCP lobby open-race rows) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 499 | `5a353e6b0` | feat(lobby): list open Racing Thoughts 3D races | WPF: LauncherWindow.Lobby.cs, LiveProvider.cs, LobbyRowView.cs, LobbyService.cs, MainWindow.Lobby.cs, RaceOpenApi.cs | needs-port (`sync9-lobby-invite`) |
| 500 | `79c468b2b` | fix(glassroad): cap the GOONWORKS road table at 8192 texels | ConditioningControlPanel/Resources/web | ported-by-merge |
| 501 | `83fa738a6` | feat(glassroad): real speed x1.25, readout unchanged | ConditioningControlPanel/Resources/web | ported-by-merge |
| 502 | `21787579c` | feat(glassroad): speed feel, the view widens and the edges blur at speed | ConditioningControlPanel/Resources/web | ported-by-merge |
| 503 | `ab9a04261` | fix(glassroad): steer assist Help never takes the wheel, autopilot bus event | ConditioningControlPanel/Resources/web | ported-by-merge |
| 504 | `4ac22d31c` | fix(glassroad): HUD playtest fixes - fire key on the item slot, opt-in speedo, Autopilot chip | ConditioningControlPanel/Resources/web | ported-by-merge |
| 505 | `46099cdc7` | feat(glassroad): dev/decision-gaps.mjs measures time between decisions per course | ConditioningControlPanel/Resources/web | ported-by-merge |
| 506 | `5df22c940` | feat(glassroad): drift sparks wear the pip colours, wind lines at speed | ConditioningControlPanel/Resources/web | ported-by-merge |
| 507 | `4d365164c` | feat(glassroad): solo races default to 1 lap, pre-race card, speedo option | ConditioningControlPanel/Resources/web | ported-by-merge |
| 508 | `3195ba8a6` | feat(glassroad): track picker shows the solo lap choice and its distance | ConditioningControlPanel/Resources/web | ported-by-merge |
| 509 | `aff0cc2b9` | fix(glassroad): a rail never turns a moving kart round; the wheel mirrors only in reverse | ConditioningControlPanel/Resources/web | ported-by-merge |
| 510 | `84010dd73` | fix(glassroad): item boxes read as item boxes - "?" cards inside chartreuse bubbles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 511 | `3b2f7328c` | fix(glassroad): wind line constants as float nodes, a touch brighter | ConditioningControlPanel/Resources/web | ported-by-merge |
| 512 | `e2cbd45de` | test(glassroad): mesh build budget scales with course length, best of two loads | ConditioningControlPanel/Resources/web | ported-by-merge |
| 513 | `c67a76ed9` | feat(glassroad): speed factor F = 1.1 with the whole boost envelope time-scaled | ConditioningControlPanel/Resources/web | ported-by-merge |
| 514 | `5649cda8a` | docs(glassroad): shownTopMps comment names the real top (33 m/s) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 515 | `c80901989` | test(glassroad): mesh build budget scales with course length, best of two loads | ConditioningControlPanel/Resources/web | ported-by-merge |
| 516 | `79f98829c` | fix(glassroad): race notices sit clear of the item slot | ConditioningControlPanel/Resources/web | ported-by-merge |
| 517 | `61b55bdbc` | test(glassroad): course length budget guard | ConditioningControlPanel/Resources/web | ported-by-merge |
| 518 | `1bfc330ed` | feat(glassroad): My files - own pictures on the course screens instead of Scrolller | ConditioningControlPanel/Resources/web | ported-by-merge |
| 519 | `b191bab61` | fix(glassroad): phone memory protections key on the device, not the pinned tier | ConditioningControlPanel/Resources/web | ported-by-merge |
| 520 | `1efaa7ae1` | fix(glassroad): drift pip ring wears the wheel sparks' tier colours | ConditioningControlPanel/Resources/web | ported-by-merge |
| 521 | `83d9a11ec` | feat(glassroad): expose course media customization and skip static matrix walks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 522 | `0e1a9dd6e` | fix(glassroad): restore saved picture sources without duplicate song downloads | ConditioningControlPanel/Resources/web | ported-by-merge |
| 523 | `9049aee20` | fix(glassroad): add rear throw and right-hand keyboard controls | ConditioningControlPanel/Resources/web | ported-by-merge |
| 524 | `3abe6b2d9` | feat(glassroad): show course themes and default lyric content | ConditioningControlPanel/Resources/web | ported-by-merge |
| 525 | `5497f7cc2` | fix(glassroad): prepare every hit-camera LOD before racing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 526 | `f006c1f2e` | fix(glassroad): keep hit-camera warm and live layers identical | ConditioningControlPanel/Resources/web | ported-by-merge |
| 527 | `78b8a5d05` | fix(glassroad): clear retired interleaved GPU buffer entries | ConditioningControlPanel/Resources/web | ported-by-merge |
| 528 | `2f0464612` | fix(glassroad): isolate and draw hit-camera warm pipelines | ConditioningControlPanel/Resources/web | ported-by-merge |
| 529 | `bc48879ae` | Separate musical drops and expressive string plucks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 530 | `1baf8cf79` | Add ten Afterimage backgrounds selected per course load | ConditioningControlPanel/Resources/web | ported-by-merge |
| 531 | `f1e910bf5` | Mix musical tile phrases with brighter drops and bending strings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 532 | `b2a8efea1` | Open distant scenery to vivid musical backgrounds | ConditioningControlPanel/Resources/web | ported-by-merge |
| 533 | `fb35e9fe6` | Restore full Scrolller and DropDuel cards inside pickup bubbles | ConditioningControlPanel/Resources/web | ported-by-merge |
| 534 | `4ea69ebdc` | Preserve momentum through drop shortcut landings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 535 | `e9a785f6b` | Add rear item drops and favor speed and shields near the front | ConditioningControlPanel/Resources/web | ported-by-merge |
| 536 | `8594c97aa` | perf(glassroad): use lighter phone scenery and distant kart meshes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 537 | `0b043a907` | fix(glassroad): preserve custom skin masks in distant kart geometry | ConditioningControlPanel/Resources/web | ported-by-merge |
| 538 | `06c4ff220` | fix(glassroad): smooth phone startup and viewport changes | ConditioningControlPanel/Resources/web | ported-by-merge |
| 539 | `f340cfccc` | fix(glassroad): report progress within race shader batches | ConditioningControlPanel/Resources/web | ported-by-merge |
| 540 | `bb40cf9ea` | Add course intro soundscapes tied to cinematic timing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 541 | `6c9079949` | Recess side pistons into sliding road pockets | ConditioningControlPanel/Resources/web | ported-by-merge |
| 542 | `74809495c` | Support piston jumps and preserve safe race corridors | ConditioningControlPanel/Resources/web | ported-by-merge |
| 543 | `845286166` | test(glassroad): cover atomic initial viewport sizing | ConditioningControlPanel/Resources/web | ported-by-merge |
| 544 | `7c95ed669` | feat(glassroad): make floor lettering answer the music | ConditioningControlPanel/Resources/web | ported-by-merge |
| 545 | `300a20cb8` | fix(glassroad): keep sung word accents clear under bright effects | ConditioningControlPanel/Resources/web | ported-by-merge |
| 546 | `78d252a42` | feat(glassroad): road words as coins (playtest fix, lane words) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 547 | `675b4510b` | feat(glassroad): air words spawn under the kart, bigger and higher, vivid with rim and sparks, springier hops | ConditioningControlPanel/Resources/web | ported-by-merge |
| 548 | `cf0a83db1` | Select semantic word pickups and validate grounded crossings | ConditioningControlPanel/Resources/web | ported-by-merge |
| 549 | `2b6568f6a` | Add ten GPU letter identities with readable arrival and collection | ConditioningControlPanel/Resources/web | ported-by-merge |
| 550 | `5d1d6b15c` | Animate actual road keywords and preserve trigger bubble ownership | ConditioningControlPanel/Resources/web | ported-by-merge |
| 551 | `087b10339` | Finish airborne word trails and honor motion off | ConditioningControlPanel/Resources/web | ported-by-merge |
| 552 | `628ad9d69` | Resolve keyword identities inside preserved trigger phrases | ConditioningControlPanel/Resources/web | ported-by-merge |
| 553 | `ea05a6ea8` | Apply semantic identities to the actual song gate signs | ConditioningControlPanel/Resources/web | ported-by-merge |
| 554 | `74ea9b678` | Animate the full matched song vocabulary with 36 word mechanisms | ConditioningControlPanel/Resources/web | ported-by-merge |
| 555 | `cdefb05a9` | Make word pickups pop with bounded lift sparkles and sound | ConditioningControlPanel/Resources/web | ported-by-merge |
| 556 | `a667ed2c5` | Enable matching online word pickups and finish reset and Gentle polish | ConditioningControlPanel/Resources/web | ported-by-merge |
| 557 | `007a2c1aa` | fix(glassroad): speed edge blur reaches the shader; intro sound fade no longer clicks | ConditioningControlPanel/Resources/web | ported-by-merge |
| 558 | `eaee8fc3d` | fix(glassroad): Gentle music floors fade section and bar steps | ConditioningControlPanel/Resources/web | ported-by-merge |
| 559 | `b1d4c57bc` | fix(glassroad): the phone's loopback server no longer hides the lobby songs | ConditioningControlPanel/Resources/web | ported-by-merge |
| 560 | `3930199ac` | feat(sparkles): claim service + wallet adoption (B1 W1) | WPF: App.xaml.cs, SparklesClaim.cs, SparklesClaimService.cs, SparklesReceipt.cs | needs-port (`sync9-sparkles`) |
| 561 | `9d7d5406e` | test(sparkles): fold the claim tests to keep the PR under the line cap | WPF: SparklesClaimService.cs | needs-port (`sync9-sparkles`) |
| 562 | `28c334234` | fix(sparkles): a place receipt only raises the wallet | WPF: SparklesClaimService.cs | needs-port (`sync9-sparkles`) |
| 563 | `21873bc05` | feat(sparkles): Arcademy, DtRH, Caucus and Intake claim Sparkles (B1 W2) | WPF: ArcademyHostService.cs, CaucusHostService.cs, DtrhHostService.cs, IntakeHostService.cs, SparklesGameEnd.cs | needs-port (`sync9-sparkles`) |
| 564 | `48d232dd9` | feat(sparkles): Racing Thoughts 3D claims Sparkles by place (B1 W4) | WPF: GlassroadHostService.cs, GlassroadRules.cs, SparklesGameEnd.cs | needs-port (`sync9-sparkles`) |
| 565 | `a568665e0` | feat(sparkles): chess, Goon and Breakout claim Sparkles (B1 W3) | WPF: BackRoomHostService.cs, GoonHostService.cs, PieceByPieceHostService.cs, SparklesGameEnd.cs, SparklesPageResults.cs | needs-port (`sync9-sparkles`) |
| 566 | `de0947d9f` | fix(sparkles): the Breakout Story Demo pays like the full game | WPF: BackRoomHostService.cs, SparklesPageResults.cs | needs-port (`sync9-sparkles`) |
| 567 | `b96b5931c` | feat(sparkles): the results slip, host side (B1 W5) | WPF: ArcademyHostService.cs, CaucusHostService.cs, DtrhHostService.cs, IntakeHostService.cs, SparklesSlip.cs | needs-port (`sync9-sparkles`) |
| 568 | `c3a4cdd43` | feat(sparkles): the results slip, page side (B1 W5) | Assets/web/arcademy, Assets/web/dtrh, Assets/web/intake, ConditioningControlPanel/Resources/web | ported-by-merge |
| 569 | `cc1bdc4b0` | feat(sparkles): Claim carries the online proof (shared by W3 and W4) | WPF: SparklesGameEnd.cs | needs-port (`sync9-sparkles`) |
| 570 | `b9b541bcb` | test(sparkles): one slip tree per line so later trees add without conflicts | docs/tests/release only: SparklesSlipParityTests.cs | n/a |
| 571 | `b2487a365` | feat(sparkles): results slip in chess and Goon | WPF: GoonHostService.cs, PieceByPieceHostService.cs | needs-port (`sync9-sparkles`) |
| 572 | `7c086f322` | feat(sparkles): results slip in Racing Thoughts 3D | WPF: GlassroadHostService.cs | needs-port (`sync9-sparkles`) |
| 573 | `70c3d96e6` | feat(sparkles): results slip in Breakout | WPF: BackRoomHostService.cs | needs-port (`sync9-sparkles`) |
| 574 | `f763e6a40` | fix(goon): the Sparkles slip sits bottom left, clear of the recap buttons | Assets/web/goon | ported-by-merge |
| 575 | `fcff83904` | fix(sparkles): the slip never takes a click, and trance pages play it calm | Assets/web/dtrh, ConditioningControlPanel/Resources/web | ported-by-merge |
| 576 | `d7348e6a0` | fix(sparkles): the slip's wallet chip counts to the app's own wallet | WPF: SparklesSlip.cs | needs-port (`sync9-sparkles`) |
| 577 | `d81611c81` | fix(sparkles): chess and Goon take the reviewed slip module (no clicks, calm option) | ConditioningControlPanel/Resources/web | ported-by-merge |
| 578 | `5ee971ae3` | fix(sparkles): Breakout takes the reviewed slip; Back Room Calm plays it calm | Assets/web/backroom, ConditioningControlPanel/Resources/web | ported-by-merge |
| 579 | `ba537a09b` | fix(sparkles): Racing Thoughts claims full length only for trial and ghost, caps solo laps | WPF: GlassroadRules.cs | needs-port (`sync9-sparkles`) |
| 580 | `a76daed30` | fix(sparkles): Racing Thoughts drops a receipt that lands mid-race and follows the Motion row | ConditioningControlPanel/Resources/web | ported-by-merge |
| 581 | `e0356be6e` | feat(sparkles): a down server reads "Bank closed", not "Offline" | WPF: SparklesClaimService.cs, SparklesSlip.cs | needs-port (`sync9-sparkles`) |
| 582 | `463045dfa` | build(glassroad): ship Racing Thoughts 3D in the WPF build (7.2 A2) | WPF build/release only (version is Version.props here; the web tree ships from Assets/web): ConditioningControlPanel.csproj | n/a |
| 583 | `099607382` | feat(glassroad): Racing Thoughts 3D for everyone, 2D race door retired (7.2 A2) | WPF: BackRoomHostService.cs, GameSurfaces.cs, GlassroadHostService.cs, GlassroadRules.cs, LauncherCatalogue.cs | needs-port (`sync9-glassroad`) |
| 584 | `c3c64ba4c` | fix(launcher): Racing Thoughts blurb fits the 3D race | CCP.Core/Localization/Languages | ported-by-merge |
| 585 | `701f9cdd6` | feat(glassroad): join-room host frame for a window that is already up | ConditioningControlPanel/Resources/web | ported-by-merge |
| 586 | `d6a39c7ac` | feat(lobby): race rows open Racing Thoughts in the app on ?join= | WPF: GlassroadHostService.cs, MainWindow.Lobby.cs, RaceOpenApi.cs | needs-port (`sync9-lobby-invite`) |
| 587 | `639e10a0b` | feat(glassroad): host-owned friends invite in the pre-race room | ConditioningControlPanel/Resources/web | ported-by-merge |
| 588 | `cc6ad7a39` | feat(friends): invite a friend into your Racing Thoughts room | WPF: FriendsLanding.cs, GlassroadHostService.cs, GlassroadRules.cs | needs-port (`sync9-lobby-invite`) |
| 589 | `c7af815a0` | fix(glassroad): Racing Thoughts 3D is Prime only, every door | WPF: BackRoomHostService.cs, FriendsLanding.cs, GlassroadHostService.cs, LauncherCatalogue.cs, MainWindow.PlayTab.cs, PlayTabView.xaml | needs-port (`sync9-glassroad`) |
| 590 | `9718fd3f4` | feat(glassroad): the garage offers only owned kart bodies | WPF: GlassroadHostService.cs, GlassroadRules.cs (web half merged in Assets/web/glassroad) | needs-port (`sync9-glassroad`) |
| 591 | `9d08ba4d7` | fix(copy): the tier-2 lock label says Prime, not Lab | CCP.Core/Localization/Languages (hm3_rail_lock_t2, all ten files) | ported-by-merge |
| 592 | `fe3c3790c` | feat(rain-wardrobe): fitting room page shell and viewer (1/4) | Assets/web/rain-wardrobe (web only, no host references it yet on either head) | ported-by-merge |
| 593 | `503817d4c` | feat(rain-wardrobe): garment, hair and jewellery modules (3/4) | Assets/web/rain-wardrobe (web only, no host references it yet on either head) | ported-by-merge |
| 594 | `9ef595efc` | feat(rain-wardrobe): colour studio, review studio and runtime info (4/4) | Assets/web/rain-wardrobe (web only, no host references it yet on either head) | ported-by-merge |

## Follow-up merge `a9605b2cc` (pin `a11ada22e`)

On `avalonia-port/main-20261030-compat`, after the compat commit. Conflict resolutions only: the two new files
`meta/owned.js` and `tests/garage-owned.test.mjs` kept where git's directory-rename detection put them
(`Assets/web/glassroad/`). The other glassroad edits followed the moved tree on their own. Checks: WPF test project builds
(0 errors); `node --test` garage-owned, garage and logbook-record-ui: 19/19.

## Follow-up merge `047449d1f` (pin `ddc43afeb`)

No conflicts. Main added a new web tree `ConditioningControlPanel/Resources/web/rain-wardrobe` (12 files, the
fitting room pages 1, 3 and 4; page 2 is not on main yet). Git does not move a new tree, so the merge `git mv`s it to
`Assets/web/rain-wardrobe`; the existing `Assets/web/**` content glob ships it for both heads. No C# references it yet.

## Watch list: on the way to main, not merged here yet

These are not in this sync. The next sync (or A5) must pick them up, and must add a ledger row for each commit:

| ref | sha | what | trap for the next sync |
|---|---|---|---|
| `preview/7.2.0` | `3c05cc1f5` | 42 commits ahead of main (24 non-merge): the Prize Counter shop (B3-W1/W2/ART), the launcher Sparkles wallet chip, `shop_*` loc keys | its web tree is new under `ConditioningControlPanel/Resources/web/prizecounter`. Git will NOT move a new tree, so `git mv` it to `Assets/web/prizecounter` and fix the csproj excludes, CI paths and tests (same as glassroad here). WPF host `Services/PrizeCounter` -> Avalonia port lane |
| `feat/glassroad-a` | `248c978c1` | 3 commits not on main: `19d633049` owned kart bodies (twin of `9718fd3f4`), `7a83f0356` host-owned friends invite in the pre-race room, `6a5ed13c4` join-room host frame for an open window | lands through A2 catch-up merges; `7a83f0356`/`6a5ed13c4` belong to `sync9-lobby-invite` |

Older open port debt that still applies (not repeated here): sync 7 needs-port rows in
[`avalonia-main-sync-20261015.md`](avalonia-main-sync-20261015.md) and sync 8 rows in
[`avalonia-main-sync-20261024.md`](avalonia-main-sync-20261024.md). The sync 8 safety lanes (panic-voice, lockdown,
remote-end, factory-reset, settings-freeze, chaster-leash) are finished locally but not pushed, so the frozen tip
`a3d9e984f` does not have them.

## How a row gets closed

A needs-port row is closed when its lane's PR lands on `port/7.2`. Change the status cell to
`ported (#PR, sha)` in that PR, never in a separate docs PR, so the ledger and the code land together. A row
that turns out to need nothing is changed to `n/a` with the reason. `~/ccp-port/bin/ledger-check` keeps the row count
and numbering honest.

## Merge notes (`avalonia-port/main-20261030`)

Conflict resolutions only (23 files):
- `sparkles-slip.js` x7: kept where git's directory-rename detection put them (`Assets/web/<tree>/`).
- `ConditioningControlPanel/Resources/web/glassroad` (new on main, 1444 files) moved to `Assets/web/glassroad`, the layout
  the stack uses and the path `GameWindow.Glassroad.cs` already expects. csproj harness excludes and the `glassroad` CI job follow.
- Languages x8: union of the stack keys and `Sparkles_Slip_*`; no duplicate keys.
- `ChasterService` (Core): main's `Waits(...)` logic with the stack's Serilog logging.
- `LobbyModel` (Core): the stack's `LobbyPollPlan` plus main's `LobbyGame.Race`; the Race label key added to `LobbyPageRules`.
- `LauncherCatalogue`: the stack's host dictionary; the race entry is now Glassroad, Prime only (main `c7af815a0`).
- `IntakeHostService`: the stack's Core `IntakeRun` plus main's Sparkles claim (`firstResult` read from `IntakeRun.ResultReceived`).
- `LobbyRowView`: the stack's `LobbyPageRules` delegate.
- WPF csproj `<Version>`: stays in `Version.props`; `build-installer.bat` keeps the stack layout with `VERSION=7.1.15`.
- `build.yml`: the stack jobs plus main's `glassroad` job (working directory `Assets/web/glassroad`).

## -compat (`avalonia-port/main-20261030-compat`)

- `RaceOpenApi.cs` moved to `CCP.Core/Services/Lobby/` (Core `LobbyModel` uses it); logger is Serilog.
- `ChasterService` line 628 and `SubliminalLayer`: `App.Logger` replaced with Serilog in Core.
- `SubliminalLayer.ResolveFace` (main `08bb30ba7`) no longer reads the WPF pack:// resource in Core; the WPF head sets
  `SubliminalLayer.BundledFaceStream = FontPickerHelper.BundledFaceStream` (static ctor of `SubliminalService`).
- `DtrhHostService` Sparkles claim reads the Core `DtrhRunBooking` (main used locals the stack moved into `DtrhRunWire.Book`).
- Tests that read the web tree (`GlassroadHostTests`, `SparklesSlipParityTests`) read `Assets/web`.
- `Version.props` 7.1.15 (matches `installer.iss`, `build-installer.bat`, `UpdateService.AppVersion`).

Local builds: CCP.Core, CCP.Avalonia and the WPF test project (EnableWindowsTargeting) all build with 0 errors.

Open for the reviewers (not changed here):
- `build-installer.bat` on this line packages the Avalonia head for Windows. The 7.2 plan ships Windows as WPF from `main`,
  so this branch must not be the Windows installer source.
