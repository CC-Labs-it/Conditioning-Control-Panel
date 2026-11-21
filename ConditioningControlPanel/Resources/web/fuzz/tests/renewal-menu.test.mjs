// tests/renewal-menu.test.mjs - the Renewal menu sheet (ui/renewal-menu.js) and the Sky's browse mode (brief 1121, lane M).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { groupByLane, renewalHtml, RENEWAL_LABELS } from '../ui/renewal-menu.js';
import { ascensionView } from '../sim/ascension.js';
import * as P from '../sim/prestige.js';

const lifeWith = (fn) => { const p = P.createPrestige(); fn(p); return { prestige: p }; };

test('groupByLane keeps sky order and one group per lane', () => {
  const v = ascensionView(lifeWith((p) => { p.depth = 3; for (const id of ['pay_1', 'pay_2', 'head_1', 'idle_1']) p.own[id] = 1; p.lit = { drop: 1 }; }));
  const g = groupByLane(v.bonuses);
  assert.deepEqual(g.map((x) => x.lane), ['pay', 'head', 'idle', 'ending']);
  assert.equal(g[0].rows.length, 2);
  assert.ok(g.every((x) => x.name && x.hue));
});

test('the sheet leads with the Depth number and shows Stardust', () => {
  const html = renewalHtml(ascensionView(lifeWith((p) => { p.depth = 4; p.dust = 1400; p.own.pay_1 = 1; })));
  assert.match(html, /class="rn-depth"[^>]*><small>Depth<\/small><b>4<\/b>/);
  assert.match(html, /data-k="dust">1\.4k</);
  assert.match(html, /data-act="sky"/);
  assert.ok(html.includes(RENEWAL_LABELS.open));
});

test('depth 0 with nothing owned: Depth 0 and the empty line, still a door to the Sky', () => {
  for (const life of [{}, { prestige: null }, { prestige: 'junk' }, null]) {
    const html = renewalHtml(ascensionView(life));
    assert.match(html, /<b>0<\/b>/);
    assert.ok(html.includes(RENEWAL_LABELS.empty));
    assert.match(html, /data-act="sky"/);
    assert.ok(!html.includes('rn-next'));
  }
});

test('stars bought mid-run sit in their own From next run group, not with the bonuses', () => {
  const life = lifeWith((p) => { p.depth = 2; p.dust = 1e6; p.own.pay_1 = 1; });
  assert.equal(P.buyLater(life.prestige, 'pay_2').ok, true);
  const v = ascensionView(life);
  const html = renewalHtml(v);
  const [main, next] = html.split('rn-group rn-next');
  assert.ok(next, 'a next group');
  assert.ok(next.includes(v.next[0].name));
  assert.ok(!main.includes(v.next[0].name));
  assert.ok(!html.includes(RENEWAL_LABELS.empty));
});

test('names and lines are escaped', () => {
  const html = renewalHtml({ depth: 1, maxDepth: 10, dust: 0, bonuses: [{ id: 'x', kind: 'star', lane: 'pay', laneName: '<b>', hue: '#fff', icon: 'wallet', name: '<img src=x>', line: '"q"', plain: '&', next: false }], next: [] });
  assert.ok(!html.includes('<img src=x>'));
  assert.ok(html.includes('&lt;img src=x&gt;'));
});

test('the Sky in browse mode buys for the next run, never renews, signs or reloads', () => {
  const src = readFileSync(new URL('../ui/prestige-sky.js', import.meta.url), 'utf8');
  assert.match(src, /if \(browse\) \{ if \(n\.kind === 'core'\) P\.buyCoreLater\(p\); else P\.buyLater\(p, n\.id\); \}/);
  assert.match(src, /if \(browse\) \{ changed\(\); closeSheet\(\); return; \}/);   // Next run sheet keeps, never finish()
  assert.match(src, /if \(browse\) renewal = null;/);
  assert.match(src, /export const PRESTIGE_EVENT = 'tc:prestige-changed'/);
});
