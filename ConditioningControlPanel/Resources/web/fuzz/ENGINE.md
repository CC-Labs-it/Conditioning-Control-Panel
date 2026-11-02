# ENGINE: how to add an unlock

Read `handoff-1102/GAME-BUILD-BRIEF.md` first (hard rules). The design for each unlock is its demo in
`ui/demos/` (or the live guide) and its row in `data/items.js`. The real feature looks and behaves like the demo, full size.

## One entry per unlock

Your group's file is `ui/unlocks/<group>.js`, `export default { <id>: { mount(ctx) { ... } } }`. Touch only your file.
Never touch the DOM at import time. No entry = the row still sells and pays, and draws nothing.

```js
/* Thoughts: every 4 s a thought crosses the top, and tapping pays a little extra while it is up. */
ticker: {
  mount(ctx) {
    ctx.every(4000, () => ctx.say('Where was I'));
    const dot = ctx.el('div', 'position:absolute;right:8px;bottom:8px;', 'hm', ctx.layer('floaters'));
    ctx.anim(dot, [{ opacity: 0.2 }, { opacity: 1 }, { opacity: 0.2 }], { duration: 2000, iterations: Infinity });
    ctx.mod({ click: (v, state) => v + (state.owned.ticker ? 1 : 0) });
    return { tick(dt, now) { /* each frame of game time */ }, off() { /* only for what ctx did not make */ } };
  },
},
```

`mount` runs on buy and again on every reload, so read what you need from `ctx.store` and redraw. It returns
`{ tick(dt, now)?, off()?, grow(count)? }`. `dt` is seconds, `now` is game-clock ms. A stacking row with `grow` stays
mounted and hears the new count; any other re-buy remounts. Your code throwing 3 times switches the unlock off; the game
carries on. An ending row may also export `ending(ctx) -> Promise`: its show between the fade and the credits, up to 90 s.

## Testing it

`node dev/serve.mjs 8791`, then:

- `/fuzz/index.html?buy=e_speed,sound`: starts owning those rows and all their parents, picks the lanes they need, skips the
  intro, saves nothing. A row on a lane the route already locked is refused with a console warning.
- `?pace=50`: income x50 (max 500). `?mood=m_pink`: force a run mood. `?fresh=1`: no save, no intro. `?pictures=none`: force a picture source.
- `window.fuzz`: `.buy(id)` `.pick('f1','eyes')` `.add(1000)` `.grant(id)` `.tap(10)` `.end('drop')` `.newRun()`, `.state`, `.life`, `.items`, `.engine`.
- `node dev/smoke.mjs` plays a whole route headless and fails on any page error. Screens land in `dev/shots/`.
- `node --test --test-concurrency=2 tests/*.test.mjs` includes the timing of all 192 routes.

## ctx

Everything made through ctx dies with the unlock and stops on pause, suspend and the end. Timers and frames run on the
game clock, so never use `setTimeout`, `setInterval` or your own `requestAnimationFrame`.

**Reading**

- `ctx.item`: the row (`id, name, what, line, cost, lane, req, fps, click, wall, grow`). `ctx.count`: how many are owned.
- `ctx.state`: the run, read only, live: `fuzz total taps walls buys clock owned order picks mood sources endless`. `ctx.life`: the lifetime, read only: `runs endings seen routes bestMs perk`.
- `ctx.has(id)`, `ctx.pick('f1')` (the chosen lane or null), `ctx.fmt(n)`, `ctx.rng()`, `ctx.rates()` (`{fps, click, wall, walls}` right now, walls = hits per second per bouncing button), `ctx.config`.
- `ctx.brand`: `.game .short .currency .currencyPlural`, `.text('{cur}')`, `.money(n)`. Never write the names; write `{game}`, `{short}`, `{cur}`.
  Text given to `el`, `say`, `toast`, `float`, `modal` is token-expanded for you.
- `ctx.gentle`, `ctx.reduced`: live. Gentle: nothing above 3 Hz, flashes become fades of 250 ms or more, spiral still, no sway, no warp. If an effect cannot be gentle, leave it off.
- `ctx.hosted`, `ctx.caps`, `ctx.host(type, fields)`: the app wire, see `CONTRACT.md`.

**Drawing**

