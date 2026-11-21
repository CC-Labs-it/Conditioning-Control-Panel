import * as T from 'three';
import { normalizeParams2, loomStyleT, loomLayerRotationRad, loomHelix } from '../../../arcademy/engine/loom/loomField.js';
import { createDepth, DOME_CUES } from './depth.js';
import { STRAY_WORDS, THEME_STRAY_WORDS } from '../../data/content/side.js';
import { herdRoster, llamaPortrait } from '../../data/content/llamas.js';
import { livePanel, placard } from './display.js';

const centre = new T.Vector3(-22,0,-5), tau = 2*Math.PI;
const smooth = (a,b,v) => T.MathUtils.smoothstep(v,a,b);
const field = normalizeParams2({layer:{style:'log',arms:4,turns:1.25,duty:.42,colors:['#81baae','#d9ba9e']}});
const tunnel = normalizeParams2({layer:{style:'tunnel',arms:5,turns:2.75,duty:.39,colors:['#91c1b7','#c9a9c0']}});
const worldVertex = 'varying vec3 expoWorld;';
const fieldHeader = `varying vec3 expoWorld;
uniform float depth,rotation,tunnelRotation,pulse,floorMode;
uniform sampler2D loomRadius;
uniform samplerCube chamberProbe;
uniform vec3 mint,pearl;
float band(float p,float duty) {
  float distance=abs(fract(p-duty*.5+.5)-.5)-duty*.5;
  float aa=clamp(fwidth(p)*.62,.00035,.18);
  return 1.-smoothstep(-aa,aa,distance);
}`;

