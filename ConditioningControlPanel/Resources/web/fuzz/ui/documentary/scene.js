import * as THREE from '../../../vendor/three/three.module.min.js';
import { mergeGeometries } from '../../../vendor/three/addons/utils/BufferGeometryUtils.js';
import { createMuseum, SCREEN_NICHES } from './environment.js';
import { createMuseumProps } from './props.js';
import { createMuseumMaterials } from './materials.js';
import { trackPoint, trackYaw, trackPitch, trackPosition, trackStyle } from './track.js';
import { subscriberPlan } from '../ads.js';
import { STORY_ART_SLOTS, isDocumentaryArtSlot } from './model.js';
import { createProjectorEffects } from './projector-effects.js';

const UNIT = 180;
const ART_ROOT = new URL('../../assets/documentary/art/', import.meta.url);
const STORY_PROJECTORS = [
  { id:'booth',at:8,slot:'intro-subject' }, { id:'lever',at:112,slot:'pump-documentary' },
  { id:'reflex',at:310,slot:'reflex-cheetah' },
  { id:'wakeup',at:418,slot:'outside-daylight' },
  { id:'conversation',at:504,slot:'conversation-chairs' },
];
const ART_CARD_ALIAS = {
  'intro-subject':'b-nametag-frame', 'pump-documentary':'l-patent',
  'reflex-cheetah':'f-cheetah', 'reflex-shrimp':'f-shrimp', 'subject-habitat':'f-hand',
  'floorboard-warning':'f-door', 'subject-spreadsheet':'f-spreadsheet',
  'outside-daylight':'w-daylight', 'conversation-chairs':'w-talk',
};
const EXHIBIT_CARDS = {
  'b-booth': ['NARRATION BUDGET', 'One afternoon', 'Paid by the letter.'],
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
  'w-case': ['THE WAKE UP BUTTON', 'Specimen: unpressed', ''],
};

function element(doc, tag, className, parent, text) {
  const node = doc.createElement(tag);
  node.className = className;
  if (text != null) node.textContent = text;
  parent.append(node);
  return node;
}

