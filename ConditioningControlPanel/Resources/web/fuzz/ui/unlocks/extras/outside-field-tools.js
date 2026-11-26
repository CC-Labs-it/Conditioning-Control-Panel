// Interaction for the thirty-second meadow. The game's own effect bus owns every sound.
import { AIR, GRASS, DIRT, LOG, LEAVES, tileFor } from './outside-meadow.js';

const REACH = 4.5;
const itemName = (id) => (id === LOG ? 'Wood' : 'Dirt');
const canDig = (id) => id === GRASS || id === DIRT || id === LOG || id === LEAVES;

// Advance from face to face so a block behind a nearer surface is never selected.
export function pickBlock(volume, origin, direction, reach = REACH) {
  const cell = origin.map(Math.floor),
    step = direction.map(Math.sign);
  const stride = direction.map((v) => (v ? 1 / Math.abs(v) : Infinity));
  const next = direction.map((v, i) => (v ? ((v > 0 ? cell[i] + 1 : cell[i]) - origin[i]) / v : Infinity));
  let face = [0, 0, 0],
    distance = 0;
  while (distance <= reach) {
    const id = volume.get(...cell);
    if (id) return { x: cell[0], y: cell[1], z: cell[2], id, face, distance };
    const i = next[0] <= next[1] && next[0] <= next[2] ? 0 : next[1] <= next[2] ? 1 : 2;
    distance = next[i];
    next[i] += stride[i];
    cell[i] += step[i];
    face = [0, 0, 0];
    face[i] = -step[i];
  }
  return null;
}

function change(world, x, y, z, id) {
  const column = z * world.W + x;
  world.blocks[(y * world.W + z) * world.W + x] = id;
  let surface = world.heights[column];
  if (id === DIRT || id === GRASS) surface = Math.max(surface, y + 1);
  while (surface > 1 && !world.blocks[((surface - 1) * world.W + z) * world.W + x]) surface--;
  world.heights[column] = surface;
}
export function breakBlock(world, volume, hit) {
  if (!hit || hit.y <= 0 || !canDig(hit.id) || volume.get(hit.x, hit.y, hit.z) !== hit.id) return null;
  change(world, hit.x, hit.y, hit.z, AIR);
  return hit.id === GRASS ? DIRT : hit.id;
}
export function placeBlock(world, volume, hit, id, count, player) {
  if (!hit || !count || (id !== DIRT && id !== LOG) || !hit.face.some(Boolean)) return null;
  const x = hit.x + hit.face[0],
    y = hit.y + hit.face[1],
    z = hit.z + hit.face[2];
  if (x < 0 || z < 0 || x >= world.W || z >= world.W || y < 1 || y >= world.H || volume.get(x, y, z)) return null;
  if (
    player.x + 0.28 > x &&
    player.x - 0.28 < x + 1 &&
    player.z + 0.28 > z &&
    player.z - 0.28 < z + 1 &&
    player.feet + 1.8 > y &&
    player.feet < y + 1
  )
    return null;
  change(world, x, y, z, id);
  return { x, y, z };
}

