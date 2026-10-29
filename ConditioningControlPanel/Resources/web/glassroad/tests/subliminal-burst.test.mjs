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

test('words pop in order, fade independently and never share a live lane', () => {
  for (const gentle of [false, true]) {
    const seen = new Set(), firstSeen = [];
    for (let age = .01; age < 2.6; age += .01) {
      const cards = subliminalCards(age, 16 / 9, 3, gentle);
      assert.ok(cards.length <= 4);
      assert.equal(new Set(cards.map(c => c.lane)).size, cards.length);
      for (const card of cards) {
        assert.ok(card.alpha > 0 && card.alpha <= 1 && card.h > .1);
        if (!seen.has(card.index)) { seen.add(card.index); firstSeen.push(card.index); }
      }
      for (const a of cards) for (const b of cards) if (a.index !== b.index) {
        assert.ok(Math.abs(a.y - b.y) > .20, 'separate vertical lanes');
      }
    }
    assert.deepEqual(firstSeen, gentle ? [0,1,2,3] : [0,1,2,3,4,5,6]);
    assert.deepEqual(subliminalCards(2.5, 16 / 9, 3, gentle), []);
  }
  const a = subliminalCards(.75, 16 / 9, 3);
  assert.ok(a.find(c => c.index === 0).alpha < .6, 'first word is already fading');
  assert.equal(a.find(c => c.index === 1).alpha, 1, 'next word holds independently');
  assert.deepEqual(a, subliminalCards(.75, 16 / 9, 3));
  assert.equal(subliminalCards(.5, 16 / 9, 3, false, .4).length, 1, 'short hits keep one soft word');
  assert.ok(subliminalCards(7, 16 / 9, 3, false, 9).length > 0, 'long hits spread their arrivals');
  const steadyA = subliminalCards(.4, 16 / 9, 3, true)[0], steadyB = subliminalCards(.6, 16 / 9, 3, true)[0];
  for (const key of ['x','y','tilt','scale']) assert.equal(steadyA[key], steadyB[key], 'Gentle holds position after its rise');
});

test('one bounded canvas draws card outlines and sparks below HUD, freezes on pause and clears on reset', () => {
  const { burst, calls, children, canvas } = setup(3840), s = screen();
  assert.equal(children.length, 1); assert.equal(canvas.style.zIndex, '2'); assert.equal(canvas.style.pointerEvents, 'none');
  burst.update(s, 1, 9 / 19.5);
  assert.ok(canvas.width <= 1280 && canvas.height <= 1280);
  assert.equal(calls.filter(c => c[0] === 'strokeText').length, burst.stats.cards * 3);
  assert.ok(calls.filter(c => c[0] === 'fill').length > 0);
  assert.ok(calls.filter(c => c[0] === 'drawImage').length > 0);
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
  assert.ok(text.length >= 2 && text.length <= 4);
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
  assert.equal(canvas.style.opacity, '0.5'); assert.ok(burst.stats.cards >= 1 && burst.stats.cards <= 3);
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

test('normal and Gentle words finish within the real hit duration and their smoke restarts per arrival', () => {
  for (const gentle of [false, true]) {
    const { burst } = setup(), seen = new Set();
    for (let i = 0; i < 260; i++) {
      burst.update(screen({ durS: 2.5 }, { gentle }), .01);
      for (const word of burst.stats.visibleTexts) seen.add(word);
    }
    assert.equal(seen.size, gentle ? 4 : 7);
    assert.equal(burst.stats.cards, 0);
    assert.equal(burst.stats.smoke, 0);
    assert.equal(burst.stats.particles, 0);
    const late = subliminalCards(2, 16 / 9, 3, gentle);
    assert.ok(late.some(card => subliminalParticle(0, card.localAge, card.seed, true).alpha > 0));
  }
});
