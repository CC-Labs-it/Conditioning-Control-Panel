/* tests/assist.test.mjs - the player's steer assist (sim/assist.js, music road pass 4) on the real Threshold.
 * A hands-off player (nothing pressed from the grid on) is carried round a whole lap by the autopilot and the
 * soft cruise: on the road, rolling, no fall. A player who steers is never fought: their sign always wins and
 * an agreeing assist only adds. Off is a pass-through, and nothing acts before go.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, RESPAWN, createState } from '../sim/state.js';
import { createRace } from '../sim/race.js';
import { createRacers } from '../sim/racers.js';
import { step as kartStep } from '../sim/kart.js';
import { createAssist, ASSIST_MODES, assistMode } from '../sim/assist.js';
import { createActions } from '../core/actions.js';
import { cleanOptions, cycleOption, OPTION_DEFAULTS } from '../hud/options.js';
import { howtoPanels, howtoHtml } from '../hud/howto.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { threshold } from './sim-racers-harness.mjs';
import { fieldAtS } from '../sim/stones.js';
import { stoneIn, stoneLaneSpan } from '../track/courts.js';

/** The player alone on Threshold, the assist between the hands and the sim (the boot.js order). */
function solo({ mode = 'light', hands = () => ({}), feelOver = {} } = {}) {
  const track = threshold(), feel = makeFeel(feelOver);
  const log = [];
  const queue = { emit(name, payload = {}) { log.push({ name, payload, t: race.timeS,
    ...(name === 'wall' ? { s: racers.player.st[S.s], lateral: racers.player.st[S.lateral] } : {}) }); } };
  const race = createRace(track, feel, queue);
  const racers = createRacers(track, feel, queue, { count: 1, seed: 3 });
  const assist = createAssist(track, feel, { mode });
  const a = Object.assign(createActions(), { item: false, back: false });
  const P = racers.player;
  let t = 0;
  const trace = [];
  race.start();
  function tick() {
    Object.assign(a, { steer: 0, throttle: 0, brake: 0, hop: false, hopPressed: false, item: false, back: false }, hands(t, P.st, race));
    const raw = a.steer;
    const pa = assist.apply(a, P.st, DT, race.phase);
    trace.push({ t, s: P.st[S.s], raw, out: pa.steer, throttle: pa.throttle, hop: pa.hop || pa.hopPressed, item: pa.item, v: P.st[S.speedPct] * feel.get('speed.topMps'), auto: assist.view.auto, cruise: assist.view.cruise });
    race.update(DT, pa, P.st);
    racers.step(DT, pa, race, {});
    t += DT;
  }
  function run(maxS, done = () => false) { const n = Math.round(maxS / DT); for (let i = 0; i < n; i++) { tick(); if (done()) return t; } return t; }
  return { track, feel, race, racers, P, assist, log, trace, run, events: (n) => log.filter((e) => e.name === n), get t() { return t; } };
}

test('options: Steer assist is Off / Light / Strong, Light by default, a bad value reads as Light', () => {
  assert.equal(OPTION_DEFAULTS.steerAssist, 'light');
  assert.deepEqual([...ASSIST_MODES].sort(), ['light', 'off', 'strong']);
  assert.equal(cleanOptions({ steerAssist: 'strong' }).steerAssist, 'strong');
  assert.equal(cleanOptions({ steerAssist: 'max' }).steerAssist, 'light');
  assert.equal(cycleOption('steerAssist', 'light'), 'strong');
  assert.equal(cycleOption('steerAssist', 'strong'), 'off');
  assert.equal(cycleOption('steerAssist', 'off'), 'light');
  assert.equal(assistMode('nope'), 'light');
  assert.equal(OPTION_DEFAULTS.howtoOff, false, 'the how-to card shows until the player says no');
});