function node(parent, tag, css, label) {
  const n = document.createElement(tag);
  n.style.cssText = css;
  if (label) n.textContent = label;
  parent.appendChild(n);
  return n;
}
function icon(canvas, atlas, id) {
  const g = canvas.getContext('2d'),
    t = atlas.json.tile;
  canvas.width = 32;
  canvas.height = 32;
  g.imageSmoothingEnabled = false;
  const faces = [
    [2, [1, 0.49, -1, 0.49, 16, 1], 1],
    [0, [1, -0.49, 0, 1, 16, 17], 0.78],
    [4, [1, 0.49, 0, 1, 1, 9], 0.9]
  ];
  for (const [f, m, shade] of faces) {
    const [u, v] = atlas.json.tiles[tileFor(id, f)];
    g.setTransform(...m);
    g.drawImage(atlas.source, u * t, v * t, t, t, 0, 0, 15.5, 15);
    g.fillStyle = 'rgba(20,15,18,' + (1 - shade) + ')';
    g.fillRect(0, 0, 15.5, 15);
  }
}
function makeHud(host, atlas, touch, reduced, select, signal) {
  const root = node(
    host,
    'div',
    'position:absolute;inset:0;pointer-events:none;z-index:2;color:#fff;font:700 14px/1 monospace;text-shadow:1px 2px #201e20;display:none;'
  );
  root.dataset.field = 'hud';
  const aim = node(
    root,
    'div',
    'position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);font-size:22px;text-shadow:0 1px 2px #222;',
    '+'
  );
  const bar = node(
    root,
    'div',
    'position:absolute;left:50%;bottom:max(14px,env(safe-area-inset-bottom));transform:translateX(-50%);display:flex;gap:3px;padding:3px;background:#241e18a6;border:1px solid #ffffff70;'
  );
  const slots = new Map();
  for (const id of [DIRT, LOG]) {
    const b = node(
      bar,
      'button',
      'position:relative;width:38px;height:38px;padding:2px;border:1px solid #211e19;box-shadow:inset 0 0 0 1px #ffffff70;background:#4c4946b8;color:white;pointer-events:auto;touch-action:none;cursor:pointer;'
    );
    b.type = 'button';
    b.title = itemName(id);
    b.setAttribute('aria-label', itemName(id));
    icon(node(b, 'canvas', 'width:25px;height:25px;image-rendering:pixelated;'), atlas, id);
    const count = node(
      b,
      'span',
      'position:absolute;right:1px;bottom:1px;font:bold 13px monospace;text-shadow:1px 1px #15110d;',
      '0'
    );
    b.onclick = (e) => {
      e.stopPropagation();
      select(id);
    };
    slots.set(id, { b, count });
  }
  const hand = node(
    root,
    'div',
    'position:absolute;right:19%;bottom:-44px;width:clamp(48px,8vmin,90px);height:clamp(120px,23vmin,210px);transform-origin:50% 110%;background:linear-gradient(90deg,#d9a88e 0 67%,#ad7867 67%);box-shadow:inset 0 -7.5em #be6e78;clip-path:polygon(0 9%,66% 0,100% 9%,100% 100%,0 100%);'
  );
  hand.style.transform = 'rotate(-31deg)';
  hand.style.zIndex = '-1';
  if (touch) hand.style.right = '16%';
  const jumpStyles =
    'position:absolute;pointer-events:auto;touch-action:none;background:#241e1899;color:white;border:1px solid #ffffffa0;font:700 13px monospace;text-shadow:1px 1px #29221c;';
  const inputs = { forward: 0, side: 0, mine: false, jump: false, place: false };
  if (touch) {
    const pad = node(
      root,
      'div',
      jumpStyles +
        'left:max(12px,env(safe-area-inset-left));bottom:max(16px,env(safe-area-inset-bottom));width:78px;height:78px;border-radius:50%;display:grid;place-items:center;'
    );
    pad.setAttribute('aria-label', 'Move');
    const nub = node(
      pad,
      'span',
      'width:32px;height:32px;border-radius:50%;background:#fff9;box-shadow:0 1px 4px #2219;pointer-events:none;'
    );
    let padPointer = null, padRect = null;
    host.ownerDocument.defaultView.addEventListener('resize', () => { padRect = null; }, { signal });
    const move = (e) => {
      const r = padRect ||= pad.getBoundingClientRect(),
        dx = (e.clientX - r.left - r.width / 2) / 28,
        dy = (e.clientY - r.top - r.height / 2) / 28;
      const scale = Math.max(1, Math.hypot(dx, dy));
      inputs.side = dx / scale;
      inputs.forward = -dy / scale;
      nub.style.transform = 'translate(' + inputs.side * 22 + 'px,' + -inputs.forward * 22 + 'px)';
    };
    pad.onpointerdown = (e) => {
      if (padPointer !== null) return;
      padPointer = e.pointerId;
      padRect = null;
      pad.setPointerCapture(e.pointerId);
      move(e);
    };
    pad.onpointermove = (e) => {
      if (e.pointerId === padPointer) move(e);
    };
    pad.onlostpointercapture = () => {
      padPointer = null;
      inputs.forward = inputs.side = 0;
      nub.style.transform = 'none';
    };
    for (const [name, css] of [
      ['Dig', 'right:12px;bottom:18px;width:52px;height:48px;'],
      ['Place', 'right:70px;bottom:18px;width:50px;height:40px;'],
      ['Jump', 'right:15px;bottom:72px;width:48px;height:36px;']
    ]) {
      const b = node(root, 'button', jumpStyles + css, name);
      b.type = 'button';
      b.onpointerdown = (e) => {
        e.preventDefault();
        b.setPointerCapture(e.pointerId);
        if (name === 'Dig') inputs.mine = true;
        else inputs[name.toLowerCase()] = true;
      };
      if (name === 'Dig')
        b.onlostpointercapture = () => {
          inputs.mine = false;
        };
    }
  }
  return {
    root,
    aim,
    inputs,
    select(id) {
      for (const [key, { b }] of slots) {
        b.style.outline = key === id ? '2px solid #fff9d8' : 'none';
        b.setAttribute('aria-pressed', String(key === id));
      }
    },
    count(id, n) {
      const { b, count } = slots.get(id);
      count.textContent = String(n);
      b.setAttribute('aria-label', itemName(id) + ' ' + n);
      b.dataset.count = n;
      if (!reduced) b.animate?.([{ transform: 'scale(1.17)' }, { transform: 'scale(1)' }], { duration: 170 });
    },
    swing(v) {
      hand.style.transform = 'translate(' + -v * 16 + '%,' + v * 8 + '%) rotate(' + (-31 - v * 16) + 'deg)';
    }
  };
}
function crackTextures(T, keep) {
  const paths = [
    [
      [7, 8],
      [6, 6],
      [4, 6],
      [3, 3],
      [1, 2]
    ],
    [
      [7, 8],
      [10, 7],
      [11, 5],
      [14, 4]
    ],
    [
      [7, 8],
      [8, 11],
      [7, 13],
      [8, 15]
    ],
    [
      [10, 7],
      [12, 8],
      [15, 8]
    ],
    [
      [6, 6],
      [6, 3],
      [8, 1]
    ],
    [
      [8, 11],
      [11, 12],
      [13, 14]
    ],
    [
      [6, 9],
      [4, 11],
      [2, 11],
      [0, 13]
    ]
  ];
  return Array.from({ length: 10 }, (_, i) => {
    const c = document.createElement('canvas');
    c.width = c.height = 16;
    const g = c.getContext('2d');
    g.strokeStyle = '#231b19';
    g.lineWidth = 1;
    for (let j = 0; j < paths.length; j++) {
      const n = Math.min(paths[j].length, Math.ceil((i + 1) * 0.62 - j * 0.48));
      if (n < 2) continue;
      g.beginPath();
      paths[j].slice(0, n).forEach(([x, y], k) => (k ? g.lineTo(x + 0.5, y + 0.5) : g.moveTo(x + 0.5, y + 0.5)));
      g.stroke();
    }
    const tex = keep(new T.CanvasTexture(c));
    tex.magFilter = tex.minFilter = T.NearestFilter;
    tex.generateMipmaps = false;
    return tex;
  });
}

