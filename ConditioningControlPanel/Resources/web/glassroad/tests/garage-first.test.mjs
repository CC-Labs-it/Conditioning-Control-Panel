// Garage first (load time trick 3): the menu opens as soon as the garage is built and the course's lights
// (shaders, mirrors, water probe, hit cam, gate screen) warm behind it. The opaque-cover rule holds because
// every warm frame is overdrawn by a garage frame in the same task and every offscreen target is handed
// back before an await. Race waits for the lights under the cover.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const boot = src('boot.js');
const between = (s, a, b) => { const i = s.indexOf(a); assert.ok(i >= 0, a); const j = s.indexOf(b, i); assert.ok(j > i, b); return s.slice(i, j); };

test('boot: the cover lifts on the garage before the lights warm, and the lights milestone follows', () => {
  const flow = between(boot, "loaded('media');", 'void warmLights();');
  const at = (needle) => { const i = flow.indexOf(needle); assert.ok(i >= 0, needle); return i; };
  assert.ok(at("hud.setReady(true);") < at('bridge.ready();'));
  assert.ok(at('toMenu();') < at('loading.finish()'));
  assert.ok(at('loading.finish()') < at('async function warmLights()'));
  assert.match(flow, /loaded\('ready'\)/);
  const warm = between(boot, 'async function warmLights()', 'void warmLights();');
  assert.match(warm, /lights\.busy = true;/);
  assert.match(warm, /renderer\.compile\(world\.scene, cam\.camera, undefined,[^;]*alive, overdraw \}\)/);
  assert.match(warm, /warmCapture\(renderer, world\.scene, cam\.camera, \{ overdraw \}\)/);
  assert.match(warm, /warmReflections\(\{ playerPos: world\.kart\.position, playerS: 0, overdraw,/);
  // the tier verdict line keeps its shape (options-quality-freeze pins it) and the race starts the moment the lights are done
  assert.match(warm, /renderer\.beginCourse\(\); \/\/ Covered preparation[^\n]*\r?\n\s*warmedTier = renderer\.tier;/);
  assert.match(warm, /lights\.ready = true; hud\.setLights\(\{ ready: true \}\);/);
  assert.match(warm, /loaded\('lights'\)/);
  assert.match(warm, /if \(lights\.covered\) \{ lights\.covered = false; void loading\.finish\(\); \}/);
  assert.match(warm, /if \(want === 'race'\) startRace\(\);\r?\n\s*else if \(want === 'online'\) goOnline\(\);/);
  // the menu's frame only touches the garage while the warm owns the world
  const overdraw = between(boot, 'function garageFrame(dt)', 'async function warmLights()');
  assert.match(overdraw, /state !== 'menu' \|\| lights\.covered\) return;/);
  assert.match(overdraw, /garage\.render\(dt,/);
});

test('boot: Race and the room go wait for the lights under the cover; the frame is the menu\'s own meanwhile', () => {
  const sr = between(boot, 'function startRace() {', 'function toMenu() {');
  assert.match(sr, /if \(!lights\.ready\) \{ waitForLights\('race'\); return; \}/);
  const go = between(boot, 'function goOnline() {', 'state = \'race\';');
  assert.match(go, /if \(!lights\.ready\) \{ waitForLights\('online'\); return; \}/);
  const wait = between(boot, 'function waitForLights(want)', 'function startRace() {');
  assert.match(wait, /if \(lights\.covered\) return;/);
  assert.match(wait, /loading\.start\(\); garage\?\.setVisible\(false\); hud\.setCard\(null\);/);
  assert.match(wait, /showLoading\(lights\.stage, lights\.p\)/);
  const fb = between(boot, 'function frameBody(now) {', 'renderer.frameDue(now)');
  assert.match(fb, /if \(lights\.busy\) \{ menuFrame\(now\); return; \}/);
  const mf = between(boot, 'function menuFrame(now)', 'frameId = requestAnimationFrame(frame);');
  assert.doesNotMatch(mf, /world\.update|renderer\.render\(|racers\.step|deferred\.step|renderer\.setHold/);
  assert.match(mf, /audio\.update\(0, \{ active: false, menu: state === 'menu'/);
  assert.match(mf, /garageFrame\(frameDt\)/);
  assert.ok(mf.indexOf('garage?.setVisible(') < mf.indexOf('if (lights.quiet'), 'card ownership updates even while the inset warm holds rendering');
  assert.ok(src('hud/garage.css').includes('body:has(#card:not([hidden]):not([data-kind=menu])) #race-garage{display:none!important}'), 'a card hides stale garage controls without waiting for a frame');
  assert.match(boot, /busy = rewarmBusy \|\| controller\.busy \|\| lights\.busy/);
});

test('cards: the Race door wears the lights\' note while they warm and turns hot when they are ready', () => {
  const cards = src('hud/cards.js');
  assert.match(cards, /const lights = \{ ready: true, stage: '', p: 0 \};/);
  assert.match(cards, /btn\('race', '<b>Race<\/b><em>' \+ raceDoorNote\(\) \+ '<\/em>', 'c-door' \+ \(lights\.ready \? ' hot' : ' c-wait'\)\)/);
  const set = between(cards, 'setLights(s) {', 'setProgress(');
  assert.match(set, /lights\.p = s\.ready \? 0 : Math\.max\(lights\.p,/);   // never backwards
  assert.match(set, /door\.classList\.toggle\('hot', lights\.ready\); door\.classList\.toggle\('c-wait', !lights\.ready\);/);
  assert.match(src('hud/hud.js'), /setLights: \(s\) => cards\.setLights\(s\),/);
  const css = src('styles.css');
  assert.match(css, /#card \.c-door\.c-wait \{/);
  assert.match(css, /prefers-reduced-motion: reduce\) \{ #card \.c-door\.c-wait em \{ animation: none; \}/);
  for (const copy of ['You and 7 bots', 'Getting the lights ready']) assert.ok(cards.includes(copy), copy);
  assert.doesNotMatch(cards.slice(cards.indexOf('raceDoorNote'), cards.indexOf('raceDoorNote') + 200), /[!–—]/);
});

test('render: every warm hands the canvas back before its await (warm frames are never presented)', () => {
  const r = src('render/renderer.js');
  const inset = between(r, 'function withInsetTarget(three, target, fn)', 'function renderInset(');
  assert.doesNotMatch(inset, /\.finally\(restore\)/);
  assert.match(inset, /try \{ out = fn\(\); \} catch \(e\) \{ restore\(\); throw e; \}\r?\n[\s\S]*restore\(\);\r?\n\s*return out;/);
  const warm = r.slice(r.indexOf('export async function warmAll('));
  assert.match(warm, /post\.compileStart \? post\.compileStart\(\) : post\.compileAsync\(\)/);
  assert.match(warm, /post\.render\(\); if \(overdraw\) overdraw\(\);/);
  assert.match(warm, /stopped: stopped\(\)/);
  const water = src('render/waterReflections.js');
  assert.match(water, /const compiling=three\.compileAsync\(scene,faceCamera\);\r?\n\s*handBack\(\);\r?\n\s*await compiling;/);
  assert.match(water, /if\(overdraw\)\{handBack\(\);overdraw\(\);three\.setRenderTarget\(target,face\);\}/);
  assert.match(water, /handBack\(\);\r?\n\s*onProgress\?\.\(face\+1,6\);/);
  const mirror = src('render/boudoirMirrorRoom.js');
  assert.match(mirror, /async function warmCapture\(renderer, scene, camera, \{ overdraw = null \} = \{\}\)/);
  assert.match(mirror, /await \(pass\.compileStart \? pass\.compileStart\(\) : pass\.compileAsync\(\)\);/);
  assert.match(mirror, /const compiling = raw\.compileAsync\([^\n]*\);\r?\n\s*raw\.setRenderTarget\(previousTarget\);\r?\n\s*await compiling;/);
  assert.match(mirror, /pass\.render\(\);\r?\n\s*if \(overdraw\) overdraw\(\);/);
});
