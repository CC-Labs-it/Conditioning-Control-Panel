import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { presentation } from '../game/preferences.js';
import { cheerForEvent, createCheerState, CHEER_DURATION } from './cheer-reactions.js';
import { createCheerHearts } from './cheer-hearts.js';

const ITEM = 'chess.companion.emi';
const SCALE = 1.35;
const BASE = new URL('../assets/cheerleader/', import.meta.url);
const NO_PICK = () => {};
const inactive = () => !!globalThis.document?.hidden || !!globalThis.window?.PBP?.isPaused?.();
const REACTION_CLIPS = { nod: 'idle_bob', cheer: 'cheer', worry: 'sulk', scared: 'sulk', win: 'cheer', comfort: 'sulk', wave: 'wave', pet: 'wave' };
const REACTION_FACES = { nod: 1, cheer: 14, worry: 12, scared: 15, win: 13, comfort: 12, wave: 1, pet: 9 };
const RIG_BONES = new Set(['EMI_root', 'ant0', 'ant1', 'ant2', 'ballpiv', 'shoulderL', 'shoulderR']);

function expressiveAtlas(image) {
  const canvas=globalThis.document?.createElement?.('canvas'),ctx=canvas?.getContext?.('2d');
  if(!ctx)return image;
  canvas.width=canvas.height=1024;ctx.drawImage(image,0,0,1024,1024);
  const background=ctx.getImageData(0,0,1,1).data;
  // Keep Emi's original eyes and palette. The source has no frown or scared mouth.
  for(const index of [12,15]) {
    const x=index%4*256,y=Math.floor(index/4)*256;
    ctx.drawImage(image,0,0,256,256,x,y,256,256);
    ctx.fillStyle=`rgb(${background[0]},${background[1]},${background[2]})`;ctx.fillRect(x+77,y+145,108,65);
    ctx.drawImage(image,15,65,90,95,x+15,y+65,90,95);
    ctx.drawImage(image,145,65,95,95,x+145,y+65,95,95);
    ctx.strokeStyle='#ff65b7';ctx.lineWidth=10;ctx.lineCap='round';ctx.beginPath();
    if(index===12){ctx.moveTo(x+99,y+184);ctx.quadraticCurveTo(x+130,y+154,x+161,y+184);}
    else ctx.ellipse(x+130,y+183,11,15,0,0,Math.PI*2);
    ctx.stroke();ctx.lineWidth=7;ctx.beginPath();ctx.moveTo(x+38,y+64);ctx.lineTo(x+86,y+54);
    ctx.moveTo(x+176,y+54);ctx.lineTo(x+224,y+64);ctx.stroke();
  }
  return canvas;
}

function releaseSource(source) {
  const resources = new Set();
  source.traverse(object => {
    if (object.geometry) resources.add(object.geometry);
    if (object.skeleton) resources.add(object.skeleton);
    for (const material of object.material ? [].concat(object.material) : []) {
      resources.add(material);
      for (const value of Object.values(material)) if (value?.isTexture) resources.add(value);
    }
  });
  for (const resource of resources) resource.dispose();
}

function cloneRig(source) {
  const model = source.clone(true);
  const originals = [], copies = [], nodes = new Map(), skeletons = new Map();
  source.traverse(node => originals.push(node));
  model.traverse(node => copies.push(node));
  originals.forEach((node, index) => nodes.set(node, copies[index]));
  for (const node of originals) {
    if (!node.isSkinnedMesh) continue;
    if (!skeletons.has(node.skeleton)) {
      const skeleton = node.skeleton.clone();
      skeleton.bones = node.skeleton.bones.map(bone => nodes.get(bone));
      skeletons.set(node.skeleton, skeleton);
    }
    nodes.get(node).bind(skeletons.get(node.skeleton), node.bindMatrix);
  }
  return { model, skeletons: [...skeletons.values()] };
}

