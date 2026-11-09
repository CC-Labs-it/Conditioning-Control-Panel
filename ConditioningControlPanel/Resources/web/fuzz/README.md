# Terms & Conditioning (T&C; working folder name: fuzz)

A free browser clicker in the shape of Stimulation Clicker that advertises CCP from inside the joke. One grey button on a
grey page. Every buy adds something to the screen and stays on. Buying Drop ends the main run. Five choices
on the way make 192 routes and 13 endings. `data/brand.js` holds the names; the currency's is still a placeholder.

Brief: `handoff-1102/GAME-BUILD-BRIEF.md`. **To add an unlock read `ENGINE.md`.** Host wire: `CONTRACT.md`.

## Run

```bash
cd ConditioningControlPanel/Resources/web/fuzz
node dev/serve.mjs 8791                              # http://localhost:8791/fuzz/index.html
node --test --test-concurrency=2 tests/*.test.mjs    # sim, routes, contract
node dev/smoke.mjs                                   # one whole route headless, screenshots in dev/shots/
node dev/gen-items.mjs                               # data/items.js + ui/demos/* from the design source (env FUZZ_DESIGN)
```

Debug door: `?pace=N`, `?buy=id,id`, `?mood=m_x`, `?pictures=none`, `?fresh=1`, `window.fuzz` (`ENGINE.md`).

Do not open `index.html` from `file://`: modules and the Loom need a server rooted at `Resources/web`.

## Four-hour player preview

Open `/fuzz/index.html?preview=journey&slot=playtest` on the combined host. A new slot starts with the picture choice, contract, 0 Fuzz and one button. Reopening the same slot resumes it. The funded showcase and the original short player preview keep their own saves.

The shop uses seven chapters and 144 purchase opportunities to reach Drop. Prices follow a reference route and respect discounts. Each main fork counts lifetime Fuzz earned. Completing its discounted purchases also opens the fork. Golden Buttons can award one free purchase per chapter. Ranks and specialties persist with the run. The two-minute activity banks fill during active play and pay out on the corresponding activity.

Run `node dev/journey-timing.mjs --all` from this directory to measure time to Drop and the longest interval between paid purchases on all 192 route combinations at four and two taps per second. The model uses average side income. Scripted browser play also exercises the individual route modifiers, prizes, and cards.

November 7 scripted browser checks, with active time measured after onboarding and free buys excluded from the gap statistics:

| Route | Taps/s | Drop | Median paid gap | Longest paid gap |
| --- | ---: | ---: | ---: | ---: |
| Watch, Trance, Swipe Feed, Rest, More Ads | 4 | 3h 54m | 1m 47s | 2m 28s |
| Listen, Bimbo, Slideshow, Tap, Fewer Ads | 4 | 3h 29m | 1m 35s | 2m 09s |
| Watch, Infection, No Pictures, Rest, More Ads | 2 | 4h 09m | 1m 53s | 2m 55s |

## Original short-run timing

November 7 baseline for the main ending (Drop), measured before the passive-income bonuses on bounces and manual taps. In scripted browser play on the Trance and Bimbo routes, Drop took 37.4 to 37.8 minutes at 4 taps/s and 40.5 to 46.9 minutes at 2 taps/s. These are active play times from the button onward. The scripted player skips alternate endings.

Across all 192 routes, the economy model with side income and modifiers took 54.4 to 61.3 minutes at 4 taps/s. `tests/routes.test.mjs` checks the model against a 50 to 62 minute window. The browser also executes individual widget rewards and route effects. Mini Slots and the dispenser pay in full outside the shared side reward budget.

To measure the actual game in a browser:

```bash
node dev/serve.mjs 8811 &
node dev/measure.mjs --port 8811                     # 8 routes, all themes, both sides of each fork, 4 taps/s; 20 to 30 min of wall time
node dev/measure.mjs --routes "eyes,sweet,slides,sell,trance" --profile both --out run.json
```

It plays the real page on a stepped clock with a scripted person (one muted Firefox, nothing left behind) and prints minutes to
each ending, money by source, longest gap between buys and fork times. The owner's November 7 direction is a multi-hour first run with early purchases that stay useful as income grows.

## Module map

