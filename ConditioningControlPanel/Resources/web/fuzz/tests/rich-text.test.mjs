// The copy markups (owner 2026-11-08): **keyword** and [[colour:words]]. Escaping first, spans second; plain consumers strip.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { richText, plainText, stripMarks, hasMarks, COLOURS } from '../ui/rich-text.js';
import { brandText, BRAND } from '../data/brand.js';
import { ITEMS, FORKS } from '../data/items.js';
import { CLAUSES, CLAUSE_EXTRAS } from '../data/content/clauses.js';

test('a string without markup renders exactly as before, escaped', () => {
  assert.equal(richText('Tap the button.'), 'Tap the button.');
  assert.equal(richText('a < b & "c"'), 'a &lt; b &amp; &quot;c&quot;');
  assert.equal(plainText('Tap the button.'), 'Tap the button.');
});

test('keywords and diegetic colours become spans', () => {
  assert.equal(richText('Let it **drop**.'), 'Let it <span class="kw">drop</span>.');
  assert.equal(richText('The cursor turns [[pink:pink]].'), 'The cursor turns <span class="kw-c" data-c="pink">pink</span>.');
  for (const c of COLOURS) assert.match(richText(`[[${c}:x]]`), new RegExp(`data-c="${c}"`));
});

test('markup can never smuggle HTML', () => {
  const evil = '**<img src=x onerror=alert(1)>** [[pink:<script>x</script>]]';
  const out = richText(evil);
  assert.doesNotMatch(out, /<img|<script/);
  assert.match(out, /&lt;img/);
  assert.equal(richText('[[pink" onclick="x:hi]]'), '[[pink&quot; onclick=&quot;x:hi]]');
});

test('unknown colours and stray stars stay literal', () => {
  assert.equal(richText('[[teal:sea]]'), '[[teal:sea]]');
  assert.equal(richText('5 * 3 ** 2'), '5 * 3 ** 2');
  assert.equal(hasMarks('5 * 3'), false);
  assert.equal(hasMarks('**go**'), true);
});

test('plain consumers get the markup stripped, brand tokens filled', () => {
  assert.equal(plainText('**Drop** for {cur} in [[gold:gold]]'), `Drop for ${BRAND.currencyPlural} in gold`);
  assert.equal(brandText('**Drop** [[pink:pink]]'), 'Drop pink');
  assert.equal(stripMarks('**a** and **b**'), 'a and b');
});

/* Every marked copy string: markups well formed, rare (two at most a line), and never a whole sentence. */
const strays = (s) => stripMarks(s).includes('**') || /\[\[[a-z]+:/.test(stripMarks(s));
const marks = (s) => (String(s).match(/\*\*[^*]+\*\*|\[\[[a-z]+:[^\]]+\]\]/g) || []);
function checkLine(where, s) {
  if (!s) return;
  assert.ok(!strays(s), `${where}: broken markup in ${s}`);
  const m = marks(s);
  assert.ok(m.length <= 2, `${where}: more than two keywords in ${s}`);
  for (const k of m) assert.ok(stripMarks(k).split(/\s+/).length <= 3, `${where}: keyword too long ${k}`);
}

test('shop copy markups are well formed and rare; names stay plain', () => {
  for (const it of ITEMS) {
    assert.ok(!hasMarks(it.name), `${it.id}: names stay plain`);
    checkLine(`${it.id}.what`, it.what);
    checkLine(`${it.id}.line`, it.line);
  }
  for (const [id, f] of Object.entries(FORKS)) {
    checkLine(`${id}.ask`, f.ask);
    for (const [k, o] of Object.entries(f.opts)) { assert.ok(!hasMarks(o.name), `${id}.${k} name`); checkLine(`${id}.${k}`, o.what); }
  }
});

test('clause copy markups are well formed and rare', () => {
  for (const [id, lines] of Object.entries(CLAUSES)) for (const l of [].concat(lines)) checkLine(`clause ${id}`, l);
  for (const [k, lines] of Object.entries(CLAUSE_EXTRAS)) for (const l of [].concat(lines)) checkLine(`extra ${k}`, l);
});

test('the stylesheet is linked and defines every colour', () => {
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  assert.match(html, /href="ui\/rich-text\.css"/);
  const css = readFileSync(new URL('../ui/rich-text.css', import.meta.url), 'utf8');
  assert.match(css, /\.kw\s*\{/);
  for (const c of COLOURS) assert.match(css, new RegExp(`data-c="${c}"`));
});

/* A content export that carries marks must not be written raw with textContent anywhere in ui/ (setRich, or brandText to strip). */
import { readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
test('marked content is never written raw with textContent', async () => {
  const content = await import('../data/content/index.js');
  const marked = (v) => typeof v === 'string' ? hasMarks(v) : v && typeof v === 'object' ? Object.values(v).some(marked) : false;
  const names = Object.keys(content).filter((k) => marked(content[k]));
  assert.ok(names.length > 0);
  const ui = new URL('../ui/', import.meta.url).pathname.replace(/^\/([A-Z]:)/, '$1');
  const files = [];
  const walk = (d) => { for (const n of readdirSync(d)) { const p = join(d, n); if (statSync(p).isDirectory()) walk(p); else if (p.endsWith('.js')) files.push(p); } };
  walk(ui);
  const bad = [];
  for (const f of files) readFileSync(f, 'utf8').split(/\r?\n/).forEach((l, i) => {
    const m = /\.textContent\s*=\s*([^;]*)/.exec(l);
    if (m && !/brand\.text|brandText|plainText/.test(m[1]) && names.some((n) => new RegExp(`\\b${n}\\b`).test(m[1]))) bad.push(`${f}:${i + 1}`);
  });
  assert.deepEqual(bad, []);
});
