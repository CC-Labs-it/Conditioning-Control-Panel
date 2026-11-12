import test from 'node:test';
import assert from 'node:assert/strict';
import { createHoverCard, ownerSources, placeCard, noteSound, soundOwners, LINGER_MS } from '../ui/dock-volume.js';
import { createMixer } from '../audio/mixer.js';

function clock() {
  let t = 0, seq = 0;
  const timers = new Map();
  return {
    setTimer(fn, ms) { const id = ++seq; timers.set(id, { at: t + ms, fn }); return id; },
    clearTimer(id) { timers.delete(id); },
    advance(ms) {
      const end = t + ms;
      for (;;) {
        const next = [...timers].filter(([, v]) => v.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
        if (!next) break;
        timers.delete(next[0]); t = next[1].at; next[1].fn();
      }
      t = end;
    },
  };
}
function card() {
  const c = clock(), log = [];
  const h = createHoverCard({ show: id => log.push('show:' + id), hide: () => log.push('hide'), setTimer: c.setTimer, clearTimer: c.clearTimer });
  return { h, c, log };
}

test('a tab maps to the sources its unlock played, then its own id, only once the mixer knows them', () => {
  const owners = new Map();
  const mixer = createMixer();
  owners.set('a_tick', new Set(['metronome']));
  assert.deepEqual(ownerSources('a_tick', mixer.sources(), owners), []);
  mixer.register('metronome', 'Metronome');
  mixer.register('other', 'Other');
  assert.deepEqual(ownerSources('a_tick', mixer.sources(), owners).map(s => s.id), ['metronome']);
  mixer.register('lofi', 'Lo-fi Radio');
  assert.deepEqual(ownerSources('lofi', mixer.sources(), owners).map(s => s.id), ['lofi']);
  owners.set('many', new Set(['s1', 's2', 's3', 's4']));
  for (const id of ['s1', 's2', 's3', 's4']) mixer.register(id, id);
  assert.equal(ownerSources('many', mixer.sources(), owners).length, 3);
  assert.deepEqual(ownerSources('none', mixer.sources(), owners), []);
});

test('noteSound keeps first-played order and ignores bad ids', () => {
  noteSound('t_owner', 'b'); noteSound('t_owner', 'a'); noteSound('t_owner', 'b'); noteSound('t_owner', ''); noteSound('t_owner', 7);
  assert.deepEqual([...soundOwners.get('t_owner')], ['b', 'a']);
  soundOwners.delete('t_owner');
});

test('hover opens after a short intent, lingers a second after leaving, then hides', () => {
  const { h, c, log } = card();
  h.enterTab('a'); assert.deepEqual(log, []);
  c.advance(200); assert.deepEqual(log, ['show:a']);
  h.leaveTab('a'); c.advance(LINGER_MS - 10); assert.deepEqual(log, ['show:a']);
  c.advance(20); assert.deepEqual(log, ['show:a', 'hide']);
});

test('sweeping past a tab without stopping never opens it', () => {
  const { h, c, log } = card();
  h.enterTab('a'); c.advance(50); h.leaveTab('a'); c.advance(2000);
  assert.deepEqual(log, []);
});

test('moving onto the card keeps it; dragging the slider never fades it', () => {
  const { h, c, log } = card();
  h.enterTab('a'); c.advance(200); h.leaveTab('a'); c.advance(500);
  h.enterCard(); c.advance(5000); assert.deepEqual(log, ['show:a']);
  h.dragStart(); h.leaveCard(); c.advance(5000); assert.deepEqual(log, ['show:a']);
  h.dragEnd(); c.advance(LINGER_MS + 10); assert.deepEqual(log, ['show:a', 'hide']);
});

test('another tab switches at once', () => {
  const { h, c, log } = card();
  h.enterTab('a'); c.advance(200); h.leaveTab('a'); h.enterTab('b');
  assert.deepEqual(log, ['show:a', 'show:b']);
});

test('shrunk pills close at once on another element; normal tabs keep the linger', () => {
  const s = card();
  s.h.enterTab('a'); s.c.advance(200); s.h.leaveTab('a'); s.h.elsewhere(true);
  assert.deepEqual(s.log, ['show:a', 'hide']);
  const n = card();
  n.h.enterTab('a'); n.c.advance(200); n.h.leaveTab('a'); n.h.elsewhere(false);
  assert.deepEqual(n.log, ['show:a']);
  n.c.advance(LINGER_MS + 10); assert.deepEqual(n.log, ['show:a', 'hide']);
});

test('a press on a tab hides it until release, then it comes back on the hovered tab', () => {
  const { h, c, log } = card();
  h.enterTab('a'); c.advance(200); h.press(); assert.deepEqual(log, ['show:a', 'hide']);
  h.enterTab('b'); c.advance(500); assert.deepEqual(log, ['show:a', 'hide']);
  h.release(); assert.deepEqual(log, ['show:a', 'hide', 'show:b']);
  h.release(); assert.equal(log.length, 3);
});

test('the card stays on screen, sits on the tab top and clears the top-right controls', () => {
  const W = 400, H = 300;
  assert.deepEqual(placeCard({ left: 0, width: 20, top: 260 }, { w: 172, h: 60 }, { W, H }), { left: 8, bottom: 40 });
  assert.deepEqual(placeCard({ left: 390, width: 10, top: 260 }, { w: 172, h: 60 }, { W, H }), { left: 220, bottom: 40 });
  const p = placeCard({ left: 300, width: 20, top: 60 }, { w: 172, h: 60 }, { W, H, avoid: { left: 250, right: 392, top: 8, bottom: 44 } });
  assert.ok(H - p.bottom - 60 >= 48);
});