| Path | What | Rule |
|---|---|---|
| `data/items.js` | every unlock, lane and fork, final names | generated, never edited |
| `data/brand.js` | the game and currency names | the only place either is written; copy uses `{game}`, `{short}`, `{cur}` |
| `data/config.js` | open owner calls and tunables | |
| `data/words.js` | recorded word clips | never synthesis; a missing clip is silence |
| `sim/economy.js` | prices, gates, forks, modifiers, shop window, endings | pure |
| `sim/moods.js` | the numbers of the Run Moods | pure; none on a first run |
| `sim/route.js` | a greedy player through one route | the timing test runs all 192 |
| `sim/save.js` | run + lifetime, versioned, clamped | never throws |
| `ui/engine.js` | game clock, scopes, verbs, the one ending flow | no drawing |
| `ui/context.js` | the `ctx` an unlock gets | the contract; `CTX_MEMBERS` is tested |
| `ui/unlocks/*.js` | one registry file per group | no switch in app code, ever |
| `ui/app.js` | layers, button, toast, floats, pause card, frame loop | the one rAF |
| `ui/shop.js` `ui/icons.js` | goal row + rolling window; one call draws an icon | icon sets swap in `icons.js` |
| `ui/cards.js` `ui/fork.js` | queued cards; the choice card | every card closes |
| `ui/demos/*` | the demo kit | generated, loaded on the first choice card |
| `ui/intro.js` | what comes before the button; one entry, `runIntro()` | a later lane replaces the whole file with the joke contract |
| `ui/pictures.js` `ui/ads.js` | the only doors to pictures and ads | sources: online, own, none; placeholder art until the Scrolller lane |
| `audio/kit.js` | one master, four buses | armed on the first gesture |
| `fx/spiral.js` | the Loom canvas | imports the Loom, never forks it |
| `bridge.js` | host wire, unhosted = no-ops | |
| `host-escape.js`, `sparkles-slip.js` | byte copies | parity tests name them |

## Rules this tree keeps

- Wake Up and Escape pause and mute; `suspend` and `close` beat everything. Wake Up sits outside `#world`, above all of it.
- A trick may annoy; it may never make the game impossible. Every card closes, every lock runs out.
- Gentle: nothing over 3 Hz, flashes become fades, spiral still.
- Juice (`docs/JUICE-PLAYBOOK.md`): every effect has an IN and an OUT, rewards fly to the counter, failure subtracts.
- Plain first: colour, fonts, sound and motion are things the player buys, so app code adds none of them.

## Object controls and tabs

Features use transparent outer surfaces and compact content. Hold an object's casing to move it; buttons, sliders,
quiz tricks and playfields keep their own controls. Bottom tabs toggle visibility and drag to reorder. A red dot means
the feature has an action ready. Hiding does not reset its timers, rewards or sound settings. There is no open-window cap.

New objects use free space, or start tucked if no space is available. Expandable toys reserve room for their active state
without drawing blank padding. The mixer starts tucked. Manual/restored positions remain authoritative. Hover a feature to reveal its top-left L resize grip; drag it to scale the complete object. Touch briefly reveals the grip too. Sizes persist with the layout; focus the grip and use arrow keys to resize or Home to reset. Tabs stay outside
page tilt effects. Old fixed window sizes migrate once; layout and tab order persist through reloads and new runs.

`node dev/object-desktop-drive.mjs` covers desktop and native touch controls, readiness, saved preferences, crowded scenes
and secondary features. `dev/layout-mixer-drive.mjs` covers source volume/mute and `dev/hud-pile-drive.mjs` covers combined toys.
Keyboard: Alt+Left/Right reorders the focused tab; Ctrl+arrows moves its object. Touch: hold a tab before reordering, swipe
normally to scroll the strip. New bitmap art is a separate asset lane; this change retains existing artwork.

## Small arcade widgets

`ui/unlocks/arcade/` contains Toy Box, Mini Breakout, Racing Thoughts footage, Mini Slots and the Prize Dispenser. Each has
its own purchase and saved state; the existing quiz uses the chosen T&C theme. Clock and payout models have Node tests.
`dev/arcade-drive.mjs` checks real desktop/touch interactions. `dev/measure.mjs` exercises their controls and reports action
counts. The race clip is authentic recorded gameplay, muted; it never controls the collect bonus. MP4 files are in Git LFS.

## Theme podcasts

Each theme's podcast unlock loads its show from `assets/audio/podcast/<theme>/manifest.json`. The Chastity lane uses the `chastity` media folder. The combined preview serves the finished season privately. For a static game build, extract the private runtime pack at the game root, next to `index.html`. Each manifest lists the three episodes, their transcripts and the 21 clips for that show.

## Still separate

Real Scrolller sign-in/feed, CCP clip ads, per-ending art, desktop/web/phone doors and the final
currency name remain separate work.
