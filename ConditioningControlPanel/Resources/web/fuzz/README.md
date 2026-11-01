# Fuzz

Stimulation Clicker, but the stimulation is a hypno session. Pop a bubble for Fuzz, buy every CCP feature as a
layer, and the last purchase, Drop, cannot be undone. Free for everyone; shareable on its own URL.

Plan: `handoff-1101/FUZZ-CLICKER-PLAN.md`. Wire: `CONTRACT.md`. The mockup that this tree ports:
artifact `QAUhkG9ZhBZJW83nBf3azw` (owner proposal, 2026-11-01).

## Run

```bash
cd ConditioningControlPanel/Resources/web/fuzz
node dev/serve.mjs            # http://localhost:8787/fuzz/index.html  (?pace=8 = a 6-minute run)
node --test tests/*.test.mjs  # pure sim tests, also in CI (build.yml "Fuzz sim tests")
```

Hosted (desktop, phone, the website shell) the page gets `init` over the bridge; unhosted it boots at once
with the default caps. Do not open `index.html` from `file://`: the Loom import needs a server rooted at
`Resources/web`.

## Module map

| Path | What | Rule |
|---|---|---|
| `data/items.js` | the 44-tile ladder: price, effect fields, line, icon, widget key | copy voice pinned by tests |
| `data/words.js` | recorded word banks (Circe clips) | never synthesis; a missing clip is silence |
| `sim/economy.js` | prices, click, tick, bounce, buy, shop window, Drop | pure, tested |
| `sim/events.js` | golden bubble, Triggers, boxes, Levels | pure, rng and clock injected |
| `sim/save.js` | one versioned localStorage save, clamped parse | never throws |
| `sim/format.js` | commas, clock | one locale on purpose |
| `bridge.js` | host wire, queue-until-init, pre-buffer | unhosted = no-ops |
| `host-escape.js` | kept Escape replay | byte copy of goon's |
| `sparkles-slip.js` | the results slip | byte copy; `SparklesSlipParityTests` lists this tree |
| `audio/kit.js` | one AudioContext, master gain, buses | armed on the first click |
| `fx/spiral.js` | the Loom canvas | imports `../../arcademy/engine/loom/loomSpiral.js`, never forks it |
| `ui/app.js` | counter, bubble, shop, pause card, loop, save | draws what sim returns |
| `ui/widgets/*.js` | one file per layer, registered in `ui/widgets/index.js` by item `widget` key | adding a layer = adding a file |
| `dev/serve.mjs` | local static server | no dependencies |

## Rules this tree keeps

- Wake Up (top right) and Escape pause and mute; `suspend`/`close` from the host win over everything. Never more
  permissive than panic.
- Gentle: nothing flickers over 3 Hz; words fade over 250 ms or longer; Flash is a 600 ms soft fade at most every 5 s;
  reduced motion = still spiral, no sway, no tunnel.
- Juice (`docs/JUICE-PLAYBOOK.md`): every effect has an IN and an OUT; rewards fly to the counter; the party is
  sized to the event; failure subtracts.
- Assets are copies: feature art from `Resources/features` (640 px webp), EMI from `arcademy/art/emi`, prize icons
  from `arcademy/art/prizes`, pops and plucks from `Resources/sounds`, Circe words from `Resources/Audio/backroom/words`.
  Nothing here fetches from another tree except the Loom import.

## Owed (in order)

1. Port the mockup's widgets: ticker, trigger words, metronome, hum, mantra, subliminals, achievements + Depth,
   heartbeat, feed panels, companion with the pet ladder, levels, screen time, boxes, flash, inbox, lock, takeover,
   triggers, the Voice, tunnel, the Drop ending.
2. A headless smoke (`dev/smoke.mjs`, Playwright Firefox) that buys the ladder with `window.fuzz` and screenshots.
3. Desktop door: launcher tile + Play tile on a `ChaosWebViewHost`, `GameSurfaces` entry, `SparklesClaim.IsGame`
   + `SparklesPageResults` parser for `fuzz-ended`, server price.
4. Pictures through `caps.scrolller` (desktop and phone), DOM `<img>` on the web.
5. Website and phone doors (sync-arcademy vendoring, phone web pack).
