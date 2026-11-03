# Terms & Conditioning (T&C; working folder name: fuzz)

A free browser clicker in the shape of Stimulation Clicker that advertises CCP from inside the joke. One grey button on a
grey page. Every buy adds something to the screen and stays on. About 30 minutes later the last buy ends the run. Five choices
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

## How long a run takes

`tests/routes.test.mjs` pins route timing with pure numbers: every route takes 25 to 37 minutes at 4 taps/s with side money and
modifiers on (`sim/route.js`, option `side` and `mods`). Ordinary `ctx.pay` prizes share a budget (`SIDE_SHARE` in
`data/config.js`, `sideLump` in `sim/economy.js`), and modifiers past a knee count at a fraction (`MOD_*`). Mini Slots is the
explicit exception: scarce free coins limit spins, and its wins pay in full. The pure sim cannot predict these interactions.
The no-prizes run buys every optional toy and remains below 51 minutes. To check the real game, which is too slow for CI:

```bash
node dev/serve.mjs 8811 &
node dev/measure.mjs --port 8811                     # 8 routes, all themes, both sides of each fork, 4 taps/s; 20 to 30 min of wall time
node dev/measure.mjs --routes "eyes,sweet,slides,sell,trance" --profile both --out run.json
```

It plays the real page on a stepped clock with a scripted person (one muted Firefox, nothing left behind) and prints minutes to
each ending, money by source, longest gap between buys and fork times. Aim: `drop` in 25 to 37 minutes, 50 at 2 taps/s, no alternate
ending before 20, gaps under about 60 s, side money 10 to 25% of income.

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

## Small arcade widgets

`ui/unlocks/arcade/` contains Toy Box, Mini Breakout, Racing Thoughts footage, Mini Slots and the Prize Dispenser. Each has
its own purchase and saved state; the existing quiz uses the chosen T&C theme. Clock and payout models have Node tests.
`dev/arcade-drive.mjs` checks real desktop/touch interactions. `dev/measure.mjs` exercises their controls and reports action
counts. The race clip is authentic recorded gameplay, muted; it never controls the collect bonus. MP4 files are in Git LFS.

## Still separate

Real Scrolller sign-in/feed, CCP clip ads, finished podcast recordings, per-ending art, desktop/web/phone doors and the final
currency name remain separate work.
