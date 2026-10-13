// Self-contained pass over the RECAP PLAN ROW (conversion lane G1, brief C10).
//   node Resources/web/goon/test/selftest-recapplan.js
//
//   1. THE DECISIONS (ui/screens/recapPlan.js): the plan, the price only with a
//      payload, the funnel line shape, the roads the count takes.
//   2. THE ROW ON THE SCREEN, mounted through the real recap module: present only
//      when the host said this account cannot host, BELOW the actions, and the
//      hosted click goes to the app's own upgrade path, never a popup.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const HERE = path.dirname(fileURLToPath(import.meta.url));
const read = (rel) => fs.readFileSync(path.join(HERE, '..', rel), 'utf8').replace(/\r\n/g, '\n');
const quiet = { info() {}, warn() {}, error() {}, debug() {} };

/* --- the DOM stub (selftest-report.js's shape, trimmed) --- */
class StubStyle {
  constructor() { this._p = new Map(); }
  setProperty(k, v) { this._p.set(k, String(v)); }
  getPropertyValue(k) { return this._p.get(k) || ''; }
}
class StubEl {
  constructor(tag) {
    this.tagName = String(tag).toUpperCase();
    this.childNodes = []; this.parentNode = null; this.isConnected = false;
    this.style = new StubStyle(); this.textContent = ''; this.value = ''; this.disabled = false; this.hidden = false;
    this._cls = new Set(); this._attrs = new Map(); this._listeners = new Map();
  }
  get className() { return Array.from(this._cls).join(' '); }
  set className(v) { this._cls = new Set(String(v).split(/\s+/).filter(Boolean)); }
  get classList() {
    const set = this._cls;
    return { add: (...c) => c.forEach((x) => set.add(x)), remove: (...c) => c.forEach((x) => set.delete(x)),
      contains: (c) => set.has(c), toggle: (c, on) => (on ? set.add(c) : set.delete(c)) };
  }
  get childElementCount() { return this.childNodes.length; }
  appendChild(c) { if (c.parentNode) c.parentNode.removeChild(c); c.parentNode = this; this.childNodes.push(c); return c; }
  removeChild(c) { const i = this.childNodes.indexOf(c); if (i >= 0) this.childNodes.splice(i, 1); c.parentNode = null; return c; }
  remove() { if (this.parentNode) this.parentNode.removeChild(this); }
  replaceChildren(...kids) { for (const c of this.childNodes.slice()) c.parentNode = null; this.childNodes = []; for (const k of kids) this.appendChild(k); }
  setAttribute(k, v) { this._attrs.set(k, String(v)); }
  getAttribute(k) { return this._attrs.has(k) ? this._attrs.get(k) : null; }
  removeAttribute(k) { this._attrs.delete(k); }
  addEventListener(t, fn) { if (!this._listeners.has(t)) this._listeners.set(t, []); this._listeners.get(t).push(fn); }
  removeEventListener(t, fn) { const a = this._listeners.get(t); if (!a) return; const i = a.indexOf(fn); if (i >= 0) a.splice(i, 1); }
  fire(t, extra) {
    const evt = Object.assign({ type: t, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, stopPropagation() {} }, extra || {});
    for (const fn of (this._listeners.get(t) || []).slice()) fn(evt);
    return evt;
  }
  findAll(cls, out = []) {
    for (const c of this.childNodes) { if (c._cls && c._cls.has(cls)) out.push(c); if (c.findAll) c.findAll(cls, out); }
    return out;
  }
}
const documentElement = new StubEl('html');
globalThis.document = {
  documentElement,
  body: new StubEl('body'),
  createElement: (tag) => new StubEl(tag),
  createTextNode: (t) => { const e = new StubEl('#text'); e.textContent = t; return e; },
  getElementById: () => null,
  addEventListener() {}, removeEventListener() {}, dispatchEvent() { return true; },
};
globalThis.window = { innerWidth: 1280, innerHeight: 720 };

/* ===========================================================================
 * 1. THE DECISIONS
 * ========================================================================= */
const rp = await import('../ui/screens/recapPlan.js');
const C1 = {
  currency: 'USD',
  basic: { name: 'Basic', monthly: '7.50', patreon_url: 'https://www.patreon.com/checkout/CodeBambi?rid=27604786' },
  prime: { name: 'Prime', monthly: '12.50', patreon_url: 'https://www.patreon.com/checkout/CodeBambi?rid=27681505' },
};
{
  const bare = rp.planOf(undefined, 'basic');
  ok(bare.url === 'https://www.patreon.com/checkout/CodeBambi?rid=27604786', 'no payload: the Basic checkout link', bare.url);
  ok(bare.price === '', 'no payload: no price');
  ok(rp.planOf(C1, 'basic').price === 'USD 7.50 a month', 'a payload adds the price');
  const evil = rp.planOf({ basic: { patreon_url: 'javascript:alert(1)', name: '<b>x</b>', monthly: '1e9' } }, 'basic');
  ok(evil.url === rp.PLAN_FALLBACK.basic.url && evil.name === 'Basic' && evil.price === '', 'a hostile payload never becomes a link, a name or a price');
  ok(rp.recapPlanWanted({ caps: { canHost: false } }) === true, 'cannot host -> the row');
  ok(rp.recapPlanWanted({ caps: { canHost: true } }) === false, 'can host -> no row');
  ok(rp.recapPlanWanted({ caps: {} }) === false, 'unknown -> no row (never sell Basic to Basic)');
  ok(rp.recapPlanWanted(null) === false, 'no session -> no row');
  ok(rp.funnelLine('cta_clicked', 'goon_host', 'patreon') === 'cta_clicked|page|goon_host|patreon', 'the funnel line shape');
  ok(rp.funnelLine('lapse', 'x', 'y') === '', 'only the two page events');
  const sent = [];
  ok(rp.postFunnel('gate_shown', 'goon_host', 'basic', { hosted: true, send: (m) => sent.push(m) }) === 'host', 'hosted -> a frame to the host');
  ok(JSON.stringify(sent) === JSON.stringify([{ type: 'funnel', event: 'gate_shown', a: 'goon_host', b: 'basic' }]), 'the C10 frame shape', JSON.stringify(sent));
  const beacons = [];
  const env = { navigator: { sendBeacon: (u, b) => { beacons.push([u, b]); return true; } }, location: { protocol: 'https:' } };
  ok(rp.postFunnel('cta_clicked', 'goon_host', 'patreon', { env }) === 'beacon', 'standalone https -> a beacon');
  ok(beacons[0] && beacons[0][0] === rp.FUNNEL_URL && beacons[0][1] === 'cta_clicked|page|goon_host|patreon', 'beacon url + body');
  ok(rp.postFunnel('cta_clicked', 'goon_host', 'patreon', { env: { location: { protocol: 'file:' } } }) === 'none', 'no host, no https -> nothing');
  ok(!/[!\u2013\u2014]/.test(rp.RECAP_PLAN_LINE), 'copy voice: no exclamation, no long dashes');
}

