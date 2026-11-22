import test from 'node:test';
import assert from 'node:assert/strict';
import {readdir, readFile} from 'node:fs/promises';
import {registerHooks} from 'node:module';
import {setEdition} from '../ui/edition.js';

const game = new URL('../', import.meta.url);
const ending = 'ui/unlocks/fate/llama/ending.js';
// Lane S empties this list when the legacy ending switches to the public door.
const legacyEndingImports = new Set([
  'ui/expo/entrance.js', 'ui/expo/portal.js', 'ui/expo/controller-menu.js',
  'ui/expo/sound-unlock.js', 'ui/expo/scene.js',
]);

async function inspectDirectory(dir, violations) {
  for (const entry of await readdir(new URL(dir, game), {withFileTypes: true})) {
    const file = dir + entry.name;
    if (entry.name.startsWith('.')) continue;
    if (entry.isDirectory()) {
      if (!['dev', 'tests', 'assets', 'vendor', 'node_modules', 'ui/expo'].includes(file)) {
        await inspectDirectory(file + '/', violations);
      }
    } else if (/\.(?:html|m?js)$/.test(file)) {
      const text = await readFile(new URL(file, game), 'utf8');
      for (const match of text.matchAll(/(?:\bfrom\s*|\bimport\s*(?:\(\s*)?|<script\b[^>]*?\bsrc\s*=\s*)['"]([^'"]+)['"]/g)) {
        const spec = match[1].split(/[?#]/, 1)[0];
        const resolved = new URL(spec, new URL(file, 'https://game.test/fuzz/')).pathname;
        const section = resolved.lastIndexOf('/ui/expo/');
        const path = section < 0 ? '' : resolved.slice(section + 1);
        if (path.startsWith('ui/expo/') && path !== 'ui/expo/door.js' && !(file === ending && legacyEndingImports.has(path))) {
          violations.push(file + ' imports ' + spec);
        }
      }
    }
  }
}

test('the game imports the Expo through its public door', async () => {
  const violations = [];
  await inspectDirectory('', violations);
  assert.deepEqual(violations, []);
});

test('importing the door is cold, and a closed edition cannot prewarm, play or open', async t => {
  const base = 'https://host.example/downloads/arrival/';
  const fetches = [];
  const sounds = [];
  t.mock.method(globalThis, 'fetch', async url => {
    fetches.push(url);
    return Response.json({start: {id: 'unused'}, zones: []});
  });
  const hook = registerHooks({resolve(spec, ctx, next) {
    assert.doesNotMatch(spec, /(?:^three(?:\/|$)|(?:^|\/)scene\.js$|assets\/expo\/)/);
    return next(spec, ctx);
  }});
  t.after(() => {hook.deregister(); setEdition(null, {noPage: true});});
  const door = await import('../ui/expo/door.js');
  t.after(door.clearEntrance);
  assert.deepEqual(fetches, []);
  const ctx = {audio: {play: (...args) => sounds.push(args)}};
  for (const [edition, state] of [['small', 'ready'], ['full', 'absent']]) {
    setEdition({caps: {edition, platform: 'desktop', expo: {state, base}}}, {hosted: true});
    await assert.rejects(door.openExpo(ctx), /unavailable/);
    door.prewarm();
    door.playSpit(ctx);
    assert.deepEqual(fetches, []);
    assert.deepEqual(sounds, []);
  }
  setEdition({caps: {edition: 'full', platform: 'desktop', expo: {state: 'ready', base}}}, {hosted: true});
  door.warmEntrance();
  assert.deepEqual(fetches, []);
  door.prewarm();
  door.prewarm();
  assert.deepEqual(fetches, [base + 'zones/zones.json']);
  assert.deepEqual(sounds, []);
  door.playSpit(ctx);
  assert.deepEqual(sounds, [[base + 'audio/spit.ogg', {bus: 'sfx', gain: .55}]]);
  for (const name of ['clearEntrance', 'createPortal', 'bindExpoMenus', 'bindExpoSoundUnlock', 'bindExpoPreviewButton']) {
    assert.equal(typeof door[name], 'function', name);
  }
});

test('opening uses the ending layer and cancels a visit closed during the lazy import', async t => {
  setEdition(null, {noPage: true});
  const {openExpo} = await import('../ui/expo/door.js');
  const fixture = 'export function createExpo(ctx, parent, options) { return ctx.mount(parent, options); }';
  const hook = registerHooks({resolve(spec, context, next) {
    if (spec === './scene.js' && context.parentURL.endsWith('/ui/expo/door.js')) {
      return {shortCircuit: true, url: 'data:text/javascript,' + encodeURIComponent(fixture)};
    }
    return next(spec, context);
  }});
  t.after(() => hook.deregister());
  let end, options, mounts = 0, disposals = 0;
  const ctx = {
    cleanup(fn) {end = fn;},
    layer(name) {assert.equal(name, 'ending'); return 'ending-layer';},
    mount(parent, opts) {
      mounts++;
      assert.equal(parent, 'ending-layer');
      options = opts;
      return {dispose() {disposals++;}};
    },
  };
  const cancelled = openExpo(ctx);
  end();
  await assert.rejects(cancelled, {name: 'AbortError'});
  assert.equal(mounts, 0);
  await openExpo(ctx);
  options.onExit();
  assert.equal(disposals, 1);
  await openExpo(ctx, 'ending-layer', {arrival: true});
  assert.equal(options.arrival, true);
  end();
  assert.equal(disposals, 2);
});
