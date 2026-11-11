// The stage (ui/stage-model.js + ui/stage.js): small look buys get a solo, and only a cast of three plays at full strength.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  CAST, CAST_LITE, FAMILIES, IDLE, NEWEST_MS, ROTATE_MS, SOLO_DIM, SOLO_MS, SOLO_PAYS,
  ambientOwned, approach, castOf, initialStage, soloAsk, soloPaysDue, soloTag, stageIdOf, stageTargets, startSolo,
} from '../ui/stage-model.js';
import { createStage } from '../ui/stage.js';
import { stageNote, stageTabOf } from '../ui/unlocks/wave2/meta.js';
import { BY_ID } from '../data/items.js';

const MIN = 60000;
const ent = (id, order, extra = {}) => ({ id, order, ...extra });
const full = (r) => Object.keys(r.strengths).filter((k) => r.strengths[k] === 1).sort();

test('a small pile plays everything at full strength', () => {
  const es = [ent('spiral', 1), ent('trail', 2), ent('words', 3)];
  const r = stageTargets(es, 10 * MIN);
  assert.deepEqual(full(r), ['spiral', 'trail', 'words']);
});

test('past three, the rest idle at about a quarter and the cast is three', () => {
  const es = [ent('spiral', 1), ent('trail', 2), ent('words', 3), ent('ticker', 4), ent('heat', 5)];
  const r = stageTargets(es, 30 * MIN);
  assert.equal(r.cast.size, CAST);
  for (const e of es) assert.ok([1, IDLE].includes(r.strengths[e.id]));
  assert.equal(full(r).length, 3);
  assert.equal(Object.values(r.strengths).filter((s) => s === IDLE).length, 2);
});

test('phones keep two', () => {
  const es = [ent('spiral', 1), ent('trail', 2), ent('words', 3), ent('ticker', 4)];
  assert.equal(castOf(es, 30 * MIN, { lite: true }).size, CAST_LITE);
});

test('the newest buy keeps a seat for its first three minutes, whatever the rotation says', () => {
  const t0 = 50 * MIN;
  const es = [ent('spiral', 1), ent('trail', 2), ent('words', 3), ent('ticker', 4), ent('heat', 5, { at: t0 })];
  for (let t = t0; t < t0 + NEWEST_MS; t += 7000) assert.ok(castOf(es, t).has('heat'), `seat at ${t - t0}`);
  /* After three minutes it rotates like the others: some window leaves it out. */
  let out = false;
  for (let t = t0 + NEWEST_MS; t < t0 + NEWEST_MS + 6 * ROTATE_MS; t += ROTATE_MS) if (!castOf(es, t).has('heat')) out = true;
  assert.ok(out);
});

test('the rest rotate in one at a time every 40 s', () => {
  const es = ['a', 'b', 'c', 'd', 'e', 'f'].map((id, i) => ent(id, i));
  let prev = castOf(es, 0);
  const seen = new Set(prev);
  for (let k = 1; k <= 8; k++) {
    const now = k * ROTATE_MS;
    const mid = castOf(es, now - ROTATE_MS / 2);
    assert.deepEqual([...mid].sort(), [...prev].sort(), 'steady inside a window');
    const next = castOf(es, now);
    const gone = [...prev].filter((id) => !next.has(id));
    assert.equal(gone.length, 1, `one leaves at step ${k}`);
    for (const id of next) seen.add(id);
    prev = next;
  }
  assert.equal(seen.size, es.length, 'everyone gets a turn');
});

test('pins always play, and Everything at once plays all', () => {
  const es = [ent('spiral', 1), ent('trail', 2), ent('words', 3), ent('ticker', 4), ent('heat', 5)];
  for (let t = 0; t < 10 * ROTATE_MS; t += ROTATE_MS) assert.ok(castOf(es, t, { pins: ['spiral'] }).has('spiral'));
  const r = stageTargets(es, 0, { all: true });
  assert.deepEqual(full(r).length, es.length);
});

test('the solo: the new thing full, the others at about 15%, the page wash never dimmed', () => {
  const es = [ent('spiral', 1), ent('trail', 2), ent('wash', 3, { steady: true }), ent('heat', 4)];
  const solo = startSolo('trail', 'Stardust', 1000);
  const mid = stageTargets(es, 1000 + SOLO_MS / 2, { solo, all: true });
  assert.equal(mid.solo, 'trail');
  assert.equal(mid.strengths.trail, 1);
  assert.equal(mid.strengths.spiral, SOLO_DIM);
  assert.equal(mid.strengths.heat, SOLO_DIM);
  assert.equal(mid.strengths.wash, 1);
  const after = stageTargets(es, 1000 + SOLO_MS + 1, { solo, all: true });
  assert.equal(after.solo, null);
  assert.equal(full(after).length, 4);
  /* The page's own buy gets a solo too: everything else steps back, the page stays. */
  const page = stageTargets(es, 2000, { solo: startSolo('wash', 'Page Colour', 1500) });
  assert.equal(page.strengths.wash, 1);
  assert.equal(page.strengths.spiral, SOLO_DIM);
  assert.equal(soloTag('Stardust'), 'New: Stardust');
});

