import * as THREE from 'three';
import { boardThemeKey } from './theme-ids.js';
import { boardMotionScale, onPresentation } from '../game/preferences.js';

// Four local pools light the rims and nearby piece flanks. The neutral studio
// key still defines the pieces and square contrast. These lights cast no shadows.
const STYLES = {
  observatory: { colors:[0x83b9ff,0xe6c481,0x83b9ff,0xe6c481], power:.85, reach:3.4, height:.38, lift:1.05 },
  conservatory: { colors:[0xd8efad,0xffcf99,0xd8efad,0xffcf99], power:1.15, reach:3.2, height:.42, lift:1.1 },
  theatre: { colors:[0xffbd72,0xffd59c,0xffbd72,0xffd59c], power:2.4, reach:3.6, height:.28, lift:1.65 },
  arcade: { colors:[0x28e8ff,0xff4299,0x28e8ff,0xff4299], power:3.1, reach:3.45, height:.34, lift:2.1 },
};
const EDGE = 4.42;
const hidden = () => !!globalThis.document?.hidden || !!globalThis.window?.PBP?.isPaused?.();
const smooth = t => { t=Math.max(0,Math.min(1,t)); return t*t*(3-2*t); };

export function createThemeLights({ view, bus }) {
  const group = new THREE.Group();
  group.name = 'pbp-theme-lights';
  group.visible = false;
  (view.boardGroup || view.scene).add(group);
  const lights = [];
  for (let i=0;i<4;i++) {
    const light = new THREE.PointLight(0xffffff,0,3.4,2);
    light.name = 'theme-rim-light-'+i;
    light.castShadow = false;
    light.position.set(i===1?EDGE:i===3?-EDGE:0,.3,i===0?EDGE:i===2?-EDGE:0);
    group.add(light);
    lights.push(light);
  }
  const starts = new Float64Array(4).fill(-100), amounts = new Float64Array(4);
  const lengths = new Float64Array(4), pulse = new Float64Array(4);
  const sources = [], stops = [];
  let theme=null, style=null, sourceRoot=null, clock=0, disposed=false;
  let motion=true, running=true, reactions=0;
  let lastTime=-100, lastX=100, lastZ=100, lastGain=0, lastKind='';

  function restoreSources() {
    for (const source of sources) {
      if (source.emissive) source.material.emissiveIntensity=source.intensity;
      else source.material.color.copy(source.color);
    }
    sources.length=0;
    sourceRoot=null;
  }
  function bindSources() {
    const root = view.boardGroup || view.scene;
    let next=null;
    for (const child of root.children) if (child.name==='theme-'+theme) { next=child; break; }
    if (next===sourceRoot) return;
    restoreSources();
    sourceRoot=next;
    if (!next) return;
    const seen=new Set();
    const add=material => {
      const tag=material?.userData?.themeLamp;
      if (!tag || !material.color || seen.has(material)) return;
      seen.add(material);
      sources.push({ material, tag, emissive:!!material.emissive,
        intensity:material.emissiveIntensity, color:material.color?.clone() });
    };
    next.traverse(object => {
      if (Array.isArray(object.material)) for (const material of object.material) add(material);
      else add(object.material);
    });
  }
  function paint() {
    if (!style) return;
    for (let i=0;i<4;i++) lights[i].intensity=style.power+pulse[i]*style.lift;
    for (const source of sources) {
      const energy=source.tag==='cyan' ? Math.max(pulse[0],pulse[2])
        : source.tag==='pink' ? Math.max(pulse[1],pulse[3]) : Math.max(pulse[0],pulse[1],pulse[2],pulse[3]);
      if (source.emissive) source.material.emissiveIntensity=source.intensity*(1+energy*.8);
      else source.material.color.copy(source.color).multiplyScalar(1+energy*.65);
    }
  }
  function reset() {
    starts.fill(-100); amounts.fill(0); pulse.fill(0);
    lastTime=-100; lastGain=0;
    paint();
  }
  function react(kind,event) {
    if (disposed || !style || event?.skipped || event?.refused) return;
    if (!motion || !running || hidden()) { reset(); return; }
    const at=event?.world;
    const broad=kind==='check'||kind==='win';
    if (!broad && (!at || !Number.isFinite(at.x) || !Number.isFinite(at.z) || Math.abs(at.x)>4 || Math.abs(at.z)>4)) return;
    const x=Number.isFinite(at?.x) ? at.x : 0;
    const z=Number.isFinite(at?.z) ? at.z : event?.side==='b' ? -3.5 : 3.5;
    let gain=kind==='capture' ? 1.25 : kind==='win' ? 1.4 : kind==='check' ? .6 : kind==='move' ? .35 : .8;
    if (Number.isFinite(event?.strength)) gain=Math.max(.1,Math.min(1.5,event.strength));
    if (clock-lastTime<.07 && ((broad && kind===lastKind) || Math.hypot(x-lastX,z-lastZ)<.08) && gain<=lastGain) return;
    lastTime=clock; lastX=x; lastZ=z; lastGain=gain; lastKind=kind;
    const duration=kind==='win'?3.5:kind==='check'?2.65:kind==='capture'?1.85:1.45;
    for (let i=0;i<4;i++) {
      const distance=Math.hypot(lights[i].position.x-x,lights[i].position.z-z);
      // A landing brightens its near rail first. Check holds a slow low glow;
      // a result lifts all four edges together, never a full-board flash.
      const weight=kind==='win' ? 1 : broad ? .65 : .22+.78*Math.max(0,1-distance/8.5);
      starts[i]=clock+(broad?0:Math.min(.22,distance*.028));
      amounts[i]=gain*weight;
      lengths[i]=duration;
    }
    reactions++;
  }
  const on=(name,fn) => { const off=bus?.on?.(name,fn); if (off) stops.push(off); };
  on('land',p=>react(p?.capture?'capture':'land',p));
  on('hopLand',p=>react('land',{...p,strength:p?.small ? .3 : .45}));
  on('hit',p=>react('capture',p));
  on('check',p=>react('check',p));
  on('gameover',p=>{ if (p?.winner) react('win',{side:p.winner}); });
  on('theme-react',p=>{ if (['move','land','capture','check','win'].includes(p?.kind)) react(p.kind,p); });
  for (const name of ['local','newgame','takeback']) on(name,reset);
  stops.push(onPresentation(p=>{
    motion=!p.reducedMotion && boardMotionScale(p)>0;
    if (!motion) reset();
  }));

  return {
    setTheme(value) {
      if (disposed) return;
      const next=boardThemeKey(value);
      if (theme===next) { bindSources(); return; }
      restoreSources();
      theme=next; style=STYLES[theme] || null;
      reset();
      group.visible=!!style;
      for (let i=0;i<4;i++) {
        const light=lights[i];
        light.intensity=style?.power || 0;
        if (style) {
          light.color.setHex(style.colors[i]); light.distance=style.reach;
          light.position.y=style.height;
        }
      }
      bindSources();
    },
    update(dt) {
      if (disposed || !style) return;
      bindSources();
      running=motion && !hidden() && Number.isFinite(dt) && dt>0;
      if (!running) { reset(); return; }
      clock+=Math.min(.1,dt);
      for (let i=0;i<4;i++) {
        const age=clock-starts[i], duration=lengths[i];
        pulse[i]=age>0 && age<duration
          ? amounts[i]*smooth(age/.34)*(1-smooth((age-.40)/(duration-.40))) : 0;
      }
      paint();
    },
    debug() {
      return {theme,clock,reactions,disposed,lights:lights.length,sources:sources.length,
        baseline:style?.power || 0,intensities:lights.map(light=>light.intensity),
        active:amounts.reduce((n,amount,i)=>n+(amount>0 && clock-starts[i]<lengths[i]?1:0),0)};
    },
    dispose() {
      if (disposed) return;
      disposed=true;
      for (const stop of stops) stop();
      restoreSources();
      group.removeFromParent();
      for (const light of lights) light.dispose();
    },
  };
}