function buildSeat(source, clips, image, side, environment, camera) {
  const root = new THREE.Group();
  root.name = 'chess-emi-' + side;
  const direction = side === 'w' ? 1 : -1;
  root.position.set(-2.2 * direction, .075, 4.45 * direction);
  // Racing Thoughts Emi faces -Z. Each seat faces toward the playing squares.
  root.rotation.y = side === 'b' ? Math.PI : 0;
  root.scale.setScalar(SCALE);
  const { model, skeletons } = cloneRig(source);
  model.position.y = -.4265;
  root.add(model);
  model.traverse(object => {
    object.raycast = NO_PICK;
    if (!object.isMesh) return;
    object.material = Array.isArray(object.material) ? object.material.map(m => m.clone()) : object.material.clone();
    for (const material of [].concat(object.material)) {
      if (!material.isMeshStandardMaterial) continue;
      material.envMap = environment;
      material.envMapIntensity = .65;
    }
    object.castShadow = true;
    object.receiveShadow = false;
    object.frustumCulled = false;
  });
  const cushion = new THREE.Mesh(
    new THREE.SphereGeometry(.265, 32, 16),
    new THREE.MeshStandardMaterial({ color: side === 'w' ? 0xe4b7cf : 0x66517d, roughness: .85 }),
  );
  cushion.name = 'emi-seat-cushion';
  cushion.position.set(0, -.0575, .035);
  cushion.scale.set(1, .217, .82);
  cushion.raycast = NO_PICK;
  root.add(cushion);
  const hearts=createCheerHearts(root);

  const faceTexture = new THREE.Texture(image);
  faceTexture.flipY = false;
  faceTexture.colorSpace = THREE.SRGBColorSpace;
  faceTexture.repeat.set(.25, .25);
  faceTexture.needsUpdate = true;
  model.traverse(object => {
    if (!object.isMesh || object.material.name !== 'emi_screen') return;
    object.material.dispose();
    object.material = new THREE.MeshBasicMaterial({ name: 'emi_screen', map: faceTexture, toneMapped: false });
  });

  const mixer = new THREE.AnimationMixer(model);
  const actions = new Map();
  for (const name of new Set(['idle_bob', ...Object.values(REACTION_CLIPS)])) {
    const sourceClip = clips.find(clip => clip.name === name);
    if (!sourceClip) continue;
    // Keep the racing rig's authored rotations. The seated case has a fixed support.
    const tracks = sourceClip.tracks.filter(track => {
      const [bone, property] = track.name.split('.');
      return RIG_BONES.has(bone) && property === 'quaternion';
    }).map(track => track.clone());
    actions.set(name, mixer.clipAction(new THREE.AnimationClip(name, sourceClip.duration, tracks)));
  }
  let active = null, previousFace = -1;
  function play(name) {
    const next = actions.get(name);
    if (!next || next === active) return;
    next.reset().setEffectiveTimeScale(1).setEffectiveWeight(1).play();
    if (active) active.crossFadeTo(next, .3, false);
    active = next;
  }
  function paint(index) {
    if (previousFace === index) return;
    previousFace = index;
    faceTexture.offset.set((index % 4) / 4, Math.floor(index / 4) / 4);
  }
  const feet = ['footL', 'footR'].map(name => model.getObjectByName(name));
  const restFeet = feet.map(foot => ({ position: foot.position.clone(), quaternion: foot.quaternion.clone() }));
  const parentQ = new THREE.Quaternion(), modelQ = new THREE.Quaternion(), tilt = new THREE.Quaternion();
  const inverseParent = new THREE.Quaternion(), inverseModel = new THREE.Quaternion();
  const modelOffset = new THREE.Vector3(), axis = new THREE.Vector3(1, 0, 0);
  function poseFeet(time) {
    model.updateMatrixWorld(true);
    model.getWorldQuaternion(modelQ);
    inverseModel.copy(modelQ).invert();
    feet.forEach((foot, index) => {
      foot.parent.getWorldQuaternion(parentQ);
      inverseParent.copy(parentQ).invert();
      // The original short boots rest on the rim just ahead of the seated case.
      modelOffset.set(0, .004 * (1 + Math.sin(time * 1.7 + index)), -.045).applyQuaternion(modelQ).applyQuaternion(inverseParent);
      foot.position.copy(restFeet[index].position).add(modelOffset);
      tilt.setFromAxisAngle(axis, .07 * Math.sin(time * 1.7 + index));
      foot.quaternion.copy(inverseParent).multiply(modelQ).multiply(tilt)
        .multiply(inverseModel).multiply(parentQ).multiply(restFeet[index].quaternion);
      foot.updateMatrixWorld(true);
    });
  }
  const state = createCheerState();
  const body=model.getObjectByName('EMI_root'),hand=model.getObjectByName('shoulderR');
  const pickMeshes=[];model.traverse(node=>{if(node.isMesh)pickMeshes.push(node);});
  const screenMesh=pickMeshes.find(mesh=>mesh.material.name==='emi_screen');
  const handAxis=new THREE.Vector3(0,1,0),handAt=new THREE.Vector3(),point=new THREE.Vector3(),mouth=new THREE.Vector3();
  const handQ=new THREE.Quaternion(),parentInverse=new THREE.Quaternion(),correction=new THREE.Quaternion();
  const faceBounds=new THREE.Box3(),rootInverse=new THREE.Matrix4(),mouthTarget=new THREE.Vector3();
  // SkinnedMesh.updateMatrixWorld also refreshes bindMatrixInverse; updateWorldMatrix does not.
  root.updateMatrixWorld(true);hand?.getWorldPosition(handAt);
  let handLength=0;
  for(const mesh of pickMeshes) {
    if(!mesh.isSkinnedMesh)continue;mesh.skeleton.update();
    const bone=mesh.skeleton.bones.indexOf(hand),indices=mesh.geometry.getAttribute('skinIndex'),weights=mesh.geometry.getAttribute('skinWeight');
    for(let i=0;i<indices.count;i++) {
      if(![0,1,2,3].some(k=>indices.getComponent(i,k)===bone&&weights.getComponent(i,k)>.8))continue;
      mesh.getVertexPosition(i,point).applyMatrix4(mesh.matrixWorld).sub(handAt);
      if(point.length()>handLength){handLength=point.length();handAxis.copy(point);}
    }
  }
  if(hand){hand.getWorldQuaternion(handQ);handAxis.applyQuaternion(handQ.invert()).normalize();}
  let fearBlend=0,petCount=0,idleKind='bob';
  function manner(time,action,dt) {
    const phase=time+(side==='b'?4.7:0),beat=phase%18;
    idleKind=action?'reaction':beat<7?'bob':beat<10?'hop':beat<14?'shuffle':'wobble';
    model.position.set(0,-.4265,0);
    if(!action&&idleKind==='hop'){
      const hop=(beat-7)%1.5;
      if(hop<.22){model.position.y-=Math.sin(hop/.22*Math.PI)*.008;body?.rotateX(Math.sin(hop/.22*Math.PI)*.045);}
      else if(hop<1.12)model.position.y+=Math.sin((hop-.22)/.9*Math.PI)**2*.065;
      else body?.rotateX(-Math.sin((hop-1.12)/.38*Math.PI)*.025);
    }
    if(!action&&idleKind==='shuffle')model.position.x=Math.sin((beat-10)/4*Math.PI*4)*.035*Math.sin((beat-10)/4*Math.PI);
    if(body&&!action&&idleKind==='wobble')body.rotateZ(Math.sin((beat-14)/4*Math.PI*2)*.045);
    fearBlend+=(Number(action==='scared')-fearBlend)*(1-Math.exp(-dt*9));
    if(fearBlend>.001&&hand&&screenMesh) {
      root.updateMatrixWorld(true);hand.getWorldPosition(handAt);
      rootInverse.copy(root.matrixWorld).invert();screenMesh.skeleton?.update();faceBounds.makeEmpty();
      for(let i=0;i<screenMesh.geometry.attributes.position.count;i++){
        screenMesh.getVertexPosition(i,point).applyMatrix4(screenMesh.matrixWorld).applyMatrix4(rootInverse);faceBounds.expandByPoint(point);
      }
      point.copy(handAt).applyMatrix4(rootInverse);
      const side=Math.sign(point.x-(faceBounds.min.x+faceBounds.max.x)*.5)||1;
      // Aim beyond the actual front surface, near the mouth corner below the eyes.
      mouth.set((faceBounds.min.x+faceBounds.max.x)*.5+side*(faceBounds.max.x-faceBounds.min.x)*.38,
        faceBounds.min.y+(faceBounds.max.y-faceBounds.min.y)*.23,faceBounds.min.z-.035).applyMatrix4(root.matrixWorld);
      mouthTarget.copy(mouth);
      hand.getWorldQuaternion(handQ);point.copy(handAxis).applyQuaternion(handQ);
      mouth.sub(handAt).normalize();correction.setFromUnitVectors(point,mouth);
      hand.parent.getWorldQuaternion(parentInverse).invert();
      handQ.premultiply(correction).premultiply(parentInverse);
      hand.quaternion.slerp(handQ,fearBlend);
      body?.rotateZ(Math.sin(time*23)*.012*fearBlend);
      hand.rotateY(Math.sin(time*12)*.025*fearBlend);
    }
  }
  let last = state.update(0), still = false;
  play('idle_bob'); mixer.update(0); poseFeet(0); paint(0);
  return {
    root,
    setChecked(value){state.setChecked(value);last=state.update(0);if(value)hearts.clear();},
    pet(){if(!state.trigger('pet','pet-'+(++petCount)))return false;hearts.emit();return true;},
    hit(raycaster){
      const hits=[];root.updateWorldMatrix(true,true);
      for(const mesh of pickMeshes){if(mesh.isSkinnedMesh){mesh.skeleton.update();mesh.computeBoundingSphere();THREE.SkinnedMesh.prototype.raycast.call(mesh,raycaster,hits);}else THREE.Mesh.prototype.raycast.call(mesh,raycaster,hits);}
      return hits.reduce((nearest,hit)=>Math.min(nearest,hit.distance),Infinity);
    },
    react(kind, payload) {
      const reaction = cheerForEvent(kind, payload, side);
      return reaction && state.trigger(reaction, [kind, payload.ply ?? '', payload.square ?? '', payload.by ?? payload.side ?? '', payload.winner ?? ''].join('|'));
    },
    reset(clearCheck=false) { state.reset(clearCheck);last=state.update(0);hearts.clear(); },
    update(dt, reduced) {
      if (reduced) {
        if (!still) {
          state.reset(); last = state.update(0);
          mixer.stopAllAction(); active = null;
          play('idle_bob');mixer.update(0);poseFeet(0);model.position.set(0,-.4265,0);fearBlend=0;hearts.clear();paint(last.checked?15:0);
        }
        paint(last.checked?15:0);
        still = true;
        return;
      }
      still = false;
      if (!(dt > 0)) return;
      last = state.update(dt);
      const phase = last.time + (side === 'b' ? 4.7 : 0);
      const idleWave = Math.floor(phase / 12) % 3 === 1 && phase % 12 > 5 && phase % 12 < 8;
      play(last.action ? REACTION_CLIPS[last.action] : idleWave ? 'wave' : 'idle_bob');
      mixer.update(Math.min(dt, .1));
      manner(last.time,last.action,Math.min(dt,.1));
      poseFeet(phase);
      paint(last.action ? REACTION_FACES[last.action] : idleWave ? 1 : phase % 5.7 < .14 ? 4 : 0);
      hearts.update(dt,camera);
    },
    debug() {
      return { side, model: 'racing-thoughts-emi', action: last.action || 'idle', clip: active?.getClip().name,
        time: last.time, age: last.age, duration: CHEER_DURATION[last.action] || 0, face: previousFace,
        checked:last.checked,idle:idleKind,hearts:hearts.debug(),fearBlend,handReach:handLength,mouthTarget:mouthTarget.toArray(),
        seated: true, facingBoard: true, position: root.position.toArray(), scale: root.scale.toArray() };
    },
    dispose() {
      mixer.stopAllAction(); mixer.uncacheRoot(model);
      for (const skeleton of skeletons) skeleton.dispose();
      model.traverse(object => { for (const material of object.material ? [].concat(object.material) : []) material.dispose(); });
      hearts.dispose();faceTexture.dispose(); cushion.geometry.dispose(); cushion.material.dispose(); root.removeFromParent();
    },
  };
}

