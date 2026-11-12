import test from 'node:test';
import assert from 'node:assert/strict';
import { TIP_IDS, TIP_TIMING, createTipSchedule, triggered } from '../ui/tips-schedule.js';
import { readTipsSeen, writeTipsSeen, TIPS_KEY } from '../ui/tips.js';
import { TIP_COPY, TIP_REPLAY } from '../data/content/tips.js';

const ALL = Object.fromEntries(TIP_IDS.map((id) => [id, true]));
const base = (o = {}) => ({ blocked: false, hardBlocked: false, fork: false, objects: 0, overlap: false, sounds: 0, clock: 0, fpsvAt: null, guide: false, has: ALL, done: [], ...o });
/** Run the schedule over time with the same facts, answering every action it gives. */
function run(s, from, to, facts, step = 250) {
  const out = [];
  for (let t = from; t <= to; t += step) { const a = s.tick(t, typeof facts === 'function' ? facts(t) : facts); if (a) out.push({ t, ...a }); }
  return out;
}

test('each tip fires on its own event', () => {
  assert.ok(triggered('move', base({ objects: 1 })) && !triggered('move', base()));
  assert.ok(triggered('resize', base({ objects: 2 })) && triggered('resize', base({ objects: 1, overlap: true })) && !triggered('resize', base({ objects: 1 })));
  assert.ok(triggered('sound', base({ sounds: 1 })) && !triggered('sound', base()));
  assert.ok(triggered('choices', base({ fork: true })) && !triggered('choices', base()));
  assert.ok(!triggered('active', base({ fpsvAt: 1000, clock: 1000 + TIP_TIMING.activeDelayMs - 1 })));
  assert.ok(triggered('active', base({ fpsvAt: 1000, clock: 1000 + TIP_TIMING.activeDelayMs })));
  assert.ok(triggered('active', base({ clock: TIP_TIMING.activeAtMs })));
  assert.ok(triggered('map', base({ guide: true })) && !triggered('map', base()));
  assert.ok(!triggered('wake', base({ clock: TIP_TIMING.wakeAtMs, objects: 2 })) && !triggered('wake', base({ clock: 1000, objects: 5 })));
  assert.ok(triggered('wake', base({ clock: TIP_TIMING.wakeAtMs, objects: 3 })));
});

test('a tip shows once, fades on its own and is remembered', () => {
  const s = createTipSchedule();
  const acts = run(s, 0, 20000, base({ objects: 1 }));
  assert.deepEqual(acts.map((a) => a.show || a.hide), ['move', 'move']);
  assert.equal(acts[1].t - acts[0].t, TIP_TIMING.showMs);
  assert.equal(acts[1].reason, 'time');
  assert.deepEqual(s.seen, { move: 1 });
  const again = createTipSchedule({ seen: s.seen });
  assert.deepEqual(run(again, 0, 20000, base({ objects: 1 })), []);
});

test('one at a time, queued, with a gap', () => {
  const s = createTipSchedule();
  const acts = run(s, 0, 40000, base({ objects: 2, sounds: 1 }));
  const shows = acts.filter((a) => a.show);
  assert.deepEqual(shows.map((a) => a.show), ['move', 'resize', 'sound']);
  for (let i = 1; i < acts.length; i++) {
    if (acts[i].show) { assert.ok(acts[i - 1].hide, 'a hide comes before the next show'); assert.ok(acts[i].t - acts[i - 1].t >= TIP_TIMING.gapMs); }
  }
});

test('cards block tips; the choices tip needs the fork card and only that', () => {
  const s = createTipSchedule();
  assert.deepEqual(run(s, 0, 10000, base({ objects: 1, blocked: true, hardBlocked: true })), []);
  assert.deepEqual(s.queue, ['move']);
  /* A fork card is up: the move tip waits, the choices tip comes beside the card. */
  const acts = run(s, 10000, 12000, base({ objects: 1, fork: true, blocked: true }));
  assert.equal(acts[0].show, 'choices');
  assert.deepEqual(s.queue, ['move']);
  /* The terms card or the pause card over a fork: nothing. */
  const t = createTipSchedule();
  assert.deepEqual(run(t, 0, 5000, base({ fork: true, blocked: true, hardBlocked: true })), []);
});

test('a card coming up takes the tip down; cut short it comes back later', () => {
  const s = createTipSchedule();
  run(s, 0, 0, base({ objects: 1 }));
  assert.equal(s.current.id, 'move');
  const [a] = run(s, 1000, 1000, base({ objects: 1, blocked: true }));
  assert.deepEqual([a.hide, a.seen, a.reason], ['move', false, 'blocked']);
  assert.deepEqual(s.queue, ['move']);
  const back = run(s, 1250, 10000, base({ objects: 1 }));
  assert.equal(back[0].show, 'move');
  assert.ok(back[0].t - 1000 >= TIP_TIMING.gapMs);
  /* Long enough on screen: counted. */
  const u = createTipSchedule();
  run(u, 0, 0, base({ objects: 1 }));
  const [b] = run(u, 3000, 3000, base({ objects: 1, blocked: true }));
  assert.equal(b.seen, true);
  assert.deepEqual(u.queue, []);
});

