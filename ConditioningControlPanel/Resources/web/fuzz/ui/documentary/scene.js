import * as THREE from '../../../vendor/three/three.module.min.js';
import { createMuseum } from './environment.js';
import { createMuseumProps } from './props.js';
import { createMuseumMaterials } from './materials.js';
import { trackPoint, trackYaw } from './track.js';

const UNIT = 180;
const ART_ROOT = new URL('../../assets/documentary/art/', import.meta.url);
const FALLBACK_ART = ['c_emi-1.webp', 'c_bubbles-1.webp', 'c_emi-2.webp', 'c_bubbles-3.webp'];

function element(doc, tag, className, parent, text) {
  const node = doc.createElement(tag);
  node.className = className;
  if (text != null) node.textContent = text;
  parent.append(node);
  return node;
}

/** Geometry and lighting are WebGL. Every framed picture is a real, display-only DOM image. */
export function createScene(ctx, root, manifest, media, { lite = false, onWake } = {}) {
  const doc = root.ownerDocument;
  const canvas = element(doc, 'canvas', 'td-webgl', root);
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: !lite, alpha: false, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(doc.defaultView.devicePixelRatio || 1, lite ? 1.25 : 1.5));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.15;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const world = new THREE.Scene();
  world.background = new THREE.Color('#26262a');
  world.fog = new THREE.Fog('#34363b', lite ? 30 : 42, lite ? 58 : 78);
  const camera = new THREE.PerspectiveCamera(61, 1, 0.08, 100);
  world.add(camera);
  world.add(new THREE.HemisphereLight('#f1efe8', '#3b3c46', 1.1));
  const key = new THREE.DirectionalLight('#fff1df', 1.6);
  key.castShadow = true;
  key.shadow.mapSize.set(lite ? 1024 : 2048, lite ? 1024 : 2048);
  key.shadow.camera.left = -14; key.shadow.camera.right = 14;
  key.shadow.camera.top = 14; key.shadow.camera.bottom = -14;
  key.shadow.camera.near = 0.1; key.shadow.camera.far = 38;
  key.shadow.bias = -0.0003; key.shadow.normalBias = 0.025;
  world.add(key, key.target);
  const viewport = element(doc, 'div', 'td-viewport', root);
  const domCamera = element(doc, 'div', 'td-camera', viewport);
  const wash = element(doc, 'div', 'td-wash', root);
  const streaks = element(doc, 'div', 'td-streaks', root);
  const instrument = element(doc, 'div', 'td-instrument', root);
  const speed = element(doc, 'strong', '', instrument, '0.0');
  element(doc, 'span', '', instrument, 'metres / second');
  const position = element(doc, 'small', '', instrument, '0 / 580 m');
  const hint = element(doc, 'p', 'td-pump-hint', root, 'Please wait at the platform');
  const specimenButton = element(doc, 'button', 'td-specimen', root, 'Wake Up');
  specimenButton.type = 'button';
  specimenButton.hidden = true;
  specimenButton.setAttribute('aria-label', 'Wake Up museum specimen');
  specimenButton.addEventListener('click', onWake);
  const stageAnchor = element(doc, 'div', 'td-stage-anchor td-plane', domCamera);
  stageAnchor.hidden = true;
  const replay = element(doc, 'div', 'td-replay', root);
  replay.hidden = true;
  const replayWall = element(doc, 'div', 'td-replay-wall', replay);
  const replayCaption = element(doc, 'p', '', replay, '');
  const loading = element(doc, 'p', 'td-loading', root, 'Opening the gallery');
  const surfaces = [], art = new Map(), animated = new Set(), geometries = new Set();
  const replayMedia = [];
  const geometryGroup = new THREE.Group();
  world.add(geometryGroup);
  let materialPack, materials, museum, props, stamp, replayOff;
  let width = 1, height = 1, focal = 1, lastState, disposed = false, readyState = false, stamped = false;
  let toyFrameOff = null, toyTime = 0;
  const depth = lite ? 38 : 58;

  function plate(parent, slot, title) {
    const face = element(doc, 'div', 'td-art', parent);
    element(doc, 'span', 'td-art-placeholder', face, title || 'The collection');
    if (slot) {
      const image = element(doc, 'img', 'td-art-image', face);
      image.alt = ''; image.hidden = true; image.decoding = 'async';
      image.onload = () => { image.hidden = false; };
      image.onerror = () => {
        if (image.dataset.fallback) { image.remove(); return; }
        image.dataset.fallback = 'true';
        const index = [...slot].reduce((sum, character) => sum + character.charCodeAt(0), 0) % FALLBACK_ART.length;
        image.src = new URL(`../../assets/classifieds/${FALLBACK_ART[index]}`, import.meta.url).href;
      };
      art.set(image, slot);
    }
    return face;
  }
  function warmArt(node) {
    for (const image of node.querySelectorAll('.td-art-image')) {
      const slot = art.get(image);
      if (!slot) continue;
      art.delete(image);
      image.src = new URL(`${slot}.webp`, ART_ROOT).href;
      image.decode?.().catch(() => {});
    }
  }
  function animate(node, frames, duration, delay = 0) {
    const animation = ctx.anim(node, frames, { duration, delay, easing: 'ease-out', fill: 'both' });
    animated.add(animation);
    animation.finished?.then(() => { animated.delete(animation); animation.cancel(); }, () => animated.delete(animation));
    return animation;
  }
  function browserTile(parent, i) {
    const tile = element(doc, 'div', 'td-tab', parent);
    element(doc, 'div', 'td-tab-chrome', tile, '•  •  •');
    const face = plate(tile, i === 0 ? 'f-tabwall' : null, 'The collection');
    const sheet = element(doc, 'div', 'td-spreadsheet', tile);
    plate(sheet, 'f-spreadsheet', 'Quarterly accounts');
    return { tile, face };
  }
  for (let i = 0; i < (lite ? 4 : 8); i++) browserTile(replayWall, i);
  const freezePlate = plate(replay, 'f-freeze', 'Manus fugiens');
  freezePlate.classList.add('td-freeze-plate'); freezePlate.hidden = true;

  function box(parent, name, size, pos, material) {
    const geometry = new THREE.BoxGeometry(...size);
    geometries.add(geometry);
    const mesh = new THREE.Mesh(geometry, material);
    mesh.name = name; mesh.position.set(...pos); mesh.castShadow = true; mesh.receiveShadow = true;
    parent.add(mesh);
    return mesh;
  }
  function frame(w, h, point, yaw, ornate = false) {
    const group = new THREE.Group();
    group.position.set(point.x, point.y, point.z); group.rotation.y = yaw;
    const border = ornate ? 0.09 : 0.055;
    box(group, 'Frame backing', [w + border * 2, h + border * 2, .13], [0,0,-.055], materials.woodDark);
    box(group, 'Cream museum mount', [w,h,.025], [0,0,.025], materials.paper);
    for (const x of [-1,1]) box(group, 'Brass vertical frame edge', [border,h+border*2,.10], [x*(w+border)/2,0,.035], materials.brass);
    for (const y of [-1,1]) box(group, 'Brass horizontal frame edge', [w,border,.10], [0,y*(h+border)/2,.035], materials.brass);
    geometryGroup.add(group);
    return group;
  }
  function positionAt(at, lateral, y) {
    const point = trackPoint(at), yaw = trackYaw(at);
    return { x: point.x + Math.cos(yaw) * lateral, y, z: point.z - Math.sin(yaw) * lateral };
  }
  function cssPlace(node, point, yaw, w, h) {
    node.style.width = `${w * UNIT}px`; node.style.height = `${h * UNIT}px`;
    node.style.transform = `translate3d(${point.x * UNIT}px,${-point.y * UNIT}px,${point.z * UNIT}px) rotateY(${yaw}rad) translate(-50%,-50%)`;
  }
  function buildExhibits() {
    for (const exhibit of manifest.exhibits) {
      if (exhibit.kind.startsWith('stage-') || exhibit.kind === 'cutout' || exhibit.kind === 'vitrine') continue;
      const sides = exhibit.side === 'both' ? ['L', 'R'] : [exhibit.side];
      const locations = exhibit.kind === 'tab-wall' ? [248,264,280,300] : [exhibit.at + (exhibit.zone === 'booth' && exhibit.side !== 'C' ? 4 : 0)];
      for (const at of locations) for (const side of sides) {
        const node = element(doc, 'div', `td-plane td-exhibit td-kind-${exhibit.kind}`, domCamera);
        node.dataset.exhibit = exhibit.id;
        const w = exhibit.w || (exhibit.kind === 'tab-wall' ? 3.1 : exhibit.kind === 'name-tag' ? 1.45 : 1.65);
        const h = exhibit.h || (exhibit.kind === 'tab-wall' ? 1.6 : exhibit.kind === 'name-tag' ? .85 : 1.1);
        const closeBooth = exhibit.zone === 'booth';
        const lateral = side === 'L' ? -4.20 : side === 'R' ? 4.20 : -1.5;
        const y = exhibit.kind === 'name-tag' ? 1.7 : 2.20;
        const point = positionAt(at, lateral, y);
        const yaw = trackYaw(at) + (side === 'L' ? Math.PI / 2 : side === 'R' ? -Math.PI / 2 : 0);
        const frameGroup = frame(w, h, point, yaw, closeBooth);
        if (exhibit.kind === 'name-tag') {
          box(frameGroup, 'Name lectern post', [.14,1.3,.14], [0,-1.05,-.08], materials.woodDark);
          box(frameGroup, 'Name lectern base', [.7,.14,.48], [0,-1.62,-.08], materials.woodDark);
          box(frameGroup, 'Lectern brass foot', [.75,.025,.52], [0,-1.54,-.08], materials.brass);
        }
        const facePoint = new THREE.Vector3(0,0,.075).applyAxisAngle(new THREE.Vector3(0,1,0), yaw).add(new THREE.Vector3(point.x,point.y,point.z));
        cssPlace(node, facePoint, yaw, w*.90, h*.86);
        const faces = [], mounts = [];
        if (exhibit.kind === 'tab-wall') {
          for (let i = 0; i < (lite ? 4 : 6); i++) {
            const tile = browserTile(node, i);
            faces.push({ face: tile.face, kind: i === 1 ? 'run-clip' : 'run-picture' });
          }
        } else {
          const face = plate(node, exhibit.slot || exhibit.fallback, exhibit.placard?.title);
          if (exhibit.kind === 'run-picture' || exhibit.kind === 'run-clip') faces.push({ face, kind: exhibit.kind });
          if (exhibit.kind === 'name-tag') {
            const name = element(doc, 'span', 'td-name', face);
            name.dir = 'auto'; name.textContent = ctx.identity?.displayName?.trim() || `Signee #${1000 + (Number(ctx.state?.seed)||0)%9000}`;
            element(doc, 'div', 'td-stamp-shadow', face);
            stamp = element(doc, 'div', 'td-stamp', face, 'THE SUBJECT'); stamp.hidden = true;
          }
          const placard = element(doc, 'div', 'td-placard', node);
          element(doc, 'strong', '', placard, exhibit.placard?.title || '');
          element(doc, 'span', '', placard, exhibit.placard?.line || '');
        }
        surfaces.push({ node, frameGroup, exhibit, at, side, w, faces, mounts, placard: node.querySelector('.td-placard') });
      }
    }
    for (const banner of manifest.banners) {
      const node = element(doc, 'div', 'td-plane td-banner', domCamera);
      element(doc, 'strong', '', node, banner.title);
      if (banner.sub) element(doc, 'span', '', node, banner.sub);
      const point = positionAt(banner.at, 0, 4.2);
      const yaw = trackYaw(banner.at);
      cssPlace(node, point, yaw, 2.3, .62);
      const frameGroup = frame(2.4,.7,{...point,z:point.z-.03},yaw);
      surfaces.push({ node, frameGroup, at: banner.at, side:'C',w:2.3,faces:[],mounts:[] });
    }
    const dock = positionAt(587, 0, 2.35);
    const dockYaw = trackYaw(587);
    const stageFrame = frame(5.7,3.5,dock,dockYaw,true);
    box(stageFrame,'Left screen support',[.20,2.2,.20],[-2.4,-2.2,-.1],materials.woodDark);
    box(stageFrame,'Right screen support',[.20,2.2,.20],[2.4,-2.2,-.1],materials.woodDark);
    cssPlace(stageAnchor, {...dock,z:dock.z+.09},dockYaw,8.7,3.4);
  }
  const ready = Promise.resolve().then(() => createMuseumMaterials(THREE, { lite })).then(async pack => {
    materialPack = pack; materials = pack.materials;
    world.environment = pack.environment || null;
    world.environmentIntensity = .65;
    if (disposed) { pack.dispose(); return; }
    museum = createMuseum(THREE, materials, { lite }); world.add(museum.group);
    props = createMuseumProps(THREE, materials, { lite, onWake }); world.add(props.group); camera.add(props.cart);
    buildExhibits();
    await Promise.all([pack.ready, props.ready]);
    if (disposed) return;
    readyState = true; loading.remove(); resize();
  }).catch(error => { loading.textContent = 'The gallery could not open'; throw error; });

  function releaseSurface(surface) { for (const release of surface.mounts) release(); surface.mounts.length = 0; }
  function resize() {
    if (disposed) return;
    const rect = root.getBoundingClientRect();
    width = rect.width || 1;
    height = rect.height || 1;
    camera.aspect = width / height;
    camera.fov = width < 600 ? 85 : 61;
    camera.updateProjectionMatrix();
    focal = height * .5 * camera.projectionMatrix.elements[5];
    renderer.setSize(width, height, false);
    viewport.style.perspective = `${focal}px`;
    if (lastState) draw(lastState);
  }
  function draw(state) {
    lastState = state;
    if (!readyState || disposed || doc.hidden) return;
    const point = trackPoint(state.z);
    const bob = ctx.reduced || ctx.gentle ? 0 : Math.sin(state.z * 2) * Math.min(state.v, 5) * .002;
    const caseLook = ctx.reduced ? 0 : .45 * THREE.MathUtils.smoothstep(state.z, 450, 462) *
      (1 - THREE.MathUtils.smoothstep(state.z, 472, 484));
    camera.position.set(point.x, 1.45 + bob, point.z);
    camera.rotation.set(.055, trackYaw(state.z) - caseLook, 0, 'YXZ');
    camera.updateMatrixWorld();
    domCamera.style.transform = `translateZ(${focal}px) rotateX(${camera.rotation.x}rad) rotateY(${-camera.rotation.y}rad) translate3d(${-camera.position.x*UNIT}px,${camera.position.y*UNIT}px,${-camera.position.z*UNIT}px)`;
    key.position.set(point.x - 3, 4.8, point.z - 3);
    key.target.position.set(point.x, 0, point.z - 13);
    museum.update(state.z);
    props.update(state.z);
    props.pump(state.leverAngle);
    for (const surface of surfaces) {
      const distance = surface.at - state.z;
      const nearest = distance - (surface.side === 'C' ? 0 : surface.w / 2);
      const visible = nearest > .3 && distance < depth && !state.docked &&
        !museum.occludes?.(surface.frameGroup.position, camera.position);
      surface.node.hidden = !visible;
      surface.frameGroup.visible = visible;
      if (visible) {
        surface.node.style.filter = `brightness(${Math.max(.45, Math.min(1, (depth - distance) / (depth - 8)))})`;
        if (surface.placard) surface.placard.hidden = distance > 14;
      }
      if (distance < depth + 12 && distance > 0) warmArt(surface.node);
      if (visible && !surface.mounts.length) {
        for (const { face, kind } of surface.faces) surface.mounts.push(media.mount(face, { ...surface.exhibit, kind }));
      }
      if (!visible) releaseSurface(surface);
    }
    stageAnchor.hidden = state.z < 535;
    stageAnchor.style.width = `${(width < 600 ? 5.6 : 8.7) * UNIT}px`;
    const target = props.interactive?.[0]?.mesh;
    specimenButton.hidden = !target || state.z < 457 || state.z > 471;
    if (!specimenButton.hidden) {
      target.updateWorldMatrix(true, false);
      const point = target.getWorldPosition(new THREE.Vector3()).project(camera);
      specimenButton.style.left = `${(point.x * .5 + .5) * width - 24}px`;
      specimenButton.style.top = `${(-point.y * .5 + .5) * height - 24}px`;
    }
    speed.textContent = state.v.toFixed(1);
    position.textContent = `${Math.floor(state.z)} / ${manifest.track.dock} m`;
    hint.textContent = state.docked ? '' : state.locked ? 'Please wait at the platform' : state.holds.length ? 'Please remain seated' : 'Click, tap or press Space to pump';
    wash.hidden = state.band !== 4 || ctx.reduced;
    streaks.hidden = state.band !== 4 || ctx.reduced || ctx.gentle;
    root.dataset.gentle = String(Boolean(ctx.gentle || ctx.reduced));
    root.dataset.docked = String(state.docked);
    renderer.render(world, camera);
  }
  function beat(id) {
    if (disposed) return;
    if (id === 'booth.stamp' && stamp && !stamped) {
      stamped = true;
      stamp.hidden = false;
      const frames = ctx.gentle || ctx.reduced ? [{ opacity: 0 }, { opacity: 1 }] : [
        { opacity: 0, transform: 'scale(2.2) rotate(-12deg)' },
        { opacity: 1, transform: 'scale(.93,1.1) rotate(-12deg)', offset: .72 },
        { opacity: 1, transform: 'scale(1) rotate(-12deg)' },
      ];
      animate(stamp, frames, ctx.gentle ? 600 : 320);
    }
    if (id === 'floor.snap') {
      root.dataset.floor = 'sheet';
      const gentle = ctx.gentle || ctx.reduced;
      domCamera.querySelectorAll('.td-tab').forEach((tile, i) => {
        animate(tile.querySelector('.td-art'), [
          { opacity: 1, transform: 'scaleY(1)' },
          { opacity: 0, transform: gentle ? 'scaleY(1)' : 'scaleY(.01)' },
        ], gentle ? 600 : 160, gentle ? 0 : i % 6 * 20);
      });
    }
    if (id === 'floor.replay') {
      for (const release of replayMedia) release();
      replayMedia.length = 0;
      replay.hidden = false;
      freezePlate.hidden = true;
      replayWall.hidden = false;
      replay.dataset.mode = 'open';
      for (const face of replayWall.querySelectorAll('.td-tab > .td-art')) {
        replayMedia.push(media.mount(face, { kind: 'run-picture' }));
      }
      warmArt(replay);
      replayCaption.textContent = 'Again, in slow motion';
      replayOff?.();
      replayOff = ctx.after(1800, () => { replay.dataset.mode = 'sheet'; });
    }
    if (id === 'floor.freeze') {
      replay.hidden = false;
      replay.dataset.mode = 'freeze';
      replayWall.hidden = true;
      freezePlate.hidden = false;
      warmArt(freezePlate);
      replayCaption.textContent = manifest.exhibits.find(item => item.id === 'ex-freeze')?.caption || 'In slow motion';
    }
    if (id === 'floor.release') {
      replay.hidden = true;
      replayOff?.();
      for (const release of replayMedia) release();
      replayMedia.length = 0;
    }
    if (id === 'pump' && lastState?.docked && !toyFrameOff) {
      toyTime = 0;
      toyFrameOff = ctx.frame(dt => {
        if (ctx.paused || doc.hidden) return;
        toyTime += dt;
        props.pump(Math.sin(toyTime * 35) * .07 * Math.max(0, 1 - toyTime / .25));
        renderer.render(world, camera);
        if (toyTime >= .25) { toyFrameOff(); toyFrameOff = null; }
      });
    }
  }
  const raycaster = new THREE.Raycaster();
  const pointer = new THREE.Vector2();
  function press(event) {
    if (!readyState || ctx.paused || event.target.closest('button,a')) return;
    const rect = root.getBoundingClientRect();
    pointer.set((event.clientX - rect.left) / rect.width * 2 - 1, -(event.clientY - rect.top) / rect.height * 2 + 1);
    raycaster.setFromCamera(pointer, camera);
    const entries = props.interactive || [];
    const hit = raycaster.intersectObjects(entries.map(entry => entry.mesh), true)[0];
    if (hit) {
      const entry = entries.find(item => item.mesh === hit.object || item.mesh.getObjectById(hit.object.id));
      entry?.onPress?.();
      event.stopPropagation();
    }
  }
  canvas.addEventListener('pointerdown', press);
  resize();
  return {
    ready, stageAnchor, draw, resize, beat,
    debug: () => ({ drawCalls: renderer.info.render.calls, triangles: renderer.info.render.triangles, textures: renderer.info.memory.textures, ready: readyState }),
    setPaused(on) {
      root.dataset.paused = String(Boolean(on));
      for (const animation of animated) if (on) animation.pause(); else animation.play();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      replayOff?.();
      toyFrameOff?.();
      for (const release of replayMedia) release();
      for (const surface of surfaces) releaseSurface(surface);
      for (const animation of animated) animation.cancel();
      canvas.removeEventListener('pointerdown', press);
      props?.dispose();
      museum?.dispose();
      for (const geometry of geometries) geometry.dispose();
      materialPack?.dispose();
      renderer.dispose();
      renderer.forceContextLoss();
      art.clear();
    },
  };
}
