// Tile surfaces bend at real contacts; local particles follow each theme.
// Contact timing comes from the same animation events used by dust and sound.
import * as THREE from 'three';
import { onPresentation, boardMotionScale } from '../game/preferences.js';
import { boardThemeKey } from './theme-ids.js';
import { createBoardWaves } from './board-waves.js';

const STYLES = {
  observatory: { index: 0, color: 0x9fcaff, accent: 0xffdf96 },
  conservatory: { index: 1, color: 0xaee8aa, accent: 0xffc2d5 },
  theatre: { index: 2, color: 0xffce79, accent: 0xe76980 },
  arcade: { index: 3, color: 0x62edff, accent: 0xff66cc },
};
const WAVE_COUNT = 8, AMBIENT_COUNT = 48, BURST_COUNT = 192;
const PARTICLE_LIFE = 1.45, WAVE_LIFE = 1.15;
const hidden = () => !!globalThis.document?.hidden || !!globalThis.window?.PBP?.isPaused?.();
const particleVertex = `
attribute vec3 aVelocity;
attribute float aBirth;
attribute float aSeed;
uniform float uTime;
uniform float uScale;
uniform float uStyle;
uniform vec3 uColor;
uniform vec3 uAccent;
varying float vAlpha;
varying float vSeed;
varying vec3 vColor;
void main() {
  vSeed = aSeed;
  vColor = mix(uColor, uAccent, step(.6, aSeed));
  vec3 point = position;
  float size = .058;
  if (aBirth < -1.0 && aBirth > -3.0) {
    point.x += sin(uTime * .23 + aSeed * 17.0) * .13;
    point.z += cos(uTime * .19 + aSeed * 31.0) * .13;
    point.y += sin(uTime * .31 + aSeed * 29.0) * .10;
    vAlpha = .34;
    size = .085 + aSeed * .065;
  } else {
    float age = uTime - aBirth;
    float phase = age / 1.45;
    if (phase < 0.0 || phase >= 1.0) {
      gl_Position = vec4(2.0,2.0,2.0,1.0); gl_PointSize=0.0; vAlpha=0.0; return;
    }
    point += aVelocity * (1.0 - pow(1.0 - phase, 2.0));
    point.y += sin(phase * 3.14159) * (.13 + aSeed * .18);
    if (uStyle > .5 && uStyle < 1.5) {
      point.x += sin(phase * 7.0 + aSeed * 13.0) * sin(phase * 3.14159) * .10;
      point.y += sin(phase * 3.14159) * .12;
    } else if (uStyle > 2.5) {
      point.xz = floor(point.xz * 12.0 + .5) / 12.0;
    }
    vAlpha = smoothstep(0.0, .30, age) * pow(1.0 - phase, .85) * .88;
    size = .13 + aSeed * .10;
  }
  vec4 mv = modelViewMatrix * vec4(point,1.0);
  gl_Position = projectionMatrix * mv;
  gl_PointSize = min(18.0, size * uScale / max(.1,-mv.z));
}`;
const particleFragment = `
uniform float uStyle;
varying float vAlpha;
varying float vSeed;
varying vec3 vColor;
void main() {
  vec2 p = gl_PointCoord - .5;
  float shape;
  if (uStyle < .5) {
    float cross = min(abs(p.x),abs(p.y)) + max(abs(p.x),abs(p.y)) * .16;
    shape = 1.0-smoothstep(.055,.11,cross);
  } else if (uStyle < 1.5) {
    float angle = vSeed * 6.28318;
    p = mat2(cos(angle),-sin(angle),sin(angle),cos(angle))*p;
    shape = 1.0-smoothstep(.27,.38,length(p*vec2(1.8,.8)));
  } else if (uStyle < 2.5) {
    float angle = vSeed * 6.28318;
    p = mat2(cos(angle),-sin(angle),sin(angle),cos(angle))*p;
    shape = 1.0-smoothstep(.27,.38,max(abs(p.x)*1.5,abs(p.y)));
  } else {
    float square = max(abs(p.x),abs(p.y));
    shape = (1.0-smoothstep(.32,.40,square))*smoothstep(.16,.23,square);
  }
  float alpha = shape*vAlpha;
  if (alpha < .003) discard;
  gl_FragColor = vec4(vColor,alpha);
}`;