test('hands off from the grid: the autopilot and the soft cruise carry the kart a whole lap, on the road', () => {
  for (const mode of ['light', 'strong']) {
    const r = solo({ mode });
    const L = r.track.lengthM;
    r.run(240, () => r.P.st[S.lap] >= 1);
    assert.ok(r.P.st[S.lap] >= 1, mode + ': a lap done hands off (lap ' + r.P.st[S.lap] + ', s ' + r.P.st[S.s].toFixed(0) + ' of ' + L.toFixed(0) + ')');
    assert.equal(r.events('drop').length, 0, mode + ': never off the road');
    const hard = r.events('wall').filter((e) => e.payload.dv > 6);
    assert.equal(hard.length, 0, mode + ': never into a wall hard (' + hard.map((e) => e.payload.dv.toFixed(1) + '@' + e.s.toFixed(1) + '/' + e.lateral.toFixed(1)).join(', ') + ')');
    // it keeps rolling: after the first seconds, never slower than a crawl for long
    const racing = r.trace.filter((x) => x.t > 12);
    const slow = Math.min(...racing.map((x) => x.v));
    assert.ok(slow > 12, mode + ': keeps rolling, slowest ' + slow.toFixed(1) + ' m/s');
    assert.ok(r.trace.every((x) => !x.item && (!x.hop || r.track.fields.some(f => f.platforming && x.s >= f.fromS && x.s < f.toS))), mode + ': automatic hops stay inside authored platform challenges');
    assert.equal(r.events('launch-boost').length, 0, mode + ': never takes the launch boost');
    assert.equal(r.events('drift-start').length, 0, mode + ': never drifts');
    const lapS = r.t;
    const capS = 80 * Math.max(1, L / 1350);           // 80 s on the 1350 m ring; polish 6 courses run longer
    assert.ok(lapS < capS, mode + ': rolls with the song, lap in ' + lapS.toFixed(1) + ' s (cap ' + capS.toFixed(0) + ')');
    // nothing at all before go plus cruiseAfterS
    const cruiseAt = r.trace.find((x) => x.cruise > 0);
    const goT = r.feel.get('juice.countdownPulses') * 60 / r.feel.get('clock.bpm');
    assert.ok(cruiseAt && cruiseAt.t >= goT + r.feel.get('assist.cruiseAfterS') - 0.05, mode + ': cruise waits ' + r.feel.get('assist.cruiseAfterS') + ' s of no input');
  }
});

test('throttle held with player jumps: assist steers after about 2 s and the lap has no fall', () => {
  let r;
  r = solo({ hands: (t, st) => {
    const field = fieldAtS(r.track, st[S.s]);
    const stone = field?.platforming && stoneIn(field, st[S.s], st[S.lateral]);
    const span = stone && stoneLaneSpan(stone, st[S.lateral]);
    const jump = st[S.grounded] > 0 && span && span[1] - st[S.s] >= 0 && span[1] - st[S.s] <= 3 && span[1] < field.toS - 2;
    return { throttle: 1, hop: !!jump, hopPressed: !!jump };
  } });
  r.run(200, () => r.P.st[S.lap] >= 1);
  assert.ok(r.P.st[S.lap] >= 1, 'a lap with the throttle held');
  assert.equal(r.events('drop').length, 0, 'never off the road');
  const autoAt = r.trace.find((x) => x.auto > 0);
  const goT = r.feel.get('juice.countdownPulses') * 60 / r.feel.get('clock.bpm');
  assert.ok(autoAt.t >= goT + r.feel.get('assist.autoSteerAfterS') - 0.05, 'steering waits for 2 s of no steer');
  assert.ok(r.trace.every((x) => x.cruise === 0), 'no cruise while the player holds the throttle');
});

test('a player who steers is never fought: their side always wins and the assist only adds', () => {
  // a weaving, edge-seeking player: long holds toward each edge, short taps, pauses between
  const pattern = (t) => {
    const k = Math.floor(t / 1.7) % 6;
    return [0.9, -0.3, 0, -1, 0.15, 0][k] * (t % 1.7 < 1.2 ? 1 : 0);
  };
  for (const mode of ['light', 'strong']) {
    const r = solo({ mode, hands: (t) => ({ throttle: 1, steer: pattern(t) }) });
    r.run(60);
    let steered = 0;
    for (const x of r.trace) {
      if (Math.abs(x.raw) <= r.feel.get('assist.inputDead')) continue;
      steered++;
      assert.ok(Math.sign(x.out) === Math.sign(x.raw), mode + ': the player\'s side wins at t ' + x.t.toFixed(2));
      assert.ok(Math.abs(x.out) >= Math.abs(x.raw) - 1e-9, mode + ': never less turn than asked at t ' + x.t.toFixed(2));
      assert.ok(x.auto === 0, mode + ': no autopilot while steering');
    }
    assert.ok(steered > 1000, 'the pattern steers');
    const added = r.trace.filter((x) => Math.abs(x.raw) > 0.04 && Math.abs(x.out) > Math.abs(x.raw) + 0.01).length;
    const kept = r.trace.filter((x) => Math.abs(x.raw) > 0.04 && x.out === x.raw).length;
    assert.ok(added > 20 && kept > 20, mode + ': it adds where it agrees (' + added + ') and stands back where it does not (' + kept + ')');
  }
});

