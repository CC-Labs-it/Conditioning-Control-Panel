import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { boardThemeKey } from './theme-ids.js';
import { boardMotionScale, onPresentation } from '../game/preferences.js';
import { installTileMotion } from './tile-motion.js';

const THEMES = {
  observatory: { tiles: ['#163352', '#ded4bc'], rim: '#1b304a', wall: '#14212e', sky: '#182c42', horizon: '#526676', accent: '#e6c481', grout: .994, gloss: .06 },
  conservatory: { tiles: ['#28533d', '#e8dfc5'], rim: '#785542', wall: '#355648', sky: '#688173', horizon: '#cfcaaf', accent: '#bad5a4', grout: .976 },
  theatre: { tiles: ['#561f30', '#edbbae'], rim: '#694134', wall: '#351924', sky: '#301722', horizon: '#79443e', accent: '#ffad8d', grout: .995 },
  arcade: { tiles: ['#27354a', '#c3d4df'], rim: '#253147', wall: '#141b2a', sky: '#13263a', horizon: '#344f68', accent: '#8bc3ce', grout: .987 },
};

function noise(seed) {
  let n = seed;
  return () => { n = (Math.imul(1664525, n) + 1013904223) >>> 0; return n / 4294967296; };
}

function texture(THREE, width, height, paint) {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  paint(canvas.getContext('2d'), width, height);
  const result = new THREE.CanvasTexture(canvas);
  result.colorSpace = THREE.SRGBColorSpace;
  result.anisotropy = 4;
  return result;
}

function tileTexture(THREE, id, base, variant) {
  return texture(THREE, 128, 128, (ctx, size) => {
    const random = noise(47 + variant * 1607);
    ctx.fillStyle = base;
    ctx.fillRect(0, 0, size, size);
    if (id === 'arcade') {
      ctx.fillStyle = 'rgba(14,30,52,.15)';
      for (let y = 2; y < size; y += 8) for (let x = 2; x < size; x += 8) ctx.fillRect(x, y, 1.1, 1.1);
      ctx.strokeStyle = 'rgba(210,230,238,.16)';
      ctx.strokeRect(3, 3, size - 6, size - 6);
    } else if (id === 'conservatory') {
      for (let i = 0; i < 600; i++) {
        ctx.fillStyle = i % 3 ? 'rgba(39,48,27,.055)' : 'rgba(255,247,202,.15)';
        ctx.fillRect(random() * size, random() * size, 1 + random() * 2, .7 + random());
      }
      ctx.strokeStyle = 'rgba(240,235,205,.19)';
      ctx.strokeRect(2, 2, size - 4, size - 4);
    } else if (id === 'observatory') {
      const isLapis = variant % 2 === 0;
      // Mineral grain stays anchored while the shader's gold fault paths flow.
      for (let k = 0; k < 250; k++) {
        ctx.fillStyle = k % 2 ? 'rgba(104,150,179,.055)' : 'rgba(10,25,42,.035)';
        ctx.beginPath(); ctx.arc(random()*size,random()*size,2+random()*6,0,Math.PI*2); ctx.fill();
      }
      ctx.fillStyle = isLapis ? 'rgba(236,207,142,.5)' : 'rgba(164,127,70,.16)';
      for (let k = 0; k < 40; k++) ctx.fillRect(random() * size, random() * size, .5 + random() * 1.4, .7);
    } else {
      const sheen = ctx.createLinearGradient(0, 0, 128, 128);
      sheen.addColorStop(0, 'rgba(255,230,192,.11)'); sheen.addColorStop(1, 'rgba(39,9,18,.04)');
      ctx.fillStyle = sheen; ctx.fillRect(0, 0, size, size);
      ctx.strokeStyle = 'rgba(255,221,183,.12)'; ctx.strokeRect(2, 2, size - 4, size - 4);
    }
  });
}