export function createCheerleaders({ view, bus, game } = {}) {
  const group = new THREE.Group();
  group.name = 'chess-cheerleaders';
  view.scene.add(group);
  const seats = new Map(), off = [];
  let assets = null, source = null, revision = 0, disposed = false, error = null, running = true;
  let checkedSide=null;
  function setChecked(side){checkedSide=side;for(const [key,seat] of seats)seat.setChecked(key===side);}
  function syncCheck(payload={}){
    const rules=game?.rules||globalThis.window?.PBP?.game?.rules;
    if(rules?.inCheck&&rules?.turn){setChecked(rules.inCheck()?rules.turn():null);return;}
    if(payload.inCheck===false||payload.check===false||payload.side&&payload.side!==checkedSide)setChecked(null);
  }
  function load() {
    if (!assets) assets = Promise.all([
      new GLTFLoader().loadAsync(new URL('emi-racing.glb', BASE).href),
      new Promise((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = reject; image.src = new URL('emi-racing-faces.png', BASE).href; }),
    ]).then(([gltf, image]) => {
      if (disposed) { releaseSource(gltf.scene); return null; }
      source = gltf.scene; error = null;
      return { source, clips: gltf.animations, image:expressiveAtlas(image) };
    }).catch(reason => { assets = null; error = String(reason); throw reason; });
    return assets;
  }
  async function setLooks(looks = {}) {
    const token = ++revision;
    const wanted = ['w', 'b'].filter(side => looks[side]?.companion === ITEM);
    for (const [side, seat] of seats) if (!wanted.includes(side)) { seat.dispose(); seats.delete(side); }
    if (disposed || !wanted.length) return;
    let loaded;
    try { loaded = await load(); } catch { return; }
    if (disposed || token !== revision || !loaded) return;
    syncCheck();
    for (const side of wanted) if (!seats.has(side)) {
      const seat = buildSeat(loaded.source, loaded.clips, loaded.image, side, view.scene.environment,view.camera);
      seat.setChecked(side===checkedSide);
      seats.set(side, seat); group.add(seat.root);
    }
  }
  function react(kind, payload = {}) {
    if (disposed) return false;
    if(kind==='check')setChecked(payload.side);
    if(kind==='gameover')setChecked(null);
    const preferences = presentation();
    if (!running || inactive() || preferences.reducedMotion || preferences.boardMotion === 'still') {
      for (const seat of seats.values()) seat.reset();
      return false;
    }
    for (const seat of seats.values()) seat.react(kind, payload);
    return true;
  }
  if (bus?.on) {
    for (const kind of ['land', 'capture', 'check', 'gameover']) off.push(bus.on(kind, payload => react(kind, payload || {})));
    for(const kind of ['turn','resync'])off.push(bus.on(kind,p=>syncCheck(p)));
    for (const kind of ['local', 'newgame', 'takeback']) off.push(bus.on(kind, () => { setChecked(null);for (const seat of seats.values()) seat.reset(true);syncCheck(); }));
  }
  const canvas=view.renderer?.domElement,raycaster=new THREE.Raycaster(),pointer=new THREE.Vector2();
  const captured=new Set();let lastPetClick={at:-Infinity,x:0,y:0};
  function pet(side){
    const p=presentation();if(disposed||!running||inactive()||p.reducedMotion||p.boardMotion==='still'||side===checkedSide)return false;
    return seats.get(side)?.pet()||false;
  }
  function pointerDown(event){
    if(event.button!==0||!view.camera||disposed)return;
    const rect=canvas.getBoundingClientRect();pointer.set((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1);
    raycaster.setFromCamera(pointer,view.camera);let selected=null,distance=Infinity;
    for(const [side,seat] of seats){const hit=seat.hit(raycaster);if(hit<distance){distance=hit;selected=side;}}
    if(!selected)return;
    event.preventDefault();event.stopImmediatePropagation();captured.add(event.pointerId);lastPetClick={at:performance.now(),x:event.clientX,y:event.clientY};pet(selected);
  }
  function pointerUp(event){if(captured.delete(event.pointerId)){event.preventDefault();event.stopImmediatePropagation();lastPetClick={at:performance.now(),x:event.clientX,y:event.clientY};}}
  function click(event){if(performance.now()-lastPetClick.at<500&&Math.hypot(event.clientX-lastPetClick.x,event.clientY-lastPetClick.y)<12){event.preventDefault();event.stopImmediatePropagation();lastPetClick.at=-Infinity;}}
  function leave(){captured.clear();lastPetClick.at=-Infinity;}
  if(canvas?.addEventListener){
    for(const [name,fn] of [['pointerdown',pointerDown],['pointerup',pointerUp],['pointercancel',pointerUp],['click',click],['pointerleave',leave]]){
      canvas.addEventListener(name,fn,true);off.push(()=>canvas.removeEventListener(name,fn,true));
    }
  }
  return {
    group, setLooks, react, pet,
    update(dt) {
      if (disposed) return;
      const preferences = presentation();
      const reduced = preferences.reducedMotion || preferences.boardMotion === 'still';
      const active = dt > 0 && !inactive();
      if (running && !active) for (const seat of seats.values()) seat.reset();
      running = active;
      if (!running && !reduced) return;
      for (const seat of seats.values()) seat.update(dt, reduced);
    },
    debug() { return { loaded: !!source, error, running, inactive: inactive(), seats: [...seats.values()].map(seat => seat.debug()) }; },
    dispose() {
      if (disposed) return;
      disposed = true; revision++;
      for (const unsubscribe of off) unsubscribe?.();
      for (const seat of seats.values()) seat.dispose();
      seats.clear(); group.removeFromParent();
      if (source) releaseSource(source);
    },
  };
}
