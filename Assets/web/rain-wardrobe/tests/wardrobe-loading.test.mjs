import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { BYTES_SHARE, LOAD_STR, createLoadProgress, createRevealGate, fill, moduleUrl, prefetchModule, releaseModule, revealTimings } from '../wardrobe-loading.js';
import { createShop, paintCompanion } from '../wardrobe-shop.js';

const read = name => readFileSync(new URL(`../${name}`, import.meta.url), 'utf8');
const sizes = { 'avatar.glb': 240e6, 'pelvis-data.js': 60e6 };

test('the bar counts bytes over both files against the host sizes, then the almost-ready phase', () => {
  const p = createLoadProgress({ sizes });
  assert.equal(p.fraction(), 0);
  assert.equal(p.line(), '0 of 300 MB');
  p.bytes('avatar.glb', 120e6, 0);
  p.bytes('pelvis-data.js', 30e6, 0);
  assert.equal(p.fraction(), BYTES_SHARE * 0.5);
  assert.equal(p.line(), '150 of 300 MB');
  p.bytes('avatar.glb', 240e6, 0);
  assert.ok(p.fraction() < BYTES_SHARE, 'one file still coming: the bytes share is not full');
  p.done('pelvis-data.js');
  assert.equal(p.fraction(), BYTES_SHARE, 'all bytes in: the bytes share and nothing more');
  assert.equal(p.line(), LOAD_STR.loadAlmost);
  p.stage(0.5);
  assert.equal(p.fraction(), BYTES_SHARE + (1 - BYTES_SHARE) * 0.5);
  p.stage(0.2);
  assert.equal(p.fraction(), BYTES_SHARE + (1 - BYTES_SHARE) * 0.5, 'a stage never goes back');
  p.stage(1);
  assert.equal(p.fraction(), 1);
});

test('a server total stands in when the host sent no size; the fallback before that; the bar never runs back', () => {
  const p = createLoadProgress({ fallback: { 'avatar.glb': 100, 'pelvis-data.js': 100 } });
  assert.equal(p.total, 200, 'fallback sizes');
  p.bytes('avatar.glb', 10, 50);
  assert.equal(p.total, 150, 'Content-Length replaces the fallback');
  p.bytes('avatar.glb', 10, 999);
  assert.equal(p.total, 150, 'the first real total sticks');
  const before = p.fraction();
  p.bytes('pelvis-data.js', 400, 0);
  assert.equal(p.total, 450, 'a file larger than its total grows it');
  assert.ok(p.fraction() >= before);
  p.bytes('avatar.glb', 5, 0);
  assert.equal(p.loaded, 410, 'a stale tick never lowers the count');
  const host = createLoadProgress({ sizes: { 'avatar.glb': 1000 } });
  host.bytes('avatar.glb', 10, 5000);
  assert.equal(host.total - 59827244, 1000, 'a host size beats Content-Length');
});

test('lines fill from the localised table; the host fills {companion}, the page fills the rest', () => {
  const p = createLoadProgress({ sizes });
  p.bytes('avatar.glb', 12.4e6, 0);
  assert.equal(p.line({ loadSize: '{loaded} di {total} MB' }), '12 di 300 MB');
  assert.equal(fill(LOAD_STR.loadTitle, { companion: 'Bambi' }), 'Bambi is getting dressed');
  assert.equal(fill('Bambi si sta vestendo', { companion: 'x' }), 'Bambi si sta vestendo');
  for (const line of Object.values(LOAD_STR)) { assert.ok(!/[–—!]/.test(line), line); }
});

test('the cover stays opaque until the look is dressed AND a frame of it is drawn', () => {
  const g = createRevealGate();
  assert.equal(g.opaque, true);
  assert.equal(g.mark('drawn'), false, 'a frame of a half-built look does not open it');
  assert.equal(g.opaque, true);
  assert.equal(g.mark('dressed'), true);
  assert.equal(g.opaque, false);
  const f = createRevealGate();
  f.mark('dressed'); f.fail(); f.mark('drawn');
  assert.equal(f.opaque, true, 'a failed boot keeps the cover (it shows the error)');
});