function backdrop(THREE, id, theme) {
  return texture(THREE, 768, 512, (ctx, w, h) => {
    const glow = ctx.createRadialGradient(w * .43, h * .4, 0, w * .5, h * .45, w * .69);
    glow.addColorStop(0, theme.horizon); glow.addColorStop(1, theme.sky);
    ctx.fillStyle = glow; ctx.fillRect(0, 0, w, h);
    if (id === 'observatory') {
      const random = noise(127);
      for (let i = 0; i < 104; i++) {
        const x = random() * w, y = random() * h, big = i % 13 === 0;
        ctx.fillStyle = big ? 'rgba(246,228,187,.64)' : 'rgba(208,221,235,.42)';
        ctx.beginPath(); ctx.arc(x, y, big ? 1.2 : .64, 0, Math.PI * 2); ctx.fill();
        if (big) { ctx.fillRect(x - 2.7, y - .36, 5.4, .72); ctx.fillRect(x - .36, y - 2.7, .72, 5.4); }
      }
    }
    if (id === 'theatre') {
      for (let x = -10; x < w; x += 32) {
        const pleat = ctx.createLinearGradient(x, 0, x + 32, 0);
        pleat.addColorStop(0, 'rgba(26,7,15,.19)'); pleat.addColorStop(.65, 'rgba(120,30,44,.04)'); pleat.addColorStop(1, 'rgba(26,7,15,.19)');
        ctx.fillStyle = pleat; ctx.fillRect(x, 0, 32, h);
      }
    }
    if (id === 'arcade') {
      ctx.strokeStyle = 'rgba(96,194,199,.085)'; ctx.lineWidth = 1;
      for (let x = -w; x < 2*w; x += 48) { ctx.beginPath(); ctx.moveTo(w/2, h*.24); ctx.lineTo(x,h); ctx.stroke(); }
      for (let i = 1; i <= 10; i++) { const y = h*.24 + i*i*3.7; ctx.beginPath(); ctx.moveTo(0,y); ctx.lineTo(w,y); ctx.stroke(); }
    }
  });
}

function mergeStaticTrim(group) {
  const byMaterial = new Map();
  group.traverse(mesh => {
    if (!mesh.isMesh || Array.isArray(mesh.material)) return;
    const transform = new THREE.Matrix4();
    for (let parent = mesh; parent !== group; parent = parent.parent) {
      if (parent.userData.themeAnimated) return;
      parent.updateMatrix();
      transform.premultiply(parent.matrix);
    }
    const source = mesh.geometry;
    const geometry = source.index ? source.toNonIndexed() : source.clone();
    geometry.applyMatrix4(transform);
    if (!geometry.attributes.uv) geometry.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(geometry.attributes.position.count * 2), 2));
    if (!byMaterial.has(mesh.material)) byMaterial.set(mesh.material, []);
    byMaterial.get(mesh.material).push({ mesh, geometry });
  });
  for (const [material, parts] of byMaterial) {
    const combined = mergeGeometries(parts.map(part => part.geometry));
    for (const part of parts) part.geometry.dispose();
    if (!combined) continue;
    for (const { mesh } of parts) {
      mesh.geometry.dispose();
      mesh.removeFromParent();
    }
    const mesh = new THREE.Mesh(combined, material);
    mesh.name = 'theme-static-trim';
    group.add(mesh);
  }
}

