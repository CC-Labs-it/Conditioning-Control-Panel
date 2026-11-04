import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  PLAN_FALLBACK, FUNNEL_URL, DAILY_GIFT_LINE, PLAN_FEATURE, PLAN_ROW_DELAY_MS,
  planOf, planRowFor, funnelLine, postFunnel, buildPlanRow,
} from '../game/planCard.js';

/* The run summary's plan row (conversion lane G1, brief C10). DtRH is a trance game: the row is calm, comes only
 * after the payout, only for a Daily Gift run, and a host that never sends the field never shows it. */

const here = path.dirname(fileURLToPath(import.meta.url));
const read = (rel) => fs.readFileSync(path.join(here, '..', rel), 'utf8').replace(/\r\n/g, '\n');
const C1 = { currency: 'USD', prime: { name: 'Prime', monthly: '12.50', patreon_url: 'https://www.patreon.com/checkout/CodeBambi?rid=27681505' } };

test('only a Daily Gift payout carries the row', () => {
  assert.deepEqual(planRowFor({ daily_gift: true, plan: 'prime', finalXp: 10 }), { key: 'prime', line: DAILY_GIFT_LINE });
  assert.equal(planRowFor({ finalXp: 10 }), null, 'an older host, or a paid player: nothing');
  assert.equal(planRowFor({ daily_gift: 'yes' }), null, 'only a real true');
  assert.equal(planRowFor({ daily_gift: true, dryRun: true }), null, 'a dry run is not a run');
  assert.equal(planRowFor(null), null);
  assert.equal(planRowFor({ daily_gift: true }).key, 'prime', 'Prime when the host names no plan');
});

test('no plans payload: the Prime checkout, no price', () => {
  const p = planOf(undefined, 'prime');
  assert.equal(p.url, PLAN_FALLBACK.prime.url);
  assert.equal(p.price, '');
  assert.equal(planOf(C1, 'prime').price, 'USD 12.50 a month');
  assert.equal(planOf({ prime: { patreon_url: 'https://evil.example/' } }, 'prime').url, PLAN_FALLBACK.prime.url);
});

test('the funnel: host frame, https beacon, or nothing', () => {
  const sent = [];
  assert.equal(postFunnel('cta_clicked', PLAN_FEATURE, 'patreon', { chrome: { webview: { postMessage: (m) => sent.push(m) } } }), 'host');
  assert.deepEqual(sent, [{ type: 'funnel', event: 'cta_clicked', a: 'dtrh', b: 'patreon' }]);
  const beacons = [];
  assert.equal(postFunnel('gate_shown', PLAN_FEATURE, 'prime', { navigator: { sendBeacon: (u, b) => beacons.push([u, b]) }, location: { protocol: 'https:' } }), 'beacon');
  assert.deepEqual(beacons, [[FUNNEL_URL, 'gate_shown|page|dtrh|prime']]);
  assert.equal(postFunnel('gate_shown', 'x', 'y', {}), 'none');
  assert.equal(funnelLine('bark', 'x'), '');
});

function fakeDoc() {
  const make = (tag) => ({ tag, children: [], attrs: {}, listeners: {}, className: '', textContent: '',
    setAttribute(k, v) { this.attrs[k] = v; }, append(...c) { this.children.push(...c); },
    addEventListener(t, f) { (this.listeners[t] ||= []).push(f); } });
  return { createElement: make };
}

test('the row: one calm line, one link, the price only with plans', () => {
  const sent = [];
  const env = { chrome: { webview: { postMessage: (m) => sent.push(m) } } };
  const box = buildPlanRow({ doc: fakeDoc(), row: planRowFor({ daily_gift: true }), plans: C1, env });
  const [line, a, price] = box.children;
  assert.equal(line.textContent, 'Open today. Prime keeps it open.');
  assert.equal(a.tag, 'a');
  assert.equal(a.href, C1.prime.patreon_url);
  assert.equal(a.textContent, 'Get Prime');
  assert.equal(price.textContent, 'USD 12.50 a month');
  let held = 0;
  a.listeners.click[0]({ preventDefault: () => { held++; } });
  assert.equal(sent[0].event, 'cta_clicked');
  assert.equal(sent[0].plan, 'prime', 'in the app the host is told which plan to open');
  assert.equal(held, 1, 'in the app the link does not open itself (it raised a bare popup)');
  const web = buildPlanRow({ doc: fakeDoc(), row: planRowFor({ daily_gift: true }), plans: C1, env: {} });
  let webHeld = 0;
  web.children[1].listeners.click[0]({ preventDefault: () => { webHeld++; } });
  assert.equal(webHeld, 0, 'with no host the link opens itself');
  assert.equal(buildPlanRow({ doc: fakeDoc(), row: planRowFor({ daily_gift: true }), env: {} }).children.length, 2, 'no plans, no price');
  assert.doesNotMatch(DAILY_GIFT_LINE, /[!\u2013\u2014]/);
});

test('the summary wires the row after the payout, never during a run, and only fades', () => {
  const ov = read('game/overlays.js');
  assert.match(ov, /mf\.staggerRows\(payoutSlot, 0\);[^\n]*\n\s*showPlanRow\(p, opts\);/, 'after the payout rows');
  assert.match(ov, /card\.appendChild\(box\)/, 'last on the card, under fall again / wake up');
  assert.match(ov, /hideRecap\(\) \{ mf\.finishCounts\(\); clearPlan\(\);/, 'a hidden summary cancels a pending row');
  assert.ok(PLAN_ROW_DELAY_MS >= 1200, 'it waits for the payout count to land');
  const css = read('styles.css');
  const rule = css.match(/\.cf-recap-plan \{[^}]*\}/);
  assert.ok(rule, 'styled');
  assert.match(rule[0], /transition: opacity/);
  assert.doesNotMatch(rule[0], /animation|box-shadow|transform/, 'no flash, no glow, no motion beyond opacity');
  assert.match(css, /html\[data-motion="off"\] \.cf-recap-plan \{ transition: none; \}/);
  const run = read('game/chaosRun.js');
  assert.match(run, /overlays\?\.showPayout\(m, \{ plans: /);
  assert.match(read('boot.js'), /hostState\.plans = \(m\.plans && typeof m\.plans === 'object'\) \? m\.plans : null;/);
});
