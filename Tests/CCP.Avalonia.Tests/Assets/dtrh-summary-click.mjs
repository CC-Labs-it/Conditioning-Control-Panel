import assert from 'node:assert/strict';
import fs from 'node:fs';
import { planRowFor, buildPlanRow } from '../../../Assets/web/dtrh/game/planCard.js';

const { payout, init, expectRow } = JSON.parse(fs.readFileSync(0, 'utf8'));
const row = planRowFor(payout);
assert.equal(row !== null, expectRow);
if (row) {
  const make = tag => ({ tag, children: [], listeners: {},
    setAttribute() {}, append(...children) { this.children.push(...children); },
    addEventListener(type, callback) { this.listeners[type] = callback; } });
  const sent = [];
  const box = buildPlanRow({ doc: { createElement: make }, row, plans: init.plans,
    env: { chrome: { webview: { postMessage: message => sent.push(message) } } } });
  const [line, link, price] = box.children;
  assert.equal(line.textContent, 'Open today. Prime keeps it open.');
  assert.equal(link.textContent, 'Get Prime');
  assert.equal(link.href, init.plans.prime.patreon_url);
  assert.equal(price.textContent, `${init.plans.currency} ${init.plans.prime.monthly} a month`);
  let prevented = false;
  link.listeners.click({ preventDefault() { prevented = true; } });
  assert.equal(prevented, true);
  assert.equal(sent.length, 1);
  assert.equal(sent[0].plan, 'prime');
  process.stdout.write(JSON.stringify(sent[0]));
}
