// dev/build-preview.mjs <outDir>: a flat copy of the game that runs from one folder, for a private playable preview.
// Copies what the page needs, brings the Loom in from the arcademy tree, and turns index.html into a page body.
// Nothing here ships: hosted builds serve the tree as it is.
import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const tree = join(dirname(fileURLToPath(import.meta.url)), '..');
const out = process.argv[2];
if (!out) { console.log('usage: node dev/build-preview.mjs <outDir>'); process.exit(2); }
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });

const KEEP = ['assets', 'audio', 'data', 'fx', 'sim', 'ui', 'bridge.js', 'main.js', 'host-escape.js', 'sparkles-slip.js', 'styles.css'];
for (const k of KEEP) cpSync(join(tree, k), join(out, k), { recursive: true });
const LOOM = join(tree, '..', 'arcademy', 'engine', 'loom');
cpSync(LOOM, join(out, 'loom'), { recursive: true });

const walk = (d) => readdirSync(d).flatMap((n) => {
  const p = join(d, n);
  return statSync(p).isDirectory() ? walk(p) : [p];
});
const files = walk(out);

// Imports of the Loom climb out of the tree; point them at the copy.
const FROM = /(['"])(?:\.\.\/)+arcademy\/engine\/loom\/([^'"]+)\1/g;
let rewrote = 0;
for (const f of files.filter((x) => x.endsWith('.js'))) {
  const s = readFileSync(f, 'utf8');
  const next = s.replace(FROM, (_, q, name) => {
    rewrote += 1;
    let rel = relative(dirname(f), join(out, 'loom', name)).split('\\').join('/');
    if (!rel.startsWith('.')) rel = './' + rel;
    return q + rel + q;
  });
  if (next !== s) writeFileSync(f, next, 'utf8');
}

// The page: the head's links and title, then the body, with no document tags.
const html = readFileSync(join(tree, 'index.html'), 'utf8');
const head = /<head>([\s\S]*?)<\/head>/.exec(html)[1];
const body = /<body[^>]*>([\s\S]*?)<\/body>/.exec(html)[1];
const keep = head.split(/\r?\n/).filter((l) => /<title|<link/.test(l)).join('\n');
/* The viewer puts all of this in its body, after its head. Every sheet the game adds later goes in the head, so a <link> left here would come
 * after them and win every tie: no theme colours, no unlock css. Move the links up before the game starts. */
const hoist = "<script>for (const l of document.querySelectorAll('link[rel=stylesheet]')) document.head.appendChild(l);</script>";
writeFileSync(join(out, 'index.html'), keep + '\n' + hoist + '\n' + body, 'utf8');

const all = walk(out);
const size = all.reduce((n, f) => n + statSync(f).size, 0);
console.log(all.length + ' files, ' + (size / 1048576).toFixed(1) + ' MB, ' + rewrote + ' Loom imports rewritten');