test('any input takes over the same step: autopilot and cruise drop to zero at once', () => {
  let grab = Infinity;
  const r = solo({ hands: (t) => (t >= grab ? { steer: -0.2 } : {}) });
  r.run(9);     // on the start straight, before the stream (its current adds steer the step the hands arrive)
  const before = r.trace[r.trace.length - 1];
  assert.ok(before.auto === 1 && before.cruise === 1, 'hands off: both eased in');
  grab = r.t;
  r.run(DT);
  const x = r.trace[r.trace.length - 1];
  assert.equal(x.auto, 0); assert.equal(x.cruise, 0);
  assert.equal(x.out, -0.2, 'the player\'s steer, untouched, the step it arrives');
  assert.equal(x.throttle, 0, 'no throttle the player did not press');
});

test('the centre pull: Strong pulls harder than Light, Off not at all; it grows past a third of the half width', () => {
  // the straightest railed, level 130 m of Threshold (found, so a new course keeps the test; 150 m no longer fits between the last corner and the first stream bend): a kart placed off
  // centre at 20 m/s, nose along the road, throttle held and no steer for 1.5 s (before the autopilot at 2 s)
  const track = threshold(), feel = makeFeel();
  let S0 = 0, bestTurn = Infinity;
  for (let s0 = 0; s0 < track.lengthM; s0 += 10) {
    let turn = 0, ok = true;
    const a0 = track.frameAt(s0);
    for (let d = 10; d <= 130 && ok; d += 10) {
      const fb = track.frameAt(s0 + d);
      ok = (fb.flags || []).includes('rail') && Math.abs(fb.t[1]) < 0.05 && Math.abs(fb.roll) < 0.05;
      turn = Math.max(turn, Math.acos(Math.min(1, a0.t[0] * fb.t[0] + a0.t[1] * fb.t[1] + a0.t[2] * fb.t[2])));
    }
    if (ok && turn < bestTurn) { bestTurn = turn; S0 = s0; }
  }
  const f = track.frameAt(S0, { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }), h = f.w / 2;
  const st = createState(), q = { normal: [0, 0, 0], p: [0, 0, 0] };
  const place = (lat) => {
    st.fill(0);
    st[S.posX] = f.p[0] + f.b[0] * lat + f.n[0] * 0.01; st[S.posY] = f.p[1] + f.b[1] * lat + f.n[1] * 0.01; st[S.posZ] = f.p[2] + f.b[2] * lat + f.n[2] * 0.01;
    const yaw = Math.atan2(-f.t[0], -f.t[2]);     // a yaw turning the kart's -Z nose onto the tangent
    st[S.quatY] = Math.sin(yaw / 2); st[S.quatW] = Math.cos(yaw / 2);
    st[S.velX] = f.t[0] * 20; st[S.velY] = f.t[1] * 20; st[S.velZ] = f.t[2] * 20; st[S.engineV] = 20;
    const qq = track.query(st[S.posX], st[S.posY], st[S.posZ], -1, q);
    st[S.seg] = qq.seg; st[S.latchSeg] = qq.seg; st[S.s] = qq.s; st[S.lateral] = qq.lateral; st[S.grounded] = 1;
    st[S.hopT] = 1e3; st[S.respawnPhase] = RESPAWN.NONE;
  };
  const NONE = { steer: 0, throttle: 1, brake: 0, hop: false, hopPressed: false, item: false, back: false };
  const sink = { emit() {} };
  const ends = {};
  for (const mode of ['off', 'light', 'strong']) {
    const as = createAssist(track, feel, { mode });
    place(h * 0.7);
    const from = st[S.lateral];
    for (let i = 0; i < 90; i++) kartStep(st, as.apply(NONE, st, DT, 'racing'), track, feel, DT, sink);
    ends[mode] = { from: +from.toFixed(2), to: +st[S.lateral].toFixed(2) };
  }
  const pulled = (m) => ends[m].from - ends[m].to;
  assert.ok(pulled('light') > pulled('off') + 0.5, 'Light pulls back ' + JSON.stringify(ends));
  assert.ok(pulled('strong') > pulled('light') + 0.3, 'Strong more ' + JSON.stringify(ends));
  assert.ok(ends.strong.to > -h * 0.33, 'Strong never throws it past the centre ' + JSON.stringify(ends));
  // the shape: nothing inside a third of the half width, a nudge toward the centre beyond it
  const as = createAssist(track, feel, { mode: 'light' });
  const probe = (lat) => { place(lat); as.reset(); return as.apply(NONE, st, DT, 'racing').steer; };
  const mid = probe(0), inner = probe(h * 0.25), outerR = probe(h * 0.9), outerL = probe(-h * 0.9);
  assert.ok(Math.abs(inner - mid) < 0.02, 'inside a third: no pull (' + inner.toFixed(3) + ' vs ' + mid.toFixed(3) + ')');
  assert.ok(outerR < mid - 0.1 && outerL > mid + 0.1, 'toward the centre from either edge: ' + outerL.toFixed(2) + ' / ' + outerR.toFixed(2));
});

