import test from 'node:test';
import assert from 'node:assert/strict';
import { playBubbleFinale } from '../ui/unlocks/themes-a/bubble-finale.js';

function cinema() {
  let now = 0, frame;
  const nodes = [], events = new Map(), cleanup = [], css = [];
  const doc = Object.assign(new EventTarget(), {
    hidden: false, defaultView: { performance: { now: () => now } },
  });
  const ctx = {
    audio: {}, paused: false,
    css: rules => css.push(rules),
    el(tag, _, text) {
      const n = { tag, text, style: { setProperty(name, value) { this[name] = value; } }, dataset: {} };
      nodes.push(n);
      return n;
    },
    frame(fn) { frame = fn; return () => { frame = null; }; },
    on(type, fn) { events.set(type, fn); },
    cleanup: fn => cleanup.push(fn),
  };
  let black = 0;
  const done = playBubbleFinale(ctx, {ownerDocument:doc}, {onBlack:() => black++});
  return {
    ctx, doc, done, veil:nodes[0], title:nodes[1], black:() => black, css,
    advance(ms) { now += ms; frame?.(); },
    emit(type, detail) { events.get(type)?.(detail); },
    dispose() { cleanup.forEach(fn=>fn()); },
  };
}

test('roam for 30 s, close both edges before the trigger, then show the card', async () => {
  const c = cinema();
  c.advance(29999);
  assert.equal(c.veil.style['--bi-close'], undefined);
  c.advance(2001);
  assert.ok(parseFloat(c.veil.style['--bi-close']) < 0);
  assert.equal(c.black(),0);
  c.advance(2201);
  assert.equal(c.black(),1);
  assert.equal(c.veil.dataset.black,'');
  assert.equal(c.title.hidden,true);
  c.advance(321);
  assert.equal(c.title.text,'BAMBI SLEEP');
  assert.equal(c.title.hidden,false);
  c.advance(299);
  assert.equal(c.title.hidden,false);
  c.advance(1);
  assert.equal(c.title.hidden,true);
  c.advance(721);
  await c.done;
  assert.equal(c.black(),1);
});

test('time spent paused or in a hidden tab does not skip the roam', () => {
  const c = cinema();
  c.advance(9000);
  c.ctx.paused = true; c.emit('pause');
  c.advance(70000);
  c.ctx.paused = false; c.emit('resume');
  c.doc.hidden = true; c.doc.dispatchEvent(new Event('visibilitychange'));
  c.advance(70000);
  c.doc.hidden = false; c.doc.dispatchEvent(new Event('visibilitychange'));
  c.advance(20999);
  assert.equal(c.veil.style['--bi-close'],undefined);
  c.advance(10);
  assert.notEqual(c.veil.style['--bi-close'],undefined);
  c.dispose();
});

test('panic and cleanup resolve immediately without displaying the trigger', async () => {
  for (const halt of [c => c.emit('suspend', {on:true,reason:'panic'}), c => c.dispose()]) {
    const c = cinema();
    c.advance(2000);
    halt(c);
    await c.done;
    c.advance(50000);
    assert.equal(c.title.hidden,true);
    assert.equal(c.black(),0);
  }
});