- `ctx.layer(name)`: `back` (behind everything), `wash` (full-page tint), `floaters` (above the page, below the shop), `ticker`, `meters` (under the counter), `banner` (ad slots, in the page flow above the shop), `side` (the emptier of the two rails; `ctx.layer('side','left'|'right')` to choose; 0 wide on phones, so check `ctx.bounds().w`), `over` (over everything except cards and Wake Up), `toast`, `credits` (the slot on the credits card, for the After lane).
- Layers ignore the pointer: a child you want tappable needs `pointer-events:auto` in its css. Nothing goes above Wake Up. Never move or restyle `#wake`.
- `ctx.el(tag, cssText, text?, parent?)`: parent is an element or a layer name; default `floaters`.
- `ctx.css(text)`: a stylesheet. `ctx.vars({'--page':'#ffdcee'})`: page variables, restored on off: `--page --ink --ink-soft --ink-faint --btn --panel --panel-short --line --fill --stamp --font --shop-font`.
- `ctx.world`: the element holding all layers and the page, for a lean or warp. Wake Up and the cards are outside it. Do not filter it every frame on a phone.
- `ctx.bounds()`: `{w, h}` of the page, cached; use it instead of measuring each frame.
- `ctx.every(ms, fn)`, `ctx.after(ms, fn)`, `ctx.frame(fn(dt, now))`: each returns a cancel function. Share these; live particles have a cap, so prefer `ctx.float(x, y, text, css?)` for rising numbers.
- `ctx.anim(el, keyframes, opts)`: Web Animations. Keep loops at or under 3 Hz.

**Money**

- `ctx.pay(amount, source, {x, y}?)`: a lump. With a point it flies to the counter. Pace applies, so pass plain numbers like `ctx.rates().fps * 30`.
- `ctx.wallHit({x, y, corner})`: a bouncing thing hit a wall: pays the wall value, emits `wall`, answers the gain.
- `ctx.mod({fps?, click?, wall?, walls?, price?})`: value modifiers `(value, state, now) => value`, `price` also gets `item` last. Pure: no DOM, no rng, no writes. Registered on each mount, folded in order. For a chance-based payoff use `ctx.on('tap', ...)` + `ctx.pay`.

**Events**

- `ctx.on(event, fn)`: `tap {gain,x,y,auto}`, `buy {item,count,cost}`, `fork {fork,lane}`, `wall {gain,x,y,corner}`, `pause`, `resume`, `golden`, `ad`, `pictures {source}`. `ctx.emit(event, data)` sends one: Golden Button emits `golden`, an ad click emits `ad`.

**The main button**: `ctx.button`

- `.el`, `.skin(name)` sets `data-skin` on it, latest wins, gone on off; style it with `ctx.css('#btn[data-skin=round]{...}')`.
- `.press()` a tap that counts, `.melt(ms)` (max 8 s), `.swap(ms)` slides to the other side and back, `.lockFor(ms, label)` (max 30 s). All stay completable. To dodge or shrink, keep it tappable; Intake's `Steer` in `Resources/web/intake/core/contracts.js` is the model.

**Words and cards**

- `ctx.say(text)`: the thought ticker. `ctx.toast(text)`: a line under the button; newest replaces.
- `ctx.modal({title, body, buttons:[{label, onPress, keepOpen?}], node?, ms?})`: one at a time, queued; returns `{close()}`. No buttons and no `ms` gets a Close button. Must never cost the run. No text inputs, ever.

**Sound and media**

- `ctx.audio.muteFor(ms)`: everything silent for up to 15 s, then back.
- `ctx.audio.play(path, {bus, gain})`, `ctx.audio.loop(path, {bus, gain})` -> `{stop(ms)}`. Buses `sfx ambient voice podcast`. A missing file is silence. Clips in `assets/audio/`.
- `ctx.voice(slug, opts?)`: a recorded word (`data/words.js`); no clip = silence. Never speech synthesis.
- `await ctx.pictures.next({niche})` -> `{url, credit}` or null. Null is normal: draw an empty frame, never an error. Show `credit` when set. Never fetch a picture any other way. `ctx.pictures.source` is `online` (signed in), `own` (the player's files, on this device only) or `none`; a change arrives as the `pictures` event, so refill what is on screen. Choosing a source is not an unlock's job.
- `ctx.ads.next({kind})` -> the next record from `data/ads.js`.
- `ctx.store.get(key)`, `ctx.store.set(key, value)`: small JSON in the save for this unlock (After lane entries: kept across runs).

## The spiral

Every spiral comes from the Loom, never a fork of it. Import `createSpiral` from `../../fx/spiral.js`:

```js
const c = ctx.el('canvas', 'position:absolute;left:50%;top:50%;width:110vmax;height:110vmax;transform:translate(-50%,-50%);', null, 'back');
const sp = createSpiral(c, { reduced: ctx.gentle, manual: true });
sp.show();
ctx.frame((dt) => sp.step(dt));
return { off() { sp.dispose(); } };
```

## Special lanes

- `meta`: moods. The sim numbers live in `sim/moods.js`. The entry for `state.mood` mounts at run start; there is none on the first run.
- `after`: every `z_*` entry mounts when the credits card is up and draws into `ctx.layer('credits')`. `ctx.life` holds what it needs; set a perk through a later API (ask the main session).
- Endings: `drop`, `outside` and the rest all go through one flow (`engine.end`). `onemore` does not end the run; `state.endless` turns on.

## Rules that bite

Pointer-events inherit. Bare `#page` transforms move the shop; use your own layer. Number formatting goes through `ctx.fmt`. No hover-only information. Cap what you spawn: a phone runs this late with 100 features on.
