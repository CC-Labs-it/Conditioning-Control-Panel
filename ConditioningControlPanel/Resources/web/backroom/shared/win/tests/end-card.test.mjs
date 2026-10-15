// THE PLAN ROW + THE BANK's end card (conversion lane G1, brief C10).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PLAN_FALLBACK, FUNNEL_URL, safePlanUrl, planOf, funnelLine, postFunnel, viewerTier, buildEndCard } from '../end-card.js';
import { bankCardWanted, BIG_WIN_ROLLUP_MS } from '../bank-card.js';
import { createBankRun, setBankCardHook } from '../bank.js';

const C1 = {
  currency: 'USD',
  basic: { name: 'Basic', tier: 1, monthly: '7.50', patreon_url: 'https://www.patreon.com/checkout/CodeBambi?rid=27604786' },
  prime: { name: 'Prime', tier: 2, monthly: '12.50', patreon_url: 'https://www.patreon.com/checkout/CodeBambi?rid=27681505' },
};

test('no plans payload: the compiled checkout link, and no price', () => {
  for (const k of ['basic', 'prime']) {
    const p = planOf(undefined, k);
    assert.equal(p.url, PLAN_FALLBACK[k].url);
    assert.equal(p.price, '');
    assert.match(p.url, /^https:\/\/www\.patreon\.com\/checkout\/CodeBambi\?rid=\d+$/);
  }
  assert.equal(planOf(null, 'basic').name, 'Basic');
  assert.equal(planOf({}, 'anything').name, 'Prime');
});

test('a plans payload adds the price and keeps its own link', () => {
  assert.deepEqual(planOf(C1, 'prime'), { key: 'prime', name: 'Prime', url: C1.prime.patreon_url, price: 'USD 12.50 a month' });
  assert.equal(planOf(C1, 'basic').price, 'USD 7.50 a month');
});

test('a hostile payload never becomes a redirect or markup', () => {
  const bad = { currency: '<b>', prime: { name: '<img src=x>', monthly: '12.50; drop', patreon_url: 'javascript:alert(1)' } };
  const p = planOf(bad, 'prime');
  assert.equal(p.url, PLAN_FALLBACK.prime.url);
  assert.equal(p.name, 'Prime');
  assert.equal(p.price, '');
  assert.equal(safePlanUrl('http://www.patreon.com/x'), '');
  assert.equal(safePlanUrl('https://evil.example/x'), '');
  assert.equal(safePlanUrl('https://subscribestar.adult/cc-labs'), 'https://subscribestar.adult/cc-labs');
});

test('funnel lines fit the server allowlist shape', () => {
  assert.equal(funnelLine('cta_clicked', 'breakout_demo', 'patreon'), 'cta_clicked|page|breakout_demo|patreon');
  assert.equal(funnelLine('gate_shown', 'Back Room!', 'BASIC'), 'gate_shown|page|backroom|basic');
  assert.equal(funnelLine('upgrade', 'x', 'y'), '');
  assert.equal(funnelLine('cta_clicked', 'a'.repeat(50)).split('|')[2].length, 32);
});

test('funnel: host message when hosted, beacon on https, nothing otherwise', () => {
  const posted = [];
  assert.equal(postFunnel('cta_clicked', 'goon_host', 'patreon', { chrome: { webview: { postMessage: m => posted.push(m) } } }), 'host');
  assert.deepEqual(posted, [{ type: 'funnel', event: 'cta_clicked', a: 'goon_host', b: 'patreon' }]);
  const beacons = [];
  const nav = { sendBeacon: (u, b) => { beacons.push([u, b]); return true; } };
  assert.equal(postFunnel('gate_shown', 'x', 'prime', { navigator: nav, location: { protocol: 'https:' } }), 'beacon');
  assert.deepEqual(beacons, [[FUNNEL_URL, 'gate_shown|page|x|prime']]);
  assert.equal(postFunnel('gate_shown', 'x', 'prime', { navigator: nav, location: { protocol: 'file:' } }), 'none');
  assert.equal(postFunnel('gate_shown', 'x', 'prime', {}), 'none');
  assert.equal(postFunnel('gate_shown', 'x', 'prime', { chrome: { webview: { postMessage() { throw new Error('gone'); } } } }), 'none');
});

test('viewer tier is read only when the host said it', () => {
  assert.equal(viewerTier({ tier: 0 }), 0);
  assert.equal(viewerTier({ tier: 2 }), 2);
  assert.equal(viewerTier({}), null);
  assert.equal(viewerTier({ tier: '0' }), null);
  assert.equal(viewerTier(null), null);
});

