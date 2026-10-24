/* render/scene.js - buildWorld (CONTRACT.md section 6): road, rails, pads and jump lips from the
 * track MeshData, the spiral sky, the teacup kart, floating props for speed reference, particles and
 * speed lines. update(view) moves everything from the interpolated kart and FxState.
 * opts.art (render/art.js) dresses the world in the wave 1 art: textured road, rails, pads and sky,
 * the EMI kart, countdown rings, the finish arch and a seeded scatter (render/dressing.js). Without
 * it, or for any asset that failed, the greybox stand-in stays.
 * B1 (CONTRACT-B1 render/world.js): eight karts (render/karts.js: tints, pose, ghost, name tags), the
 * sprite particles for every kart plus world bursts from bus events (bumps, pickup pops, item boxes,
 * landings), boost ribbons and ground rings (render/wakes.js), ambient motes (render/motes.js), the
 * road sheen and the SCREENFX lane's item visuals (render/items/visuals.js).
 * Polish 3: the full-spectrum palette is driven by the player's speed (pal.drive), the Loom spiral gates
 * (render/spirals3d.js, tracks/<id>.spirals.json) and the media slots with their placeholders
 * (render/media.js, tracks/<id>.media.json). Each is optional: a module or layout that fails to load
 * leaves the race running without it.
 */
import {
  Scene, Group, Mesh, InstancedMesh, Object3D, BufferGeometry, BufferAttribute, Float32BufferAttribute, Color, Vector2,
  SphereGeometry, LatheGeometry, TorusGeometry, CylinderGeometry, RingGeometry, IcosahedronGeometry,
  HemisphereLight, DirectionalLight, Vector3, FogExp2,
} from 'three/webgpu';
import { S } from '../sim/state.js';
import { clamp, lerp, smoothstep, qrotate, v3cross, v3norm } from '../core/math.js';
import { LOOK, SKIES, FALLBACK_SKY } from './look.js';
import { Q } from './quality.js';
import { createPalette } from './materials/palette.js';
import { createRoadMaterial } from './materials/road.js';
import { createRailMaterial } from './materials/rails.js';
import { createEdges } from './edges.js';
import { createPadMaterial } from './materials/pads.js';
import { createSkyMaterial, SKY_PALETTES } from './materials/sky.js';
import { buildCountdown, buildFinish, buildScatter, reelTexture } from './dressing.js';
import { buildTerrace } from './terrace.js';
import { buildColossi } from './colossi.js';
import { buildShowcaseDressing } from './showcaseDressing.js';
import { buildCourt } from './court.js';
import { buildShadows, CATCH_LAYER } from './shadows.js';
import { sampleTrack } from './colossiMath.js';
import { createPropMaterial } from './materials/kart.js';
import { createGlowMaterial } from './materials/glow.js';
import { createParticles } from './particles.js';
import { createSpeedLines } from './speedlines.js';
import { buildKarts } from './karts.js';
import { createWakes } from './wakes.js';
import { createMotes } from './motes.js';
import { createSkids } from './skids.js';
import { kickWobble, moteDensity, bumpCount } from './kartPose.js';
import { setLensCamera } from './lens.js';
import { racerLabels } from '../hud/format.js';
import { createMusicRoad } from './musicRoad.js';
import { createWordBubbles } from './wordBubbles.js';
import { createWordPopFx } from './wordPopFx.js';
import { createRoadWords } from './roadWords.js';
import { createNearDressing } from './nearDressing.js';
import { createWordRush } from './wordRush.js';
import { createWaterfall } from './waterfall.js';
import { buildWaterVolume } from './waterVolume.js';
import { waterTable, tableCode } from './waterMath.js';
import { buildStones } from './stones.js';
import { buildMainframe } from './mainframe.js';
import { buildKeepDressing, applyKeepRoad } from './keepDressing.js';
import { createKeepGateView } from './keepGates.js';
import { createKeepChainView } from './keepChains.js';
import { keepSpiralLayout } from './keepSpirals.js';
import { keepMediaLayout } from './keepMedia.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { createKeepDrawbridge } from './keepDrawbridge.js';
import { createKeepBackdrop } from './keepBackdrop.js';

const FWD = new Vector3();
const P4_EVENTS = new Set(['drift-start', 'drift-tier', 'drift-boost', 'trick', 'trick-land']);
// the SCREENFX lane's item visuals; missing or broken, the race runs without them
const itemsMod = await import('./items/visuals.js').catch((e) => { console.warn('[glassroad/world] no item visuals: ' + (e && e.message)); return null; });
// polish 3 layers, loaded the same way: missing or broken, the race runs without them
const spiralsMod = await import('./spirals3d.js').catch((e) => { console.warn('[glassroad/world] no spiral gates: ' + (e && e.message)); return null; });
const routesMod = await import('./routes.js').catch((e) => { console.warn('[glassroad/world] no route marks: ' + (e && e.message)); return null; });
const mediaMod = await import('./media.js').catch((e) => { console.warn('[glassroad/world] no media slots: ' + (e && e.message)); return null; });
// Layouts load with the module so every material exists before the renderer warms.
const SPIRAL_LAYOUTS = new Map();
if (spiralsMod && globalThis.location) SPIRAL_LAYOUTS.set('threshold', await spiralsMod.fetchSpiralLayout('threshold'));
if (spiralsMod && globalThis.location?.search?.includes('mainframe')) SPIRAL_LAYOUTS.set('mainframe', await spiralsMod.fetchSpiralLayout('mainframe'));
const MAINFRAME_MEDIA_LAYOUT = mediaMod && globalThis.location?.search?.includes('mainframe')
  ? await mediaMod.loadMediaLayout('mainframe') : null;