function drawTheme(view, id, appearance) {
  const theme = THEMES[id];
  if (!theme) throw Error('Unknown board theme ' + id);
  const group = appearance.group;
  group.name = 'theme-' + id;
  const plinth = view.boardGroup.children[0];
  plinth.material[0].color.set(theme.rim);
  plinth.material[1].color.set(theme.wall);
  if (id === 'observatory') {
    // The stock clearcoat reflects a large studio softbox across the entire
    // dark rim at the playing angle. Cut that reflection on the board alone.
    plinth.material[0].roughness = .84;
    plinth.material[0].clearcoat = .04;
    plinth.material[0].envMapIntensity = .08;
    plinth.material[0].specularIntensity = .20;
  } else if (id === 'conservatory') {
    Object.assign(plinth.material[0], {metalness:.64,roughness:.62,clearcoat:.08,envMapIntensity:.30});
  } else if (id === 'theatre') {
    Object.assign(plinth.material[0], {metalness:.08,roughness:.32,clearcoat:.78,clearcoatRoughness:.24,envMapIntensity:.26});
  } else {
    Object.assign(plinth.material[0], {metalness:.64,roughness:.37,clearcoat:.34,envMapIntensity:.32});
    Object.assign(plinth.material[1], {metalness:.24,roughness:.23,clearcoat:.88,clearcoatRoughness:.16,envMapIntensity:.38});
  }
  view.boardGroup.add(group);
  const tileClock = {value:0};
  const maps = [0,1,2,3].map(i => {
    const map = tileTexture(THREE, id, theme.tiles[i % 2], i);
    appearance.textures.add(map);
    return map;
  });
  for (const [square, mesh] of view.squares) {
    const f = square.charCodeAt(0) - 97, r = Number(square[1]) - 1;
    mesh.material.map = maps[(f+r) % 2 + (r+Math.floor(f/2)) % 2 * 2];
    mesh.material.color.set(0xffffff);
    installTileMotion(mesh.material,{id,light:(f+r)%2===1,seed:f*1.73+r*2.97,clock:tileClock});
    mesh.scale.x = mesh.scale.z = theme.grout;
    mesh.material.needsUpdate = true;
  }
  const bg = backdrop(THREE, id, theme);
  appearance.textures.add(bg);
  view.scene.background = bg;
  if (view.scene.fog) view.scene.fog.color.set(theme.sky);
  view.lights.hemi.color.set(0xdadfdf);
  view.lights.fill.color.set(0xdce7e6);
  view.lights.rim.color.set(theme.accent);
  const callbacks = [t=>{tileClock.value=t;}];
  const vec = (x,y,z) => new THREE.Vector3(x,y,z);
  const materials = appearance.materials;
  const ownMaterial = material => { materials.add(material); return material; };
  const mat = (color, metalness = .1, roughness = .42) => ownMaterial(new THREE.MeshStandardMaterial({color, metalness, roughness, envMap: view.scene.environment, envMapIntensity: .66}));
  function add(geometry, material, x = 0, y = 0, z = 0, parent = group) {
    const mesh = new THREE.Mesh(geometry, material);
    materials.add(material);
    mesh.position.set(x,y,z); parent.add(mesh); return mesh;
  }
  function rod(points, material, radius = .026, parent = group) {
    return add(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(points), Math.max(points.length * 3, 10), radius, 6, false), material, 0, 0, 0, parent);
  }
  function torus(radius, thickness, material, x,y,z, parent = group) {
    const m = add(new THREE.TorusGeometry(radius,thickness,8,56), material, x,y,z,parent); m.rotation.x = -Math.PI/2; return m;
  }
  function edgeRails(radius, y, thickness, material) {
    const a = radius - .26, ends = [[a,radius],[-a,radius],[-radius,a],[-radius,-a],[-a,-radius],[a,-radius],[radius,-a],[radius,a],[a,radius]];
    for(let i=0;i<8;i++) {
      const p=ends[i], q=ends[i+1], v=vec(q[0]-p[0],0,q[1]-p[1]);
      const m=add(new THREE.CylinderGeometry(thickness,thickness,v.length(),8),material,(p[0]+q[0])/2,y,(p[1]+q[1])/2);
      m.quaternion.setFromUnitVectors(vec(0,1,0),v.normalize());
    }
  }
  const brass=mat('#bb8a42', .62), paleGold=mat('#dfbc71',.54), darkMetal=mat('#273643',.50);
  if (id === 'observatory') {
    darkMetal.color.set('#193347');
    darkMetal.roughness = .76;
    darkMetal.metalness = .18;
    darkMetal.envMapIntensity = .12;
    edgeRails(4.63,-.065,.042,brass); edgeRails(4.73,-.34,.035,brass); edgeRails(4.045,-.038,.018,paleGold);
    const dialZ = 4.60, dialY = -.045;
    add(new THREE.CylinderGeometry(.60,.63,.13,72),brass,0,dialY,dialZ);
    add(new THREE.CylinderGeometry(.559,.55,.035,72),darkMetal,0,.032,dialZ);
    for(const radius of [.54,.43,.29]) torus(radius,.012,paleGold,0,.06,dialZ);
    const ticks = [];
    for (let i=0;i<60;i++) {
      const angle = Math.PI*2*i/60, end = i%5 === 0 ? .438 : .485;
      ticks.push(Math.sin(angle)*.525,.068,dialZ + Math.cos(angle)*.525, Math.sin(angle)*end,.068,dialZ + Math.cos(angle)*end);
    }
    for (let k=0;k<4;k++) {
      const sign = k%2 ? -1 : 1;
      for (let i=-17;i<=17;i++) {
        const t=i*.208;
        if (Math.abs(t)<.64 && k<2) continue;
        const edge=4.50, end=edge-(i%4 ? .07 : .15);
        if(k<2) ticks.push(t,-.052,sign*edge,t,-.052,sign*end);
        else ticks.push(sign*edge,-.052,t,sign*end,-.052,t);
      }
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(ticks, 3));
    group.add(new THREE.LineSegments(geo, ownMaterial(new THREE.LineBasicMaterial({color:'#ecd080'}))));
    const hand = new THREE.Group(); hand.position.set(0,.086,dialZ); hand.userData.themeAnimated=true; group.add(hand);
    const pointer = new THREE.Shape();
    pointer.moveTo(0,.46); pointer.lineTo(.064,-.15); pointer.lineTo(0,-.23); pointer.lineTo(-.064,-.15); pointer.closePath();
    const blade = add(new THREE.ShapeGeometry(pointer),paleGold,0,.008,0,hand); blade.rotation.x=-Math.PI/2;
    add(new THREE.SphereGeometry(.063,16,8),brass,0,.1,dialZ);
    const planet = add(new THREE.SphereGeometry(.035,12,8),mat('#72a6c3',.3),0,.09,dialZ);
    planet.userData.themeAnimated=true;
    callbacks.push(t=>{planet.position.x=Math.sin(t*.12)*.29;planet.position.z=dialZ+Math.cos(t*.12)*.29;});
    callbacks.push((t, pulse)=>{hand.rotation.y=t*.14+.46+pulse*.16;});
  }
  if (id === 'conservatory') {
    const copper=mat('#a46e49',.52), patina=mat('#608374',.4), cream=mat('#e9ba88');
    edgeRails(4.59,-.055,.047,copper); edgeRails(4.72,-.42,.048,patina);
    const leafMat = ownMaterial(new THREE.MeshStandardMaterial({vertexColors:true,side:THREE.DoubleSide,roughness:.68,metalness:.04}));
    const leafShades=['#648b48','#38684d','#9fad62','#497943'].map(c=>new THREE.Color(c));
    function fern(x,z,dir) {
      const g = new THREE.Group(); g.position.set(x,0,z); g.rotation.y=dir; g.scale.set(.64,1,.85); group.add(g);
      g.userData.themeAnimated=true;
      const p=[], col=[];
      const tri=(a,b,c,color)=>{p.push(...a,...b,...c); for(let n=0;n<3;n++) col.push(color.r,color.g,color.b);};
      for(let k=0;k<3;k++) {
        const tilt=(k-1)*.50, length=1.08-k*.16, lift=.25+k*.08;
        const point=(t)=>[Math.sin(tilt)*length*t, .025+lift*Math.sin(t*2.1),Math.cos(tilt)*length*t];
        rod([0,.33,.68,1].map(t=>vec(...point(t))),copper,.011,g);
        for(let n=1;n<=9;n++) for(const s of [-1,1]) {
          const t=n/11, a=point(t), len=.19*(1-.70*t)+.052;
          const rx=Math.cos(tilt)*s, rz=-Math.sin(tilt)*s, dx=Math.sin(tilt), dz=Math.cos(tilt);
          const b=[a[0]+rx*len*.57, a[1]+.045,a[2]+rz*len*.57+dz*.005];
          const c=[a[0]+rx*len+dx*.10,a[1]+.05+len*.2,a[2]+rz*len+dz*.10];
          const d=[b[0]+dx*.085,b[1]+.014,b[2]+dz*.085];
          const shade=leafShades[(k+n)%4]; tri(a,b,c,shade); tri(a,c,d,shade);
        }
      }
      const geo=new THREE.BufferGeometry();
      geo.setAttribute('position',new THREE.Float32BufferAttribute(p,3)); geo.setAttribute('color',new THREE.Float32BufferAttribute(col,3)); geo.computeVertexNormals();
      add(geo,leafMat,0,0,0,g);
      mergeStaticTrim(g);
      callbacks.push((t,pulse)=>{g.rotation.z=(.024+pulse*.028)*Math.sin(t*.85+x+z);});
    }
    for(const side of [-1,1]) {
      for(const z of [-3.75,-2.45,-1.15,.15,1.45,2.75]) fern(side*4.48,z,-side*.035);
      // Front and back seats use x=-2.2 and x=+2.2 respectively. Fronds
      // point along the rail and stop before either seat footprint.
      const railPlants = side > 0 ? [-3.85,-1.35,.15,1.65] : [-3.7,-2.2,-.7,3.05];
      for(const x of railPlants) fern(x,side*4.38,Math.PI/2);
      for(let x=-3.8;x<4.2;x+=.60) {
        const y=-.27, z=side*4.70;
        rod([vec(x,y+.12,z),vec(x+.15,y,z),vec(x+.30,y-.06,z),vec(x+.45,y,z),vec(x+.60,y+.12,z)],copper,.019);
        if(x<3.75) add(new THREE.SphereGeometry(.045,8,6),cream,x+.3,y-.033,z);
      }
    }
  }
  if(id === 'theatre') {
    const velvet=ownMaterial(new THREE.MeshPhysicalMaterial({color:'#9b243e',roughness:.92,metalness:0,sheen:1,sheenColor:'#bc5660',sheenRoughness:.72,side:THREE.DoubleSide,envMapIntensity:.12}));
    const cord=mat('#d29b4c',.42), bulb=new THREE.MeshBasicMaterial({color:'#fff0be'}), shade=mat('#955d32',.5);
    bulb.userData.themeLamp='bulb';
    edgeRails(4.68,-.04,.034,brass); edgeRails(4.07,-.045,.024,brass);
    for(let side=0;side<4;side++) {
      const rail=new THREE.Group(); rail.rotation.y=side*Math.PI/2; group.add(rail);
      for(let n=0;n<6;n++) {
        const width=1.46, x0=-4.38+n*width, z=4.74, p=[], ix=[], cordPoints=[];
        for(let j=0;j<=34;j++) {
          const t=j/34, x=x0+t*width, dip=Math.pow(Math.sin(t*Math.PI),.84), fold=.045*Math.cos(t*Math.PI*16);
          const top=-.075, bottom=-.24-.32*dip, front=z+.025+.10*dip+fold;
          p.push(x,top,z+fold*.5,x,(top+bottom)*.5,front+.022,x,bottom,front);
          if(j<34) { const a=j*3; ix.push(a,a+3,a+1,a+1,a+3,a+4,a+1,a+4,a+2,a+2,a+4,a+5); }
          if(j%2===0) cordPoints.push(vec(x,bottom,front+.008));
        }
        const geom=new THREE.BufferGeometry(); geom.setAttribute('position',new THREE.Float32BufferAttribute(p,3)); geom.setIndex(ix); geom.computeVertexNormals();
        add(geom,velvet,0,0,0,rail); rod(cordPoints,cord,.018,rail);
        add(new THREE.SphereGeometry(.065,12,8),brass,x0,-.18,z+.06,rail);
        add(new THREE.ConeGeometry(.039,.13,8),cord,x0,-.29,z+.06,rail).rotation.z=Math.PI;
      }
      for(let j=0;j<12;j++) {
        const x=-3.88+j*.705;
        if ((side === 0 || side === 2) && Math.abs(x + 2.2) < .52) continue;
        const socket=add(new THREE.CylinderGeometry(.11,.095,.06,14),shade,x,-.035,4.49,rail);
        add(new THREE.SphereGeometry(.068,12,8),bulb,socket.position.x,.007,4.49,rail);
      }
    }
  }
  if(id === 'arcade') {
    const steel=mat('#43506a',.50), black=mat('#141c2c',.45), cyan=mat('#35dee0',.2), pink=mat('#f06fae',.2);
    cyan.emissive.set('#00abbf'); cyan.emissiveIntensity=1.18; pink.emissive.set('#ef438a'); pink.emissiveIntensity=.84;
    cyan.userData.themeLamp='cyan'; pink.userData.themeLamp='pink';
    edgeRails(4.72,-.34,.055,cyan); edgeRails(4.69,-.12,.026,pink);
    edgeRails(4.08,-.051,.026,steel);
    const white = new THREE.MeshBasicMaterial({color:'#e5ffff'});
    for(let k=0;k<4;k++) {
      const rail=new THREE.Group(); rail.rotation.y=k*Math.PI/2; group.add(rail);
      for(let i=0;i<24;i++) {
        const x=-3.77+i*.328, material=i%6<3 ? cyan : pink;
        add(new THREE.BoxGeometry(.22,.023,.068),material,x,-.048,4.44,rail);
        add(new THREE.BoxGeometry(.15,.08,.018),steel,x,-.23,4.744,rail).rotation.z=.4;
      }
      const runner=add(new THREE.BoxGeometry(.36,.027,.08),white,0,-.023,4.44,rail);
      runner.userData.themeAnimated=true;
      callbacks.push((t,pulse)=>{runner.position.x=3.74*Math.cos(t*.72+k*1.57);runner.scale.x=1+pulse*.4;});
    }
    const panel=add(new THREE.BoxGeometry(2.9,.19,.54),black,0,-.11,4.58);
    panel.rotation.x=.07;
    for(const [x,r,m] of [[.62,.115,pink],[.95,.115,cyan]]) {
      add(new THREE.CylinderGeometry(r+.035,r+.045,.10,20),steel,x,-.008,4.61);
      add(new THREE.CylinderGeometry(r,r,.058,20),m,x,.058,4.61);
    }
    add(new THREE.BoxGeometry(.40,.065,.13),steel,-.85,.044,4.60);
    add(new THREE.BoxGeometry(.13,.065,.40),steel,-.85,.046,4.60);
    add(new THREE.BoxGeometry(.31,.014,.033),cyan,0,-.002,4.55);
    add(new THREE.BoxGeometry(.31,.014,.033),pink,0,-.002,4.64);
  }
  mergeStaticTrim(group);
  group.traverse(object => { object.raycast = () => {}; });
  const animate = (seconds, pulse = 0) => { for(const fn of callbacks) fn(seconds, pulse); };
  animate(0);
  appearance.animate = animate;
}

