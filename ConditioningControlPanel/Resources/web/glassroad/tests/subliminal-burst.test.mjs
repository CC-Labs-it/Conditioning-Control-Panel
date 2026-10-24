import test from 'node:test';
import assert from 'node:assert/strict';
import { createSubliminalBurst, subliminalCards, subliminalParticle, subliminalPrimary, SUBLIMINAL_PHRASES } from '../fx/subliminalBurst.js';
import { createBus } from '../core/events.js';
import { createEffects } from '../fx/effects.js';
import { makeFeel } from './sim-fake.mjs';

function setup(width = 1280, primary = '#c9eceb') {
  const calls = [], children = [];
  const create = () => {
    const ctx = {};
    for (const method of ['clearRect','save','restore','translate','rotate','scale','fillRect','strokeRect','strokeText','beginPath','moveTo','lineTo','closePath','fill','arc','drawImage']) ctx[method] = (...args) => calls.push([method, ...args]);
    ctx.fillText = (...args) => calls.push(['fillText', ...args, ctx.font, ctx.fillStyle]);
    ctx.measureText = text => ({ width: Number.parseFloat(ctx.font.split(' ')[1]) * text.length * .6 });
    ctx.createRadialGradient = () => ({ addColorStop: (...args) => calls.push(['colourStop', ...args]) });
    return { width: 0, height: 0, style: {}, setAttribute() {}, getContext(kind) { assert.equal(kind, '2d'); return ctx; }, remove() { children.splice(children.indexOf(this), 1); } };
  };
  const parent = { clientWidth: width, appendChild(c) { children.push(c); } }, doc = { createElement(kind) { assert.equal(kind, 'canvas'); return create(); } };
  const burst = createSubliminalBurst({ parent, doc, primary }), canvas = children[0];
  calls.length = 0;
  return { burst, calls, children, canvas };
}
const screen = (extra = {}, params = {}) => ({ kind: 'brainDrain', k: 1, strength: 1, params: { seed: 3, gentle: false, ...params }, ...extra });

test('seven large overlapping cards form an asymmetric seeded stack with readable tilt and bounded motion', () => {
  const a = subliminalCards(1, 16 / 9, 3), b = subliminalCards(1, 16 / 9, 3), moved = subliminalCards(2, 16 / 9, 3);
  assert.deepEqual(a, b); assert.equal(a.length, 7);
  assert.equal(new Set(a.map(c => c.text)).size, 7);
  assert.ok(a.every(c => SUBLIMINAL_PHRASES.includes(c.text) && c.w > .5 && c.h > .1 && c.alpha === 1));
  assert.ok(a.some(c => c.tilt > .07) && a.some(c => c.tilt < -.07));
  assert.ok(a.some((c, i) => i && Math.abs(c.x - a[i - 1].x) < (c.w + a[i - 1].w) / 2 && Math.abs(c.y - a[i - 1].y) < (c.h + a[i - 1].h) / 2));
  assert.notDeepEqual(a, moved);
  assert.ok(a.every((c, i) => Math.abs(c.x - moved[i].x) < .03 && Math.abs(c.tilt - moved[i].tilt) < .04));
  const gentleA = subliminalCards(1, 16 / 9, 3, true), gentleB = subliminalCards(2, 16 / 9, 3, true);
  assert.equal(gentleA.length, 4); assert.deepEqual(gentleA, gentleB, 'Gentle holds cards steady');
});

test('one bounded canvas draws card outlines and sparks below HUD, freezes on pause and clears on reset', () => {
  const { burst, calls, children, canvas } = setup(3840), s = screen();
  assert.equal(children.length, 1); assert.equal(canvas.style.zIndex, '2'); assert.equal(canvas.style.pointerEvents, 'none');
  burst.update(s, 1, 9 / 19.5);
  assert.ok(canvas.width <= 1280 && canvas.height <= 1280);
  assert.equal(calls.filter(c => c[0] === 'strokeText').length, 21);
  assert.equal(calls.filter(c => c[0] === 'fill').length, 98);
  assert.equal(calls.filter(c => c[0] === 'drawImage').length, 42);
  assert.equal(calls.filter(c => ['fillRect', 'strokeRect'].includes(c[0])).length, 0, 'no background panels');
  const before = calls.length, state = burst.stats;
  for (let i = 0; i < 100; i++) burst.update(s, 0, 9 / 19.5);
  assert.equal(calls.length, before); assert.deepEqual(burst.stats, state);
  burst.update({ kind: 'none', k: 0 }, 0, 9 / 19.5);
  assert.equal(canvas.style.display, 'none'); assert.equal(burst.stats.clock, 0);
  burst.dispose(); burst.dispose(); assert.equal(children.length, 0);
});