test('a solo pays three small pays, each once', () => {
  const s = startSolo('trail', 'Stardust', 0);
  let paid = 0;
  for (let t = 0; t <= SOLO_MS + 1000; t += 16) { const n = soloPaysDue(s, t); s.paid += n; paid += n; }
  assert.equal(paid, SOLO_PAYS);
  assert.ok(soloAsk({ fps: 100, click: 5 }) === 150);
  assert.ok(soloAsk({}) >= 1);
});

test('easing moves toward the target without overshoot', () => {
  let v = 1;
  for (let i = 0; i < 200; i++) { const n = approach(v, SOLO_DIM, 16); assert.ok(n >= SOLO_DIM && n <= v); v = n; }
  assert.equal(v, SOLO_DIM);
  for (let i = 0; i < 200; i++) v = approach(v, 1, 16);
  assert.equal(v, 1);
});

test('saves that already own more than three effects start with everything at once', () => {
  assert.equal(initialStage({}).all, false);
  assert.equal(initialStage({ trail: 1, starburst: 1, word_comet: 1, overlay: 1, e_bounce: 1 }).all, false, 'families, not rows');
  assert.equal(ambientOwned({ trail: 1, starburst: 1, overlay: 1, e_bounce: 1, ticker: 1, colour: 1 }), 3, 'the wash and cursor feedback are steady');
  assert.equal(initialStage({ trail: 1, overlay: 1, e_bounce: 1, ticker: 1 }).all, true);
});

test('only decorative layers are on the stage: no toys, games, picture screens or the button', () => {
  for (const [fam, ids] of Object.entries(FAMILIES)) {
    for (const id of ids) {
      assert.ok(BY_ID[id], `${id} is a real row`);
      assert.notEqual(BY_ID[id].lane, 'side', `${id} is a toy`);
      assert.equal(stageIdOf(id), fam);
    }
  }
  for (const id of ['v_gif', 'wheel', 'mini_slots', 'bubble', 'bigbtn', 'confetti']) assert.equal(stageIdOf(id), null, id);
});

function fakeEngine() {
  const paid = [];
  const run = { store: {}, owned: {}, order: [], clock: 0, ended: null };
  const events = [];
  return {
    run, paid, events, paused: false, suspended: false, stopped: false,
    emit: (e) => events.push(e),
    pay: (amount, source, at) => { paid.push({ amount, source, at }); return amount; },
    rates: () => ({ fps: 10, click: 2 }),
  };
}
const scopeOf = (restored = false) => ({ cleanups: new Set(), restored });
const target = () => ({ style: { opacity: '' } });
const strengthOf = (t) => (t.style.opacity === '' ? 1 : Number(t.style.opacity));
function run(engine, stage, ms) { for (let t = 0; t < ms; t += 16) { engine.run.clock += 16; stage.tick(16); } }

test('runtime: a buy solos, eases the others down, pays through the normal path and lets go', () => {
  const engine = fakeEngine();
  const stage = createStage({ engine, lite: false });
  const old = scopeOf(true);
  for (const id of ['overlay', 'e_bounce', 'ticker']) { engine.run.owned[id] = 1; engine.run.order.push(id); }
  const spiral = target(), word = target(), strip = target();
  stage.forScope(old, BY_ID.overlay).register('spiral', spiral);
  stage.forScope(scopeOf(true), BY_ID.e_bounce).register(word);
  stage.forScope(scopeOf(true), BY_ID.ticker).register(strip);
  run(engine, stage, 2000);
  assert.equal(stage.solo, null, 'reloaded rows never solo');
  assert.equal(engine.paid.length, 0);
  for (const t of [spiral, word, strip]) assert.equal(strengthOf(t), 1);

  engine.run.owned.trail = 1; engine.run.order.push('trail');
  let knob = 1;
  const fresh = scopeOf(false);
  stage.forScope(fresh, BY_ID.trail).register('trail', (s) => { knob = s; });
  assert.equal(stage.solo?.id, 'trail');
  run(engine, stage, 2000);
  assert.ok(strengthOf(spiral) <= SOLO_DIM + 0.01, `spiral ${strengthOf(spiral)}`);
  assert.equal(knob, 1);
  run(engine, stage, SOLO_MS);
  assert.equal(stage.solo, null);
  assert.equal(engine.paid.length, SOLO_PAYS);
  assert.ok(engine.paid.every((p) => p.source === 'stage' && p.amount > 0));
  /* Cursor feedback is steady: the three ambient effects can all keep their seats. */
  run(engine, stage, 3000);
  const strengths = [spiral, word, strip].map(strengthOf);
  assert.equal(strengths.filter((s) => s === 1).length, 3);
  assert.equal(strengths.filter((s) => Math.abs(s - IDLE) < 0.01).length, 0);
  assert.equal(knob, 1);
  /* Everything at once from the panel. */
  stage.setAll(true);
  run(engine, stage, 2000);
  for (const t of [spiral, word, strip]) assert.equal(strengthOf(t), 1);
  /* The unlock goes: its target is put back. */
  stage.setAll(false);
  run(engine, stage, 2000);
  for (const fn of old.cleanups) fn();
  assert.equal(spiral.style.opacity, '');
  assert.equal(stage.list().some((r) => r.id === 'spiral'), false);
  /* Pause holds everything; the end puts every layer back at full strength. */
  engine.run.ended = { id: 'drop' };
  stage.tick(16);
  for (const t of [word, strip]) assert.equal(strengthOf(t), 1);
});