/** Geometry and lighting are WebGL. Every framed picture is a real, display-only DOM image. */
export function createScene(ctx, root, manifest, media, { lite = false, autoProjectors = true, onWake, onPump, onStageAction } = {}) {
  const doc = root.ownerDocument;
  const projectorEffects = createProjectorEffects(ctx,{document:doc});
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
  const stageControlsAnchor = element(doc,'div','td-stage-controls-anchor',root);
  stageControlsAnchor.hidden = true;
  const replay = element(doc, 'div', 'td-plane td-projector-face td-replay', domCamera);
  replay.hidden = true; replay.style.padding = '0';
  const loading = element(doc, 'p', 'td-loading', root, 'Opening the gallery');
  const surfaces = [], art = new Map(), animated = new Set(), geometries = new Set();
  const artLoads = new Set(), storyImageStatus = new Map();
  const projectors = [];
  const geometryGroup = new THREE.Group();
  world.add(geometryGroup);
  let materialPack, materials, museum, props, stamp;
  let width = 1, height = 1, focal = 1, lensTime = 0, lastState, disposed = false, readyState = false, stamped = false;
  let toyFrameOff = null, toyTime = 0, renders = 0, projectorFrameOff = null;
  let bulbJob=null,bulbRestore=null,bulbDim=false,bulbCycle=0;
  let lookYaw = 0, lookPitch = 0, gesture = null;
  const viewMatrix = new THREE.Matrix4(), projectionView = new THREE.Matrix4();
  const frustum = new THREE.Frustum(), surfaceSphere = new THREE.Sphere();
  const pointInView = new THREE.Vector3(), projectedPump = new THREE.Vector3();
  let pumpPosition = null, pumpHitBounds = null;
  const pumpBounds = new THREE.Box3(new THREE.Vector3(-.50,-.78,-1.48),new THREE.Vector3(.50,-.31,-1.05));
  const pumpRay = new THREE.Ray(), pumpLocalHit = new THREE.Vector3(), cartInverse = new THREE.Matrix4();
  const pendingProjectorBeats = [];
  let scriptedProjectors = !autoProjectors, narrationSpeaking = false;
  const depth = lite ? 38 : 58;

  function plate(parent, slot, title) {
    const face = element(doc, 'div', 'td-art', parent);
    if(slot) face.dataset.artSlot=slot;
    const card = element(doc, 'div', 'td-exposition-card', face);
    const cardSlot=ART_CARD_ALIAS[slot]||slot;
    const [label, value, detail] = EXHIBIT_CARDS[cardSlot] || ['THE COLLECTION', title || 'A still from this evening', ''];
    element(doc, 'small', 'td-exposition-label', card, label);
    element(doc, 'span', 'td-exposition-rule', card);
    element(doc, 'strong', 'td-exposition-value', card, value);
    if (detail) element(doc, 'p', 'td-exposition-detail', card, detail);
    element(doc, 'span', 'td-exposition-number', card, 'MUSEUM COLLECTION');
    if (cardSlot && !cardSlot.startsWith('b-')) cardFigure(card, cardSlot);
    if (slot) {
      const image = element(doc, 'img', 'td-art-image', face);
      image.dataset.artSlot=slot;
      image.alt = ''; image.hidden = true; image.decoding = 'async';
      image.onload = () => { image.hidden = false; face.dataset.artReady='true'; };
      image.onerror = () => { image.onload = image.onerror = null; image.remove(); };
      art.set(image, slot);
    }
    return face;
  }
  function preloadStoryArt() {
    return Promise.all(STORY_ART_SLOTS.map(slot=>new Promise(resolve=>{
      const image=doc.createElement('img');
      let done=false;
      const finish=ok=>{
        if(done) return;done=true;
        image.onload=image.onerror=null;artLoads.delete(job);
        storyImageStatus.set(slot,ok?'ready':'fallback');resolve();
      };
      const job={image,finish};artLoads.add(job);storyImageStatus.set(slot,'loading');
      image.decoding='async';
      image.onload=()=>image.decode?image.decode().then(()=>finish(true),()=>finish(true)):finish(true);
      image.onerror=()=>finish(false);
      image.src=new URL(`${slot}.webp`,ART_ROOT).href;
    })));
  }
  function cardFigure(parent, slot, projection = false) {
    const svg = doc.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox','0 0 160 90'); svg.setAttribute('aria-hidden','true');
    svg.classList.add('td-card-figure'); parent.append(svg);
    const shape = (tag, values) => {
      const node = doc.createElementNS(svg.namespaceURI,tag);
      for (const [key,value] of Object.entries(values)) node.setAttribute(key,String(value));
      svg.append(node); return node;
    };
    const line = (x1,y1,x2,y2) => shape('line',{x1,y1,x2,y2});
    if (projection && slot.startsWith('b-')) {
      shape('rect',{x:68,y:12,width:24,height:40,rx:12});
      shape('path',{d:'M58 32 V42 Q58 62 80 62 Q102 62 102 42 V32 M80 62 V78 M60 78 H100'});
      for(let y=22;y<45;y+=7) line(73,y,87,y);
      shape('path',{d:'M43 24 Q30 35 43 48 M117 24 Q130 35 117 48 M31 15 Q12 35 31 57 M129 15 Q148 35 129 57'});
    } else if (projection && slot.startsWith('l-')) {
      shape('path',{d:'M32 73 H128 V79 H32 Z M40 73 V62 H120 V73 M80 62 V31 M45 31 H115','stroke-width':3});
      shape('circle',{cx:48,cy:78,r:8}); shape('circle',{cx:112,cy:78,r:8});
      shape('path',{d:'M42 18 Q80 -2 118 18 M108 11 L118 18 L108 22 M52 11 L42 18 L52 22'});
      line(45,19,115,43); line(45,43,115,19);
    } else if (projection && slot === 'w-case') {
      shape('path',{d:'M40 73 V20 Q40 10 50 10 H110 Q120 10 120 20 V73 M29 73 H131 V82 H29 Z'});
      shape('rect',{x:54,y:38,width:52,height:23,rx:5,'stroke-width':3});
      const text = shape('text',{x:80,y:53,'text-anchor':'middle','font-size':10,fill:'currentColor',stroke:'none'}); text.textContent='Wake Up';
      line(48,17,48,67); line(112,17,112,67);
    } else if (projection && (slot === 'f-hand' || slot === 'f-freeze')) {
      shape('path',{d:'M45 78 L35 56 Q31 48 37 46 Q42 44 49 57 V21 Q49 13 55 13 Q61 13 61 21 V43 V15 Q61 8 67 8 Q73 8 73 15 V42 V19 Q73 12 79 12 Q85 12 85 19 V45 V29 Q85 22 91 22 Q97 22 97 29 V58 Q97 73 87 79 Z'});
      shape('rect',{x:113,y:43,width:24,height:35,rx:12}); line(125,43,125,56);
      shape('path',{d:'M103 26 H139 M133 21 L139 26 L133 31'});
    } else if (slot === 'l-motion-study' || slot === 'l-patent') {
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
    plate(sheet, 'subject-spreadsheet', 'Quarterly accounts');
    return { tile, face };
  }
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
  function frame(w, h, point, yaw, ornate = false, room = 'intake', niche = false) {
    const group = new THREE.Group();
    group.position.set(point.x, point.y, point.z); group.rotation.y = yaw;
    const modern = niche || ['connector', 'reflex', 'descent', 'wakeup'].includes(room);
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
      box(group,'Wall mounting standoff',[.075,.075,niche?.24:.075],[x*w*.37,y*h*.34,niche?-.25:-.19],materials.iron);
    }
    if (modern) {
      for (let i=0;i<9;i++) box(group,'Cooling vent',[.026,.004,.035],[-w*.34+i*.045,-h/2-border*.36,.07],materials.dark);
      box(group,'Display status indicator',[.042,.008,.01],[w*.4,-h/2-border*.36,.074],materials.cyanLight || materials.lamp);
    }
    if (!niche) for (const side of [-1,1]) {
      box(group,'Picture light wall plate',[.075,.10,.025],[side*w*.22,h/2+.14,-.07],shell);
      box(group,'Picture light bracket',[.025,.025,.36],[side*w*.22,h/2+.18,.11],edge);
    }
    if (!niche) {
      box(group, 'Picture light housing', [w*.62,.065,.14], [0,h/2+.18,.29], shell);
      box(group, 'Picture light diffuser', [w*.56,.012,.09], [0,h/2+.141,.30], materials.lamp);
      for (const side of [-1,1]) box(group,'Light end cap',[.025,.068,.14],[side*w*.315,h/2+.18,.29],edge);
    }
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
  function placeNicheFace(node, frameGroup, spec) {
    frameGroup.rotateZ(spec.roll || 0);
    frameGroup.updateWorldMatrix(true, false);
    const matrix = frameGroup.matrixWorld.clone().multiply(new THREE.Matrix4().makeTranslation(0, 0, .075));
    const values = matrix.elements.map((value, index) => {
      const row = index % 4, column = Math.floor(index / 4);
      return value * (row === 1 ? -1 : 1) * (column === 1 ? -1 : 1) * (column === 3 && row < 3 ? UNIT : 1);
    });
    node.style.width = `${(spec.width - .08) * UNIT}px`;
    node.style.height = `${(spec.height - .08) * UNIT}px`;
    node.style.transform = `matrix3d(${values.join(',')}) translate(-50%,-50%)`;
    node.style.overflow = 'hidden';
    node.style.clipPath = 'inset(0)';
    node.dataset.niche = `${spec.distance}/${spec.side}`;
    node.dataset.frameInset = '.04';
  }
  function clipNicheFace(surface) {
    const spec = surface.niche;
    if (!surface.nicheOpening) {
      const height = { intake: 3.8, connector: 4.05, reflex: 3.9, descent: 4.05, wakeup: 3.7, theatre: 3.8 }[surface.room];
      surface.nicheOpening = [[-2.45, .34], [2.45, .34], [2.45, height - .22], [-2.45, height - .22]].map(([offset, y]) => {
        const point = trackPosition(spec.distance + offset, spec.side * 4.44, y);
        return new THREE.Vector3(point.x, point.y, point.z);
      });
      surface.nicheInverse = surface.frameGroup.matrixWorld.clone().invert();
      surface.nicheNormal = new THREE.Vector3(0, 0, 1).applyQuaternion(surface.frameGroup.quaternion);
      surface.nicheFacePoint = new THREE.Vector3(0, 0, .075).applyMatrix4(surface.frameGroup.matrixWorld);
    }
    const origin = camera.position, normal = surface.nicheNormal;
    const planeDistance = surface.nicheFacePoint.clone().sub(origin).dot(normal);
    const opening = [];
    for (const point of surface.nicheOpening) {
      const ray = point.clone().sub(origin), denominator = ray.dot(normal);
      if (Math.abs(denominator) < .00001 || planeDistance / denominator <= 0) return false;
      const hit = origin.clone().addScaledVector(ray, planeDistance / denominator).applyMatrix4(surface.nicheInverse);
      opening.push({ x: hit.x, y: hit.y });
    }
    const width = spec.width - .08, height = spec.height - .08;
    let polygon = [{ x: -width / 2, y: -height / 2 }, { x: width / 2, y: -height / 2 }, { x: width / 2, y: height / 2 }, { x: -width / 2, y: height / 2 }];
    const winding = Math.sign(opening.reduce((area, point, index) => {
      const next = opening[(index + 1) % opening.length]; return area + point.x * next.y - next.x * point.y;
    }, 0)) || 1;
    for (let edge = 0; edge < opening.length && polygon.length; edge++) {
      const a = opening[edge], b = opening[(edge + 1) % opening.length];
      const signed = point => winding * ((b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x));
      const input = polygon; polygon = [];
      for (let index = 0; index < input.length; index++) {
        const current = input[index], previous = input[(index + input.length - 1) % input.length];
        const here = signed(current), there = signed(previous);
        if ((here >= 0) !== (there >= 0)) {
          const t = there / (there - here);
          polygon.push({ x: previous.x + (current.x - previous.x) * t, y: previous.y + (current.y - previous.y) * t });
        }
        if (here >= 0) polygon.push(current);
      }
    }
    if (polygon.length < 3) return false;
    surface.node.style.clipPath = `polygon(${polygon.map(point => `${((point.x / width + .5) * 100).toFixed(3)}% ${((.5 - point.y / height) * 100).toFixed(3)}%`).join(',')})`;
    return true;
  }
  function buildExhibits() {
    for (const exhibit of manifest.exhibits) {
      if (exhibit.kind.startsWith('stage-') || ['cutout','vitrine','run-picture','run-clip'].includes(exhibit.kind)) continue;
      const sides = exhibit.side === 'both' ? ['L', 'R'] : [exhibit.side];
      const locations = exhibit.kind === 'tab-wall' ? [248,264,280,300] :
        [exhibit.at + (exhibit.kind === 'name-tag' ? 2 : exhibit.zone === 'booth' && exhibit.side !== 'C' ? 4 : 0)];
      for (const sourceAt of locations) for (const side of sides) {
        const niche = exhibit.kind === 'tab-wall' ? SCREEN_NICHES.find(spec => spec.collectionAt === sourceAt && spec.side === (side === 'L' ? -1 : 1)) : null;
        const at = niche?.distance ?? sourceAt;
        const node = element(doc, 'div', `td-plane td-exhibit td-kind-${exhibit.kind}`, domCamera);
        node.dataset.exhibit = exhibit.id;
        const room = trackStyle(at).id;
        node.dataset.room = room;
        const w = niche?.width || exhibit.w || (exhibit.kind === 'tab-wall' ? 3.1 : exhibit.kind === 'name-tag' ? 1.45 : 1.65);
        const h = niche?.height || exhibit.h || (exhibit.kind === 'tab-wall' ? 1.6 : exhibit.kind === 'name-tag' ? .85 : 1.1);
        const closeBooth = exhibit.zone === 'booth';
        const lateral = niche ? niche.side * niche.lateral : side === 'L' ? -4.35 : side === 'R' ? 4.35 : exhibit.kind === 'name-tag' ? -3.15 : -1.5;
        const y = niche?.elevation ?? (exhibit.kind === 'name-tag' ? 1.7 : 2.20);
        const point = positionAt(at, lateral, y);
        const yaw = trackYaw(at) + (side === 'L' ? Math.PI / 2 : side === 'R' ? -Math.PI / 2 : 0);
        const frameGroup = frame(w, h, point, yaw, closeBooth, room, Boolean(niche));
        if (exhibit.kind === 'name-tag') {
          box(frameGroup, 'Name lectern post', [.14,1.3,.14], [0,-1.05,-.08], materials.woodDark);
          box(frameGroup, 'Name lectern base', [.7,.14,.48], [0,-1.62,-.08], materials.woodDark);
          box(frameGroup, 'Lectern brass foot', [.75,.025,.52], [0,-1.54,-.08], materials.brass);
        }
        const facePoint = new THREE.Vector3(0,0,.075).applyAxisAngle(new THREE.Vector3(0,1,0), yaw).add(new THREE.Vector3(point.x,point.y,point.z));
        if (niche) placeNicheFace(node, frameGroup, niche);
        else cssPlace(node, facePoint, yaw, w*.90, h*.86);
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
        surfaces.push({ node, frameGroup, exhibit, at, side, w, h, yaw, room, faces, mounts, niche, placard: node.querySelector('.td-placard') });
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
    buildNicheScreens();
    buildProjectors();
    const dock = positionAt(587, 0, 2.35), dockYaw = trackYaw(587);
    cssPlace(stageAnchor, {...dock,z:dock.z+.09},dockYaw,8.7,3.4);
    for (const display of geometryGroup.children) batchDisplay(display);
  }
  function buildNicheScreens() {
    for (const spec of SCREEN_NICHES) {
      if (spec.collectionAt != null) continue;
      const at=spec.distance,side=spec.side===-1||spec.side==='L'?'L':'R';
      const original = manifest.exhibits.find(item => item.kind.startsWith('run-') && item.side === side &&
        Math.round((item.at-4)/8)*8+4 === at);
      const exhibit = original || { id:`personal-${at}-${side}`,kind:'run-picture',placard:{title:'The collection',line:'A still from this evening.'} };
      const room = trackStyle(at).id, w=spec.width, h=spec.height;
      const point = positionAt(at,(side==='L'?-1:1)*spec.lateral,spec.elevation);
      const yaw = trackYaw(at)+(side==='L'?Math.PI/2:-Math.PI/2);
      const frameGroup = frame(w,h,point,yaw,false,room,true);
      const node = element(doc,'div','td-plane td-exhibit td-personal-screen',domCamera);
      node.dataset.exhibit=exhibit.id; node.dataset.room=room;
      placeNicheFace(node,frameGroup,spec);
      const face = element(doc,'div','td-art',node);
      face.style.background = '#111820';
      surfaces.push({node,frameGroup,exhibit,at,side,w,h,yaw,room,faces:[{face,kind:exhibit.kind}],mounts:[],personal:true,niche:spec});
    }
  }
  function buildProjectors() {
    for (const spec of STORY_PROJECTORS) {
      const w=6.2,h=3.1,top=4.3;
      const group = new THREE.Group(), fixed = new THREE.Group();
      const point=positionAt(spec.at,0,0), yaw=trackYaw(spec.at);
      group.position.set(point.x,point.y,point.z); group.rotation.y=yaw;
      group.add(fixed); world.add(group);
      box(fixed,'Motorized projection screen case',[w+.28,.20,.27],[0,top+.14,0],materials.iron);
      box(fixed,'Case end trim',[w+.34,.10,.28],[0,top+.14,0],materials.chrome||materials.brass);
      box(fixed,'Roller slot',[w,.018,.06],[0,top+.035,.12],materials.dark);
      for (const side of [-1,1]) {
        box(fixed,'Ceiling suspension bracket',[.05,.5,.08],[side*(w/2-.3),top+.47,0],materials.iron);
        box(fixed,'Screen motor end cap',[.12,.22,.30],[side*(w/2+.12),top+.14,0],materials.dark);
      }
      const cloth=box(group,'Projection fabric',[w,h,.014],[0,top-h/2,.11],materials.paper);
      const bar=box(group,'Weighted screen rail',[w+.04,.045,.045],[0,top-h,.11],materials.iron);
      const node=spec.id==='reflex'?replay:element(doc,'div','td-plane td-projector-face',domCamera);
      node.dataset.projector=spec.id;
      const projector={...spec,w,h,top,group,cloth,bar,node,progress:0,target:0,override:null,mounts:[]};
      projectorContent(projector,spec.slot);
      cloth.visible=bar.visible=false; node.hidden=true;
      projectors.push(projector); batchDisplay(fixed);
    }
  }
  function projectorContent(projector,slot,title,line) {
    const validSlot=isDocumentaryArtSlot(slot)?slot:projector.slot;
    if(projector.activeSlot===validSlot && projector.node.dataset.requestedSlot===validSlot) {
      if(title) projector.node.querySelector('.td-exposition-value').textContent=String(title);
      return;
    }
    const version=(projector.contentVersion||0)+1;
    projector.contentVersion=version;projector.node.dataset.requestedSlot=validSlot;
    const staging=doc.createElement('div');
    const face=plate(staging,validSlot,title);
    face.querySelector('.td-card-figure')?.remove();
    cardFigure(face.querySelector('.td-exposition-card'),ART_CARD_ALIAS[validSlot]||validSlot,true);
    if(title) face.querySelector('.td-exposition-value').textContent=String(title);
    if(line) element(doc,'p','td-projection-caption',staging,String(line));
    warmArt(staging);
    const image=face.querySelector('.td-art-image');
    const commit=()=>{
      if(disposed||projector.contentVersion!==version) return;
      const outgoing=[...projector.node.querySelectorAll(':scope > .td-art')].at(-1);
      for(const child of [...projector.node.children]) if(child!==outgoing) child.remove();
      for(const layer of [outgoing,face]) if(layer) Object.assign(layer.style,{position:'absolute',inset:'0'});
      projector.node.append(...staging.childNodes);
      projectorEffects.transition(face,{outgoing,key:`${projector.id}:${version}`,initial:!projector.activeSlot,
        visible:!projector.node.hidden&&projector.progress>.05});
      projector.activeSlot=validSlot;projector.node.dataset.slot=validSlot;
      projector.node.dataset.slideReady=String(Boolean(image?.naturalWidth));
    };
    if(image?.decode) image.decode().then(commit,commit);
    else if(image&&!image.complete) { image.addEventListener('load',commit,{once:true});image.addEventListener('error',commit,{once:true}); }
    else commit();
  }
  function setProjectorTarget(projector,target) {
    if(target===1&&projector.target!==1&&readyState) projectorEffects.unroll(projector.id);
    projector.target=target;
    if(ctx.reduced) { projector.progress=target;if(!target) projectorEffects.resetUnroll(projector.id);return; }
    if(projectorFrameOff || projectors.every(item=>item.progress===item.target)) return;
    projectorFrameOff=ctx.frame(dt=>{
      if(disposed||ctx.paused||doc.hidden) return;
      let moving=false;
      for(const item of projectors) {
        const step=Math.max(0,dt)/(ctx.gentle?1.6:1.1);
        item.progress=item.target>item.progress?Math.min(item.target,item.progress+step):Math.max(item.target,item.progress-step);
        if(item.progress===0) projectorEffects.resetUnroll(item.id);
        moving ||= item.progress!==item.target;
      }
      if(lastState?.docked) draw(lastState);
      if(!moving) { projectorFrameOff?.(); projectorFrameOff=null; }
    });
  }
  function projectorBeat(id,data={}) {
    if(!projectors.length) { pendingProjectorBeats.push([id,data]); return; }
    const requested=typeof data==='string'?{id:data}:data||{};
    const physicalId=requested.id==='replay'?'reflex':requested.id;
    const projector=projectors.find(item=>item.id===physicalId)||projectors.reduce((best,item)=>
      Math.abs(item.at-(Number(requested.at)||lastState?.z||0))<Math.abs(best.at-(Number(requested.at)||lastState?.z||0))?item:best);
    const show=id.endsWith('.show');
    if(show) scriptedProjectors=true;
    projector.override=show;
    if(show && (requested.slot||requested.title||requested.line)) projectorContent(projector,requested.slot,requested.title,requested.line);
    if(lastState) draw(lastState);
  }
  function drawProjectors(state) {
    for(const projector of projectors) {
      const distance=projector.at-state.z;
      const approach=distance<14&&distance>Math.max(5,state.v*.65+2);
      const requested=projector.override??((!scriptedProjectors||narrationSpeaking)&&approach);
      const target=requested&&distance>3.8&&distance<16?1:0;
      setProjectorTarget(projector,target);
      const p=projector.progress;
      projector.cloth.scale.y=Math.max(.001,p);
      projector.cloth.position.y=projector.top-projector.h*p/2;
      projector.bar.position.y=projector.top-projector.h*p;
      const center=positionAt(projector.at,0,projector.top-projector.h*p/2);
      pointInView.set(center.x,center.y,center.z).applyMatrix4(camera.matrixWorldInverse);
      const visible=p>.001&&pointInView.z<-.15&&distance>-4&&distance<depth&&
        !museum.occludes?.(center,camera.position);
      projector.group.visible=distance>-18&&distance<depth;
      projector.cloth.visible=projector.bar.visible=p>.001;
      projector.node.hidden=!visible;
      const point=positionAt(projector.at,0,projector.top-projector.h/2),yaw=trackYaw(projector.at);
      point.x+=Math.sin(yaw)*.127; point.z+=Math.cos(yaw)*.127;
      cssPlace(projector.node,point,yaw,projector.w-.05,projector.h-.025);
      projector.node.style.clipPath=`inset(0 0 ${(1-p)*100}% 0)`;
      if(visible) warmArt(projector.node);
    }
  }
  function scheduleBulb() {
    if(disposed||bulbJob||bulbRestore||!lastState?.docked||ctx.gentle||ctx.reduced) return;
    const delay=[6200,9100,7300,8400][bulbCycle++%4];
    bulbJob=ctx.after(delay,()=>{
      bulbJob=null;
      if(disposed||!lastState?.docked) return;
      if(ctx.paused||doc.hidden||ctx.gentle||ctx.reduced) { scheduleBulb(); return; }
      bulbDim=true; props.update(lastState.z,{time:0,gentle:false});
      renderer.render(world,camera); renders++;
      bulbRestore=ctx.after(1600,()=>{
        bulbRestore=null; bulbDim=false;
        if(disposed) return;
        props.update(lastState.z,{gentle:true});
        if(!ctx.paused&&!doc.hidden) { renderer.render(world,camera); renders++; }
        scheduleBulb();
      });
    });
  }
  const ready = Promise.resolve().then(() => createMuseumMaterials(THREE, { lite })).then(async pack => {
    materialPack = pack; materials = pack.materials;
    world.environment = pack.environment || null;
    world.environmentIntensity = .35;
    if (disposed) { pack.dispose(); return; }
    museum = createMuseum(THREE, materials, { lite }); world.add(museum.group);
    props = createMuseumProps(THREE, materials, { lite, onWake }); world.add(props.group); cartRig.add(props.cart);
    props.setAdVisible(!subscriberPlan(ctx.caps));
    buildExhibits();
    await Promise.all([pack.ready, props.ready, preloadStoryArt()]);
    if (disposed) return;
    readyState = true; loading.remove();
    for(const [id,data] of pendingProjectorBeats.splice(0)) projectorBeat(id,data);
    resize();
  }).catch(error => { loading.textContent = 'The gallery could not open'; throw error; });

  function releaseSurface(surface) { for (const release of surface.mounts) release(); surface.mounts.length = 0; }
  function resize() {
    if (disposed) return;
    const rect = root.getBoundingClientRect();
    width = rect.width || 1;
    height = rect.height || 1;
    camera.aspect = width / height;
    camera.fov = width < 600 ? 85 : 61;
    lensTime = 0;
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
    camera.position.set(point.x, point.y + 1.45 + bob, point.z);
    camera.rotation.set(pitch + .055 + lookPitch, viewYaw + lookYaw, bank, 'YXZ');
    camera.updateMatrixWorld();
    let fov = (width < 600 ? 85 : 61) + (calm ? 0 : THREE.MathUtils.smoothstep(state.v, 4, 14) * 5);
    // Portrait windows need a wider lens as the physical screen approaches.
    // Keep the entire photograph in view without changing the player's look direction.
    if (width < 600) {
      const viewing = projectors.find(item => item.at-state.z>3.8 && item.at-state.z<16 &&
        (item.override===true || item.progress>0));
      if(viewing) {
        let halfWidthRatio = viewing.w/(2*Math.max(4.5,viewing.at-state.z));
        // A bend can put the far edge off-centre even when its total width fits.
        if(Math.abs(lookYaw)<.35&&Math.abs(lookPitch)<.25) {
          viewing.group.updateWorldMatrix(true,false);
          for(const x of [-viewing.w/2,viewing.w/2]) for(const y of [viewing.top,viewing.top-viewing.h]) {
            const corner=new THREE.Vector3(x,y,.13).applyMatrix4(viewing.group.matrixWorld).applyMatrix4(camera.matrixWorldInverse);
            if(corner.z<-.5) halfWidthRatio=Math.max(halfWidthRatio,Math.abs(corner.x)/-corner.z);
          }
        }
        const fit = 2*Math.atan((height/width)*halfWidthRatio/.84)*180/Math.PI;
        fov = Math.max(fov,Math.min(115,fit));
      }
    }
    const lensNow = doc.defaultView.performance.now();
    const lensBlend = calm || !lensTime ? 1 : 1-Math.exp(-Math.max(0,lensNow-lensTime)*.008);
    lensTime = lensNow;
    if (camera.fov !== fov) { camera.fov += (fov-camera.fov)*lensBlend; camera.updateProjectionMatrix(); }
    focal = height * .5 * camera.projectionMatrix.elements[5];
    viewport.style.perspective = `${focal}px`;
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
    museum.update(state.z,{time:doc.defaultView.performance.now()/1000,gentle:calm||ctx.paused});
    props.update(state.z,{time:0,gentle:calm||!bulbDim});
    props.pump(state.leverAngle);
    const lit = [], visiblePersonal = [];
    for (const surface of surfaces) {
      const distance = surface.at - state.z;
      pointInView.copy(surface.frameGroup.position).applyMatrix4(camera.matrixWorldInverse);
      surfaceSphere.center.copy(surface.frameGroup.position);
      surfaceSphere.radius = Math.hypot(surface.w, surface.h) * .55;
      let visible = distance > -18 && distance < depth && pointInView.z < -.2 &&
        frustum.intersectsSphere(surfaceSphere) &&
        !museum.occludes?.(surface.frameGroup.position, camera.position);
      if (visible && surface.niche) visible = clipNicheFace(surface);
      surface.node.hidden = !visible;
      surface.frameGroup.visible = visible;
      if (visible) {
        surface.node.style.filter = `brightness(${Math.max(.52, Math.min(1, (depth - Math.abs(distance)) / (depth - 8)))})`;
        if (surface.placard) surface.placard.hidden = Math.abs(distance) > 14;
        lit.push(surface);
      }
      if (distance < depth + 12 && distance > -18) warmArt(surface.node);
      if(visible&&surface.personal) visiblePersonal.push(surface);
      if (visible && !surface.personal && !surface.mounts.length) {
        for (const { face, kind } of surface.faces) surface.mounts.push(media.mount(face, { ...surface.exhibit, kind }));
      }
      if (!visible) releaseSurface(surface);
    }
    visiblePersonal.sort((a,b)=>Math.abs(a.at-state.z)-Math.abs(b.at-state.z));
    const activePersonal=new Set(visiblePersonal.slice(0,lite?10:16));
    for(const surface of surfaces) if(surface.personal&&!activePersonal.has(surface)) releaseSurface(surface);
    for(const surface of activePersonal) if(!surface.mounts.length) {
      for(const {face,kind} of surface.faces) surface.mounts.push(media.mount(face,{...surface.exhibit,kind}));
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
    drawProjectors(state);
    const stagePoint = positionAt(587, 0, 2.35);
    pointInView.set(stagePoint.x, stagePoint.y, stagePoint.z).applyMatrix4(camera.matrixWorldInverse);
    stageAnchor.hidden = state.z < 535 || pointInView.z > -.2;
    const controlPoint=positionAt(586.70,0,1.45);
    pointInView.set(controlPoint.x,controlPoint.y,controlPoint.z).project(camera);
    stageControlsAnchor.hidden=state.z<570||pointInView.z< -1||pointInView.z>1||Math.abs(pointInView.x)>1.1||Math.abs(pointInView.y)>1.1||
      Boolean(museum.occludes?.(controlPoint,camera.position));
    stageControlsAnchor.style.left=`${(pointInView.x*.5+.5)*width}px`;
    stageControlsAnchor.style.top=`${(-pointInView.y*.5+.5)*height}px`;
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
    hint.textContent = state.docked ? 'Drag to look around' : state.locked ? 'Drag to look around. Please wait at the platform' : state.holds.length ? 'Viewing pause. Drag to look around' : 'Drag to look. Click the lever or press Space to pump';
    centerButton.dataset.turned = String(Math.abs(lookYaw) + Math.abs(lookPitch) > .03);
    wash.hidden = state.band !== 4 || ctx.reduced;
    streaks.hidden = state.band !== 4 || ctx.reduced || ctx.gentle;
    root.dataset.gentle = String(Boolean(ctx.gentle || ctx.reduced));
    root.dataset.docked = String(state.docked);
    renderer.render(world, camera); renders++;
    if(state.docked) scheduleBulb();
    const pumpTarget = props.pumpTargets?.[0];
    pumpPosition = null;
    if (pumpTarget) {
      if (props.pumpGrip) props.pumpGrip.getWorldPosition(projectedPump);
      else pumpTarget.localToWorld(projectedPump.set(0,.27,0));
      projectedPump.project(camera);
      pumpPosition = { x: (projectedPump.x*.5+.5)*width, y: (-projectedPump.y*.5+.5)*height,
        visible: Math.abs(projectedPump.x) < 1 && Math.abs(projectedPump.y) < 1 && projectedPump.z > -1 && projectedPump.z < 1 };
    }
    let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;
    for(const x of [pumpBounds.min.x,pumpBounds.max.x]) for(const y of [pumpBounds.min.y,pumpBounds.max.y]) {
      for(const z of [pumpBounds.min.z,pumpBounds.max.z]) {
        projectedPump.set(x,y,z).applyMatrix4(cartRig.matrixWorld).project(camera);
        if(projectedPump.z< -1||projectedPump.z>1) continue;
        const px=(projectedPump.x*.5+.5)*width,py=(-projectedPump.y*.5+.5)*height;
        minX=Math.min(minX,px);maxX=Math.max(maxX,px);minY=Math.min(minY,py);maxY=Math.max(maxY,py);
      }
    }
    pumpHitBounds=Number.isFinite(minX)?{left:minX,top:minY,right:maxX,bottom:maxY,
      visible:maxX>0&&minX<width&&maxY>0&&minY<height}:null;
  }
  function beat(id,data={}) {
    if (disposed) return;
    if(['projector.show','projector.hide','story.show','story.hide'].includes(id)) { projectorBeat(id,data); return; }
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
      domCamera.querySelectorAll('.td-kind-tab-wall .td-tab').forEach((tile, i) => {
        animate(tile.querySelector('.td-art'), [
          { opacity: 1, transform: 'scaleY(1)' },
          { opacity: 0, transform: gentle ? 'scaleY(1)' : 'scaleY(.01)' },
        ], gentle ? 600 : 160, gentle ? 0 : i % 6 * 20);
      });
    }
    if (id === 'floor.replay') {
      replay.dataset.mode = 'replay';
      projectorBeat('projector.show',{id:'reflex',slot:'subject-spreadsheet',title:'Again, in slow motion',line:''});
    }
    if (id === 'floor.freeze') {
      replay.dataset.mode = 'freeze';
      projectorBeat('projector.show',{id:'reflex',slot:'subject-spreadsheet',line:''});
    }
    if (id === 'floor.release') {
      projectorBeat('projector.hide',{id:'reflex'});
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
  function pointerRay(event) {
    const rect=root.getBoundingClientRect();
    pointer.set((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1);
    raycaster.setFromCamera(pointer,camera);
  }
  function hitsPump(event) {
    if(!props?.pumpTargets?.length) return false;
    pointerRay(event);
    cartRig.updateWorldMatrix(true,false);
    cartInverse.copy(cartRig.matrixWorld).invert();
    pumpRay.copy(raycaster.ray).applyMatrix4(cartInverse);
    return Boolean(pumpRay.intersectBox(pumpBounds,pumpLocalHit));
  }
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
    gesture = { id: event.pointerId, x: event.clientX, y: event.clientY, yaw: lookYaw, pitch: lookPitch,
      dragged: false, pump: hitsPump(event) };
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
    const pumpStarted=gesture.pump;
    cancelGesture();
    if (dragged || !readyState || disposed || ctx.paused || doc.hidden) return;
    if(pumpStarted||hitsPump(event)) {
      onPump?.();event.preventDefault();return;
    }
    pointerRay(event);
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
      if (entry?.onPress && !museum.occludes?.(hit.point, camera.position)) { entry.onPress(); return; }
    }
    if(lastState?.docked) onStageAction?.({id:'empty-click'});
  }
  root.addEventListener('pointerdown', pointerDown);
  root.addEventListener('pointermove', pointerMove);
  root.addEventListener('pointerup', pointerUp);
  root.addEventListener('pointercancel', cancelGesture);
  root.addEventListener('lostpointercapture', cancelGesture);
  resize();
  return {
    ready, stageAnchor, stageControlsAnchor, draw, resize, beat,
    debug: () => ({ drawCalls: renderer.info.render.calls, triangles: renderer.info.render.triangles,
      textures: renderer.info.memory.textures, ready: readyState, renders, lookYaw, lookPitch,
      cameraPosition: camera.position.toArray(), pumpPosition, pumpHitBounds,
      projectors:projectors.map(({id,at,progress,target,activeSlot,node})=>({id,at,progress,target,slot:activeSlot||null,
        requestedSlot:node.dataset.requestedSlot||null,imageReady:node.dataset.slideReady==='true'})),
      storyArt:Object.fromEntries(storyImageStatus), projectorEffects:projectorEffects.debug(), environment: museum?.debug?.(),
      personalScreens:surfaces.filter(surface=>surface.personal).length,
      activePersonalScreens:surfaces.filter(surface=>surface.personal&&surface.mounts.length).length }),
    setSpeaking(on) { narrationSpeaking=Boolean(on);museum?.setSpeaking?.(narrationSpeaking); },
    setPaused(on) {
      if (on) cancelGesture();
      root.dataset.paused = String(Boolean(on));
      for (const animation of animated) if (on) animation.pause(); else animation.play();
      projectorEffects.pause(on);
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      projectorEffects.dispose();
      toyFrameOff?.();
      projectorFrameOff?.();
      bulbJob?.(); bulbRestore?.();
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
      for(const job of [...artLoads]) { job.finish(false);job.image.removeAttribute('src'); }
      renderer.dispose();
      renderer.forceContextLoss();
      art.clear();
    },
  };
}
