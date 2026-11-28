/* The pause card's credits line inside a desktop host (wave 4 lane E item 3, desk run 2026-11-24,
 * 02-tc-pause-card.png): the game ran inside the CCP app and still offered "Get the desktop app". On a desktop host that
 * link and the dot before it do not show; on the web nothing changes; no copy changes. Built from the real index.html. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { trimCredits, DESKTOP_URL } from '../ui/links.js';
import { readEdition } from '../ui/edition.js';

const read = (f) => readFileSync(new URL(`../${f}`, import.meta.url), 'utf8');

/** The credits paragraphs of index.html as plain objects, in order, with previousElementSibling wired. */
function creditsRoot() {
  const html = read('index.html');
  const block = html.slice(html.indexOf('<p class="made-by">'), html.indexOf('</p>', html.indexOf('made-by inspired-by')));
  const els = [];
  for (const m of block.matchAll(/<span aria-hidden="true">([^<]*)<\/span>|<button type="button" data-credit="([^"]+)">([^<]*)<\/button>/g)) {
    const el = m[2]
      ? { tag: 'button', dataset: { credit: m[2] }, text: m[3], hidden: false, getAttribute: () => null }
      : { tag: 'span', text: m[1], hidden: false, getAttribute: (k) => (k === 'aria-hidden' ? 'true' : null) };
    el.previousElementSibling = els.at(-1) || null;
    els.push(el);
  }
  return { els, querySelectorAll: (q) => (q === '[data-credit]' ? els.filter((e) => e.tag === 'button') : []) };
}
const shown = (root) => root.els.filter((e) => !e.hidden).map((e) => e.text).join(' ');

test('index.html still carries the desktop link the rule looks for (copy unchanged)', () => {
  const root = creditsRoot();
  const desk = root.els.find((e) => e.dataset?.credit === DESKTOP_URL);
  assert.ok(desk, 'the link is there');
  assert.equal(desk.text, 'Get the desktop app');
});

test('a desktop host hides the desktop app link and its dot; the web keeps every credit', () => {
  const web = creditsRoot();
  const before = shown(web);
  assert.equal(trimCredits(web, 'web'), 0);
  assert.equal(shown(web), before, 'web: untouched');
  assert.match(before, /Get the desktop app/);
  const desk = creditsRoot();
  assert.equal(trimCredits(desk, 'desktop'), 1);
  assert.equal(shown(desk), '· cclabs.app · Discord Stimulation Clicker neal.fun', 'desktop: the link and one dot gone, the rest kept');
});

test('the platform the pause card reads is the host\'s: desktop only from a hosted desktop init', () => {
  const desktop = readEdition({ caps: { platform: 'desktop', edition: 'full' } }, { hostname: 'ccp.game', hosted: true });
  const web = readEdition({ caps: { platform: 'web', edition: 'small' } }, { hostname: 'tc.cclabs.app', hosted: true });
  const unhosted = readEdition({ caps: { platform: 'desktop' } }, { hostname: 'tc.cclabs.app', hosted: false });
  assert.equal(desktop.platform, 'desktop');
  assert.equal(web.platform, 'web');
  assert.equal(unhosted.platform, 'web', 'a page nobody hosts never reads as desktop');
  assert.match(read('ui/app.js'), /trimCredits\(el\.pause, editionState\(\)\.platform\)/, 'the pause card asks the rule');
});