test('runtime: pins and the panel list', () => {
  const engine = fakeEngine();
  const stage = createStage({ engine, lite: true });
  const ids = ['overlay', 'e_bounce', 'ticker', 'score_heat'];
  ids.forEach((id, i) => { engine.run.owned[id] = 1; engine.run.order.push(id); stage.forScope(scopeOf(true), BY_ID[id]).register(i === 0 ? 'spiral' : target(), i === 0 ? target() : undefined); });
  /* A save that owns four effects reads as everything at once on its first read. */
  assert.equal(stage.all, true);
  assert.ok(stage.list().every((r) => r.cast));
  stage.setAll(false);
  const rows = stage.list();
  assert.deepEqual(rows.map((r) => r.id), ['spiral', 'words', 'ticker', 'heat']);
  assert.equal(rows.filter((r) => r.cast).length, CAST_LITE);
  stage.pin('ticker', true);
  assert.ok(stage.list().find((r) => r.id === 'ticker').pinned);
  assert.ok(stage.list().find((r) => r.id === 'ticker').cast);
  assert.ok(engine.events.includes('stage'));
});

test('runtime: cursor trails and Word Comet stay readable through later solos and ambient rotation', () => {
  for (const lite of [false, true]) {
    const engine = fakeEngine(), stage = createStage({ engine, lite });
    engine.run.store.stage = initialStage();
    const trail = target(), ambient = [];
    engine.run.owned.word_comet = 1; engine.run.order.push('word_comet');
    stage.forScope(scopeOf(true), BY_ID.word_comet).register('trail', trail);
    for (const id of ['overlay', 'e_bounce', 'ticker', 'score_heat']) {
      engine.run.owned[id] = 1; engine.run.order.push(id);
      const el = target(); ambient.push(el);
      stage.forScope(scopeOf(true), BY_ID[id]).register(el);
    }
    for (let i = 0; i < 6; i++) {
      run(engine, stage, ROTATE_MS + 1500); // Let the new ambient cast finish easing.
      assert.equal(strengthOf(trail), 1, `cursor remains readable, lite=${lite}`);
      assert.equal(ambient.filter(el => strengthOf(el) === 1).length, lite ? CAST_LITE : CAST);
    }
    engine.run.owned.sp_colors = 1; engine.run.order.push('sp_colors');
    stage.forScope(scopeOf(false), BY_ID.sp_colors).register('spiral');
    run(engine, stage, 1500);
    assert.equal(stage.solo?.id, 'spiral');
    assert.equal(strengthOf(trail), 1, 'a later spiral purchase never fades the cursor');
    assert.ok(ambient.slice(1).every(el => strengthOf(el) <= SOLO_DIM + .01), 'other ambient effects still yield to the solo');
    assert.equal(stage.list().some(row => row.id === 'trail'), false, 'cursor feedback does not consume an ambient seat');
  }
});

test('panel copy for the stage rows', () => {
  assert.equal(stageNote({ pinned: true, cast: true }, false), 'Pinned at full strength');
  assert.equal(stageNote({ pinned: false, cast: true }, false), 'On stage now');
  assert.equal(stageNote({ pinned: false, cast: false }, false), 'Resting, takes its turn');
  assert.equal(stageNote({ pinned: false, cast: false }, true), 'At full strength');
  assert.equal(stageTabOf([{ id: 'effects' }, { id: 'looks' }]), 'looks');
  assert.equal(stageTabOf([{ id: 'effects' }]), 'effects');
});