test('style: the cover is opaque, full window, above the room; the room is hidden while booting', () => {
  const css = read('style.css').replace(/\r/g, '');
  const rules = [...css.matchAll(/(^|\})\s*\.load-card\{([^}]*)\}/g)].map(m => m[2]);
  assert.ok(rules.length >= 1);
  for (const body of rules) {
    const bg = /background:([^;]+)/.exec(body);
    if (!bg) continue;
    assert.doesNotMatch(bg[1], /rgba|hsla|transparent|#[0-9a-f]{8}\b|#[0-9a-f]{4}\b/i, `cover background has alpha: ${bg[1]}`);
  }
  const base = rules.find(r => /position:fixed/.test(r));
  assert.ok(base && /inset:0/.test(base), 'fixed over the whole window');
  assert.ok(Number(/z-index:(\d+)/.exec(base)[1]) > 6, 'above the look bar');
  assert.match(css, /body\.booting \.topbar,body\.booting main,body\.booting \.look-bar\{visibility:hidden\}/);
  assert.match(read('index.html'), /<body class="booting">/);
});

test('viewer: nothing is drawn before the look is dressed, and the cover leaves only through the gate', () => {
  const src = read('viewer.js').replace(/\r/g, '');
  const loop = src.slice(src.indexOf('function render(now)'), src.indexOf('function drawPreview()'));
  assert.ok(loop.indexOf('if(!state.ready)return;') > -1 && loop.indexOf('if(!state.ready)return;') < loop.indexOf('drawPreview();'));
  assert.doesNotMatch(src, /\$\('loading'\)\.hidden=true/, 'only reveal() hides the cover');
  const boot = src.slice(src.indexOf('async function init()'), src.indexOf('function renderReviewCase'));
  assert.ok(boot.indexOf("revealGate.mark('dressed')") < boot.indexOf('await reveal(document'));
  assert.ok(boot.indexOf('compileAsync') < boot.indexOf('state.ready=true'), 'shaders compile behind the cover');
});

test('reveal timings: Calm only lengthens and never moves', () => {
  const full = revealTimings(false), calm = revealTimings(true);
  assert.equal(full.move, true);
  assert.equal(calm.move, false);
  assert.ok(calm.coverOut > full.coverOut && calm.roomIn > full.roomIn);
  assert.ok(full.coverOut >= 120 && full.coverOut <= 300, 'an exit from the house table');
});

test('prefetchModule counts streamed bytes and hands back an importable blob url', async () => {
  const chunks = [new Uint8Array(3), new Uint8Array(5)];
  const fake = async () => ({ ok: true, headers: { get: () => '8' }, body: { getReader: () => ({ read: async () => chunks.length ? { done: false, value: chunks.shift() } : { done: true } }) } });
  const ticks = [];
  const url = 'https://ccp.rain-assets/pelvis-data.js';
  const blob = await prefetchModule(url, (l, t) => ticks.push([l, t]), fake);
  assert.deepEqual(ticks, [[3, 8], [8, 8]]);
  assert.match(blob, /^blob:/);
  assert.equal(await moduleUrl(url), blob);
  releaseModule(url);
  assert.equal(await moduleUrl(url), url, 'released: the plain url');
  prefetchModule('https://x/y.js', null, async () => ({ ok: false, status: 404 })).catch(() => {});
  assert.equal(await moduleUrl('https://x/y.js'), 'https://x/y.js', 'a failed prefetch falls back to the url');
});

test('the brand shows the query name from the first paint, and an init without a name keeps it', () => {
  const span = { nodeType: 1 };
  const brand = { firstChild: span, attrs: {}, setAttribute(k, v) { this.attrs[k] = v; }, insertBefore(node, ref) { assert.equal(ref, span); this.firstChild = node; } };
  const doc = { title: '', getElementById: id => (id === 'brand' ? brand : null), createTextNode: t => ({ nodeType: 3, nodeValue: t }) };
  paintCompanion(doc, 'Bambi');
  assert.equal(brand.firstChild.nodeValue, 'BAMBI');
  assert.equal(doc.title, 'Bambi | Dressing room');
  paintCompanion(doc, 'Bimbo');
  assert.equal(brand.firstChild.nodeValue, 'BIMBO', 'the second paint rewrites the same text node');
  const shop = createShop({ items: { items: [] }, companion: 'Bambi' });
  assert.equal(shop.companion, 'Bambi');
  shop.handle({ type: 'init', look: { rev: 1, items: {} } });
  assert.equal(shop.companion, 'Bambi');
  shop.handle({ type: 'init', look: { rev: 1, items: {} }, companion: 'Bimbo' });
  assert.equal(shop.companion, 'Bimbo');
});