test('doing the thing takes the tip down early, or skips it before it shows', () => {
  const s = createTipSchedule();
  run(s, 0, 0, base({ objects: 1 }));
  const [a] = run(s, 1000, 1000, base({ objects: 1, done: ['move'] }));
  assert.deepEqual([a.hide, a.reason, a.seen], ['move', 'done', true]);
  const t = createTipSchedule();
  run(t, 0, 0, base({ done: ['sound'] }));
  assert.deepEqual(run(t, 250, 10000, base({ sounds: 1 })), []);
  assert.deepEqual(t.seen, { sound: 1 });
  /* Tapping never cancels the active tip: everyone taps. */
  const u = createTipSchedule();
  run(u, 0, 0, base({ clock: TIP_TIMING.activeAtMs }));
  assert.equal(u.current.id, 'active');
  assert.deepEqual(run(u, 500, 1500, base({ clock: TIP_TIMING.activeAtMs, done: ['active'] })), []);
});

test('the choices tip closes with its card', () => {
  const s = createTipSchedule();
  run(s, 0, 0, base({ fork: true, blocked: true }));
  assert.equal(s.current.id, 'choices');
  const [a] = run(s, 3000, 3000, base({ fork: false }));
  assert.deepEqual([a.hide, a.reason, a.seen], ['choices', 'closed', true]);
});

test('a missing target keeps the tip queued and lets the next one go', () => {
  const s = createTipSchedule();
  const has = { ...ALL, move: false };
  const acts = run(s, 0, 1000, base({ objects: 1, sounds: 1, has }));
  assert.equal(acts[0].show, 'sound');
  assert.deepEqual(s.queue, ['move']);
});

test('the Guide replay plays all seven in order and ignores what was seen', () => {
  const seen = Object.fromEntries(TIP_IDS.map((id) => [id, 1]));
  const s = createTipSchedule({ seen });
  assert.deepEqual(run(s, 0, 5000, base({ objects: 3, sounds: 1, guide: true })), []);
  s.replay(5000);
  const has = { ...ALL, choices: false };
  const shows = run(s, 5000, 80000, base({ objects: 3, sounds: 1, guide: true, has })).filter((a) => a.show);
  assert.deepEqual(shows.map((a) => a.show), TIP_IDS);
  assert.equal(shows.find((a) => a.show === 'choices').target, false);   // drawn without an arrow
  assert.deepEqual(run(s, 80000, 90000, base({ objects: 3 })), []);
});

test('seen is a per-device preference that survives bad storage', () => {
  const data = new Map();
  const storage = { getItem: (k) => data.get(k) ?? null, setItem: (k, v) => data.set(k, v) };
  writeTipsSeen(storage, { move: 1, bogus: 1 });
  assert.deepEqual(readTipsSeen(storage), { move: 1 });
  assert.ok(data.has(TIPS_KEY));
  assert.deepEqual(readTipsSeen({ getItem() { throw new Error('denied'); } }), {});
  assert.deepEqual(readTipsSeen({ getItem: () => '{bad' }), {});
  assert.doesNotThrow(() => writeTipsSeen({ setItem() { throw new Error('full'); } }, {}));
});

test('tip copy is plain house style', () => {
  for (const line of [...Object.values(TIP_COPY), ...Object.values(TIP_REPLAY)]) assert.doesNotMatch(line, /[–—!]/);
  for (const id of TIP_IDS) assert.ok(TIP_COPY[id], id);
});

test('a touch that dragged without holding gets the keep-pressed tip first, once per device, never in the replay', async () => {
  const { ALL_TIP_IDS, SITUATIONAL_TIPS } = await import('../ui/tips-schedule.js');
  assert.deepEqual(SITUATIONAL_TIPS, ['keep']);
  assert.equal(TIP_COPY.keep, 'Trying to move this? Keep pressed.');
  assert.ok(!triggered('keep', base({ objects: 3 })) && triggered('keep', base({ holdMiss: true })));
  const has = { ...ALL, keep: true };
  const s = createTipSchedule();
  // The move and resize tips are queued and waiting for their gap when the miss happens: keep goes ahead of them.
  run(s, 0, 7500, base({ objects: 2, has }));
  assert.equal(s.current, null);
  assert.deepEqual(s.queue, ['resize']);
  const next = run(s, 7750, 12000, base({ objects: 2, has, holdMiss: true })).filter((a) => a.show);
  assert.equal(next[0].show, 'keep', 'the answer to what the player just tried comes first');
  run(s, 12000, 40000, base({ objects: 2, has, holdMiss: true }));
  assert.equal(s.seen.keep, 1);
  const data = new Map();
  const storage = { getItem: (k) => data.get(k) ?? null, setItem: (k, v) => data.set(k, v) };
  writeTipsSeen(storage, s.seen);
  assert.equal(readTipsSeen(storage).keep, 1, 'kept per device');
  const later = createTipSchedule({ seen: readTipsSeen(storage) });
  assert.equal(run(later, 0, 20000, base({ has, holdMiss: true })).length, 0, 'never again on this device');
  later.replay(20000);
  const shows = run(later, 20000, 90000, base({ objects: 3, sounds: 1, guide: true, has, holdMiss: true })).filter((a) => a.show).map((a) => a.show);
  assert.ok(!shows.includes('keep'), 'the Guide replay is the seven first-run tips');
  assert.ok(ALL_TIP_IDS.includes('keep'));
});