/** Track MeshData -> Mesh, or null when the data is empty. */
export function meshFrom(d, material) {
  if (!d || !d.indices || !d.indices.length) return null;
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(d.positions, 3));
  g.setAttribute('normal', new BufferAttribute(d.normals, 3));
  g.setAttribute('uv', new BufferAttribute(d.uvs, 2));
  // water (wave 1): the road carries `surface` (0 glass .. 3 stream, 4 pool) and `flow` (m/s along, lateral)
  // per vertex; a mesh without them reads as glass with no current (zero filled so the shader always binds)
  const n = d.positions.length / 3;
  g.setAttribute('surface', new BufferAttribute(d.surface && d.surface.length === n ? d.surface : new Float32Array(n), 1));
  g.setAttribute('flow', new BufferAttribute(d.flow && d.flow.length === n * 2 ? d.flow : new Float32Array(n * 2), 2));
  g.setIndex(new BufferAttribute(d.indices, 1));
  g.computeBoundingSphere();
  return new Mesh(g, material);
}

/** Merge [geometry, hex] pairs into one non-indexed geometry with a `color` attribute. */
function merge(parts) {
  const pos = [], nor = [], col = [], c = new Color();
  for (const [geo, hex] of parts) {
    const g = geo.index ? geo.toNonIndexed() : geo;
    c.set(hex);
    pos.push(...g.attributes.position.array); nor.push(...g.attributes.normal.array);
    for (let i = 0; i < g.attributes.position.count; i++) col.push(c.r, c.g, c.b);
  }
  const out = new BufferGeometry();
  out.setAttribute('position', new Float32BufferAttribute(pos, 3));
  out.setAttribute('normal', new Float32BufferAttribute(nor, 3));
  out.setAttribute('color', new Float32BufferAttribute(col, 3));
  return out;
}

/** Greybox teacup on a saucer with an EMI placeholder bust. Origin = saucer base centre, forward -Z. */
function kartGeometry() {
  const R = LOOK.saucerR, v = (x, y) => new Vector2(x * R, y * R);
  const saucer = new LatheGeometry([v(0, 0), v(0.45, 0), v(1, 0.1), v(0.97, 0.16), v(0.5, 0.12), v(0, 0.12)], 32);
  const cup = new LatheGeometry([v(0, 0.12), v(0.42, 0.12), v(0.7, 0.55), v(0.78, 1.1), v(0.72, 1.1), v(0.64, 0.6), v(0, 0.3)], 32);
  const handle = new TorusGeometry(0.22 * R, 0.06 * R, 8, 16, Math.PI).rotateY(Math.PI / 2).translate(0, 0.7 * R, 0.78 * R);
  const torso = new CylinderGeometry(0.16 * R, 0.3 * R, 0.6 * R, 14).translate(0, 1.1 * R, 0);
  const head = new SphereGeometry(0.28 * R, 18, 12).translate(0, 1.62 * R, 0);
  const eye = (x) => new SphereGeometry(0.05 * R, 8, 6).translate(x * R, 1.68 * R, -0.25 * R);
  return merge([[saucer, LOOK.saucerColour], [cup, LOOK.cupColour], [handle, LOOK.cupColour],
    [torso, LOOK.emiColour], [head, LOOK.emiColour], [eye(-0.1), LOOK.faceColour], [eye(0.1), LOOK.faceColour]]);
}

function props(track) {
  const geo = new IcosahedronGeometry(1, 0), { material } = createPropMaterial(LOOK.propColour);
  const n = LOOK.props, im = new InstancedMesh(geo, material, n), o = new Object3D();
  let seed = 7;
  const rnd = (a) => { seed = (seed * 16807) % 2147483647; return a[0] + (seed / 2147483647) * (a[1] - a[0]); };
  for (let i = 0; i < n; i++) {
    const f = track.frameAt((i + 0.5) / n * track.lengthM), side = i & 1 ? 1 : -1, off = f.w / 2 + rnd(LOOK.propSideM);
    const up = rnd(LOOK.propUpM);
    o.position.set(f.p[0] + f.b[0] * side * off + f.n[0] * up, f.p[1] + f.b[1] * side * off + f.n[1] * up, f.p[2] + f.b[2] * side * off + f.n[2] * up);
    o.rotation.set(rnd([0, 6.28]), rnd([0, 6.28]), 0);
    o.scale.setScalar(rnd(LOOK.propSizeM));
    o.updateMatrix(); im.setMatrixAt(i, o.matrix);
  }
  return im;
}

/**
 * Polish 2 opts: renderer (createRenderer's wrapper: the shadow map and its tier hook), shadowsOn (the
 * Shadows option), photosafe (fx/photosafe.js, the impact glows ask it). Returns { ..., shadows, colossi }.
 */