test('primary colours free text, measured fitting makes every phrase larger and short phrases huge', () => {
  const { burst, calls } = setup(1280, '#521744');
  burst.update(screen(), 1, 16 / 9);
  const text = calls.filter(c => c[0] === 'fillText');
  assert.equal(text.length, 7);
  assert.ok(text.every(c => c.at(-1) === '#521744'));
  const sizes = text.map(c => Number.parseFloat(c.at(-2).split(' ')[1]));
  assert.ok(sizes.every(s => s >= 86.4), JSON.stringify(sizes));
  assert.ok(Math.max(...sizes) >= 130);
  assert.equal(subliminalPrimary('bad'), '#c9eceb');
  assert.equal(subliminalPrimary('#abc'), '#aabbcc');
});

test('smoke expands and drifts while smoke and sparks fade to zero on their own finite ages', () => {
  for (const smoke of [false, true]) for (let i = 0; i < 14; i++) {
    const first = subliminalParticle(i, 1, 17, smoke), later = subliminalParticle(i, 2, 17, smoke);
    assert.ok(first.alpha > 0 && first.alpha <= 1);
    assert.ok(later.y < first.y);
    if (smoke) assert.ok(later.size > first.size);
    assert.equal(subliminalParticle(i, 8, 17, smoke).alpha, 0);
    assert.equal(subliminalParticle(i, 0, 17, smoke).alpha, 0);
  }
  const { burst } = setup(); burst.update(screen(), 1);
  assert.ok(burst.stats.smoke > 0 && burst.stats.particles > 0);
  burst.update(screen(), 8);
  assert.equal(burst.stats.smoke, 0); assert.equal(burst.stats.particles, 0);
});

test('live primary callback follows the actual road, freezes on pause and survives replacement', () => {
  let primary = '#521744', count = 0;
  const { burst, calls } = setup(1280, () => { count++; return primary; });
  burst.update(screen(), 1);
  assert.equal(burst.stats.primary, '#521744');
  const before = calls.length, reads = count;
  primary = '#20c8dd'; burst.update(screen(), 0);
  assert.equal(count, reads); assert.equal(calls.length, before); assert.equal(burst.stats.primary, '#521744');
  burst.update(screen(), .1); assert.equal(burst.stats.primary, '#20c8dd');
  primary = '#ffffff'; burst.update({ kind: 'melt', prevKind: 'brainDrain', prevK: .5 }, .1);
  assert.equal(burst.stats.primary, '#20c8dd', 'replacement keeps the outgoing text theme');
  const broken = setup(1280, () => { throw Error('unavailable'); });
  broken.burst.update(screen(), 1); assert.equal(broken.burst.stats.primary, '#c9eceb');
});

test('replacement preserves the Brain Drain seed/Gentle state and fades the existing stack', () => {
  const { burst, canvas } = setup();
  burst.update(screen({}, { gentle: true }), 1);
  assert.equal(canvas.style.opacity, '0.5'); assert.equal(burst.stats.cards, 4);
  burst.update({ kind: 'spiral', k: 1, strength: .35, prevKind: 'brainDrain', prevK: .4, params: { seed: 99, gentle: true } }, .1);
  assert.equal(canvas.style.opacity, '0.2'); assert.equal(burst.stats.seed, 3);
  burst.update(screen({}, { seed: 10 }), .05);
  assert.equal(burst.stats.seed, 10); assert.equal(burst.stats.clock, .05);
});

test('actual local-hit filtering, refused photosafe rise, pause and reset control the burst', () => {
  const { burst, canvas } = setup(), bus = createBus(), feel = makeFeel();
  const effects = createEffects(bus, feel, { photosafe: { request: () => false } });
  const hit = victim => bus.emit('item-hit', { victim, verdict: 'hit', kind: 'brainDrain', durationS: 3, by: 1 });
  hit(1); effects.update(.1); burst.update(effects.screen, .1);
  assert.equal(burst.stats.active, false, 'opponent hit does not obstruct sender');
  hit(0); effects.update(.05); burst.update(effects.screen, .05);
  assert.ok(Number(canvas.style.opacity) > 0 && Number(canvas.style.opacity) < .25, 'photosafe refusal keeps a soft onset');
  bus.emit('pause', { on: true });
  const before = effects.screen.t; effects.update(1); burst.update(effects.screen, 0);
  assert.equal(effects.screen.t, before);
  effects.reset(); burst.update(effects.screen, 0); assert.equal(canvas.style.display, 'none');
  effects.dispose(); burst.dispose();
});
