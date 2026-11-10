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
A row can export `take(ctx, releaseWorld) -> Promise` to use the frozen game page before the fade. Its streams play until the take
ends or calls `releaseWorld()`. That call unmounts the game while keeping the take and its prepared media alive. Bimbodoll uses it
between swallowing the page and entering the sky. A take has `ENDING_TAKE_MAX_MS` of game time by default, or the row's `takeMaxMs`.
The row's `fadeColour` and `fadeMs` control the final fade, up to 1600 ms.

## Testing it

`node dev/serve.mjs 8791`, then:

- `/fuzz/index.html?buy=e_speed,sound`: starts owning those rows and all their parents, picks the lanes they need, skips the
  intro, saves nothing. A row on a lane the route already locked is refused with a console warning.
- `?pace=50`: income x50 (max 500). `?mood=m_pink`: force a run mood. `?fresh=1`: no save, no intro. `?pictures=none|own|online`: force a picture source. `online` uses local placeholder art. `?preview=toys` grants the six toys, refill upgrades, Prize Wheel, metronome, rain, binaural hum and sample pictures, with ready prizes. Reload resets its in-memory run, but preserves window layouts and sound preferences in `fuzz.preview.prefs.v1`, separate from the normal save. Optional `&theme=trance` selects a theme.
- `window.fuzz`: `.buy(id)` `.pick('f1','eyes')` `.add(1000)` `.grant(id)` `.tap(10)` `.end('drop')` `.newRun()`, `.state`, `.life`, `.items`, `.engine`.
- `node dev/smoke.mjs` plays a whole route headless and fails on any page error, then runs `dev/pile-check.mjs`: with a whole route on at once, Wake Up, Escape and a host suspend freeze the page, the audio and every stream; a pop-up nobody answers lets go of the button and the goal row; nothing flickers above 3 Hz under Gentle or OS reduced motion. `PILE_ALL=1` does all eight themes. Screens land in `dev/shots/`.
- `node dev/intro-drive.mjs` drives the contract, Decline, skimming, the host wire and the pictures card by real input, desktop and 390 px.
- `node dev/object-desktop-drive.mjs` checks hold movement, tab toggling/reordering, native touch, readiness, saved layouts and crowded features. `node dev/layout-mixer-drive.mjs` checks source volume/mute. Drivers use one muted browser at a time and leave normal saves untouched. The object driver defaults to Chromium for native touch scrolling; OBJECT_BROWSER=firefox retains gesture comparison support.
- `node dev/prestige-measure.mjs`: Renewal Sky pacing, minutes to Drop and Stardust per Depth for the cheapest-first sky, the every-star-the-Depth-opens sky and the full sky (pure loop on dev/journey-timing.mjs plus the boost helpers).
- `node dev/prestige-drive.mjs` is the Renewal Sky regression: end a run, Renew, reload mid-Sky (renewed once, the Sky reopens), light a star, the Looks shelf (locked before the core), Next run with rules, a word and a keepsake, then reload the renewed run and check the stamp, pay, pstore, ghost recording and looks. Muted Chrome, its own server on 8839, desktop and 390 px; exits 1 on a failed check or page error (`SHOTS=1` saves screens).
- `node dev/emi-takeover-drive.mjs` (its own `node dev/serve.mjs 8861`) drives the EMI Takeover run-up in muted Chrome: Transmission, FREE WILL, Founded Startup and her guest tiles, Self Replication (player clicks turn page objects into EMIs or multiply them), the forced HIVE MIND and its patterns. `node dev/emi-takeover-end-drive.mjs` (`node dev/serve.mjs 8862`) drives the go_end take through `dev/play-ending.html?end=go_end`: pixel grid + mixed-size pile, shut, DROP, error, freeze, giant EMI, eyelid, then the recap with the poster. `W=390 H=844 TOUCH=1` for the phone. Rules: `sim/takeover.js` (run-up) and `sim/takeover-end.js` (ending); brief handoff-1109/TC-EMI-TAKEOVER-V3-BRIEF.md. Her tiles are one guest element in the shop grid (`shopDoor.guest(el)`), never engine rows.
- `node --test --test-concurrency=2 tests/*.test.mjs` includes the timing of all 192 routes.

## ctx