export function buildWorld(track, feel, { art = null, bus = null, seed = 1, count = 8, renderer = null, shadowsOn = true, photosafe = null, spiralLayout = SPIRAL_LAYOUTS.get(track.id), looks = [] } = {}) {
  const scene = new Scene();
  const isMainframe = track.id === 'mainframe';
  // Fog stays in the warmed shader; its density eases as the kart dives into the coolant reservoir.
  const submerged = track.sections.find(s => s.id === 'submerged');
  if (isMainframe) scene.fog = new FogExp2('#063e49', 0);
  const keep = track.id === 'keep', pal = createPalette(track.id);
  const owned = [], offs = [];
  const add = (m) => { if (m) { scene.add(m); owned.push(m); } return m; };

  let sun = null;
  if (art) {
    // the glTF props and kart are lit (standard / physical materials); everything else is unlit
    const L = isMainframe ? { ...LOOK.artLight, sky: '#d1ffe8', ground: '#142c31' } : LOOK.artLight;
    scene.add(new HemisphereLight(L.sky, L.ground, L.hemi));
    sun = new DirectionalLight(L.sky, L.sun);
    sun.position.set(LOOK.lightDir[0], LOOK.lightDir[1], LOOK.lightDir[2]);
    scene.add(sun);
  }
  const sky = createSkyMaterial(pal, art);
  const skyMesh = add(new Mesh(new SphereGeometry(LOOK.skyRadiusM, 48, 32), sky.material));
  skyMesh.renderOrder = -1; skyMesh.frustumCulled = false; skyMesh.visible = !keep;
  const keepBackdrop = keep ? createKeepBackdrop(scene, track) : null;
  const keepDrawbridge = keep ? createKeepDrawbridge(scene, track, art) : null;
  const road = createRoadMaterial(pal, art, track);
  if (keep) applyKeepRoad(road, art);
  const roadMesh = add(meshFrom(track.meshes.road, road.material));
  // Built before warmup: shared static construction gives the glass road depth.
  const showcaseDressing = keep ? null : buildShowcaseDressing(track, pal, art);
  if (showcaseDressing) scene.add(showcaseDressing.group);
  const court = isMainframe ? null : buildCourt(track, pal);
  if (court) scene.add(court.group);
  // wave 2: the stepping stones of every field (render/stones.js), in the scene before the warm so their
  // materials link behind the card (Firefox links programs one by one)
  let stones = null;
  try { stones = buildStones(track, art, pal); scene.add(stones.group); } catch (e) { console.warn('[glassroad/world] stones failed: ' + (e && e.message)); }
  const rails = createRailMaterial(pal, art);
  add(meshFrom(track.meshes.rails, rails.material));
  // EDGES lane: open edges (hazard strip, lip, haze below) and rail end posts, one draw (render/edges.js)
  let edges = null;
  try { edges = createEdges(scene, track, pal, { pulse: rails.uniforms.pulse }); } catch (e) { console.warn('[glassroad/world] edges failed: ' + (e && e.message)); }
  const pad = createPadMaterial(isMainframe ? '#75f8a7' : LOOK.padColour, art && art.tex('boost-chevron-mask')), lip = createPadMaterial(LOOK.lipColour);
  const padMesh = add(meshFrom(track.meshes.pads, pad.material));
  add(meshFrom(track.meshes.jumps, lip.material));
  // the hero colossi lean over the road; the scatter keeps clear of them (WORLD lane)
  const samples = sampleTrack(track, 4);
  const mainframe = isMainframe ? buildMainframe(track, art, { samples, mediaLayout: MAINFRAME_MEDIA_LAYOUT }) : null;
  if (mainframe) scene.add(mainframe.group);
  const keepDressing = keep ? buildKeepDressing(track, art, pal, samples) : null;
  if (keepDressing) scene.add(keepDressing.group);
  const keepGates = keep ? createKeepGateView(scene, track, art, { gates: getKeepLayout(track).gates }) : null;
  const keepChains = keep ? createKeepChainView(scene, track, { chains: getKeepLayout(track).chains }) : null;
  let colossi = null;
  try { colossi = art && !keep && !isMainframe ? buildColossi(art, track, pal, { samples }) : null; } catch (e) { console.warn('[glassroad/world] colossi failed: ' + (e && e.message)); }
  if (colossi) add(colossi.object);
  const scatter = (keep || isMainframe) ? null : buildScatter(art, track, seed, { pal, avoid: colossi ? colossi.placed : [], samples });
  if (!keep && !isMainframe) add(scatter ? scatter.object : props(track));
  if (!keep && !isMainframe) add(createNearDressing(track, { phone: Q.name === 'phone' }).object);
  let terrace = null, terraceScreen = null;
  try {
    if (!keep && !isMainframe && art && art.model(Q.name === 'phone' ? 'threshold-slot-row-lod1' : 'threshold-slot-row-lod0')) {
      terraceScreen = reelTexture();
      terrace = buildTerrace(art, track, pal, { phone: Q.name === 'phone', samples, screenMap: terraceScreen,
        avoid: [...(colossi ? colossi.placed : []), ...(scatter && scatter.heroes ? scatter.heroes.bodies : []), ...(scatter ? scatter.placed : [])] });
      scene.add(terrace.object);
    }
  } catch (e) { console.warn('[glassroad/world] terrace failed: ' + (e && e.message)); }
  const countdown = buildCountdown(art, track, pal), finish = buildFinish(art, track, pal);
  if (countdown) {
    add(countdown.object);
    if (bus) {
      offs.push(bus.on('countdown', ({ n = 3 } = {}) => countdown.pulse(n, feel.get('juice.countdownPulses'))));
      offs.push(bus.on('go', () => countdown.go()));
    }
  }
  if (finish) add(finish.object);

  // bot looks from the race seed; online each seat's { outfit, vehicle } (B2: boot calls setLook per seat on the room's start)
  const karts = buildKarts(art, count, art && art.model('emi-teacup-kart') ? null : kartGeometry(), { seed, looks });
  for (const k of karts.list) add(k.root);
  const kartObjs = karts.list.map((k) => k.root);
  const kart = kartObjs[0];
  const ring = createGlowMaterial(LOOK.ringColour);
  const ringMesh = new Mesh(new RingGeometry(LOOK.saucerR * 0.9, LOOK.saucerR * 1.1, 48).rotateX(-Math.PI / 2), ring.material);
  ringMesh.position.y = 0.03; kart.add(ringMesh);
  const tendril = createGlowMaterial(LOOK.tendrilColour);
  const tendrilMesh = add(new Mesh(new CylinderGeometry(0.15, 0.6, 1, 12, 1, true).translate(0, 0.5, 0), tendril.material));
  // water (wave 1): per-metre surface codes so a wake laid on water goes wider and whiter with one table read
  const water = waterTable((s) => track.frameAt(s).surface, track.lengthM);
  const waterAt = (s) => tableCode(water, s, track.lengthM);
  const particles = createParticles(feel, art), lines = createSpeedLines(feel), wakes = createWakes(feel, count, undefined, { waterAt }), motes = createMotes(pal, seed);
  add(particles.object); add(lines.object); add(motes.object);
  for (const o of wakes.objects) add(o);
  // the chute's waterfall sheet, spray and splash ring (render/waterfall.js; in the scene before the warm)
  let waterfall = null;
  try { waterfall = createWaterfall(track, pal, { particles, wakes }); if (waterfall.object) add(waterfall.object); } catch (e) { console.warn('[glassroad/world] waterfall failed: ' + (e && e.message)); }
  const waterVolume = buildWaterVolume(track, pal, art);
  add(waterVolume.object);
  // polish 4: glowing skid ribbons under a drift (one pooled draw call for every kart)
  const skids = createSkids(feel, count);
  add(skids.object);
  // reactions (REACT lane): bumps, walls, the countdown glance, finishes and the head sprays
  karts.attach({ bus, particles });
  // the lamp's soft shadows: colossi, karts and props onto the road and pads; blobs on the phone tier
  let shadows = null;
  if (renderer) {
    try {
      shadows = buildShadows(renderer, scene, sun, Q, { enabled: shadowsOn, track, samples, receivers: [roadMesh, padMesh].filter(Boolean),
        casters: keepDressing ? keepDressing.casters : colossi ? colossi.casters : [], scatterCasters: scatter ? [scatter.object] : [], karts: kartObjs, colossi: colossi ? colossi.placed : [] });
    } catch (e) { console.warn('[glassroad/world] shadows failed: ' + (e && e.message)); }
    if (shadows && renderer.onTier) offs.push(renderer.onTier(() => shadows.retier()));
  }
  // polish 3: the Loom spiral gates across the road and the media slots (placeholders only)
  let spirals = null, media = null;
  // onPass: the player crossed a gate; boot plays that gate's own Loom on screen
  try { spirals = spiralsMod ? spiralsMod.createSpiralGates(scene, track, { layout: keep && spiralLayout !== null ? keepSpiralLayout(track) : spiralLayout, photosafe, feel,
    onPass: (g) => { if (bus) bus.emit('gate-pass', g); } }) : null; } catch (e) { console.warn('[glassroad/world] spiral gates failed: ' + (e && e.message)); }
  try { media = mediaMod ? mediaMod.createMediaSlots(scene, track, { feel, samples, layout: keep ? keepMediaLayout(track) : undefined }) : null; } catch (e) { console.warn('[glassroad/world] media slots failed: ' + (e && e.message)); }
  // polish 4: the drop shortcut's rail opening and landing pad (track routes); the ring swells on the player's clean landing
  let routes = null;
  try { routes = routesMod ? routesMod.createRoutes(scene, track, { photosafe }) : null; } catch (e) { console.warn('[glassroad/world] route marks failed: ' + (e && e.message)); }
  if (routes && bus) offs.push(bus.on('route-land', (p) => routes.land(p || {})));
  const kartPos = [];
  let itemVis = null;
  try { itemVis = itemsMod ? itemsMod.createItemVisuals(scene, art, feel, { photosafe, backend: renderer ? renderer.backend : '' }) : null; } catch (e) { console.warn('[glassroad/world] item visuals failed: ' + (e && e.message)); }
  // the music road (handoff-1022/MUSIC-ROAD-BRIEF.md): the road, rails and helix answer the song; the
  // player's own word bubbles ride the road. No song: both rest and the race looks as before
  let musicRoad = null, wordVis = null;
  try { musicRoad = createMusicRoad({ road, rails, track, pal, photosafe, scene }); } catch (e) { console.warn('[glassroad/world] music road failed: ' + (e && e.message)); }
  try { wordVis = createWordBubbles(scene, { feel, photosafe }); } catch (e) { console.warn('[glassroad/world] word bubbles failed: ' + (e && e.message)); }
  // pass 4: the ROAD words painted on the track like road markings (render/roadWords.js, in the scene from boot for the warm)
  let roadWords = null;
  try { roadWords = createRoadWords(scene, { track, feel, photosafe }); } catch (e) { console.warn('[glassroad/world] road words failed: ' + (e && e.message)); }
  // a word taken: the pop burst, a ring under the kart and light streaks (render/wordPopFx.js, on `word-pop`)
  let wordFx = null;
  try { wordFx = createWordPopFx(scene, { particles, wakes, kart, bus, feel, photosafe }); } catch (e) { console.warn('[glassroad/world] word pop fx failed: ' + (e && e.message)); }
  // pass 4: the popped word rushes at the camera (render/wordRush.js, a DOM layer under the cards, on `word-pop`)
  let wordRush = null;
  try { wordRush = globalThis.document ? createWordRush({ bus, feel }) : null; } catch (e) { console.warn('[glassroad/world] word rush failed: ' + (e && e.message)); }

  // positional juice from the bus, applied in the next update (bus events flush before world.update)
  const pending = [];
  if (bus) {
    const on = (name, fn) => offs.push(bus.on(name, (p) => pending.push(fn(p || {}))));
    on('land', ({ tier = 0 }) => ['land', 0, tier]);
    // polish 4 drift and trick moments go to the particle layer with their payloads; the player's arrive bare
    for (const n of P4_EVENTS) on(n, (p) => ['p4', 0, n, p]);
    // Go bursts at the player's own kart (the countdown card is the HUD half of it)
    on('go', () => ['p4', 0, 'go', {}]);
    on('racer-event', ({ racer = -1, name, p = {} }) => (P4_EVENTS.has(name) ? ['p4', racer, name, p] : [name === 'water-hop' ? 'splash' : name, racer, (p && p.tier) || 0, p]));
    on('bump', (p) => ['bump', p.a, p.dv || 0, p.p, p.b]);
    on('bubble-ram', (p) => ['ram', p.attacker, 0, p.p, p.victim]);
    on('scan-hit', (p) => ['machine', p.racer, p.effect === 'boost' ? 1 : 0]);
    on('industry-hit', (p) => ['machine', p.racer, 0]);
    on('industry-boost', (p) => ['machine', p.racer, 1]);
    for (const name of ['keep-gate-hit', 'keep-chain-hit']) on(name, () => ['bump', 0, 12]);
    on('bubble-pop', (p) => ['pop', p.racer, 0, p.p]);
    on('item-box', (p) => ['box', p.racer, 0, p.p]);
    on('item-hit', (p) => ['hit', p.victim, p.verdict, p.p, p.kind]);
    // water (CONTRACT splash / chute): the ring, drops and lip spray; inert until the sim emits them
    on('splash', (p) => ['splash', 0, 0, p]);
    on('water-hop', (p) => ['splash', 0, 0, p]);
    on('chute', (p) => ['chute', 0, 0, p]);
    // stepping stones (BRIEF-STONES stone / void): a small ring under the player on a stone; the void is camera only
    on('stone', (p) => ['stone', 0, 0, p]);
    on('void', (p) => ['void', 0, 0, p]);
  }
  const posOf = (i) => { const o = kartObjs[i]; return o ? [o.position.x, o.position.y, o.position.z] : null; };
  function juice(e, gentle) {
    const [kind, i, a, p, b] = e, k = karts.list[i], obj = kartObjs[i], few = gentle ? 0.6 : 1;
    if (kind === 'splash') { if (waterfall) waterfall.splash(p || {}, gentle); return; }
    if (kind === 'chute') { if (waterfall) waterfall.chute(p || {}, gentle); return; }
    if (kind === 'stone') {
      const deck = stones?.hit(p);
      if (obj && obj.visible) {
        const near = i === 0 || obj.position.distanceTo(kart.position) < Q.kartLodM;
        wakes.ring(posOf(i), obj.quaternion, LOOK.ringMaxM[deck ? 1 : 0], { gentle, gain: LOOK.ringGain * (deck ? .8 : .4) });
        if (deck && near) {
          particles.dust(obj, gentle ? 1 : 2);
          particles.burst(posOf(i), (i ? 14 : 28) * few, { colours: track.id === 'mainframe' ? ['#50eeb2', '#e1fff4', '#45bacb'] : ['#ed3e9d', '#e8d6c0', '#b68ba5'],
            speed: gentle ? 1.4 : 3.8, sizeM: .18, lifeS: .9, sparkle: .3, up: 1.2, g: .5, near: i === 0 });
        }
      }
      return;
    }
    if (kind === 'void') return;
    if (kind === 'machine') {
      if (!obj || !obj.visible) return;
      particles.burst(posOf(i), Math.round(36 * few), { colours: a ? ['#5cff9d','#c0ffe0','#40dbc5'] : ['#ffd04e','#ef9b30','#fff1b8'],
        speed: a ? 5 : 3, up: 1.8, sizeM: .16, lifeS: .8, sparkle: .7, near: i === 0 });
      wakes.ring(posOf(i), obj.quaternion, a ? 3.2 : 2, { gentle, gain: .65 });
      return;
    }
    if (kind === 'ram') {
      particles.burst(p, Math.round(42*few), { colours: ['#adfff1','#5cd8ff','#ffffff'], speed: 9, up: 3, sparkle: 0.8, near: i===0 || b===0 });
      if(obj) wakes.ring(p,obj.quaternion,3.5,{gentle,gain:0.8});
      return;
    }
    if (kind === 'p4') {
      if (k && obj && obj.visible) {
        particles.event(obj, k.mem, a, p, { mul: i ? LOOK.botSparkMul : 1 });
        if (a === 'trick-land') wakes.ring(posOf(i), obj.quaternion, LOOK.ringMaxM[2], { gentle });
        else if (a === 'go') wakes.ring(posOf(i), obj.quaternion, LOOK.ringMaxM[1], { gentle });
      }
      return;
    }
    // squash and stretch on hops and landings belong to the reaction layer (karts.js stepReact)
    if (kind === 'land' && a >= 1 && k && obj.visible) {
      particles.dust(obj, a);
      wakes.ring(posOf(i), obj.quaternion, LOOK.ringMaxM[Math.min(a, 3) - 1], { gentle });
    } else if (kind === 'bump') {
      // near: the player is in it, so the burst sits right under the camera (half the count and the stars)
      particles.burst(p || posOf(i), Math.round(bumpCount(a, LOOK) * few), { colours: LOOK.bumpColours, speed: LOOK.bumpSpeedMps, sparkle: 0.7, near: i === 0 || b === 0 });
    }
    else if (kind === 'pop') {
      particles.burst(p || posOf(i), Math.round(LOOK.popCount * few), { colours: LOOK.popColours, speed: LOOK.popSpeedMps, sizeM: LOOK.popSizeM, petal: 0.3, sparkle: 0.3, g: 0.15 });
      if (obj && obj.visible) wakes.ring(p || posOf(i), obj.quaternion, LOOK.ringMaxM[0], { gentle, gain: LOOK.ringGain * 0.6 });
    } else if (kind === 'box') particles.burst(p || posOf(i), Math.round(LOOK.boxCount * few), { colours: LOOK.boxColours, speed: 4, sparkle: 0.8, up: 2 });
    else if (kind === 'hit' && k) {
      if (a === 'hit') kickWobble(k.pose, LOOK, gentle, b === 'shatter' ? 0.5 : 1);
      // atlas sprays first (frost, stars, drips, glass pops); the plain burst stays the fallback
      if (particles.hit(p || posOf(i), a === 'hit' ? b : a, { few, near: i === 0 })) { /* sprayed */ }
      else if (a === 'glass' || a === 'golden' || a === 'decoy') {
        particles.burst(p || posOf(i), Math.round(14 * few), { colours: a === 'golden' ? LOOK.boxColours : LOOK.popColours, speed: 3, sparkle: 0.8 });
      }
    }
  }

  let lastT = null, padPhase = 0, eyeSet = false;
  const fwd = [0, 0, -1], head = [0, 0, -1], eye = [0, -1, 0], eR = [1, 0, 0], eU = [0, 0, -1], q = [0, 0, 0, 1], F = [0, 0, -1], UP = [0, 1, 0];
  /** The spiral eye: tilted from the nadir toward a slow (skyEyeTcS) flat heading, below the horizon ahead. */
  function aimEye(k, dt) {
    q[0] = k[S.quatX]; q[1] = k[S.quatY]; q[2] = k[S.quatZ]; q[3] = k[S.quatW];
    qrotate(fwd, q, F); fwd[1] = 0;
    if (Math.hypot(fwd[0], fwd[2]) < 1e-3) return;
    v3norm(fwd, fwd);
    const a = eyeSet ? Math.min(1, dt / LOOK.skyEyeTcS) : 1;
    eyeSet = true;
    head[0] += (fwd[0] - head[0]) * a; head[2] += (fwd[2] - head[2]) * a; head[1] = 0; v3norm(head, head);
    const tilt = LOOK.skyEyeTiltDeg * Math.PI / 180;
    eye[0] = head[0] * Math.sin(tilt); eye[1] = -Math.cos(tilt); eye[2] = head[2] * Math.sin(tilt);
    v3norm(eR, v3cross(eR, eye, UP)); v3cross(eU, eR, eye);
    sky.uniforms.eye.value.set(eye[0], eye[1], eye[2]);
    sky.uniforms.eyeR.value.set(eR[0], eR[1], eR[2]);
    sky.uniforms.eyeU.value.set(eU[0], eU[1], eU[2]);
  }
  const secs = track.sections;
  const sec = (i) => secs[clamp(i | 0, 0, secs.length - 1)];

  const solo = [{ st: null, meta: null, fxv: null }];
  // tag names match the HUD toasts, minimap and results (hud/format.js racerLabels); rebuilt only
  // when the racer list changes
  let labelsOf = null, labels = [];
  function update(view) {
    if (keepGates) keepGates.update(view.raceTimeS ?? 0);
    if (keepChains) keepChains.update(view.raceTimeS ?? 0);
    const { kart: k, fx } = view;
    solo[0].st = k;
    const racers = view.karts && view.karts.length ? view.karts : solo;
    if (labelsOf !== racers || labels.length !== racers.length) {
      labelsOf = racers;
      labels = racerLabels(racers.map((r, i) => (r && r.meta) || { i, name: '', isBot: i > 0 }));
    }
    const dt = lastT === null ? 0 : clamp(view.timeS - lastT, 0, 0.1);
    lastT = view.timeS;
    if (isMainframe && submerged && !view.paused) {
      const emerge = track.sections.find(s => s.id === 'emerge');
      const inside = k[S.s] >= submerged.fromS && k[S.s] <= (emerge?.toS ?? submerged.toS);
      const depth = (track.reservoir?.waterY ?? 4) - (view.camera?.position.y ?? k[S.posY] + 3);
      const within = inside ? smoothstep(-0.6, 5, depth) : 0;
      const target = 0.013 * within;
      scene.fog.density += (target - scene.fog.density) * Math.min(1, dt / 0.5);
    }
    fx.submerged = isMainframe && scene.fog ? scene.fog.density / 0.013 : 0;
    setLensCamera(view.camera);   // the lens rule's view height (render/lens.js) follows the rendering camera
    const gentle = !!feel.get('gentle.on');
    const scroll = gentle ? feel.get('gentle.scrollMul') : 1, contrast = gentle ? feel.get('gentle.contrastMul') : 1;
    // the road and rail patterns stream toward the player at a speed from the kart (spectrum.js flowMps)
    pal.drive({ speedMps: Math.max(0, k[S.speedPct] || 0) * feel.get('speed.topMps'), gentle, paused: !!view.paused });

    // karts: pose every racer, hide the unused slots; distance to the player picks rival LOD and tags
    const topMps = feel.get('speed.topMps');
    for (let i = 0; i < karts.list.length; i++) {
      const r = racers[i], kk = karts.list[i];
      if (!r || !r.st || (r.meta && r.meta.left)) { karts.hide(kk); continue; }   // online: a seat that left is removed
      const d = i ? Math.hypot(r.st[S.posX] - k[S.posX], r.st[S.posY] - k[S.posY], r.st[S.posZ] - k[S.posZ]) : 0;
      karts.pose(kk, r.st, r.fxv, dt, d, { glide: !!r.glide, squash: i ? null : fx.squash || 0, gentle, topMps, name: r.meta ? labels[i] || r.meta.name : '' });
      kk.root.updateMatrixWorld();
      const near = i === 0 || d <= Q.kartLodM;
      if (near) particles.kart(kk.root, r.st, kk.mem, dt, { mul: i ? LOOK.botSparkMul : 1, slip: (r.fxv && r.fxv.slip) || 0 });
      wakes.kart(i, kk.root, r.st, dt, near);
      if (waterfall) waterfall.kart(i, r.st, dt, { gentle, visible: near, paused: !!view.paused });
      skids.kart(i, kk.root, r.st, near);
    }
    skids.update(dt, !!view.paused);
    for (const e of pending) juice(e, gentle);
    pending.length = 0;
    if (stones) stones.update(dt, { gentle, paused: !!view.paused });

    const pm = fx.paletteMix || { from: view.sectionIdx, to: view.sectionIdx, t: 1 };
    // the from side is a weighted blend when a gate came mid-fade (fx/reactions.js shownWeights)
    const fromW = pm.base ? Object.entries(pm.base).map(([k, w]) => [sec(+k), w]) : [[sec(pm.from), 1]];
    const b = sec(pm.to), t = clamp(pm.t, 0, 1);
    const mixOf = (get) => lerp(fromW.reduce((acc, [x, w]) => acc + get(x) * w, 0), get(b), t);
    pal.set(fromW.map(([x, w]) => [x.palette, w]), b.palette, t);
    const gw = [0, 0, 0], gi = (id) => Math.max(0, SKY_PALETTES.indexOf(id));
    for (const [x, w] of fromW) gw[gi(x.palette)] += w * (1 - t);
    gw[gi(b.palette)] += t;
    sky.uniforms.galaxy.value.set(gw[0], gw[1], gw[2]);
    motes.update(dt, kart.position, moteDensity(gw[gi('deep')], LOOK, gentle), scroll);
    const band = mixOf((x) => x.bandSpeed) * (fx.bandSpeedMul || 1) * scroll;
    road.uniforms.phase.value = (road.uniforms.phase.value + dt * LOOK.bandScrollMps * band) % LOOK.bandLenM;
    road.uniforms.contrast.value = contrast;
    road.uniforms.sheen.value = gentle ? LOOK.sheenGentle : 1;
    // water: the ripple and sheet clocks run at the Gentle scroll share (slower ripples, nothing pulses)
    if (road.uniforms.water && !view.paused) road.uniforms.water.t.value += dt * scroll;
    if (waterfall) waterfall.update(dt, { playerPos: kart.position, gentle, paused: !!view.paused, scroll });
    waterVolume.update(dt, { gentle, paused: !!view.paused, scroll });
    keepBackdrop?.update(dt, { gentle, paused: !!view.paused, scroll });
    // the song clock (music.songS) drives the music road; with a song the breath below rides its real beats
    const mf = musicRoad ? musicRoad.update(view.song || null, view.songS, { kartS: k[S.s] || 0, gentle }) : null;
    // beat breath: a soft swell with no step at the beat (rises over breathRise of the beat, falls to
    // the next); under 10% luminance, so it is not a photosafe transition (owner call, see CONTRACT)
    const ph = mf && mf.on ? mf.beatPhase : view.beat.phase, r = LOOK.beatBreathRise;
    const breath = gentle ? 0 : LOOK.beatBreath * (ph < r ? smoothstep(0, 1, ph / r) : ((1 - ph) / (1 - r)) ** 2);
    road.uniforms.bright.value = (fx.bandBright || 0) + breath;

    const skyOf = (x) => SKIES[x.sky] || SKIES[FALLBACK_SKY];
    sky.uniforms.twist.value = mixOf((x) => skyOf(x).twist);
    sky.uniforms.turn.value = (sky.uniforms.turn.value + dt * mixOf((x) => skyOf(x).turnRadps) * scroll) % (Math.PI * 2);
    sky.uniforms.contrast.value = LOOK.skyContrast * contrast;
    sky.uniforms.rescue.value = fx.rescueGlow || 0;
    skyMesh.position.copy(kart.position);
    aimEye(k, dt);

    padPhase = (padPhase + dt * LOOK.padScrollMps * scroll / LOOK.padStripeM) % 1;
    // pad ring: scale from padRing, brightness from the padGlow envelope (photosafe + Gentle rise)
    const pr = fx.padRing || 0, glow = fx.padGlow || 0;
    pad.uniforms.phase.value = lip.uniforms.phase.value = padPhase;
    pad.uniforms.pulse.value = glow;
    ringMesh.visible = pr > 0 && glow > 0;
    ringMesh.scale.setScalar(1 + pr * LOOK.ringMaxM);
    ring.uniforms.opacity.value = glow;

    tendrilMesh.visible = (fx.tendril || 0) > 0;
    tendrilMesh.position.copy(kart.position);
    tendrilMesh.scale.set(1, LOOK.tendrilM, 1);
    tendril.uniforms.opacity.value = (fx.tendril || 0) * LOOK.tendrilGain;

    const turn = LOOK.artRingTurnRadps * scroll;
    if (countdown) countdown.update(dt, turn, gentle);
    if (finish) finish.update(dt, turn * 0.5, gentle);
    if (scatter) scatter.update(dt, scroll);
    if (colossi) colossi.update(dt, scroll);
    if (mainframe) mainframe.update(dt, view, gentle);
    rails.uniforms.flow.value = road.uniforms.phase.value;
    if (shadows && view.camera) { view.camera.layers.enable(CATCH_LAYER); view.camera.getWorldDirection(FWD); shadows.update(view.camera.position, FWD); }

    if (view.camera) {
      kartPos.length = 0;
      for (const o of kartObjs) if (o.visible) kartPos.push(o.position);
      if (spirals) spirals.update(dt, { camera: view.camera, karts: kartPos, player: 0, gentle, paused: !!view.paused });
      if (media) media.update(dt, { camera: view.camera, gentle, scrollMul: scroll, contrastMul: contrast, paused: !!view.paused });
    }
    if (routes) routes.update(dt, { gentle, paused: !!view.paused, scroll });
    if (edges) edges.update(dt, { gentle, paused: !!view.paused });
    particles.update(dt);
    wakes.update(dt);
    lines.update(dt, k, fx, kart);
    if (view.mode === 'menu') lines.object.visible = false;
    if (itemVis && view.items) itemVis.update(view.items, dt, racers, view.camera);   // visuals read racer .st (CONTRACT-B1 view.karts)
    if (wordVis) wordVis.update(view.words || [], view.song || null, dt);
    if (wordFx) wordFx.update(dt, view.camera);
    if (roadWords) roadWords.update(view.song || null, view.songS, k, mf, { gentle, paused: !!view.paused });
    if (wordRush) wordRush.update(dt, !!view.paused);
  }

  function dispose() {
    for (const o of owned) o.traverse((m) => { if (m.geometry) m.geometry.dispose(); if (m.material) m.material.dispose(); });
    particles.dispose(); skids.dispose(); lines.dispose(); wakes.dispose(); motes.dispose(); karts.dispose();
    if (itemVis) itemVis.dispose();
    if (shadows) shadows.dispose();
    if (colossi && colossi.dispose) colossi.dispose();
    if (spirals) spirals.dispose();
    if (media) media.dispose();
    if (routes) routes.dispose();
    if (edges) edges.dispose();
    if (musicRoad) musicRoad.dispose();
    if (wordVis) wordVis.dispose();
    if (wordFx) wordFx.dispose();
    if (roadWords) roadWords.dispose();
    if (wordRush) wordRush.dispose();
    if (waterfall) waterfall.dispose();
    waterVolume.dispose();
    if (showcaseDressing) showcaseDressing.dispose();
    if (keepDressing) keepDressing.dispose();
    if (keepGates) keepGates.dispose();
    if (keepChains) keepChains.dispose();
    if (keepBackdrop) keepBackdrop.dispose();
    if (keepDrawbridge) keepDrawbridge.dispose();
    if (court) court.dispose();
    if (mainframe) mainframe.dispose();
    if (stones) stones.dispose();
    if (terrace) terrace.dispose();
    if (terraceScreen) terraceScreen.dispose();
    for (const off of offs) off();
  }
  return { scene, kart, karts: kartObjs, update, dispose, shadows, colossi, spirals, media, musicRoad, roadWords, wordRush, terrace, court, stones, mainframe, keepDressing, keepGates, keepChains, keepBackdrop, keepDrawbridge,
    // multiplayer looks (ART5 render/wardrobe.js): re-dress seat i with { outfit, vehicle } ids, undefined = the seat's own pick
    setLook: (i, ids) => karts.setLook(i, ids), looks: () => karts.looks(),
    // a restart also wipes the last race's skid marks and word bubbles
    resetReactions: () => { stones?.reset(); karts.resetReactions(); skids.clear(); if (wordVis) wordVis.clear(); if (wordFx) wordFx.clear(); if (roadWords) roadWords.clear(); if (wordRush) wordRush.clear(); } };
}
