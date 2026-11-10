import * as THREE from '../../../vendor/three/three.module.min.js';
import { mergeGeometries } from '../../../vendor/three/addons/utils/BufferGeometryUtils.js';
import { createMuseum } from './environment.js';
import { createMuseumProps } from './props.js';
import { createMuseumMaterials } from './materials.js';
import { trackPoint, trackYaw, trackPitch, trackPosition, trackStyle } from './track.js';

const UNIT = 180;
const ART_ROOT = new URL('../../assets/documentary/art/', import.meta.url);
const EXHIBIT_CARDS = {
  'b-booth': ['RECORDING BOOTH', 'One afternoon', 'In the studio.'],
  'b-usernames': ['THE REGISTER', '11,000', 'Usernames.'],
  'b-nametag-frame': ['GOONING MUSEUM', 'The Subject', ''],
  'l-patent': ['RAILWAY HANDCAR', 'The pump lever', 'Patent drawing.'],
  'l-motion-study': ['MOTION STUDY', 'One stroke', 'Twelve frames.'],
  'l-writers': ['THE WRITERS', 'Session length', ''],
  'f-cheetah': ['THE CHEETAH', '100 km/h', 'In three seconds.'],
  'f-shrimp': ['THE MANTIS SHRIMP', 'Mid-strike', 'A club claw.'],
  'f-hand': ['MANUS FUGIENS', '0.3 seconds', "The Subject's hand. Response time."],
  'f-tabwall': ['THE COLLECTION', '41 tabs', 'Two videos and an entire personality.'],
  'f-spreadsheet': ['QUARTERLY ACCOUNTS', 'The spreadsheet', ''],
  'f-door': ['THE HALLWAY DOOR', 'A floorboard creaks', ''],
  'f-freeze': ['MANUS FUGIENS', 'In slow motion', ''],
  'w-daylight': ['DAYLIGHT', 'Unanswered messages', 'A world of unanswered messages.'],
  'w-talk': ['WE NEED TO TALK', 'Two chairs', 'And a box of tissues.'],
};

function element(doc, tag, className, parent, text) {
  const node = doc.createElement(tag);
  node.className = className;
  if (text != null) node.textContent = text;
  parent.append(node);
  return node;
}