// The continuous field uses the Loom's exact radius curves, normalized layer values, helix and rotation clock.
function weave(source, floor, uniforms, table, probe) {
  const mat = source.clone();
  mat.onBeforeCompile = shader => {
    Object.assign(shader.uniforms,uniforms,{floorMode:{value:floor?1:0},loomRadius:{value:table},chamberProbe:{value:probe},
      mint:{value:new T.Color(field.layer.colors[0])},pearl:{value:new T.Color(field.layer.colors[1])}});
    shader.vertexShader = worldVertex+'\n'+shader.vertexShader.replace('#include <project_vertex>',
      'expoWorld=(modelMatrix*vec4(transformed,1.)).xyz;\n#include <project_vertex>');
    shader.fragmentShader = fieldHeader+'\n'+shader.fragmentShader.replace('#include <opaque_fragment>',`
      vec3 local=expoWorld-vec3(-22.,0.,-5.);
      float r=length(local.xz)/8.65, ang=atan(local.z,local.x);
      float shellR=acos(clamp(normalize(vec3(local.x,local.y-2.75,local.z)).y,0.,1.))/1.5707963;
      vec2 radii=texture2D(loomRadius,vec2(clamp(mix(shellR,r,floorMode),0.,1.),.5)).rg;
      float position=(ang/6.2831853-rotation/6.2831853-${field.layer.turns}*radii.r)*${field.layer.arms}.;
      float coverage=band(position,${field.layer.duty});
      vec3 thread=mix(mint,pearl,mod(floor(position),2.));
      float sky=smoothstep(.25,.33,depth), paper=smoothstep(.10,.19,depth);
      vec3 tile=outgoingLight;
      float relief=.5+.5*cos(6.2831853*(position-${field.layer.duty}*.48));
      vec3 skyColour=mix(vec3(.081,.12,.19)*(.75+.31*relief),thread*(.46+.31*relief+.09*pulse),coverage);
      outgoingLight=mix(tile,skyColour*(.73+.27*clamp(tile,0.,1.)),sky*.87*(1.-floorMode));
      outgoingLight=mix(outgoingLight,min(tile,vec3(1.8))*(.51+thread*.45),paper*(.34+sky*.18)*coverage*floorMode);
      // Project the far spiral well beyond the plaster surface so the drum disappears with correct parallax.
      vec3 eyeRay=normalize(expoWorld-cameraPosition);
      vec3 virtualDir=normalize(cameraPosition-vec3(-22.,1.65,-5.)+eyeRay*37.);
      vec3 axis=normalize(vec3(-.86,.46,.20));
      vec3 east=normalize(cross(vec3(0.,1.,0.),axis)), north=cross(axis,east);
      float arclength=acos(clamp(dot(virtualDir,axis),-.99999,.99999))/3.14159265;
      float value=texture2D(loomRadius,vec2(arclength,.5)).g;
      float spin=atan(dot(virtualDir,north),dot(virtualDir,east))/6.2831853;
      float rings=value*${tunnel.layer.arms}.*2.0 - tunnelRotation*${tunnel.layer.arms}./6.2831853
                + ${loomHelix(tunnel.layer)}.*spin;
      float dream=smoothstep(.75,.89,depth);
      vec3 ink=vec3(.018,.027,.077);
      vec3 lapis=mix(vec3(.077,.48,.44),vec3(.47,.22,.45),.5+.5*cos(value*9.+virtualDir.y*3.));
      float mid=.5+.5*cos(6.2831853*(rings-${tunnel.layer.duty}*.46));
      float satin=smoothstep(.04,.94,mid),edge=band(rings+.012,.018);
      vec3 below=mix(ink*(.45+.7*satin),lapis*(.23+.86*satin),band(rings,${tunnel.layer.duty}));
      below+=mix(vec3(.53,.41,.22),lapis,.45)*edge*(.28+.46*satin);
      float farLight=smoothstep(.012,.40,arclength);
      outgoingLight=mix(outgoingLight,below*(.28+.72*farLight)*(.84+.16*pulse),dream*(1.-floorMode));
      if(floorMode>.5 && dream>.0){
        vec3 ray=normalize(expoWorld-cameraPosition);
        float a=max(dot(ray.xz,ray.xz),.0001), b=dot(local.xz,ray.xz);
        float distanceToWall=(-b+sqrt(max(0.,b*b+a*(8.66*8.66-dot(local.xz,local.xz)))))/a;
        vec3 well=local+ray*distanceToWall;
        float level=clamp(-well.y/32.,0.,1.);
        float shaft=texture2D(loomRadius,vec2(level,.5)).g*${tunnel.layer.arms}. -
          tunnelRotation*${tunnel.layer.arms}./6.2831853+${loomHelix(tunnel.layer)}.*atan(well.z,well.x)/6.2831853;
        float bevel=.45+.55*cos(6.2831853*(shaft-${tunnel.layer.duty}*.46));
        vec3 cavity=mix(ink*(.93+.3*bevel),lapis*(.44+.48*bevel),band(shaft,${tunnel.layer.duty}));
        cavity+=.20*lapis*band(shaft+.015,.018);
        cavity*=1.-smoothstep(10.,32.,-well.y);
        vec3 reflected=reflect(ray,vec3(0.,1.,0.));reflected.x=-reflected.x;
        float fresnel=.045+.74*pow(1.-max(0.,-ray.y),4.);
        vec3 reflection=textureCube(chamberProbe,reflected).rgb;
        float clear=smoothstep(.81,1.,depth);
        vec3 enamel=mix(mix(tile*.61,below,.46),cavity,clear*.96);
        outgoingLight=mix(outgoingLight,mix(enamel,reflection*.6,fresnel)*(.97+.06*pulse),dream);
      }
      #include <opaque_fragment>`);
  };
  mat.customProgramCacheKey = () => 'expo-loom-surface-' + Number(floor);
  return mat;
}
function radiusTable() {
  const data = new Float32Array(1024*4);
  for(let i=0;i<1024;i++){
    data[4*i]=loomStyleT(i/1023,field.layer.style);
    data[4*i+1]=loomStyleT(i/1023,tunnel.layer.style);
    data[4*i+3]=1;
  }
  // Half floats retain the continuous ramp even on mobile GPUs without linear float filtering.
  const half=Uint16Array.from(data,T.DataUtils.toHalfFloat);
  const tex=new T.DataTexture(half,1024,1,T.RGBAFormat,T.HalfFloatType);
  tex.magFilter=tex.minFilter=T.LinearFilter;tex.needsUpdate=true;return tex;
}
function firstMesh(node) {
  let mesh=null;node?.traverse(n=>{if(!mesh && n.isMesh)mesh=n;});return mesh;
}
function walkMeshes(node,fn) {node?.traverse(n=>{if(n.isMesh)fn(n);});}

const bubbleVertex=`
varying vec3 wpos,wn;
void main(){wpos=(modelMatrix*vec4(position,1.)).xyz;wn=normalize(mat3(modelMatrix)*normal);
gl_Position=projectionMatrix*viewMatrix*vec4(wpos,1.);}`;
const bubbleFragment=`
uniform samplerCube room;
uniform sampler2D bubbleArt;
uniform float opacity,time,film;
varying vec3 wpos,wn;
void main(){
  vec3 n=normalize(wn),v=normalize(cameraPosition-wpos);
  float mu=clamp(dot(n,v),0.,1.), fresnel=.022+.978*pow(1.-mu,4.);
  vec3 reflected=reflect(-v,n);reflected.x=-reflected.x;
  vec3 glass=textureCube(room,reflected).rgb;
  vec3 refracted=refract(-v,n,1./1.333);refracted.x=-refracted.x;
  vec3 inside=textureCube(room,refracted).rgb;
  float thickness=film+55.*sin(wpos.y*2.2+time*.14)+31.*n.y;
  vec3 interference=.5+.5*cos(6.2831853*2.*1.33*thickness*(.62+.38*mu)/vec3(650.,530.,445.));
  vec2 artUv=n.xy*.47+.5;
  vec4 art=texture2D(bubbleArt,artUv);
  vec3 colour=mix(inside*.26,glass*1.35,fresnel)+interference*(.15+1.45*fresnel)+art.rgb*art.a*.10;
  gl_FragColor=vec4(colour,opacity*(.15+.82*fresnel+.12*art.a));
}`;

