// FUZZ_URL points at a served fuzz directory. No player media or app instance is needed.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const { chromium } = createRequire('C:/wt-fe/e2e/package.json')('playwright');
const base = (process.env.FUZZ_URL || 'http://localhost:8787/fuzz').replace(/\/$/, '');
const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--mute-audio'] });
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  await page.goto(base + '/vendor/gifenc.esm.js');
  const result = await page.evaluate(async base => {
    const { captureButtonBoard } = await import(base + '/ui/adcard/button-board-capture.js');
    document.head.innerHTML = '<style>.dk{display:none;position:absolute;width:180px;height:120px;top:200px;left:100px}.dk-chip{display:inline-block;width:70px;height:30px}.row{width:220px;height:40px}</style>';
    document.body.innerHTML = '<main id="world"><div id="btn-wrap">button</div><div id="counter">123</div><div id="account-chip">player</div><div id="shop" hidden><div class="row">buy</div></div><div id="dock-strip"></div></main>';
    for (const id of ['mini_slots', 'wheel', 'toy_box', 'dispenser']) {
      const el = document.createElement('div'); el.className = 'dk'; el.dataset.fzOwner = id;
      el.innerHTML = '<span>cabinet ' + id + '</span><button>play</button>'; document.body.append(el);
      const chip = document.createElement('div'); chip.className = 'dk-chip'; chip.dataset.fzOwner = id;
      chip.textContent = id; document.querySelector('#dock-strip').append(chip);
    }
    function normal(node) {
      if (node.nodeType === 3) return node.data;
      return { tag: node.localName,
        attrs: Array.from(node.attributes || []).filter(a => a.name !== 'style').map(a => [a.name, a.value]).sort(),
        style: Array.from(node.style || []).sort().map(k => [k, node.style.getPropertyValue(k)]),
        children: Array.from(node.childNodes || [], normal) };
    }
    const serialize = board => board.surfaces.map(({ node, source, ...surface }) =>
      ({ ...surface, html: Array.from((node.shadowRoot || node).childNodes, normal) }));
    const ctx = { world: document.querySelector('#world') }, sync = captureButtonBoard(ctx);
    const expected = serialize(sync); sync.dispose();
    const before = document.body.innerHTML.replace(/ style=""/g, '');
    const pending = captureButtonBoard(ctx, undefined, { deferStyles: true });
    const button = document.querySelector('#btn-wrap');
    button.textContent = 'changed after snapshot'; button.style.color = 'red';
    const ready = await pending.prepare();
    const actual = serialize(pending); pending.dispose();
    button.textContent = 'button'; button.removeAttribute('style');
    const cancelled = captureButtonBoard(ctx, undefined, { deferStyles: true }); cancelled.dispose();
    const stopped = await cancelled.prepare();
    return { ready, same: JSON.stringify(expected) === JSON.stringify(actual), stopped,
      restored: before === document.body.innerHTML.replace(/ style=""/g, ''), surfaces: actual.length };
  }, base);
  assert.deepEqual(result, { ready: true, same: true, stopped: false, restored: true, surfaces: 12 });
  console.log('Immutable capture, hidden-source restoration and cancellation passed.');
} finally { await browser.close(); }
