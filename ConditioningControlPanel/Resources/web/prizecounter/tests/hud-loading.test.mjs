// The loading card: there from the start, gone exactly once, and never the reason the page is stuck.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildLoading, createLoading, settledWithin, LOADING_ID, LOADING_OUT_MS } from '../hud/loading.js';
import { fakeDocument } from './fake-dom.mjs';

const here = (f) => new URL(`../${f}`, import.meta.url);

function rig({ drawn = false } = {}) {
  const doc = fakeDocument(), timers = [];
  doc.getElementById = (id) => doc.body.all().find((n) => n.id === id) || null;
  if (drawn) doc.body.append(buildLoading(doc));
  const loading = createLoading(doc, { wait: (ms, fn) => timers.push({ ms, fn }) });
  return { doc, loading, timers };
}

test('a page without the markup gets the card built', () => {
  const r = rig();
  assert.equal(r.doc.body.querySelectorAll('.pc-loading').length, 1);
  assert.equal(r.loading.el.getAttribute('aria-busy'), 'true');
  assert.equal(r.loading.el.querySelectorAll('.pc-loading-dot').length, 3);
});

test('the card index.html drew is taken over, not doubled', () => {
  const r = rig({ drawn: true });
  assert.equal(r.doc.body.querySelectorAll('.pc-loading').length, 1);
});

test('done plays the OUT, then removes the card', () => {
  const r = rig();
  r.loading.done();
  assert.ok(r.loading.el.classList.contains('pc-loading-out'));
  assert.equal(r.loading.el.getAttribute('aria-busy'), 'false');
  assert.equal(r.doc.body.querySelectorAll('.pc-loading').length, 1, 'still there while it leaves');
  assert.deepEqual(r.timers.map((t) => t.ms), [LOADING_OUT_MS]);
  r.timers[0].fn();
  assert.equal(r.doc.body.querySelectorAll('.pc-loading').length, 0);
});

test('done twice is one OUT', () => {
  const r = rig();
  r.loading.done();
  r.loading.done();
  assert.equal(r.timers.length, 1);
  assert.equal(r.loading.gone, true);
});

test('gentle and the motion level land on the card', () => {
  const r = rig();
  r.loading.gentle(true);
  r.loading.motion('off');
  assert.ok('gentle' in r.loading.el.dataset);
  assert.equal(r.loading.el.dataset.motion, 'off');
});

test('settledWithin resolves on the promise, on a rejection and on the cap', async () => {
  await settledWithin(Promise.resolve(1), 10_000, () => {});
  await settledWithin(Promise.reject(new Error('no shelf')), 10_000, () => {});
  let fire = null;
  const p = settledWithin(new Promise(() => {}), 50, (ms, fn) => { fire = fn; });
  fire();
  await p;
});

test('index.html and dev.html ship the card the script builds', () => {
  for (const f of ['index.html', 'dev.html']) {
    const html = readFileSync(here(f), 'utf8');
    assert.ok(html.includes(`id="${LOADING_ID}"`), `${f} draws the card`);
    assert.equal((html.match(/pc-loading-dot"/g) || []).length, 3, `${f} has three dots`);
    assert.ok(html.indexOf(`id="${LOADING_ID}"`) < html.indexOf('<script type="module">'), `${f} draws it before any module runs`);
  }
});

test('the card has an OUT and both calm modes are covered in page.css', () => {
  const css = readFileSync(here('page.css'), 'utf8');
  assert.ok(css.includes('.pc-loading.pc-loading-out'));
  assert.ok(css.includes('.pc-loading[data-gentle]'));
  assert.ok(/prefers-reduced-motion: reduce\) \{[^}]*\.pc-loading/.test(css));
});

test('main.js lets the card go from a finally', () => {
  const js = readFileSync(here('main.js'), 'utf8');
  assert.ok(/finally \{[^}]*loading\.done\(\)/.test(js));
});