/** Geometry and lighting are WebGL. Every framed picture is a real, display-only DOM image. */
export function createScene(ctx, root, manifest, media, { lite = false, onWake, onPump } = {}) {
  const doc = root.ownerDocument;
  const canvas = element(doc, 'canvas', 'td-webgl', root);
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: !lite, alpha: false, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(doc.defaultView.devicePixelRatio || 1, lite ? 1.25 : 1.5));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.12;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const world = new THREE.Scene();
  world.background = new THREE.Color('#26262a');
  world.fog = new THREE.Fog('#34363b', lite ? 30 : 42, lite ? 58 : 78);
  const camera = new THREE.PerspectiveCamera(61, 1, 0.08, 100);
  const cartRig = new THREE.Group();
  world.add(camera, cartRig);
  world.add(new THREE.HemisphereLight('#f1efe8', '#30343e', .88));
  const key = new THREE.DirectionalLight('#fff1df', .72);
  key.castShadow = true;
  key.shadow.mapSize.set(lite ? 1024 : 2048, lite ? 1024 : 2048);
  key.shadow.camera.left = -14; key.shadow.camera.right = 14;
  key.shadow.camera.top = 14; key.shadow.camera.bottom = -14;
  key.shadow.camera.near = 0.1; key.shadow.camera.far = 38;
  key.shadow.bias = -0.0003; key.shadow.normalBias = 0.025;
  world.add(key, key.target);
  const exhibitLights = Array.from({ length: lite ? 2 : 3 }, () => {
    const light = new THREE.SpotLight('#fff2db', 14, 13, .65, .85, 2);
    world.add(light, light.target);
    return light;
  });
  const viewport = element(doc, 'div', 'td-viewport', root);
  const domCamera = element(doc, 'div', 'td-camera', viewport);
  const wash = element(doc, 'div', 'td-wash', root);
  const streaks = element(doc, 'div', 'td-streaks', root);
  const instrument = element(doc, 'div', 'td-instrument', root);
  const speed = element(doc, 'strong', '', instrument, '0.0');
  element(doc, 'span', '', instrument, 'metres / second');
  const position = element(doc, 'small', '', instrument, '0 / 580 m');
  const hint = element(doc, 'p', 'td-pump-hint', root, 'Please wait at the platform');
  const centerButton = element(doc, 'button', 'td-center-view', root, 'Center view');
  centerButton.type = 'button';
  centerButton.setAttribute('aria-label', 'Center the view along the railway');
  centerButton.addEventListener('click', centerView);
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
  let toyFrameOff = null, toyTime = 0, renders = 0;
  let lookYaw = 0, lookPitch = 0, gesture = null;
  const viewMatrix = new THREE.Matrix4(), projectionView = new THREE.Matrix4();
  const frustum = new THREE.Frustum(), surfaceSphere = new THREE.Sphere();
  const pointInView = new THREE.Vector3(), projectedPump = new THREE.Vector3();
  let pumpPosition = null;
  const depth = lite ? 38 : 58;

  function plate(parent, slot, title) {
    const face = element(doc, 'div', 'td-art', parent);
    const card = element(doc, 'div', 'td-exposition-card', face);
    const [label, value, detail] = EXHIBIT_CARDS[slot] || ['THE COLLECTION', title || 'A still from this evening', ''];
    element(doc, 'small', 'td-exposition-label', card, label);
    element(doc, 'span', 'td-exposition-rule', card);
    element(doc, 'strong', 'td-exposition-value', card, value);
    if (detail) element(doc, 'p', 'td-exposition-detail', card, detail);
    element(doc, 'span', 'td-exposition-number', card, 'MUSEUM COLLECTION');
    if (slot && !slot.startsWith('b-')) cardFigure(card, slot);
    if (slot) {
      const image = element(doc, 'img', 'td-art-image', face);
      image.alt = ''; image.hidden = true; image.decoding = 'async';
      image.onload = () => { image.hidden = false; };
      image.onerror = () => { image.onload = image.onerror = null; image.remove(); };
      art.set(image, slot);
    }
    return face;
  }
  function cardFigure(parent, slot) {
    const svg = doc.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox','0 0 160 90'); svg.setAttribute('aria-hidden','true');
    svg.classList.add('td-card-figure'); parent.append(svg);
    const shape = (tag, values) => {
      const node = doc.createElementNS(svg.namespaceURI,tag);
      for (const [key,value] of Object.entries(values)) node.setAttribute(key,String(value));
      svg.append(node); return node;
    };
    const line = (x1,y1,x2,y2) => shape('line',{x1,y1,x2,y2});
    if (slot === 'l-motion-study' || slot === 'l-patent') {
      line(6,72,154,72);
      for(let i=0;i<12;i++) { const x=8+i*13, y=45-Math.sin(i/11*Math.PI*2)*24;
        line(x,72,x,y); shape('circle',{cx:x,cy:y,r:3}); }
    } else if (slot === 'f-cheetah') {
      for(let y=20;y<80;y+=20) line(10,y,150,y);
      line(10,78,10,10); line(10,78,150,78);
      shape('path',{d:'M10 76 C35 72 50 52 75 35 S120 15 150 12','stroke-width':4});
      shape('circle',{cx:150,cy:12,r:5});
    } else if (slot === 'f-hand' || slot === 'f-freeze' || slot === 'f-shrimp') {
      for(let r=12;r<=40;r+=14) shape('circle',{cx:80,cy:45,r});
      line(10,45,150,45); line(80,4,80,86);
      shape('path',{d:'M31 68 L65 45 L83 24 L91 45 L132 62','stroke-width':4});
    } else if (slot === 'f-tabwall' || slot === 'f-spreadsheet' || slot === 'f-door') {
      for(let i=0;i<4;i++) { shape('rect',{x:8+i*13,y:9+i*9,width:96,height:48,rx:3}); line(8+i*13,20+i*9,104+i*13,20+i*9); }
    } else {
      shape('circle',{cx:119,cy:26,r:17});
      shape('path',{d:'M4 79 L47 40 L81 67 L106 47 L157 79 Z'});
      line(4,85,157,85);
    }
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
  function roundedOutline(path, w, h, radius) {
    const r = Math.min(radius, w/2, h/2), x = -w/2, y = -h/2;
    path.moveTo(x+r,y); path.lineTo(x+w-r,y); path.quadraticCurveTo(x+w,y,x+w,y+r);
    path.lineTo(x+w,y+h-r); path.quadraticCurveTo(x+w,y+h,x+w-r,y+h);
    path.lineTo(x+r,y+h); path.quadraticCurveTo(x,y+h,x,y+h-r);
    path.lineTo(x,y+r); path.quadraticCurveTo(x,y,x+r,y); path.closePath();
    return path;
  }
  function housing(parent, name, w, h, depth, z, material, inset = 0) {
    const shape = roundedOutline(new THREE.Shape(), w, h, .045);
    if (inset) shape.holes.push(roundedOutline(new THREE.Path(), w-inset*2, h-inset*2, .025));
    const geometry = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: true,
      bevelSegments: 2, steps: 1, bevelSize: .007, bevelThickness: .007, curveSegments: 4 });
    geometries.add(geometry);
    const mesh = new THREE.Mesh(geometry, material);
    mesh.name = name; mesh.position.z = z;
    mesh.castShadow = mesh.receiveShadow = true;
    parent.add(mesh);
    return mesh;
  }
  function fastener(parent, x, y, z, material) {
    const shape = new THREE.CylinderGeometry(.014,.014,.009,10);
    geometries.add(shape);
    const screw = new THREE.Mesh(shape,material);
    screw.rotation.x = Math.PI/2; screw.position.set(x,y,z); parent.add(screw);
    box(parent,'Recessed screw slot',[.018,.003,.003],[x,y,z+.006],materials.dark);
  }
  function batchDisplay(group) {
    const parts = new Map();
    for (const child of [...group.children]) {
      if (!child.isMesh) continue;
      child.updateMatrix();
      let geometry = child.geometry.clone();
      if (geometry.index) { const indexed = geometry; geometry = indexed.toNonIndexed(); indexed.dispose(); }
      geometry.applyMatrix4(child.matrix);
      if (!parts.has(child.material)) parts.set(child.material,[]);
      parts.get(child.material).push(geometry);
      geometries.delete(child.geometry); child.geometry.dispose(); group.remove(child);
    }
    for (const [material, source] of parts) {
      const geometry = mergeGeometries(source,false);
      source.forEach(part => part.dispose());
      if (!geometry) continue;
      geometries.add(geometry);
      const mesh = new THREE.Mesh(geometry,material);
      mesh.castShadow = mesh.receiveShadow = true;
      group.add(mesh);
    }
  }
  function frame(w, h, point, yaw, ornate = false, room = 'intake') {
    const group = new THREE.Group();
    group.position.set(point.x, point.y, point.z); group.rotation.y = yaw;
    const modern = ['connector', 'reflex', 'descent', 'wakeup'].includes(room);
    const border = modern ? .075 : ornate ? .09 : .055;
    const edge = modern ? materials.chrome || materials.iron : materials.brass;
    const shell = modern ? materials.lacquerBlack || materials.iron : materials.woodDark;
    const mount = modern ? materials.dark : materials.paper;
    housing(group, modern ? 'Cast display enclosure' : 'Carved frame backing',w+border*2,h+border*2,modern?.19:.12,-.16,shell);
    housing(group,'Machined outer bezel',w+border*2-.018,h+border*2-.018,.035,.027,edge,border*.62);
    housing(group,'Inset shadow reveal',w+.035,h+.035,.018,.027,materials.dark,.04);
    box(group, modern ? 'Inset display face' : 'Cream museum mount', [w,h,.019], [0,0,.031], mount);
    if (!modern) housing(group,'Fine inner frame bead',w-.035,h-.035,.016,.046,materials.brass,.012);
    for (const x of [-1,1]) for (const y of [-1,1]) {
      fastener(group,x*(w/2+border*.38),y*(h/2+border*.38),.075,edge);
      box(group,'Wall mounting standoff',[.075,.075,.075],[x*w*.37,y*h*.34,-.19],materials.iron);
    }
    if (modern) {
      for (let i=0;i<9;i++) box(group,'Cooling vent',[.026,.004,.035],[-w*.34+i*.045,-h/2-border*.36,.07],materials.dark);
      box(group,'Display status indicator',[.042,.008,.01],[w*.4,-h/2-border*.36,.074],materials.cyanLight || materials.lamp);
    }
    for (const side of [-1,1]) {
      box(group,'Picture light wall plate',[.075,.10,.025],[side*w*.22,h/2+.14,-.07],shell);
      box(group,'Picture light bracket',[.025,.025,.36],[side*w*.22,h/2+.18,.11],edge);
    }
    box(group, 'Picture light housing', [w*.62,.065,.14], [0,h/2+.18,.29], shell);
    box(group, 'Picture light diffuser', [w*.56,.012,.09], [0,h/2+.141,.30], materials.lamp);
    for (const side of [-1,1]) box(group,'Light end cap',[.025,.068,.14],[side*w*.315,h/2+.18,.29],edge);
    geometryGroup.add(group);
    return group;
  }
  function positionAt(at, lateral, y) {
    return trackPosition(at, lateral, y);
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
        const room = trackStyle(at).id;
        node.dataset.room = room;
        const w = exhibit.w || (exhibit.kind === 'tab-wall' ? 3.1 : exhibit.kind === 'name-tag' ? 1.45 : 1.65);
        const h = exhibit.h || (exhibit.kind === 'tab-wall' ? 1.6 : exhibit.kind === 'name-tag' ? .85 : 1.1);
        const closeBooth = exhibit.zone === 'booth';
        const lateral = side === 'L' ? -4.35 : side === 'R' ? 4.35 : -1.5;
        const y = exhibit.kind === 'name-tag' ? 1.7 : 2.20;
        const point = positionAt(at, lateral, y);
        const yaw = trackYaw(at) + (side === 'L' ? Math.PI / 2 : side === 'R' ? -Math.PI / 2 : 0);
        const frameGroup = frame(w, h, point, yaw, closeBooth, room);
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
          box(frameGroup, 'Exhibit information plate', [w*.76,.28,.035], [0,-h/2-.24,.04], materials.paper);
          box(frameGroup, 'Information plate backing', [w*.80,.31,.045], [0,-h/2-.24,.012], materials.iron);
          for (const x of [-1,1]) fastener(frameGroup,x*w*.365,-h/2-.24,.066,materials.brass);
        }
        surfaces.push({ node, frameGroup, exhibit, at, side, w, h, yaw, room, faces, mounts, placard: node.querySelector('.td-placard') });
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
      surfaces.push({ node, frameGroup, at: banner.at, side:'C',w:2.3,h:.7,yaw,faces:[],mounts:[] });
    }
    const dock = positionAt(587, 0, 2.35);
    const dockYaw = trackYaw(587);
    const stageFrame = frame(5.7,3.5,dock,dockYaw,true);
    box(stageFrame,'Left screen support',[.20,2.2,.20],[-2.4,-2.2,-.1],materials.woodDark);
    box(stageFrame,'Right screen support',[.20,2.2,.20],[2.4,-2.2,-.1],materials.woodDark);
    cssPlace(stageAnchor, {...dock,z:dock.z+.09},dockYaw,8.7,3.4);
    for (const display of geometryGroup.children) batchDisplay(display);
  }
  const ready = Promise.resolve().then(() => createMuseumMaterials(THREE, { lite })).then(async pack => {
    materialPack = pack; materials = pack.materials;
    world.environment = pack.environment || null;
    world.environmentIntensity = .35;
    if (disposed) { pack.dispose(); return; }
    museum = createMuseum(THREE, materials, { lite }); world.add(museum.group);
    props = createMuseumProps(THREE, materials, { lite, onWake }); world.add(props.group); cartRig.add(props.cart);
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
    const calm = ctx.reduced || ctx.gentle;
    const bob = calm ? 0 : Math.sin(state.z * 2) * Math.min(state.v, 5) * .002;
    const yaw = trackYaw(state.z), pitch = trackPitch(state.z);
    const viewYaw = calm || state.motion === 'manual' ? yaw : trackYaw(state.z+THREE.MathUtils.clamp(state.v*.35,0,4));
    const curvature = trackYaw(state.z + 1) - trackYaw(Math.max(0, state.z - 1));
    const bank = calm ? 0 : THREE.MathUtils.clamp(curvature * state.v * -.12, -.035, .035);
    const fov = (width < 600 ? 85 : 61) + (calm ? 0 : THREE.MathUtils.smoothstep(state.v, 4, 14) * 5);
    if (camera.fov !== fov) { camera.fov = fov; camera.updateProjectionMatrix(); }
    focal = height * .5 * camera.projectionMatrix.elements[5];
    viewport.style.perspective = `${focal}px`;
    camera.position.set(point.x, point.y + 1.45 + bob, point.z);
    camera.rotation.set(pitch + .055 + lookPitch, viewYaw + lookYaw, bank, 'YXZ');
    cartRig.position.copy(camera.position);
    cartRig.rotation.set(pitch + .055, yaw, bank, 'YXZ');
    camera.updateMatrixWorld();
    // CSS uses downward-positive Y and pixel units. Conjugate the exact view matrix.
    viewMatrix.copy(camera.matrixWorldInverse);
    const values = viewMatrix.elements.map((value, index) => {
      const row = index % 4, column = Math.floor(index / 4);
      return value * (row === 1 ? -1 : 1) * (column === 1 ? -1 : 1) * (column === 3 && row < 3 ? UNIT : 1);
    });
    domCamera.style.transform = `translateZ(${focal}px) matrix3d(${values.join(',')})`;
    projectionView.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
    frustum.setFromProjectionMatrix(projectionView);
    const roomStyle = trackStyle(state.z);
    root.dataset.room = roomStyle.id;
    if (roomStyle.bg) { world.background.set(roomStyle.bg); world.fog.color.set(roomStyle.bg); }
    key.position.set(point.x - 3, point.y + 4.8, point.z - 3);
    const ahead = trackPosition(state.z + 10, 0, .4);
    key.target.position.set(ahead.x, ahead.y, ahead.z);
    museum.update(state.z);
    props.update(state.z);
    props.pump(state.leverAngle);
    const lit = [];
    for (const surface of surfaces) {
      const distance = surface.at - state.z;
      pointInView.copy(surface.frameGroup.position).applyMatrix4(camera.matrixWorldInverse);
      surfaceSphere.center.copy(surface.frameGroup.position);
      surfaceSphere.radius = Math.hypot(surface.w, surface.h) * .55;
      const visible = distance > -18 && distance < depth && pointInView.z < -.2 &&
        frustum.intersectsSphere(surfaceSphere) &&
        !museum.occludes?.(surface.frameGroup.position, camera.position);
      surface.node.hidden = !visible;
      surface.frameGroup.visible = visible;
      if (visible) {
        surface.node.style.filter = `brightness(${Math.max(.52, Math.min(1, (depth - Math.abs(distance)) / (depth - 8)))})`;
        if (surface.placard) surface.placard.hidden = Math.abs(distance) > 14;
        lit.push(surface);
      }
      if (distance < depth + 12 && distance > -18) warmArt(surface.node);
      if (visible && !surface.mounts.length) {
        for (const { face, kind } of surface.faces) surface.mounts.push(media.mount(face, { ...surface.exhibit, kind }));
      }
      if (!visible) releaseSurface(surface);
    }
    lit.sort((a, b) => Math.abs(a.at-state.z)-Math.abs(b.at-state.z));
    exhibitLights.forEach((light, index) => {
      const surface = lit[index];
      light.visible = Boolean(surface);
      if (!surface) return;
      const point = surface.frameGroup.position;
      light.position.set(point.x+Math.sin(surface.yaw)*.85, point.y+surface.h/2+.3, point.z+Math.cos(surface.yaw)*.85);
      light.target.position.copy(point);
    });
    const stagePoint = positionAt(587, 0, 2.35);
    pointInView.set(stagePoint.x, stagePoint.y, stagePoint.z).applyMatrix4(camera.matrixWorldInverse);
    stageAnchor.hidden = state.z < 535 || pointInView.z > -.2;
    stageAnchor.style.width = `${(width < 600 ? 5.6 : 8.7) * UNIT}px`;
    const target = props.interactive?.[0]?.mesh;
    specimenButton.hidden = !target || state.z < 450 || state.z > 478;
    if (!specimenButton.hidden) {
      target.updateWorldMatrix(true, false);
      const worldPoint = target.getWorldPosition(new THREE.Vector3());
      const point = worldPoint.clone().project(camera);
      specimenButton.hidden = point.z < -1 || point.z > 1 || Math.abs(point.x) > 1 || Math.abs(point.y) > 1 ||
        Boolean(museum.occludes?.(worldPoint, camera.position));
      specimenButton.style.left = `${(point.x * .5 + .5) * width - 24}px`;
      specimenButton.style.top = `${(-point.y * .5 + .5) * height - 24}px`;
    }
    speed.textContent = state.v.toFixed(1);
    position.textContent = `${Math.floor(state.z)} / ${manifest.track.dock} m`;
    hint.textContent = state.docked ? 'Drag to look around' : state.locked ? 'Drag to look around. Please wait at the platform' : state.holds.length ? 'Please remain seated. Drag to look around' : 'Drag to look. Click the lever or press Space to pump';
    centerButton.dataset.turned = String(Math.abs(lookYaw) + Math.abs(lookPitch) > .03);
    wash.hidden = state.band !== 4 || ctx.reduced;
    streaks.hidden = state.band !== 4 || ctx.reduced || ctx.gentle;
    root.dataset.gentle = String(Boolean(ctx.gentle || ctx.reduced));
    root.dataset.docked = String(state.docked);
    renderer.render(world, camera); renders++;
    const pumpTarget = props.pumpTargets?.[0];
    pumpPosition = null;
    if (pumpTarget) {
      if (props.pumpGrip) props.pumpGrip.getWorldPosition(projectedPump);
      else pumpTarget.localToWorld(projectedPump.set(0,.27,0));
      projectedPump.project(camera);
      pumpPosition = { x: (projectedPump.x*.5+.5)*width, y: (-projectedPump.y*.5+.5)*height,
        visible: Math.abs(projectedPump.x) < 1 && Math.abs(projectedPump.y) < 1 && projectedPump.z > -1 && projectedPump.z < 1 };
    }
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
        renderer.render(world, camera); renders++;
        if (toyTime >= .25) { toyFrameOff(); toyFrameOff = null; }
      });
    }
  }
  const raycaster = new THREE.Raycaster();
  const pointer = new THREE.Vector2();
  const isControl = target => target?.closest?.('button,a,input,textarea,select,[contenteditable=true],[role=button]');
  function centerView() {
    if (disposed || ctx.paused || doc.hidden) return;
    cancelGesture();
    lookYaw = lookPitch = 0;
    if (lastState) draw(lastState);
  }
  function cancelGesture() {
    const old = gesture;
    gesture = null;
    root.dataset.dragging = 'false';
    if (old && root.hasPointerCapture?.(old.id)) root.releasePointerCapture(old.id);
  }
  function pointerDown(event) {
    if (!readyState || disposed || ctx.paused || doc.hidden || event.button !== 0 ||
        event.isPrimary === false || isControl(event.target)) return;
    root.tabIndex = -1;
    root.focus({ preventScroll: true });
    gesture = { id: event.pointerId, x: event.clientX, y: event.clientY, yaw: lookYaw, pitch: lookPitch, dragged: false };
    root.setPointerCapture?.(event.pointerId);
    event.preventDefault();
  }
  function pointerMove(event) {
    if (!gesture || event.pointerId !== gesture.id || ctx.paused || doc.hidden) return;
    const dx = event.clientX - gesture.x, dy = event.clientY - gesture.y;
    if (!gesture.dragged && Math.hypot(dx, dy) < 6) return;
    gesture.dragged = true;
    const sensitivity = Math.PI / Math.max(420, width);
    lookYaw = THREE.MathUtils.clamp(gesture.yaw - dx*sensitivity, -110*Math.PI/180, 110*Math.PI/180);
    lookPitch = THREE.MathUtils.clamp(gesture.pitch - dy*sensitivity, -42*Math.PI/180, 42*Math.PI/180);
    root.dataset.dragging = 'true';
    event.preventDefault();
    if (lastState) draw(lastState);
  }
  function pointerUp(event) {
    if (!gesture || event.pointerId !== gesture.id) return;
    const dragged = gesture.dragged || Math.hypot(event.clientX-gesture.x, event.clientY-gesture.y) >= 6;
    cancelGesture();
    if (dragged || !readyState || disposed || ctx.paused || doc.hidden) return;
    const rect = root.getBoundingClientRect();
    pointer.set((event.clientX - rect.left) / rect.width * 2 - 1, -(event.clientY - rect.top) / rect.height * 2 + 1);
    raycaster.setFromCamera(pointer, camera);
    const entries = (props.interactive || []).filter(entry => {
      for (let parent = entry.mesh; parent; parent = parent.parent) if (!parent.visible) return false;
      return true;
    });
    const targets = props.pumpTargets || [];
    const hit = raycaster.intersectObjects([...targets, ...entries.map(entry => entry.mesh)], true)[0];
    if (hit) {
      if (targets.some(target => target === hit.object || target.getObjectById(hit.object.id))) {
        onPump?.();
        event.preventDefault();
        return;
      }
      const entry = entries.find(item => item.mesh === hit.object || item.mesh.getObjectById(hit.object.id));
      if (!museum.occludes?.(hit.point, camera.position)) entry?.onPress?.();
    }
  }
  root.addEventListener('pointerdown', pointerDown);
  root.addEventListener('pointermove', pointerMove);
  root.addEventListener('pointerup', pointerUp);
  root.addEventListener('pointercancel', cancelGesture);
  root.addEventListener('lostpointercapture', cancelGesture);
  resize();
  return {
    ready, stageAnchor, draw, resize, beat,
    debug: () => ({ drawCalls: renderer.info.render.calls, triangles: renderer.info.render.triangles,
      textures: renderer.info.memory.textures, ready: readyState, renders, lookYaw, lookPitch,
      cameraPosition: camera.position.toArray(), pumpPosition }),
    setPaused(on) {
      if (on) cancelGesture();
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
      cancelGesture();
      root.removeEventListener('pointerdown', pointerDown);
      root.removeEventListener('pointermove', pointerMove);
      root.removeEventListener('pointerup', pointerUp);
      root.removeEventListener('pointercancel', cancelGesture);
      root.removeEventListener('lostpointercapture', cancelGesture);
      centerButton.removeEventListener('click', centerView);
      specimenButton.removeEventListener('click', onWake);
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