/* ===========================================================================
 * 2. THE ROW ON THE SCREEN
 * ========================================================================= */
const recap = await import('../ui/screens/recap.js');
function fakeCtx(session, actions) {
  return {
    logger: quiet,
    audio: { sfx() {}, music() {}, stopMusic() {}, tone() {} },
    prefs: { get: () => 0, set() {} },
    matchLog: { stats: () => ({ landedOnYou: 0, enduredByYou: 0 }), sawPhase: () => false, payloads: () => [] },
    actions: Object.assign({ leave() {} }, actions || {}),
    getMatch: () => null,
    session,
  };
}
const ident = { identity: { unifiedId: 'u', displayName: 'A' } };
const indexOfClass = (box, cls) => box.childNodes.findIndex((c) => c._cls && c._cls.has(cls));

{
  const posted = [];
  globalThis.chrome = { webview: { postMessage: (m) => posted.push(m), addEventListener() {} } };
  let seen = 0;
  const box = new StubEl('section');
  const h = recap.mount(box, fakeCtx(Object.assign({ hosted: true, caps: { canHost: false } }, ident), { seePrime: () => { seen++; } }));
  const col = box.childNodes[0];
  const rows = col.findAll('gg-recap-plan');
  ok(rows.length === 1, 'hosted, cannot host -> one plan row');
  ok(indexOfClass(col, 'gg-recap-plan') > indexOfClass(col, 'gg-recap-actions') && indexOfClass(col, 'gg-recap-actions') >= 0,
    'the row sits BELOW the actions, never between the player and Back to menu');
  const btn = rows[0] && rows[0].findAll('gg-recap-plan-btn')[0];
  ok(!!btn && btn.tagName === 'A' && btn.textContent === 'Get Basic', 'one button, Get Basic');
  ok(!!btn && btn.getAttribute('href') === rp.PLAN_FALLBACK.basic.url, 'its link is the Basic checkout');
  ok(rows[0] && rows[0].findAll('gg-recap-plan-price').length === 0, 'no plans payload -> no price');
  ok(posted.some((m) => m.type === 'funnel' && m.event === 'gate_shown' && m.a === 'goon_host'), 'gate_shown counted');
  const evt = btn.fire('click');
  ok(seen === 1, 'hosted click -> the app\'s own upgrade path (open-prime)');
  ok(evt.defaultPrevented === true, 'and the link does not also open a WebView2 popup');
  ok(posted.some((m) => m.type === 'funnel' && m.event === 'cta_clicked' && m.b === 'patreon'), 'cta_clicked counted');
  h.unmount();
  delete globalThis.chrome;
}
{
  const box = new StubEl('section');
  const h = recap.mount(box, fakeCtx(Object.assign({ hosted: true, caps: { canHost: false }, plans: C1 }, ident)));
  const price = box.childNodes[0].findAll('gg-recap-plan-price');
  ok(price.length === 1 && price[0].textContent === 'USD 7.50 a month', 'a plans payload shows the price');
  h.unmount();
}
for (const [label, session] of [
  ['can host', Object.assign({ hosted: true, caps: { canHost: true } }, ident)],
  ['unknown host answer', Object.assign({ hosted: true, caps: {} }, ident)],
  ['standalone', Object.assign({ hosted: false }, ident)],
]) {
  const box = new StubEl('section');
  const h = recap.mount(box, fakeCtx(session));
  ok(box.childNodes[0].findAll('gg-recap-plan').length === 0, label + ' -> no plan row');
  ok(box.childNodes[0].findAll('gg-recap-actions').length === 1, label + ' -> the recap still paints its actions');
  h.unmount();
}
{
  const css = read('ui/screens.css');
  for (const cls of ['gg-recap-plan', 'gg-recap-plan-line', 'gg-recap-plan-btn', 'gg-recap-plan-price']) ok(css.includes('.' + cls), 'ui/screens.css styles .' + cls);
}

console.log(failures === 0 ? `PASS - ${n} checks` : `FAILED - ${failures}/${n} checks`);
process.exit(failures === 0 ? 0 : 1);
