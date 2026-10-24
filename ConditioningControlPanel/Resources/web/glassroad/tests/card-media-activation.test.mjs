import test from 'node:test';
import assert from 'node:assert/strict';
import { bindCardMediaActivation } from '../host/cardMediaActivation.js';

const settle = () => new Promise(resolve => setImmediate(resolve));
function harness(start) {
  const listeners = new Map();
  const node = () => ({ hidden: false, rendered: true, style: { display: 'block', visibility: 'visible' },
    getClientRects() { return this.rendered ? [{}] : []; } });
  const button = { ...node(), disabled: false, disabledByParent: false, act: 'race', ariaDisabled: null,
    matches(selector) { assert.equal(selector, ':disabled'); return this.disabledByParent; },
    getAttribute(name) { assert.equal(name, 'aria-disabled'); return this.ariaDisabled; },
  };
  const card = { ...node(), dataset: { kind: 'menu' }, contains: el => el === button };
  const garage = { ...node(), hidden: true, contains: el => el === button };
  const document = {
    getElementById: id => id === 'card' ? card : id === 'race-garage' ? garage : null,
    defaultView: { getComputedStyle: el => el.style },
    addEventListener(type, fn, capture) { assert.equal(type, 'click'); assert.equal(capture, true); listeners.set(type, fn); },
    removeEventListener(type, fn, capture) { assert.equal(capture, true); if (listeners.get(type) === fn) listeners.delete(type); },
  };
  const target = { closest(selector) { assert.equal(selector, 'button[data-act="race"],button[data-g="race"]'); return button.act === 'race' ? button : null; } };
  const dispose = bindCardMediaActivation(start, { document });
  return { card, garage, button, listeners, dispose, click: (extra = {}) => listeners.get('click')?.({ isTrusted: true, detail: 1, target, ...extra }) };
}

test('a real Race click from nested label content activates synchronously and only once', async () => {
  let calls = 0;
  const h = harness(() => { calls++; });
  h.click(); assert.equal(calls, 1);
  h.click(); await settle(); h.click();
  assert.equal(calls, 1);
  h.dispose();
});

test('native keyboard Race clicks with detail zero activate, synthetic clicks do not', async () => {
  let calls = 0;
  const h = harness(() => { calls++; });
  h.click({ isTrusted: false, detail: 0 });
  h.click({ isTrusted: false, detail: 1 });
  assert.equal(calls, 0);
  h.click({ detail: 0 }); await settle();
  assert.equal(calls, 1); h.dispose();
});

test('only an enabled Race button contained in a visible menu can activate', () => {
  const cases = [
    h => { h.card.hidden = true; }, h => { h.card.rendered = false; },
    h => { h.card.style.visibility = 'hidden'; }, h => { h.card.style.display = 'none'; },
    h => { h.card.dataset.kind = 'results'; }, h => { h.card.dataset.kind = 'start'; },
    h => { h.card.dataset.kind = 'howto'; }, h => { h.card.contains = () => false; },
    h => { h.button.hidden = true; }, h => { h.button.rendered = false; },
    h => { h.button.style.visibility = 'collapse'; }, h => { h.button.disabled = true; },
    h => { h.button.disabledByParent = true; }, h => { h.button.ariaDisabled = 'true'; },
    h => { h.button.act = 'online'; }, h => { h.button.act = 'again'; }, h => { h.button.act = 'restart'; },
  ];
  for (const change of cases) {
    let calls = 0; const h = harness(() => { calls++; });
    change(h); h.click(); assert.equal(calls, 0, String(change)); h.dispose();
  }
  let calls = 0; const h = harness(() => { calls++; });
  h.click({ defaultPrevented: true }); h.click({ target: {} });
  assert.equal(calls, 0); h.dispose();
});

test('pending activation is shared; failed requests retry only on a later trusted Race click', async () => {
  let calls = 0, reject;
  const h = harness(() => { calls++; return calls === 1 ? new Promise((_, r) => { reject = r; }) : Promise.resolve(); });
  h.click(); h.click(); assert.equal(calls, 1);
  reject(new Error('fixture request failed')); await settle();
  assert.equal(calls, 1, 'no background retry');
  h.click({ isTrusted: false }); assert.equal(calls, 1);
  h.click(); await settle(); h.click();
  assert.equal(calls, 2); h.dispose();
});

test('synchronous failures and explicit false results release activation for a future Race click', async () => {
  let calls = 0;
  const h = harness(() => { calls++; if (calls === 1) throw Error('fixture failure'); return calls === 2 ? false : undefined; });
  h.click(); h.click(); await settle(); h.click(); await settle(); h.click();
  assert.equal(calls, 3); h.dispose();
});

test('binding and disposal never activate media and disposal removes the capture listener', async () => {
  let calls = 0; const h = harness(() => { calls++; });
  assert.equal(calls, 0); assert.equal(h.listeners.size, 1);
  h.dispose(); h.dispose(); h.click(); await settle();
  assert.equal(calls, 0); assert.equal(h.listeners.size, 0);
  assert.doesNotThrow(() => bindCardMediaActivation(() => {}, { document: null })());
});

test('trusted garage Race activates while the fallback card is hidden, and hidden garage cannot', async()=>{
  let calls=0;const h=harness(()=>{calls++;});
  h.card.hidden=true;h.garage.hidden=false;h.click({isTrusted:false});assert.equal(calls,0);
  h.click();await settle();assert.equal(calls,1);h.dispose();
  let other=0;const closed=harness(()=>{other++;});closed.card.hidden=true;closed.click();
  assert.equal(other,0);closed.dispose();
});