/** A board theme owns its copied materials and decorations. Original game materials stay reusable. */
export function createBoardTheme({ view }) {
  const plinth = view.boardGroup.children[0];
  const room = view.scene.getObjectByName('pbp-room');
  const original = {
    background: view.scene.background,
    fog: view.scene.fog?.color.clone(),
    roomVisible: room?.visible,
    lights: Object.values(view.lights).map(light => ({ light, color: light.color.clone() })),
    plinth: plinth.material,
    squares: [...view.squares.values()].map(mesh => ({
      mesh, material: mesh.material, x: mesh.scale.x, z: mesh.scale.z,
      base: mesh.userData.base,
    })),
  };
  const sky = new THREE.Color(0xdadfdf);
  const fill = new THREE.Color(0xdce7e6);
  const rim = new THREE.Color();
  const fog = new THREE.Color();
  let appearance = null, id = null, elapsed = 0, pulse = 0, disposed = false;
  let edgeKick = 0, edgeAge = 1;
  let copies = [];
  let motionRate = 1;
  const stopPreference = onPresentation(p => {
    motionRate = p.reducedMotion ? 0 : boardMotionScale(p);
    if (!motionRate && appearance) {
      edgeKick = 0;
      edgeAge = 1;
      appearance.group.position.y = 0;
    }
  });

  function restore() {
    view.scene.background = original.background;
    if (view.scene.fog && original.fog) view.scene.fog.color.copy(original.fog);
    if (room) room.visible = original.roomVisible;
    for (const { light, color } of original.lights) light.color.copy(color);
    plinth.material = original.plinth;
    for (const saved of original.squares) {
      saved.mesh.material = saved.material;
      saved.mesh.scale.x = saved.x;
      saved.mesh.scale.z = saved.z;
      saved.mesh.userData.base = saved.base;
    }
    if (appearance) {
      const geometries = new Set();
      appearance.group.traverse(object => { if (object.geometry) geometries.add(object.geometry); });
      for (const geometry of geometries) geometry.dispose();
      for (const material of appearance.materials) material.dispose();
      for (const map of appearance.textures) map.dispose();
      appearance.group.removeFromParent();
    }
    appearance = null;
    for (const material of copies) material.dispose();
    copies = [];
  }

  function setTheme(value) {
    if (disposed) return false;
    const next = boardThemeKey(value);
    if (next === id) return false;
    restore();
    id = next;
    elapsed = 0;
    pulse = 0;
    edgeKick = 0;
    edgeAge = 1;
    if (id === null) return true;
    plinth.material = original.plinth.map(material => material.clone());
    copies.push(...plinth.material);
    for (const saved of original.squares) {
      saved.mesh.material = saved.material.clone();
      copies.push(saved.mesh.material);
    }
    try {
      appearance = { group: new THREE.Group(), materials: new Set(), textures: new Set(), animate: null };
      drawTheme(view, id, appearance);
      rim.set(THEMES[id].accent);
      fog.set(THEMES[id].sky);
      if (room) room.visible = false;
      return true;
    } catch (error) {
      restore();
      id = null;
      throw error;
    }
  }

  function update(dt) {
    if (!appearance || disposed) return;
    // Room warmth writes these colors each frame. Its light intensities, turn
    // direction and capture dips still drive the themed board's illumination.
    view.lights.hemi.color.copy(sky);
    view.lights.fill.color.copy(fill);
    view.lights.rim.color.copy(rim);
    if (view.scene.fog) view.scene.fog.color.copy(fog);
    if (!(dt > 0) || !Number.isFinite(dt) || !motionRate) return;
    const step = Math.min(.1, dt);
    elapsed += step;
    pulse *= Math.exp(-step * 3.2);
    edgeAge = Math.min(.8, edgeAge + step);
    appearance.group.position.y = edgeAge < .8
      ? -edgeKick * Math.sin(edgeAge * Math.PI * 5) * Math.exp(-edgeAge * 5)
      : 0;
    appearance.animate(elapsed, pulse);
  }

  function react(event) {
    if (!appearance || !motionRate) return;
    const gain = { move: .16, land: .25, capture: .55, check: .45, win: .8 }[event?.kind];
    if (!gain) return;
    const strength = Number.isFinite(event.strength) ? Math.max(0, Math.min(1, event.strength)) : 1;
    pulse = Math.max(pulse, gain * strength);
    if (event.kind === 'land' || event.kind === 'capture') {
      edgeKick = .018 * strength;
      edgeAge = 0;
    }
  }

  return {
    setTheme, update, react,
    debug: () => ({ id, elapsed, pulse, objects: appearance?.group.children.length || 0 }),
    dispose() {
      if (disposed) return;
      restore();
      stopPreference();
      disposed = true;
      id = null;
    },
  };
}