Everything made through ctx dies with the unlock and stops on pause, suspend and the end. Timers and frames run on the
game clock, so never use `setTimeout`, `setInterval` or your own `requestAnimationFrame`.

**Reading**

- `ctx.item`: the row (`id, name, what, line, cost, lane, req, fps, click, wall, grow`). `ctx.count`: how many are owned.
- `ctx.state`: the run, read only, live: `fuzz total taps walls buys clock owned order picks mood sources spent endless`. `ctx.life`: the lifetime, read only: `runs endings seen routes bestMs perk`.
- `ctx.prestige`: Renewal Sky, read only: `{ depth, dust, next, run, level(lane), has(starId), lit(endingId) }` (`run` = this run's stamp: rules, words, minutes). `ctx.pstore`: a prestige lane's own bag in the life save, kept across runs (null for ordinary unlocks). Lane entries live in `ui/unlocks/prestige/`, brief handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md. `ctx.endRun(id = 'drop')` ends the run now on that ending, once (Fine Print Speedrun); `ctx.audio.silence(on)` keeps the whole page silent while on (Fine Print Silent). `ctx.shop(n)`, `ctx.price(id)`, `ctx.buy(id)`: the shop door, prestige lanes only (null elsewhere). A mod with `post: true` folds after the MOD_*_KNEE soft cap like a timed one but still pays away time; mod fields `away(rate)` and `awayCap(ms)` shape away pay.
- `ctx.has(id)`, `ctx.pick('f1')` (the chosen lane or null), `ctx.fmt(n)`, `ctx.rng()`, `ctx.rates()` (`{fps, click, wall, walls, manualClick, bounce}` right now. `manualClick` includes Round Corners and `bounce` includes the Bouncing Button bonus. `walls` is hits per second per bouncing button), `ctx.config`.
- `ctx.brand`: `.game .short .currency .currencyPlural`, `.text('{cur}')`, `.money(n)`. Never write the names; write `{game}`, `{short}`, `{cur}`.
  Text given to `el`, `say`, `toast`, `float`, `modal` is token-expanded for you.
- `ctx.paused`: true during a pause or a suspend. Timers and frames stop by themselves; only your own window or document listeners need to ask.
- `ctx.gentle`, `ctx.reduced`: live. Gentle: nothing above 3 Hz, flashes become fades of 250 ms or more, spiral still, no sway, no warp. If an effect cannot be gentle, leave it off.
- `ctx.hosted`, `ctx.caps`, `ctx.host(type, fields)`: the app wire, see `CONTRACT.md`.
- `ctx.identity`: `{displayName}` when the host signed the player in (`init.identity`, web /tc), else null. Only the Living Ad card prints it.
- `ctx.openLink(url)`: the one way to open a real page. Only `https://cclabs.app` and its paths, and only called directly inside the player's own press on your element or card button: not after an `await` or `ctx.after`, not from a tap on the game button. Answers whether it opened. Never `window.open` or an `<a>` of your own.
- `ctx.openAdLink(url)`: the same rule for a real CCP ad, and at most `CONFIG.AD_TABS_PER_RUN` (2) tabs a run (owner 2026-11-08: more was annoying); past the cap it opens nothing and answers false, and the press still pays. Basic and Prime players (host `init.caps.plan`) never get a real card from `ctx.ads.next`.

**Drawing**

- `ctx.layer(name)`: `back` (behind everything), `wash` (full-page tint), `screens` (ui/screens.js: under the stage and the shop), `floaters` (above the page, below the shop), `ticker`, `meters` (under the counter), `banner` (ad slots, in the page flow above the shop), `side` (the window hosts; use `ctx.dock`, this is only for reading them), `over` (above windows), `fx` (noninteractive effects and drag ghosts above windows), `toast`, `ending` (above the end fade: an ending's show draws here, never on `ctx.world` with a z-index), `credits` (the slot on the credits card, for the After lane).
- Layers ignore the pointer: a child you want tappable needs `pointer-events:auto` in its css. Nothing goes above Wake Up. Never move or restyle `#wake`.
- `ctx.el(tag, cssText, text?, parent?)`: parent is an element or a layer name; default `floaters`.
- The shop is square tiles: `.row` holds the icon and price; its `b`, `small` and `.cost i` stay in the tile, hidden, so unlocks can keep rewriting them. `ui/shop-tip.js` copies them and the tile's classes into the card over the tile (hover, or first tap on a touch screen; second tap or its Buy button buys), and skips anything the unlock hid. Style a row with `.row`: the card picks it up too.
- `ctx.palette()`: the run's colours, `{ a, b, soft, ink, spiral: [c1, c2], themes }`, from the theme picked and any second one. Event `palette` fires when they change (Page Colour's own event is `page-colour`). `ink` reads on `a`, `b` and `soft`. Canvas code reads this; css reads variables, and they follow by themselves:
  - `--niche`, `--niche-2`, `--niche-soft`, `--niche-ink`: the raw palette. Grey before a pick, on purpose.
  - `--accent`, `--accent-2`, `--accent-soft`, `--accent-deep`: for a bar, a burst or a ring. The game's pink until the pick, then the theme.
  - `--niche-text` (or `--stamp`): the accent as text on the page; it follows `--ink`, so it reads on any page. `--mix`: both themes as one colour. `--btn-fill`, `--toast-bg`, `--toast-ink`: what the button and toasts wear.
  - `.row` carries `--hue-a`, `--hue-b`: a theme lane's tile has its own hue in both, every other tile the two themes.
  - From the pick on `--btn`, `--btn-ink`, `--btn-edge`, `--line`, `--fill`, `--stamp` belong to the palette (`!important`): a route tone cannot take them back. The page (`--page`, `--ink`, `--panel`) stays free. A panel that reads these needs nothing else; never hard-code a pink. Jokes that imitate something else keep their own colours.
- `ctx.dock({ edge, title, icon, width, reserveHeight, min, onMin, layout })`: returns the body of a compact object with transparent surroundings. Mouse-drag a non-control surface to move it immediately; on touch, hold for 320 ms first. Buttons, sliders, canvases, `[data-dock-interactive]` regions and scrolling text keep their native gestures. Mark a casing with `[data-dock-surface]` for reliable touch movement. No title bar, minimize button or resize chrome. The bottom tab toggles visibility on every click; hidden features retain their existing clocks and state. Mouse-drag tabs to reorder; touch-hold then drag, or swipe normally to scroll the strip. Alt+Left/Right reorders the focused tab; Ctrl+arrows moves its object. No automatic window limit. Fresh objects use free space or arrive tucked; existing positions remain authoritative. `reserveHeight` reserves room for an expanding toy during initial placement without adding visible padding. Tabs are outside the transformed world and disappear during endings. `extras.windowLayouts` persists positions, hidden state and tab order through reloads/new runs; old fixed window dimensions reset once. Preview preferences remain separate. `body.dock` exposes `setReady(boolean, label)`, `setVisible(boolean)`, `visible`, `moving`, `root`, `chip`, and `cancelMove()`. Readiness is derived from actual game state, never a visual payout or a generic unread flag. Hide does not clear readiness. `onMin(hidden)` handles media visibility; pause, suspend and endings remain authoritative. `door` remains for Guide/Control Panel tabs that open a sheet. Picture screens use this same system; old fractional screen positions migrate on first mount.

- `ctx.css(text)`: a stylesheet. `ctx.vars({'--page':'#ffdcee'})`: page variables, restored on off: `--page --ink --ink-soft --ink-faint --btn --panel --panel-short --line --fill --ready --stamp --font --shop-font`. `--ready` is the flash on a goal row you can afford: set it with your theme, never restyle `#goal`.
- `ctx.world`: the element holding all layers and the page, for a lean or warp. Wake Up and the cards are outside it. Do not filter it every frame on a phone.
- `ctx.parts`: `.shop`, `.buttonWrap`, `.counter`, `.stage`: the elements themselves, to measure or to animate with `ctx.anim` (`.buttonWrap` moves the button without touching its own transform). Use these, never `querySelector` into `ctx.world`.
- `ctx.bounds()`: `{w, h}` of the page, cached; use it instead of measuring each frame.
- `ctx.every(ms, fn)`, `ctx.after(ms, fn)`, `ctx.frame(fn(dt, now))`: each returns a cancel function. Share these; live particles have a cap, so prefer `ctx.float(x, y, text, css?)` for rising numbers.
- `ctx.anim(el, keyframes, opts)`: Web Animations. Keep loops at or under 3 Hz.
- `ctx.speed(multiplier)`: while held, every unlock's `every`, `after`, `frame`, `tick` and `anim` run at that speed; holds multiply, held between 0.1 and 4; returns a release, and it ends with your unlock. Per-second income, button locks and swaps stay on real time, so route timing holds. `now` in frames and modifiers follows it; `ctx.state.clock` does not, so never mix the two.

**Money**

Mini Slots and the Prize Dispenser use `mini_slots` and `dispenser`, the explicit full-prize exceptions in config. Other sources share the usual side budget.
Always show the returned credited amount, and consume a saved claim before paying it.

- `ctx.pay(amount, source, {x, y}?)`: a lump. With a point it flies to the counter. Pace applies, so pass plain numbers like `ctx.rates().fps * 30`. An ad press uses the source `'ad'`, which is what the Broke mood doubles.
- `ctx.spend(amount, reason?)`: takes from the bank, never below zero, answers what it took. No debt, and it does not touch `total`. Pace does not apply. Reasons add up in `ctx.state.spent`.
- `ctx.guardBuy(fn(item) -> {refuse: 'line'} | null)`: refuse or delay a buy; the line shows as a toast. Returns a remover. Use this, never capture listeners on the shop. It cannot make a run impossible: a row goes through after 3 refusals whatever you answer, a fork or ending row never reaches you, and a throw lets the row through.
- `ctx.wallHit({x, y, corner})`: a bouncing thing hit a wall: pays the wall value, emits `wall`, answers the gain.
- `ctx.mod({fps?, click?, wall?, walls?, price?})`: value modifiers `(value, state, now) => value`, `price` also gets `item` last. Pure: no DOM, no rng, no writes. Registered on each mount, folded in order. For a chance-based payoff use `ctx.on('tap', ...)` + `ctx.pay`.
- Away pay (owner 2026-11-08): a hidden tab, a minimised window, a frame off screen or a host `focus` suspend draws no frames, so `ui/away.js` hands the gap to `engine.away` (`E.away`): per-second pay only, at most 8 h an absence, nothing on pause, panic or after the end. No timer, frame, `ctx.now` or `run.clock` moves while away, so nothing timed burns down. An fps mod with `timed: true` never pays away time, and the away rate is the lower of now and `run.clock` 8 h later, so a bonus that ends on `s.clock` does not pay either. A closed tab earns nothing on reopen.

**Events**

- `ending {id}` fires immediately when an ending starts, before its visual fade. Video owners pause on this event and release their media on cleanup.

- `ctx.on(event, fn)`: `tap {gain,x,y,auto}`, `buy {item,count,cost}`, `fork {fork,lane}`, `wall {gain,x,y,corner}`, `pause`, `resume`, `suspend {on, reason}` (the host's panic or focus stop; sound and clocks are already stopped when you hear it), `away {gain, ms}` (per-second pay credited for time the page was open but not drawn), `golden`, `ad`, `pictures {source}`, `ads {jokesOff}`. `ctx.emit(event, data)` sends one: Golden Button emits `golden`, an ad click emits `ad`.

**The main button**: `ctx.button`

- `.el`, `.skin(name)` sets `data-skin` on it, latest wins, gone on off; style it with `ctx.css('#btn[data-skin=round]{...}')`.
- `.press()` a tap that counts, `.melt(ms)` (max 8 s), `.swap(ms)` slides to the other side and back, `.lockFor(ms, label)` (max 30 s). All stay completable. To dodge or shrink, keep it tappable; Intake's `Steer` in `Resources/web/intake/core/contracts.js` is the model.

**Words and cards**

- `ctx.say(text)`: the thought ticker. `ctx.sayRate(r)`: while held, only that share of every lane's thoughts gets through (0.5 = every second one; holds multiply; ends with your unlock). `ctx.toast(text)`: a line under the button; newest replaces.
- `ctx.modal({title, body, buttons:[{label, onPress, keepOpen?}], node?, ms?})`: one at a time, queued; returns `{close()}`. No buttons and no `ms` gets a Close button. Must never cost the run. No text inputs, ever. Left alone for 15 s it takes its last button, so make that the harmless one; `noDefault: true` closes it without choosing.

**Sound and media**

- The speaker beside Wake Up toggles master mute from the start. Its arrow opens the audio mixer with master volume and individual controls for discovered sounds. Each ctx tags audio with its unlock source; related hum/headphone features share a source. `source` and `sourceLabel` can override that grouping. `ctx.audio.mixer` exposes `sources`, `setVolume(id, 0..1)`, `setMuted(id, bool)`, and `subscribe(fn)`. Main saves preferences in `extras.audioMix`, restores them before mounts, and keeps undiscovered sources out of the UI. Source gains sit after each sound's fade envelope and before pan/master, including streamed audio. Master mute and pause always win. Rain uses half its previous default gain with the existing three-second fade. Trusted toy/mixer input arms audio as well as the main button.
- `ctx.audio.muteFor(ms)`: everything silent for up to 15 s, then back.
- `ctx.audio.solo(on)`: while on, only this unlock's own sources are heard (playing ones and ones started later read gain 0, streams mute; kit `solo(keep)`); it lets go with the unlock. An ending show (Go Outside) uses it so nothing else plays underneath.
- `ctx.audio.preload(path)` warms a short local clip after audio is armed. A later `ctx.audio.play` uses the decoded clip.
- `ctx.audio.play(path, {bus, gain, pan})`, `ctx.audio.loop(path, {bus, gain, pan})` -> `{stop(ms)}`. Buses `sfx ambient voice podcast`. `pan` is -1 (left) to 1 (right), fixed at the start, on `stream` too; files on this site only for a stream. A missing file is silence. Clips in `assets/audio/`.
- `ctx.audio.stream(url, {bus, gain, loop, autoplay, pan})` -> `{el, play(), pause(), setGain(v), stop(ms), playing}`: long audio on a media element the kit owns, never decoded whole. Use it for anything long, and never `new Audio()` or an `<audio>` of your own: those keep playing through panic. It follows mute, quiet, muteFor and hold, pauses at once on pause, suspend and panic, comes back with them, and dies at the end and with your unlock. Starts on the first gesture. Read `el` (`currentTime`, `duration`, listeners); volume changes and a play during a hold are put back.
- `ctx.voice(slug, opts?)`: a recorded word (`data/words.js`); no clip = silence. Never speech synthesis.
- `await ctx.pictures.next({niche})` -> `{url, credit}` or null. Null is normal: draw an empty frame, never an error. Show `credit` when set. Never fetch a picture any other way. `ctx.pictures.source` is `online` (signed in, a stub until the website build), `own` (the player's files, object URLs on this device, gone on reload) or `none` (the default); a change arrives as the `pictures` event, so refill what is on screen. Choosing a source is not an unlock's job: the pictures card (`ui/picture-card.js`) asks the first time the route is Slideshow or Swipe Feed, again after a reload lost the files, and from the pause card. A frame holding a gif is fine: own files are images and gifs.
- `await ctx.pictures.next({niche, kind})`: `kind` is `picture`, `gif` or `clip`, and the answer says what it really is. Leave it out for a css background: never a clip, and never a gif under Gentle. Own files are also videos now.
- `screen(ctx, opts)` from `ui/screens.js`: a framed panel that shows pictures, gifs or clips, shows what the row pays, and drags by mouse or finger (`handle: true` when its body has gestures of its own). Its spot is in the save, clamped on resize, never all the way behind the shop, and it sits under the button, the shop and Wake Up. With nothing to show it draws a test card in `--niche` colours with an Add your files button, and still pays (the pay is the row's own `fps`). Pause, suspend and Gentle stop its clip and still its gif. Drive it: `dev/screens-drive.mjs`, real mouse and real touch.
- `ctx.ads.next({kind, theme, ccpOnly})` -> the next record: `{id, kind, headline, sub, cta, ccp, url?, tone?}`. Every ad slot in every lane draws from this and never hand-picks a CCP chip: once a theme is picked (`theme` defaults to the `f5` lane; pass `null` for none) its CCP cards are in the mix. `ccpOnly` asks for real cards only.
- `ctx.ads.jokesOff(on = true)`: Joke Ads Gone. While any unlock holds it, every `next` in every lane is real cards only; it ends with the holder. `ctx.ads.jokesAreOff` reads it; the `ads {jokesOff}` event says it changed, so swap a joke already on screen.
- `ctx.store.get(key)`, `ctx.store.set(key, value)`: small JSON in the save for this unlock (After lane entries: kept across runs).

**Switched off and on**

The Control Panel can unmount any owned row and mount it again at any moment, so `off` plus the scope must leave nothing behind, and a second mount must work with the other rows still up. Keep shared state out of module scope, or make it survive that. What goes through ctx (`vars`, `css`, `button.skin`, `mod`) comes back to its old place in the order, not on top. A row switched off stays unmounted on load, and buying it again switches it on. Its own row pay stays in the sim; anything it adds through `ctx.mod` goes with it.

- `ctx.cleanup(fn)`: runs when the unlock goes, for what ctx cannot see: a registry of its own, a media element it made by hand.
- `ctx.restored`: true on a reload from a save and when the Control Panel switched this row back on. It did not just arrive: no arrival show, no first-time gift. `fresh(ctx)` in `basics/fx.js` already asks.
- `ctx.switches`: `{ isOn(id), set(id, on), waitingOn(id), groups() }`, the Control Panel's door. Event `switch {id, on}`. Nothing else should need it.
- Rows that lean on a parent's live state (a module-level object, hooks, a shared element) go in `FAMILIES` in `sim/switches.js`: a switch in a family remounts the family, and with the parent off its children wait.

## The spiral

Every spiral comes from the Loom, never a fork of it. Import `createSpiral` from `../../fx/spiral.js`:

```js
const c = ctx.el('canvas', 'position:absolute;left:50%;top:50%;width:110vmax;height:110vmax;transform:translate(-50%,-50%);', null, 'back');
const sp = createSpiral(c, { reduced: ctx.gentle, manual: true, ctx });
sp.show();
ctx.frame((dt) => sp.step(dt));
return { off() { sp.dispose(); } };
```

Pass `ctx` and it follows the run by itself: `ctx.palette().spiral` (a slow fade on the `palette` event), Spiral Colours and Spiral Shapes. Do not recolour it. `spiralCanvas` in `ui/unlocks/basics/fx.js` is the same for small or see-through spirals; return its `off`. Everything goes through `fx/spiral-bus.js`; the shapes live in `fx/spiral-patterns.js`.

## Special lanes

- Forks: a fork row (`f1` to `f5`) may be registered by several group files; all of them mount when it is taken, on one shared `ctx`. Every handler checks `ctx.pick('f5')` and returns early if the pick is not its own, and keeps its `ctx.store` keys to itself. No other row may be claimed twice. Already mounted unlocks can also hear the `fork` event.

- `meta`: moods. The sim numbers live in `sim/moods.js`. The entry for `state.mood` mounts at run start; there is none on the first run.
- `after`: every `z_*` entry mounts when the credits card is up and draws into `ctx.layer('credits')`. `ctx.life` holds what it needs; `ctx.setPerk(id|null)` keeps one owned Look for the next run.
- Endings: `drop`, `outside` and the rest all go through one flow (`engine.end`). `onemore` does not end the run; `state.endless` turns on.

## The intro

`ui/intro.js` opens before the engine mounts anything; the words live in `data/content/terms.js` (kept out of `content/index.js` because they hold markup). First visit: the whole contract, hidden things pay a starting lump (`sources.intro`), once a lifetime. Every later load: the short update window with the age box, a new llama line per visit (`life.updates`). A New run reload does not ask again. Decline: no game, no audio, nothing written. `main.js` writes no save until the window is accepted, so nothing an unlock does can run before it. While it is up Wake Up and Escape only say nothing is running; a host's `suspend` puts the real pause card over it, and `close` ends it.

## Rules that bite

Pointer-events inherit. Bare `#page` transforms move the shop; use your own layer. Number formatting goes through `ctx.fmt`. No hover-only information. Cap what you spawn: a phone runs this late with 100 features on.

Metronome: `a_tick` stores integer `bpm` (40-208, default 60). The gold weight is an accessible vertical slider: up slows, down speeds up; arrow keys and Home/End work too. Its pointer capture freezes swing phase and tick audio until release/cancel, pause, suspend or blur. The casing remains a dock move surface. Beats follow `ctx.state.clock`, independent of `ctx.speed`, and rhythm rewards share one window per beat at the selected tempo. `tests/metronome.test.mjs` and the metronome checks in `dev/object-desktop-drive.mjs` cover timing and native mouse/touch interaction.