test('the bank card: a big win to a free viewer, once; never a bust, a small win or an unknown tier', () => {
  const big = { fromValue: 100, toValue: 900, rollupMs: BIG_WIN_ROLLUP_MS };
  assert.equal(bankCardWanted(big, { tier: 0 }), true);
  assert.equal(bankCardWanted(big, { tier: 0 }, true), false, 'once per visit');
  assert.equal(bankCardWanted({ ...big, toValue: 100 }, { tier: 0 }), false, 'a bust pays nothing');
  assert.equal(bankCardWanted({ ...big, toValue: 50 }, { tier: 0 }), false);
  assert.equal(bankCardWanted({ ...big, rollupMs: 1200 }, { tier: 0 }), false, 'a small win stays clean');
  assert.equal(bankCardWanted(big, {}), false, 'no tier from the host, no offer');
  assert.equal(bankCardWanted(big, undefined), false);
  assert.equal(bankCardWanted(big, { tier: 1 }), false, 'Basic already has it');
  assert.equal(bankCardWanted(big, { tier: 0, breakout: { demo: false } }), false, 'full Breakout = Prime');
});

test('THE BANK tells its card only when a PAY settles on its own', () => {
  const calls = [];
  setBankCardHook({ start: () => calls.push(['start']), paid: (i) => calls.push(['paid', i]) });
  try {
    const run = createBankRun({ kind: 'pay', n: 3, fromValue: 10, toValue: 500, rollupMs: 2000, startMs: 0 });
    assert.deepEqual(calls, [['start']]);
    run.step(100); run.step(1000);
    assert.equal(calls.length, 1, 'nothing while the tokens fly or the count rolls');
    run.step(5000);
    assert.deepEqual(calls[1], ['paid', { fromValue: 10, toValue: 500, rollupMs: 2000 }]);
    run.step(6000);
    assert.equal(calls.length, 2, 'once');

    calls.length = 0;
    const cut = createBankRun({ kind: 'pay', n: 3, fromValue: 10, toValue: 500, rollupMs: 2000 });
    cut.skip({ land: true });
    assert.deepEqual(calls, [['start']], 'a skip (Back, lever, close) never raises the card');

    calls.length = 0;
    createBankRun({ kind: 'pay', n: 3, fromValue: 10, toValue: 500, rollupMs: 2000, reduced: true }).step(0);
    assert.equal(calls[1][0], 'paid', 'reduced motion settles on its own too');

    calls.length = 0;
    createBankRun({ kind: 'spend', n: 3, fromValue: 500, toValue: 10, startMs: 0 }).step(99999);
    assert.deepEqual(calls, [['start']], 'a spend is never a win');
  } finally { setBankCardHook(null); }
});

test('a card hook that throws never breaks the bank', () => {
  setBankCardHook({ start() { throw new Error('x'); }, paid() { throw new Error('y'); } });
  try {
    const r = createBankRun({ kind: 'pay', n: 1, fromValue: 0, toValue: 5, reduced: true });
    assert.equal(r.step(0).shown, 5);
  } finally { setBankCardHook(null); }
});

/** The smallest DOM the row needs. */
function fakeDoc() {
  const make = (tag) => {
    const n = { tag, children: [], attrs: {}, listeners: {}, className: '', textContent: '',
      setAttribute(k, v) { this.attrs[k] = v; }, append(...c) { this.children.push(...c); },
      addEventListener(t, f) { (this.listeners[t] ||= []).push(f); } };
    return n;
  };
  return { createElement: make };
}

test('the row: a link to the plan, the price only with plans, a click counts', () => {
  const posted = [];
  const env = { chrome: { webview: { postMessage: m => posted.push(m) } } };
  const row = buildEndCard({ doc: fakeDoc(), line: 'Host the next one.', plan: planOf(C1, 'basic'), feature: 'backroom_bank', env });
  const [line, a, price] = row.children;
  assert.equal(line.textContent, 'Host the next one.');
  assert.equal(a.tag, 'a');
  assert.equal(a.href, C1.basic.patreon_url);
  assert.equal(a.target, '_blank');
  assert.match(a.rel, /noopener/);
  assert.equal(a.textContent, 'Get Basic');
  assert.equal(price.textContent, 'USD 7.50 a month');
  let held = 0;
  a.listeners.click[0]({ preventDefault: () => { held++; } });
  // In the app the host is told which plan to open and the link does not open itself (it raised a bare popup).
  assert.deepEqual(posted, [{ type: 'funnel', event: 'cta_clicked', a: 'backroom_bank', b: 'patreon', plan: 'basic' }]);
  assert.equal(held, 1);

  const bare = buildEndCard({ doc: fakeDoc(), plan: planOf(undefined, 'prime'), feature: 'breakout_demo', env: {} });
  assert.equal(bare.children.length, 1, 'no line, no price: just the button');
  assert.equal(bare.children[0].textContent, 'Get Prime');
});

test('copy voice: no exclamation marks, no em or en dashes', () => {
  for (const s of ['Bank it with a Lockdown stake.', planOf(C1, 'prime').price, 'Get Prime']) {
    assert.doesNotMatch(s, /[!\u2013\u2014]/);
  }
});