function squarePoint(square) {
  if (!/^[a-h][1-8]$/.test(square || '')) return null;
  return { x: square.charCodeAt(0) - 100.5, y: 0, z: 4.5 - Number(square[1]) };
}

export function createThemeEffects({ view, bus, pieces }) {
  let theme = null, clock = 0, cursor = 0, waveCursor = 0, bursts = 0, disposed = false;
  let reduced = false, running = true;
  let tracked = new WeakMap();
  const off = [], group = new THREE.Group();
  group.name = 'pbp-theme-effects';
  group.visible = false;
  view.scene.add(group);
  const waves = Array.from({ length: WAVE_COUNT }, () => new THREE.Vector4(0,0,-100,0));
  const uniforms = {
    uWaves: { value: waves }, uTime: { value: 0 }, uStyle: { value: 0 },
    uColor: { value: new THREE.Color() }, uAccent: { value: new THREE.Color() }, uScale: { value: 400 },
  };
  const count = AMBIENT_COUNT + BURST_COUNT;
  const geometry = new THREE.BufferGeometry();
  const position = new Float32Array(count * 3), velocity = new Float32Array(count * 3);
  const birth = new Float32Array(count).fill(-100), seed = new Float32Array(count);
  for (let i=0; i<count; i++) {
    seed[i] = ((i*73+17)%199)/199;
    if (i >= AMBIENT_COUNT) continue;
    const edge = i%4, along = ((i*17)%47)/47*8.8-4.4;
    position.set([edge<2 ? along : (edge===2 ? -4.55 : 4.55), .20+seed[i]*.75,
      edge<2 ? (edge===0 ? -4.55 : 4.55) : along], i*3);
    birth[i] = -2;
  }
  geometry.setAttribute('position', new THREE.BufferAttribute(position,3));
  geometry.setAttribute('aVelocity', new THREE.BufferAttribute(velocity,3));
  geometry.setAttribute('aBirth', new THREE.BufferAttribute(birth,1));
  geometry.setAttribute('aSeed', new THREE.BufferAttribute(seed,1));
  const points = new THREE.Points(geometry, new THREE.ShaderMaterial({
    uniforms, vertexShader: particleVertex, fragmentShader: particleFragment,
    transparent: true, depthWrite: false,
  }));
  points.frustumCulled = false;
  points.raycast = () => {};
  group.add(points);
  const light = new THREE.PointLight(0xffffff,0,3.5,2);
  light.position.y = .7;
  group.add(light);

  const boardWaves = createBoardWaves({ view, pieces, waves, life: WAVE_LIFE });

  function reset() {
    boardWaves.reset();
    for (const wave of waves) wave.set(0,0,-100,0);
    birth.fill(-100,AMBIENT_COUNT);
    geometry.attributes.aBirth.needsUpdate = true;
    light.intensity = 0;
    tracked = new WeakMap();
  }
  function react(event) {
    if (!theme || disposed || !event) return false;
    if (reduced || !running || hidden()) {
      // A hidden tab can stop frames while network turns still arrive.
      trackMoves(false);
      return false;
    }
    const at = event.world || squarePoint(event.square);
    if (!at || !Number.isFinite(at.x) || !Number.isFinite(at.z) || Math.abs(at.x)>4 || Math.abs(at.z)>4) return false;
    if (event.skipped || event.refused) return false;
    const gain = Math.max(.15,Math.min(1.5,Number(event.strength)||1));
    // A repeated contact frame cannot double the pool. Separate hops keep their cadence.
    if (waves.some(w => clock-w.z<.09 && Math.hypot(w.x-at.x,w.y+at.z)<.06)) return false;
    waves[waveCursor++ % WAVE_COUNT].set(at.x,-at.z,clock,gain);
    light.position.set(at.x,.7,at.z);
    const amount = Math.round(12*gain);
    for (let n=0; n<amount; n++) {
      const i = AMBIENT_COUNT + cursor++%BURST_COUNT;
      const angle = n/amount*Math.PI*2 + seed[i]*.4;
      position.set([at.x,.025,at.z],i*3);
      const reach = (.22+seed[i]*.5)*gain;
      velocity.set([Math.cos(angle)*reach,.08,Math.sin(angle)*reach],i*3);
      birth[i] = clock;
    }
    geometry.attributes.position.needsUpdate = true;
    geometry.attributes.aVelocity.needsUpdate = true;
    geometry.attributes.aBirth.needsUpdate = true;
    bursts++;
    bus?.emit?.('theme-react', { kind: event.kind, world: { x:at.x,y:0,z:at.z }, side:event.side, strength:gain });
    return true;
  }
  const on = (name, fn) => { const stop = bus?.on?.(name, fn); if (stop) off.push(stop); };
  on('land', p => react({ ...p, kind:'land', strength:p?.capture ? 1.25 : .8 }));
  on('hopLand', p => react({ ...p, kind:'land', strength:p?.small ? .3 : .45 }));
  on('hit', p => react({ ...p, kind:'capture', strength:1.25 }));
  on('contact', p => react({ ...p, kind:'capture', strength:.9 }));
  on('captureAccent', p => react({ ...p, kind:'capture', strength:.65 }));
  on('check', p => {
    const king = [...(pieces?.pieces?.values() || [])].find(piece => piece.userData.type==='k' && piece.userData.side===p?.side);
    if (king) react({ kind:'check', square:king.userData.square, side:p.side, strength:.6 });
  });
  on('gameover', p => { if (p?.winner) react({ kind:'win', world:{x:0,z:0}, side:p.winner, strength:1.4 }); });
  for (const name of ['local','newgame','takeback']) on(name,reset);
  off.push(onPresentation(p => {
    reduced = p.reducedMotion || boardMotionScale(p)===0;
    if (reduced) reset();
  }));
  function trackMoves(animate) {
    for (const piece of pieces?.pieces?.values() || []) {
      const square = piece.userData.square, previous = tracked.get(piece);
      if (animate && previous && square!==previous) react({ kind:'move', square:previous, side:piece.userData.side, strength:.35 });
      tracked.set(piece,square);
    }
  }

  return {
    react,
    setTheme(id) {
      const next = boardThemeKey(id);
      if (disposed || theme===next) return;
      theme = next;
      reset();
      group.visible = !!theme;
      if (!theme) return;
      const style = STYLES[theme];
      uniforms.uStyle.value = style.index;
      uniforms.uColor.value.setHex(style.color);
      uniforms.uAccent.value.setHex(style.accent);
      light.color.setHex(style.color);
    },
    update(dt) {
      if (disposed || !theme) return;
      running = !reduced && !hidden() && dt>0 && Number.isFinite(dt);
      if (!running) { trackMoves(false); return; }
      clock += Math.min(dt,.1);
      uniforms.uTime.value = clock;
      const canvas = view.renderer?.domElement;
      uniforms.uScale.value = (canvas?.height || 800)/2;
      let glow = 0;
      for (const wave of waves) {
        const age = clock-wave.z;
        if (age>=0 && age<WAVE_LIFE) glow = Math.max(glow,Math.min(1,age/.3)*(1-age/WAVE_LIFE)*wave.w);
      }
      light.intensity = glow*.45;
      trackMoves(true);
      boardWaves.update(clock);
    },
    debug() {
      let active = 0;
      for (let i=AMBIENT_COUNT; i<count; i++) if (clock-birth[i]>=0 && clock-birth[i]<PARTICLE_LIFE) active++;
      return { theme, clock, bursts, active, waves:waves.filter(w=>clock-w.z<WAVE_LIFE).length,
        light:light.intensity, surface:boardWaves.debug(), pool:BURST_COUNT, ambient:AMBIENT_COUNT, disposed };
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const stop of off) stop();
      reset();
      view.scene.remove(group);
      boardWaves.dispose();
      geometry.dispose(); points.material.dispose(); light.dispose();
    },
  };
}