test('Off is a pass-through, and nothing acts before go', () => {
  const r = solo({ mode: 'off' });
  r.run(20);
  assert.ok(r.trace.every((x) => x.out === x.raw && x.throttle === 0 && x.auto === 0 && x.cruise === 0), 'off: the hands as they are');
  assert.ok(r.P.st[S.speedPct] < 0.01, 'off, hands off: the kart waits on the grid');
  const l = solo({ mode: 'light' });
  l.run(l.feel.get('juice.countdownPulses') * 60 / l.feel.get('clock.bpm') - DT);
  assert.equal(l.race.phase, 'countdown');
  assert.ok(l.trace.every((x) => x.throttle === 0 && x.out === 0), 'the countdown is the player\'s alone');
});

test('how-to card: three picture panels named for the device in use, plain copy, a Go and a don\'t-show-again', () => {
  const kb = howtoPanels({ last: 'keyboard' }), xb = howtoPanels({ last: 'pad', pad: { family: 'xbox', profile: 'standard' } });
  const ps = howtoPanels({ last: 'pad', pad: { family: 'ps', profile: 'standard' } }), tc = howtoPanels({ last: 'touch' });
  for (const set of [kb, xb, ps, tc]) {
    assert.deepEqual(set.map((p) => p.n), [1, 2, 3]);
    for (const p of set) {
      assert.ok(p.art.startsWith('<svg') && p.art.length > 400, 'a drawing in every panel');
      assert.ok(p.cap.split(' ').length <= 7, 'near-zero text: ' + p.cap);
      assert.ok(!/[!–—]/.test(p.cap), 'no exclamation marks or dashes: ' + p.cap);
      assert.ok(!/<image|<img|url\(/.test(p.art), 'inline drawings, no image files');
    }
  }
  assert.equal(kb[0].cap, 'Hold W on GO');
  assert.ok(kb[1].cap.includes('Space') && kb[2].cap.includes('E'));
  assert.equal(xb[0].cap, 'Hold RT on GO'); assert.ok(xb[1].cap.includes('A') && xb[2].cap.includes('X'));
  assert.equal(ps[0].cap, 'Hold R2 on GO'); assert.ok(ps[1].cap.includes('Cross') && ps[2].cap.includes('Square'));
  assert.equal(tc[0].cap, 'Touch on GO'); assert.ok(tc[2].cap.includes('tap'));
  assert.ok(kb[0].art.includes('>W<') && xb[0].art.includes('>RT<'), 'the drawing shows the real key');
  const html = howtoHtml({ last: 'keyboard' }, { off: true });
  assert.ok(html.includes('data-act="howto-go"') && html.includes('Press W to start'));
  assert.ok(/data-act="howto-off"[^>]*aria-pressed="true"/.test(html) || /aria-pressed="true"[^>]*>Don/.test(html), 'the switch shows its state');
  assert.ok(howtoHtml({ last: 'touch' }).includes('>Start<'), 'touch: a Start button (the throttle is automatic)');
  assert.ok(!/[–—]/.test(html) && !/[!][^=]/.test(html.replace(/<!--[^]*?-->/g, '')), 'plain copy');
});

test('how-to card: Throttle dismisses it, the Race door opens it once a session, and boot passes the throttle edge', async () => {
  const { readFileSync } = await import('node:fs');
  const cards = readFileSync(new URL('../hud/cards.js', import.meta.url), 'utf8');
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.ok(/dir === 'throttle'/.test(cards) && /howtoGo\(\)/.test(cards), 'nav(throttle) leaves the card');
  assert.ok(/a === 'race' && kind === 'menu' && howtoDue\(\)/.test(cards), 'the Race door opens it before the first race');
  assert.ok(/howtoSeen = true/.test(cards) && /opts\.howtoOff/.test(cards), 'once a session, never with the switch on');
  assert.ok(/hud\.nav\('throttle'\)/.test(boot) && /!cardThrottle/.test(boot), 'boot: a throttle press, an edge, reaches the card');
  assert.ok(/assist\.apply\(a, P\.st, clock\.dt, race\.phase\)/.test(boot), 'boot: the assist sits between the input and the sim');
  assert.ok(/racers\.step\(clock\.dt, pa,/.test(boot) && /race\.update\(clock\.dt, pa,/.test(boot), 'boot: the sim reads the assisted hands');
});


test('holding throttle alone never triggers assisted platform jumps', () => {
  const r = solo({ hands: () => ({ throttle: 1 }) });
  r.run(90, () => r.events('void').length > 0);
  assert.ok(r.events('void').length > 0, 'the real jump gap cannot be coasted through');
  assert.ok(r.trace.every(x => !x.hop), 'active driving still requires the player to jump');
});