export function createFieldTools({
  T,
  world,
  volume,
  scene,
  camera,
  atlas,
  makeBlock,
  keep,
  remesh,
  host,
  audio,
  touch,
  reduced,
  paused,
  me
}) {
  const counts = new Map([
    [DIRT, 0],
    [LOG, 0]
  ]);
  let selected = DIRT,
    hit = null,
    target = '',
    progress = 0,
    rest = 0,
    swing = 0,
    stepDistance = 0;
  const select = (id) => {
    if (!paused()) {
      selected = id;
      hud.select(id);
    }
  };
  const signal = new AbortController();
  const hud = makeHud(host, atlas, touch, reduced, select, signal.signal);
  hud.select(selected);
  const box = keep(new T.BoxGeometry(1.004, 1.004, 1.004));
  const line = new T.LineSegments(
    keep(new T.EdgesGeometry(box)),
    keep(new T.LineBasicMaterial({ color: 0x342921, transparent: true, opacity: 0.55 }))
  );
  const crackMap = crackTextures(T, keep);
  const crackMat = keep(
    new T.MeshBasicMaterial({
      transparent: true,
      opacity: 0.82,
      map: crackMap[0],
      depthWrite: false,
      polygonOffset: true,
      polygonOffsetFactor: -2
    })
  );
  const cracks = new T.Mesh(box, crackMat);
  cracks.scale.setScalar(1.001);
  line.visible = cracks.visible = false;
  scene.add(line, cracks);
  const chipGeo = keep(new T.BoxGeometry(1, 1, 1));
  const chips = new T.InstancedMesh(chipGeo, keep(new T.MeshBasicMaterial()), 64);
  chips.count = 0;
  chips.frustumCulled = false;
  scene.add(chips);
  const flakes = [],
    drops = [],
    direction = new T.Vector3(),
    dummy = new T.Object3D(),
    tint = new T.Color();

  const sound = (path, gain, rate) => {
    audio?.play?.('assets/audio/' + path, {
      bus: 'sfx',
      source: 'outside-blocks',
      sourceLabel: 'Meadow',
      gain,
      rate,
      signal: signal.signal
    });
  };
  const knock = (id, finish) => {
    const wood = id === LOG;
    sound(
      wood ? 'pluck/wood_04.wav' : 'sfx/thud.mp3',
      finish ? 0.23 : 0.12,
      (wood ? 0.75 : 0.88) * (finish ? 1 : 0.8) + Math.random() * 0.13
    );
  };
  function chipsAt(id, x, y, z, n) {
    if (reduced) return;
    for (let i = 0; i < n; i++) {
      if (flakes.length >= 64) flakes.shift();
      flakes.push({
        x,
        y,
        z,
        vx: (Math.random() - 0.5) * 4,
        vy: 1 + Math.random() * 2.1,
        vz: (Math.random() - 0.5) * 4,
        age: 0,
        life: 0.22 + Math.random() * 0.32,
        size: 0.04 + Math.random() * 0.095,
        color: (id === LOG ? [0x805237, 0x9f7544] : id === LEAVES ? [0x589144, 0x3d7536] : [0x936a49, 0x6b5035])[
          i % 2
        ]
      });
    }
  }
  const items = new Map([DIRT, LOG].map((id) => [id, makeBlock(id)]));
  function harvest(h) {
    const id = breakBlock(world, volume, h);
    if (!id) return;
    remesh(h.x, h.y, h.z, true);
    knock(h.id, true);
    chipsAt(h.id, h.x + 0.5, h.y + 0.5, h.z + 0.5, 20);
    if (id === LEAVES) return;
    const mesh = items.get(id).clone();
    mesh.position.set(h.x + 0.5, h.y + 0.55, h.z + 0.5);
    mesh.scale.setScalar(0.25);
    scene.add(mesh);
    drops.push({ mesh, id, age: 0, base: mesh.position.clone() });
  }
  function place() {
    const cell = placeBlock(world, volume, hit, selected, counts.get(selected), me);
    if (!cell) return;
    counts.set(selected, counts.get(selected) - 1);
    hud.count(selected, counts.get(selected));
    remesh(cell.x, cell.y, cell.z, false);
    knock(selected, true);
    swing = 1;
    rest = 0.3;
  }
  const eyeCoords = [0, 0, 0], rayCoords = [0, 0, 0];
  let lastSwing = NaN;
  function frame(dt, live, mining, placeRequested, walked) {
    camera.getWorldDirection(direction);
    hit = live ? pickBlock(volume, camera.position.toArray(eyeCoords), direction.toArray(rayCoords)) : null;
    const key = hit ? hit.x + ',' + hit.y + ',' + hit.z : '';
    if (key !== target || !mining) progress = 0;
    target = key;
    rest = Math.max(0, rest - dt);
    swing = Math.max(0, swing - dt * 5);
    line.visible = !!hit;
    cracks.visible = false;
    if (hit) {
      line.position.set(hit.x + 0.5, hit.y + 0.5, hit.z + 0.5);
      cracks.position.copy(line.position);
    }
    if (placeRequested && live && !paused()) place();
    if (live && mining && hit && canDig(hit.id) && hit.y > 0 && rest === 0 && !paused()) {
      const before = progress;
      progress += dt;
      if (before === 0 || Math.floor(progress / 0.2) > Math.floor(before / 0.2)) {
        swing = 1;
        knock(hit.id, false);
        chipsAt(
          hit.id,
          hit.x + 0.5 + hit.face[0] * 0.55,
          hit.y + 0.5 + hit.face[1] * 0.55,
          hit.z + 0.5 + hit.face[2] * 0.55,
          2
        );
      }
      const fraction = progress / (hit.id === LOG ? 1.15 : hit.id === LEAVES ? 0.34 : 0.75);
      if (fraction >= 1) {
        harvest(hit);
        progress = 0;
        rest = 0.3;
        line.visible = false;
      } else if (fraction > 0.1) {
        crackMat.map = crackMap[Math.min(9, Math.floor(Math.pow(fraction, 0.8) * 10))];
        cracks.visible = true;
      }
    }
    if (live) {
      stepDistance += walked;
      if (stepDistance >= 1.67) {
        stepDistance %= 1.67;
        sound('sfx/thud.mp3', 0.048, 0.66 + Math.random() * 0.14);
      }
    }
    for (let i = flakes.length - 1; i >= 0; i--) {
      const p = flakes[i];
      p.age += dt;
      if (p.age >= p.life) {
        flakes.splice(i, 1);
        continue;
      }
      p.vy -= 9 * dt;
      p.x += p.vx * dt;
      p.y += p.vy * dt;
      p.z += p.vz * dt;
    }
    flakes.forEach((p, i) => {
      dummy.position.set(p.x, p.y, p.z);
      dummy.rotation.set(p.age * 5, p.age * 3, 0);
      dummy.scale.setScalar(p.size * (1 - p.age / p.life));
      dummy.updateMatrix();
      chips.setMatrixAt(i, dummy.matrix);
      chips.setColorAt(i, tint.setHex(p.color));
    });
    chips.count = flakes.length;
    chips.instanceMatrix.needsUpdate = true;
    if (chips.instanceColor) chips.instanceColor.needsUpdate = true;
    for (let i = drops.length - 1; i >= 0; i--) {
      const d = drops[i];
      d.age += dt;
      const p = d.mesh.position;
      if (d.age <= 0.45) {
        p.y = d.base.y + (reduced ? 0 : Math.sin((d.age / 0.45) * Math.PI) * 0.4);
      } else {
        const fly = Math.min(1, (d.age - 0.45) / 0.24);
        p.copy(d.base).lerp(camera.position, fly * fly);
        if (fly === 1) {
          counts.set(d.id, counts.get(d.id) + 1);
          hud.count(d.id, counts.get(d.id));
          if (live) sound('sfx/chip_pop.mp3', 0.18, 1.45 + Math.random() * 0.4);
          scene.remove(d.mesh);
          drops.splice(i, 1);
          continue;
        }
      }
      if (!reduced) d.mesh.rotation.y += dt * 4;
    }
    const handSwing = reduced ? 0 : Math.sin(swing * Math.PI * 0.8);
    if (live && handSwing !== lastSwing) { hud.swing(handSwing); lastSwing = handSwing; }
  }
  return {
    inputs: hud.inputs,
    select,
    frame,
    show() {
      hud.root.style.display = 'block';
    },
    hide() {
      hud.root.style.display = 'none';
      line.visible = cracks.visible = false;
    },
    stop() {
      signal.abort();
      hud.root.remove();
      chips.dispose();
    },
    debug: () => ({
      inventory: { dirt: counts.get(DIRT), wood: counts.get(LOG) },
      selected: itemName(selected),
      hit: hit && { ...hit },
      progress
    })
  };
}