/** The clock belongs to the visit, so leaving, unloading and returning preserve depth and the real visit duration. */
export function createDomeEffects({ctx,camera,say,play,register,bindMedia,setSoundDepth,lite}) {
  const state=createDepth(), heard=new Set(), weather={density:0,time:0};
  const uniforms={depth:{value:0},rotation:{value:0},tunnelRotation:{value:0},pulse:{value:0}};
  let art=null, hallPanel=null, inside=false, motion=0, lifetime=0, actualVisit=0,lastVisit=0,totalVisit=0, visits=0;
  const calm=()=>!!(ctx.gentle || ctx.reduced);
  const phrases=[...(THEME_STRAY_WORDS[ctx.pick?.('f5')] || []),...(THEME_STRAY_WORDS[ctx.state?.store?.taste2?.theme] || [])];
  if (!phrases.length) phrases.push(...STRAY_WORDS);
  function label() {
    if (!hallPanel) return;
    const s=Math.floor(lastVisit), fmt=n=>Math.floor(n/60)+':'+String(n%60).padStart(2,'0');
    placard(hallPanel,'Visitor Services',visits?'You spent '+fmt(s)+'. The average is now '+fmt(Math.max(1,Math.round(totalVisit/(visits+240))))+'.':'Visitors spend an average of 0 seconds in this room.');
  }
  function mount(id,group) {
    if(id==='hall'){
      const face=firstMesh(group.getObjectByName('dome_time')); if(face){hallPanel=livePanel(T,face);label();}
      return;
    }
    if(id!=='dome')return;
    const clean=[], words=[], frames=[], bubbles=[], panes=[], occluders=[];
    const own=x=>(clean.push(()=>x.dispose()),x);
    const table=own(radiusTable()), room=group.userData.reflectionProbe;
    const bubbleArt=own(new T.TextureLoader().load(new URL('../../assets/img/bubble.png',import.meta.url).href));
    bubbleArt.colorSpace=T.SRGBColorSpace;
    for(const name of ['dome_floor','dome_shell'])walkMeshes(group.getObjectByName(name),mesh=>{
      const original=mesh.material;
      const change=m=>own(weave(m,name==='dome_floor',uniforms,table,room));
      mesh.material=Array.isArray(original)?original.map(change):change(original);
      clean.push(()=>{mesh.material=original;});
    });
    const sign=firstMesh(group.getObjectByName('dome_sign')), signMap=sign?.material.map;
    const dynamic=document.createElement('canvas');dynamic.width=1152;dynamic.height=768;
    const dynamicTex=own(new T.CanvasTexture(dynamic));dynamicTex.colorSpace=T.SRGBColorSpace;dynamicTex.flipY=false;
    const originalSign=sign?.material;
    if(sign){sign.material=own(originalSign.clone());clean.push(()=>{sign.material=originalSign;});occluders.push({mesh:sign});}
    const localHerd=herdRoster(ctx.life?.llamas ?? ctx.state?.herd), loader=new T.TextureLoader();
    for(let i=0;i<6;i++){
      const w=group.getObjectByName('dome_word_'+i);if(w){
        const size=new T.Box3().setFromObject(w,true).getSize(new T.Vector3());
        const inks=[];
        words.push({node:w,inks,rest:w.position.clone(),scale:w.scale.clone(),rotation:w.rotation.clone(),halfWidth:Math.max(size.x,size.z)*.5,
          dir:new T.Vector3(Math.cos(i*2.4),.19+Math.sin(i+1)*.12,Math.sin(i*2.4)).normalize()});
        walkMeshes(w,mesh=>{
          occluders.push({mesh});
          const original=mesh.material,ink=own(original.clone());
          ink.color.set('#516745');ink.metalness=.43;ink.roughness=.39;mesh.material=ink;
          inks.push(ink);
          clean.push(()=>{mesh.material=original;});
        });
      }
      const frame=group.getObjectByName('dome_frame_'+i);if(!frame) continue;
      frame.visible=false;
      const panel=localHerd[i%Math.max(1,localHerd.length)];
      if(panel)walkMeshes(frame,mesh=>{
        const original=mesh.material;
        const change=m=>{ if(!/portrait/i.test(m.name))return m;const copy=own(m.clone());
          const map=own(loader.load(llamaPortrait(panel.id)));map.colorSpace=T.SRGBColorSpace;map.flipY=false;copy.map=map;return copy; };
        mesh.material=Array.isArray(original)?original.map(change):change(original);
        clean.push(()=>{mesh.material=original;});
      });
      const pane={object:frame,width:1.43,height:1.05,offset:.053,opacity:0};
      frames.push({node:frame,pane}); if(i%3!==0) panes.push(pane);
      walkMeshes(frame,mesh=>occluders.push({mesh,owner:frame}));
    }
    const geometry=own(new T.SphereGeometry(1,lite?18:28,lite?12:18));
    for(let i=0;i<(lite?9:16);i++){
      const radius=.19+(i%5)*.085;
      const mat=own(new T.ShaderMaterial({vertexShader:bubbleVertex,fragmentShader:bubbleFragment,transparent:true,depthWrite:false,
        uniforms:{room:{value:room},bubbleArt:{value:bubbleArt},opacity:{value:0},time:{value:0},film:{value:285+i*23}}}));
      const mesh=new T.Mesh(geometry,mat);mesh.scale.setScalar(radius);mesh.visible=false;group.add(mesh);
      clean.push(()=>mesh.removeFromParent());
      const anchor=new T.Object3D();group.add(anchor);clean.push(()=>anchor.removeFromParent());
      const pane={object:anchor,width:radius*1.33,height:radius*1.33,offset:0,opacity:0,round:true};
      if(i<3)panes.push(pane);
      const bubble={mesh,mat,anchor,pane,radius,i,pop:-20};bubbles.push(bubble);
      clean.push(register(mesh,'Pop bubble',()=>pop(bubble),1.75));
    }
    art={group,clean,words,frames,bubbles,panes,occluders,sign,signMap,dynamic,dynamicTex,phrase:null};
    bindMedia({picturePanes:panes,mediaOccluders:occluders,get inside(){return inside;},get depth(){return state.depth;}});
    update(0);
  }
  function pop(b) {
    if(!b.mesh.visible||b.pop>lifetime-1)return;
    b.pop=lifetime;b.mesh.visible=false;b.anchor.visible=false;b.pane.opacity=0;play('bubble',.4);
  }
  function update(dt) {
    if(!art)return;
    const d=state.depth,still=calm(), sky=smooth(.25,.3,d), dreams=smooth(.75,.82,d), loosen=smooth(.5,.59,d);
    const {sign,signMap,words,bubbles,frames}=art;
    uniforms.depth.value=d;uniforms.rotation.value=loomLayerRotationRad(field.layer,motion*.008);
    uniforms.tunnelRotation.value=loomLayerRotationRad(tunnel.layer,motion*.006);
    const breath=.5+.5*Math.sin(motion*.43),full=smooth(.92,1,d),pulse=full*(breath-.5);
    uniforms.pulse.value=breath;
    weather.time=motion;weather.density=inside?smooth(.5,.81,d)*(1+pulse*.075):0;
    const inkReveal=smooth(.1,.12,d);
    if(sign) {
      const key=d>=1?phrases[Math.floor(motion/8)%phrases.length]:d>=.1?'':null;
      const inkStep=Math.round(inkReveal*24);
      if(key!==art.phrase || inkStep!==art.inkStep){
        art.phrase=key;art.inkStep=inkStep;
        if(key!==null){
          const g=art.dynamic.getContext('2d');g.fillStyle='#eee8d5';g.fillRect(0,0,1152,768);
          g.strokeStyle='#b99c62';g.lineWidth=3;g.strokeRect(18,18,1116,732);
          g.fillStyle='#826839';g.textAlign='left';g.textBaseline='middle';g.font='600 29px system-ui';g.fillText('VISITOR SERVICES',76,75);
          g.fillRect(76,108,1000,3);g.fillStyle='#15372c';g.textAlign='center';g.font='100px Georgia';g.fillText(key,576,382,1000);
          if(inkStep<24&&signMap?.image){g.globalAlpha=1-inkStep/24;g.drawImage(signMap.image,0,0,1152,768);g.globalAlpha=1;}
          art.dynamicTex.needsUpdate=true; sign.material.map=art.dynamicTex;
        } else sign.material.map=signMap;
        sign.material.needsUpdate=true;
      }
      sign.material.color.setScalar(1+pulse*.045);
    }
    for(let i=0;i<words.length;i++){
      const {node,inks,rest,scale,rotation,dir,halfWidth}=words[i];
      node.visible=inkReveal>0;
      for(const m of inks){
        const fade=inkReveal>0&&inkReveal<1;
        if(m.transparent!==fade){m.transparent=fade;m.needsUpdate=true;}
        m.opacity=inkReveal;
      }
      const t=motion*.33+i*.59, wave=1-Math.abs(((t%4)+4)%4-2);
      node.position.copy(rest);node.position.y+=Math.sin(motion*.22+i)*.004*smooth(.10,.13,d);
      const lift=1.45+1.31*Math.sin(t*.67+i);
      const clearance=halfWidth*(still?3:4)+.05;
      const wall=Math.sqrt(8.65**2-Math.max(0,rest.y+lift-2.75)**2)-clearance;
      const nx=dir.x/Math.hypot(dir.x,dir.z),nz=dir.z/Math.hypot(dir.x,dir.z);
      const rx=rest.x-centre.x,rz=rest.z-centre.z,along=rx*nx+rz*nz;
      const forward=(wave*Math.sqrt(wall*wall-rx*rx-rz*rz+along*along)-along);
      node.position.x+=nx*forward*loosen;node.position.z+=nz*forward*loosen;
      node.position.y+=lift*loosen;
      node.scale.copy(scale).multiplyScalar((1+loosen*(still?2:3))*(1+pulse*.035));
      node.rotation.copy(rotation);node.rotation.y+=loosen*.65*Math.sin(t*.45);
    }
    frames.forEach(({node,pane},i)=>{
      const phase=i*tau/6+motion*.025, r=4.5+(i%2)*1.25;
      node.position.set(centre.x+r*Math.cos(phase),2.16+.65*Math.sin(phase*2+i),centre.z+r*Math.sin(phase));
      // Slowly turning frames keep their picture faces within reach around either side of the orbit.
      node.rotation.y=Math.atan2(centre.x-node.position.x,centre.z-node.position.z)+.24*Math.sin(phase+i);
      pane.opacity=dreams*.94;node.visible=dreams>.008&&(!still||i<4);
      node.scale.setScalar(Math.max(.001,dreams)*(1+pulse*.04));
    });
    for(const b of bubbles){
      const {mesh,anchor,pane,i,radius,mat}=b, orbit=1.95+(i%5)*1.12, a=i*2.39996+motion*.028;
      const y=.4+((motion*(.10+i%3*.02)+i*.81)%5);
      mesh.position.set(centre.x+Math.cos(a)*orbit,y,centre.z+Math.sin(a)*orbit);
      const rise=smooth(1.8,2.8,lifetime-b.pop), grown=sky*rise;
      mesh.visible=grown>.01&&(!still||i<5);
      mesh.scale.setScalar(radius*(.83+.17*grown)*(1+dreams*breath*.045));
      mat.uniforms.opacity.value=grown;mat.uniforms.time.value=motion;
      anchor.visible=mesh.visible;anchor.position.copy(mesh.position);anchor.quaternion.copy(camera.quaternion);pane.opacity=mesh.visible?dreams*.68:0;
      if(mesh.visible&&inside&&mesh.position.distanceTo(camera.position)<radius+.22)pop(b);
    }
  }
  function step(dt, zone) {
    const was=inside;inside=zone==='dome';
    lifetime+=dt;
    if(inside)actualVisit+=dt;
    const change=state.step(dt*(inside && calm() ? .65 : 1),inside);
    if(change.entered && !heard.has(0)){hear(0);}
    if(inside)for(let i=1;i<=state.phase;i++)if(!heard.has(i))hear(i);
    if(change.left){
      lastVisit=actualVisit;totalVisit+=actualVisit;visits++;actualVisit=0;label();
      if(lastVisit>=60)say(DOME_CUES.dome_leave,'dome_leave');
    }
    if(inside||was)setSoundDepth(inside?state.depth:0);
    if(inside||art){motion+=dt*(ctx.reduced ? .075 : ctx.gentle ? .23 : 1);update(dt);}
  }
  function hear(i){heard.add(i);const id='dome_'+i;say(DOME_CUES[id],id);}
  function unmount(id) {
    if(id==='hall'){hallPanel?.dispose();hallPanel=null;}
    if(id!=='dome'||!art)return;
    bindMedia(null);for(const fn of art.clean.reverse())fn();art=null;weather.density=0;
  }
  return {mount,unmount,step,weather,get state(){return state;},dispose(){unmount('dome');unmount('hall');}};
}
