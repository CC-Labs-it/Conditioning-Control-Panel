import * as THREE from 'three';

// Coordinates are Three world metres. Each source follows a fitted luminaire,
// skylight or tank. Footprints are limited to the visible receiving surface.
const daylight = '#fff0c7', glass = '#b9e9ff', folio = '#ffcf89', grow = '#e0f6ab';
const rooms = {
  atrium: { kind: 'pollen', sources: [
    shaft([-6,11.85,28.4],[-1.1,.12,28.0],1.04,2.14,daylight,53),
    shaft([6,11.85,32.3],[2.4,.12,31.8],.80,1.58,'#deefdc',43),
    shaft([-6,11.85,20.6],[-3,.12,20.1],.71,1.45,glass,34)],
    floor: [[-1.1,28,2.05,2.65],[2.4,31.8,1.62,2.32],[-3,20.1,1.45,2.15]], pattern: 'panes' },
  chronology: { kind:'dust', sources:[
    shaft([8.25,3.47,51.94],[8.25,.08,52.09],.40,.91,folio,34),
    shaft([7.14,3.47,68.65],[7.14,.08,68.85],.55,1.07,folio,39),
    shaft([8.03,3.47,87.66],[8.03,.08,87.8],.42,.92,folio,34)],
    floor:[[8.25,51.94,1.03,2.60],[7.14,68.65,1.18,3.00],[8.03,87.66,1.01,2.28]],pattern:'slats' },
  forum: { kind:'dust', sources:[
    shaft([-9.64,3.6,103],[-9.9,.1,103],.42,1.35,'#cbd8ff',19),
    shaft([-30.08,3.69,104.12],[-30.8,.42,105.35],.49,.96,'#dddfff',31),
    shaft([-46.15,3.6,103],[-46.43,.1,103],.43,1.35,'#b5ddff',19)],
    floor:[[-9.9,103,2.30,1.3],[-46.43,103,2.30,1.3]],pattern:'folds' },
  commons: { kind:'seeds', sources:[
    shaft([-68.95,3.69,88.37],[-65.66,.12,87.56],.53,1.20,grow,46),
    shaft([-63.45,3.69,62.08],[-66.17,.12,62.72],.52,1.31,'#f3f2bd',47),
    shaft([-68.19,3.51,75.20],[-67.49,1.02,75.2],.60,.85,'#d5ffb9',25)],
    floor:[[-65.66,87.56,1.42,2.74],[-66.17,62.72,1.65,2.85]],pattern:'leaves' },
  aquarium: { kind:'bubbles', sources:[
    shaft([-57.88,-.38,49.85],[-57.65,4.92,49.93],.15,.49,'#a9e9fc',60),
    shaft([-48.23,-.38,40.69],[-47.94,5.06,40.83],.13,.48,'#b7f7ff',58),
    shaft([-39.84,-.38,49.46],[-40.08,4.89,49.61],.13,.44,'#afffea',61)],
    floor:[[-49.29,45.50,12.35,2.95]],pattern:'caustics' },
  hall: { kind:'dust', sources:[
    shaft([11.6,5.75,10],[4,.18,9],.64,1.30,daylight,50),
    shaft([-11.6,5.75,2],[-4,.18,1],.61,1.45,glass,46),
    shaft([11.6,5.75,-6],[4,.18,-7],.58,1.36,'#ffe8a8',49)],
    floor:[[4,9,2.13,1.49],[-4,1,2.21,1.65],[4,-7,2.25,1.51]],pattern:'panes' },
  service: {kind:'dust',sources:[shaft([21.5,2.64,32.10],[21.5,.16,31.20],.30,.79,'#bde1e6',23)],
    floor:[[21.5,31.20,1.21,.84]],pattern:'work' },
  secret: {kind:'dust',sources:[47.4,60.4,73.4].map(x=>shaft([x,2.45,41],[x+.35,1.25,42.22],.21,.43,'#ffd39b',14))},
  hoof: {kind:'dust',sources:[shaft([-18.48,2.74,10.34],[-18.48,1.18,9.94],.27,.61,'#ffe1a3',20)]},
  habitat: {kind:'pollen',sources:[shaft([18.45,2.84,11.99],[18.2,.91,11.28],.32,.67,'#b8ffcf',21)]},
  shop: {kind:'dust',sources:[shaft([21.4,2.88,-11.44],[21.3,1.02,-11.54],.31,.77,'#ffe4ba',18)]},
};

function shaft(start,end,head,foot,color,count) {return {start,end,head,foot,color,count};}
const vector = a => new THREE.Vector3(...a);
const attribute = (data,size) => new THREE.Float32BufferAttribute(data,size);
function seeded(seed) {
  return () => { seed=(Math.imul(1664525,seed)+1013904223)>>>0; return seed/4294967296; };
}
function shader(vertexShader,fragmentShader,uniforms,options={}) {
  return new THREE.ShaderMaterial({vertexShader,fragmentShader,uniforms,transparent:true,
    depthTest:true,depthWrite:false,blending:THREE.AdditiveBlending,toneMapped:false,...options});
}
const uniformsFor = clock => ({clock,bufferHeight:{value:768}});

const moteVertex = `
  uniform float clock, bufferHeight;
  attribute vec3 run, tint;
  attribute vec4 seed;
  varying vec3 ink;
  varying float opacity, turn, filterScale;
  void main(){
    float age=fract(seed.x + clock*seed.y);
    float phi=6.28318*(age+seed.x), border=smoothstep(0.,.13,age)*(1.-smoothstep(.82,1.,age));
    vec3 p=position+run*age;
    p.x+=sin(phi+seed.w)*DRIFT;
    p.z+=cos(phi*1.23+seed.w)*DRIFT*.62;
    vec4 eye=modelViewMatrix*vec4(p,1.);
    float physicalSize = seed.z*bufferHeight*projectionMatrix[1][1]*.5/max(.12,-eye.z);
    gl_PointSize=clamp(physicalSize,1.65,POINT_LIMIT);
    filterScale=min(1.,physicalSize/1.65);
    opacity=border*mix(.35,.90,seed.x)*smoothstep(.35,1.2,-eye.z)*(1.-smoothstep(15.,28.,-eye.z));
    turn=phi+seed.w;ink=tint;
    gl_Position=projectionMatrix*eye;
  }`;
const moteFragment = `
  varying vec3 ink;
  varying float opacity,turn,filterScale;
  void main(){
    vec2 q=(gl_PointCoord-.5)*2.;
    float r=length(q), shape;
    #if MOTIF == 3
      float aa=max(fwidth(r),.085),ring=1.-smoothstep(.045,.07+aa,abs(r-.71));
      float glint=1.-smoothstep(.02,.30,length(q-vec2(-.32,-.45)));
      shape=ring*.52+glint*.91+.06*(1.-smoothstep(.15,.8,r));
    #elif MOTIF == 2
      float c=cos(turn),s=sin(turn);
      vec2 leaf=mat2(c,-s,s,c)*q;
      shape=(1.-smoothstep(.32,1.,length(leaf*vec2(.97,2.35))))*.80;
    #else
      shape=(1.-smoothstep(.08,.96,r))*(.88+.12*max(0.,1.-5.*abs(q.x*q.y)));
    #endif
    gl_FragColor=vec4(ink,opacity*shape*filterScale);
  }`;
function motes(spec,lite,clock,seed) {
  const rng=seeded(seed),pos=[],runs=[],seeds=[],colors=[],box=new THREE.Box3();
  const bubbles=spec.kind==='bubbles',leaves=spec.kind==='seeds';
  for(const source of spec.sources) {
    const a=vector(source.start),b=vector(source.end),along=b.clone().sub(a),color=new THREE.Color(source.color);
    const across=new THREE.Vector3(along.y,-along.x,0).normalize();
    const forward=new THREE.Vector3().crossVectors(along,across).normalize();
    const offset=Math.max(source.head,source.foot),bound=new THREE.Vector3(offset+.6,offset+.6,offset+.6);
    for(const end of [a,b])box.expandByPoint(end.clone().sub(bound)).expandByPoint(end.clone().add(bound));
    const count=Math.round(source.count*(lite ? .57 : 1));
    for(let i=0;i<count;i++){
      const angle=rng()*Math.PI*2,r=Math.sqrt(rng()),dx=Math.cos(angle)*r,dz=Math.sin(angle)*r;
      const p=a.clone().addScaledVector(across,dx*source.head).addScaledVector(forward,dz*source.head);
      const end=b.clone().addScaledVector(across,dx*source.foot).addScaledVector(forward,dz*source.foot);
      pos.push(...p.toArray());runs.push(...end.sub(p).toArray());
      const size=bubbles ? .023+rng()*.043 : leaves ? .022+rng()*.035 : .014+rng()*.026;
      const speed=bubbles ? .065 : leaves ? .035 : .019;
      seeds.push(rng(),speed*(1+rng()*.65),size,rng()*6.283);
      colors.push(...color.toArray());
    }
  }
  const geometry=new THREE.BufferGeometry();
  geometry.setAttribute('position',attribute(pos,3));geometry.setAttribute('run',attribute(runs,3));
  geometry.setAttribute('seed',attribute(seeds,4));geometry.setAttribute('tint',attribute(colors,3));
  geometry.boundingSphere=box.getBoundingSphere(new THREE.Sphere());
  const material=shader(moteVertex,moteFragment,uniformsFor(clock),{defines:{MOTIF:bubbles?3:leaves?2:1,
    DRIFT:bubbles?'0.085':leaves?'0.23':'0.095',POINT_LIMIT:bubbles?'17.0':'11.0'}});
  const points=new THREE.Points(geometry,material);points.name=spec.kind;points.renderOrder=bubbles?-5:1;
  return points;
}

// A view-facing cross section keeps the airy edge soft from the opposite
// side of a shaft. Its fragments depth-test against the real room.
function beams(spec) {
  const p=[],axis=[],width=[],uv=[],colors=[],idx=[],box=new THREE.Box3();
  for(const s of spec.sources) {
    const a=vector(s.start),b=vector(s.end),along=b.clone().sub(a).normalize(),color=new THREE.Color(s.color);
    const i=p.length/3;
    for(const [point,span,h] of [[a,s.head,0],[b,s.foot,1]]){
      box.expandByPoint(point.clone().addScalar(span)).expandByPoint(point.clone().addScalar(-span));
      for(const sign of [-1,1]){p.push(...point.toArray());axis.push(...along.toArray());width.push(span*sign);uv.push(sign,h);colors.push(...color.toArray());}
    }
    idx.push(i,i+1,i+2,i+2,i+1,i+3);
  }
  const geometry=new THREE.BufferGeometry();
  geometry.setAttribute('position',attribute(p,3));geometry.setAttribute('axis',attribute(axis,3));
  geometry.setAttribute('width',attribute(width,1));geometry.setAttribute('uv',attribute(uv,2));geometry.setAttribute('tint',attribute(colors,3));
  geometry.setIndex(idx);geometry.boundingSphere=box.getBoundingSphere(new THREE.Sphere());
  const material=shader(`attribute vec3 axis,tint;attribute float width;varying vec2 st;varying vec3 ink;
    void main(){
      vec3 side=cross(axis,cameraPosition-position);
      float l=max(length(side),.01);
      vec3 p=position+side/l*width;
      st=uv;ink=tint;gl_Position=projectionMatrix*viewMatrix*vec4(p,1.);
    }`,`varying vec2 st;varying vec3 ink;
    void main(){
      float profile=pow(max(0.,1.-st.x*st.x),2.5);
      float end=smoothstep(0.,.075,st.y)*(1.-smoothstep(.59,1.,st.y));
      gl_FragColor=vec4(ink,profile*end*(.083-.032*st.y));
    }`,{}, {side:THREE.DoubleSide,forceSinglePass:true});
  const mesh=new THREE.Mesh(geometry,material);mesh.name='light-shafts';mesh.renderOrder=-2;return mesh;
}

const floorPattern = {panes:1,slats:2,folds:3,leaves:4,caustics:5,work:6};
function footlights(spec,clock) {
  const pos=[],uv=[],colors=[],indices=[];
  for(const [j,[x,z,rx,rz]] of spec.floor.entries()){
    const tint=new THREE.Color(spec.sources[j%spec.sources.length].color);
    for(const [u,v] of [[-1,-1],[1,-1],[-1,1],[1,1]]){
      pos.push(x+rx*u,.014,z+rz*v);uv.push(u,v);colors.push(...tint.toArray());
    }
    const i=j*4;indices.push(i,i+2,i+1,i+2,i+3,i+1);
  }
  const geo=new THREE.BufferGeometry();geo.setAttribute('position',attribute(pos,3));geo.setAttribute('uv',attribute(uv,2));
  geo.setAttribute('tint',attribute(colors,3));geo.setIndex(indices);geo.computeBoundingSphere();
  const material=shader(`attribute vec3 tint;varying vec3 ink;varying vec2 st,world;
    void main(){st=uv;world=position.xz;ink=tint;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}
  `,`uniform float clock;varying vec3 ink;varying vec2 st,world;
    vec2 hash(vec2 p){return fract(sin(vec2(dot(p,vec2(34.82,71.11)),dot(p,vec2(65.34,48.15))))*4821.61);}
    void main(){
      float a;
      #if PATTERN == 5
        vec2 p=world*vec2(1.48,1.81);
        vec2 tide=.31*vec2(sin(p.y*1.72+clock*.27),cos(p.x*1.42-clock*.24));
        p+=tide+.14*vec2(sin(p.y*3.41+p.x*.64+clock*.19),cos(p.x*2.62+p.y*.62-clock*.17));
        vec2 cell=floor(p),f=fract(p);float lo=8.,hi=8.;
        for(int i=-1;i<=1;i++)for(int j=-1;j<=1;j++){
          vec2 n=vec2(i,j),h=hash(cell+n);
          vec2 d=n+.5+.33*sin(h*6.283+clock*.19)-f;float dist=dot(d,d);
          if(dist<lo){hi=lo;lo=dist;}else{hi=min(hi,dist);}
        }
        float gap=sqrt(hi)-sqrt(lo),line=1.-smoothstep(.006,.125+fwidth(gap),gap);
        float light=.57+.43*sin(world.y*1.51+sin(world.x*1.29-clock*.16)+clock*.14);
        a=(.015+line*line*(.044+.153*light))*(1.-smoothstep(.81,1.,abs(st.x)))*(1.-smoothstep(.67,1.,abs(st.y)));
      #else
        float border=1.-smoothstep(.61,1.,length(st));
        #if PATTERN == 1
          vec2 bar=abs(fract((world+vec2(.0,.17))*vec2(.89,.74))-.5);
          a=(.075+.147*smoothstep(.018,.092,min(bar.x,bar.y)))*border;
        #elif PATTERN == 2
          float slat=.5+.5*cos(world.y*17.);
          a=(.073+.13*smoothstep(.08,.64,slat))*border;
        #elif PATTERN == 4
          vec2 foliage=world*2.3+vec2(.075*sin(clock*.27),.055*cos(clock*.22));
          float leaf=sin(foliage.x+sin(foliage.y*1.75))*cos(foliage.y+sin(foliage.x*1.2));
          a=(.035+.17*smoothstep(-.30,.38,leaf))*border;
        #else
          a=.098*border;
        #endif
      #endif
      gl_FragColor=vec4(ink,a);
    }`,{clock},{defines:{PATTERN:floorPattern[spec.pattern]},polygonOffset:true,polygonOffsetFactor:-1,polygonOffsetUnits:-1});
  const mesh=new THREE.Mesh(geo,material);mesh.name='incident-floor-light';mesh.renderOrder=-3;return mesh;
}

function cableTrace(clock) {
  // The turquoise inspection conductor follows the underside of the service ladder.
  const geometry=new THREE.CylinderGeometry(.022,.022,15.70,8,1,true);
  geometry.rotateZ(-Math.PI/2);geometry.translate(21.70,2.538,32.67);
  const material=shader(`varying float x;void main(){x=position.x;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
    `uniform float clock;varying float x;void main(){float train=fract((x-13.85)/6.-clock*.092);
      float pulse=smoothstep(.07,.13,train)*(1.-smoothstep(.16,.30,train));
      gl_FragColor=vec4(.23,.83,.77,.12+.56*pulse);}`,{clock});
  const mesh=new THREE.Mesh(geometry,material);mesh.name='service-conductor';return mesh;
}

function forumInlay(clock,speech){
  // A slender inlay follows the fronts of the three actual bronze arcs
  // behind the forum lectern. The radii and end angles match their bends.
  const p=[],st=[],indices=[];
  for(let ring=1;ring<=3;ring++){
    const start=p.length/3;
    for(let i=0;i<=20;i++){
      const a=(-.95+i*.095)*Math.PI;
      for(const side of [-1,1]){
        const r=.27*ring+side*.009;
        p.push(-30.80-r*Math.cos(a),2.64+r*Math.sin(a),105.759);
        st.push(i/20,ring);
      }
      if(i>0){const k=start+i*2;indices.push(k-2,k-1,k,k,k-1,k+1);}
    }
  }
  const g=new THREE.BufferGeometry();g.setAttribute('position',attribute(p,3));g.setAttribute('uv',attribute(st,2));g.setIndex(indices);
  const m=shader(`varying vec2 rail;void main(){rail=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
    `varying vec2 rail;uniform float clock,speech;void main(){
      float phase=fract(.11*clock-.19*rail.y-.11*cos(rail.x*3.14159));
      float lift=smoothstep(.12,.34,phase)*(1.-smoothstep(.35,.68,phase));
      gl_FragColor=vec4(.60,.79,1.,.12+speech*(.23+.61*lift));}`,{clock,speech},{side:THREE.DoubleSide,forceSinglePass:true});
  const mesh=new THREE.Mesh(g,m);mesh.name='lectern-voice-inlay';return mesh;
}

function caseWash(anchor){
  const p=[],uv=[],strength=[],indices=[];
  // Anchor axes follow the local case mesh. The felt back and felt sill
  // catch the tube at different angles. Separate depth tests keep the
  // artifact, brass sill and its printed card in front of the illumination.
  function quad(a,b,c,d,gain){
    const i=p.length/3;
    for(const [point,st] of [[a,[-1,0]],[b,[1,0]],[c,[-1,1]],[d,[1,1]]]){
      p.push(...point);uv.push(...st);strength.push(gain);
    }
    indices.push(i,i+1,i+2,i+2,i+1,i+3);
  }
  quad([-.79,-.944,-.726],[.79,-.944,-.726],[-.79,.003,-.726],[.79,.003,-.726],.145);
  quad([-.79,-.982,.043],[.79,-.982,.043],[-.79,-.982,-.727],[.79,-.982,-.727],.102);
  const g=new THREE.BufferGeometry();g.setAttribute('position',attribute(p,3));g.setAttribute('uv',attribute(uv,2));
  g.setAttribute('strength',attribute(strength,1));g.setIndex(indices);
  const m=shader(`attribute float strength;varying vec2 pane;varying float power;
    void main(){pane=uv;power=strength;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
    `varying vec2 pane;varying float power;void main(){
      float edge=1.-smoothstep(.73,1.,abs(pane.x));
      gl_FragColor=vec4(1.,.71,.38,edge*power*mix(.28,1.,pane.y));}`,{},{side:THREE.DoubleSide,forceSinglePass:true});
  const mesh=new THREE.Mesh(g,m);mesh.name='collection-cabinet-wash';mesh.renderOrder=-2;
  anchor.add(mesh);return mesh;
}

function teaSteam(pot,clock){
  const p=[],uv=[],indices=[];
  for(let k=0;k<2;k++){
    const start=p.length/3;
    for(let i=0;i<=20;i++){
      const u=i/20;
      for(const side of [-1,1]){p.push(k*.005,u*.26+.004,0);uv.push(side,u);}
      if(i>0){const n=start+i*2;indices.push(n-2,n-1,n,n,n-1,n+1);}
    }
  }
  const g=new THREE.BufferGeometry();g.setAttribute('position',attribute(p,3));g.setAttribute('uv',attribute(uv,2));g.setIndex(indices);
  g.boundingSphere=new THREE.Sphere(new THREE.Vector3(0,.14,0),.24);
  const m=shader(`uniform float clock;varying vec2 st;
    void main(){
      float u=uv.y,phase=u*8.-clock*.66+position.x*245.;
      vec3 p=position+vec3(sin(phase)*u*.041-u*u*.022,0.,cos(phase*.89)*u*.032);
      vec4 eye=modelViewMatrix*vec4(p,1.);
      eye.x+=uv.x*mix(.007,.030,u);st=uv;
      gl_Position=projectionMatrix*eye;
    }`,`varying vec2 st;void main(){
      float w=(1.-smoothstep(.0,1.,abs(st.x)))*smoothstep(0.,.09,st.y)*(1.-smoothstep(.32,1.,st.y));
      gl_FragColor=vec4(.91,.95,.91,w*.13);}`,{clock},{side:THREE.DoubleSide,forceSinglePass:true});
  const mesh=new THREE.Mesh(g,m);mesh.position.set(.039,.936,-.041);mesh.name='teapot-spout-steam';
  pot.add(mesh);return mesh;
}

export function createAmbience({ctx,renderer,lite,meter}) {
  const mounted=new Map(),fittings=new Set(),clock={value:7.23},speech={value:0},activity={};
  let drawingHeight=renderer.domElement.height,enabled=true;
  function mount(id,parent) {
    const spec=rooms[id];
    if(!spec||mounted.has(id))return;
    const group=new THREE.Group();group.name='ambience_'+id;
    group.add(motes(spec,lite,clock,Array.from(id).reduce((n,c)=>31*n+c.charCodeAt(0),91)));
    if(spec.kind!=='bubbles')group.add(beams(spec));
    if(spec.floor)group.add(footlights(spec,clock));
    if(id==='service')group.add(cableTrace(clock));
    if(id==='forum')group.add(forumInlay(clock,speech));
    group.children[0].material.uniforms.bufferHeight.value=drawingHeight;
    group.visible=enabled;parent.add(group);mounted.set(id,group);
  }
  function unmount(id) {
    const group=mounted.get(id);if(!group)return;
    group.removeFromParent();
    for(const mesh of group.children){mesh.geometry.dispose();mesh.material.dispose();}
    mounted.delete(id);
  }
  function fitCollection(group){
    const anchor=group.getObjectByName('collection_case_light');
    const pot=group.getObjectByName('collection_tea'),local=[];
    if(anchor)local.push(caseWash(anchor));
    if(pot)local.push(teaSteam(pot,clock));
    for(const mesh of local){mesh.visible=enabled;fittings.add(mesh);}
    return ()=>{for(const mesh of local)if(fittings.delete(mesh)){
      mesh.removeFromParent();mesh.geometry.dispose();mesh.material.dispose();
    }};
  }
  function tick(dt) {
    const calm=ctx.gentle||ctx.reduced;
    if(!calm)clock.value+=dt;
    if(mounted.has('forum')){
      meter(activity);
      const level=activity.voices?1:0;
      speech.value=calm?level:THREE.MathUtils.damp(speech.value,level,2.8,dt);
    }
    const nextHeight=renderer.domElement.height;
    if(nextHeight===drawingHeight)return;
    drawingHeight=nextHeight;
    for(const group of mounted.values())group.children[0].material.uniforms.bufferHeight.value=nextHeight;
  }
  function debug(active) {
    if(typeof active==='boolean'){
      enabled=active;for(const group of mounted.values())group.visible=active;
      for(const mesh of fittings)mesh.visible=active;
    }
    return {enabled,calm:!!(ctx.gentle||ctx.reduced),clock:clock.value,voice:speech.value,
      fittings:[...fittings].map(mesh=>mesh.name),
      rooms:Object.fromEntries([...mounted].map(([id,group])=>[id,{
        particles:group.children[0].geometry.attributes.position.count,
        layers:group.children.map(m=>m.name)}]))};
  }
  return {mount,unmount,tick,fitCollection,debug,dispose(){
    for(const id of mounted.keys())unmount(id);
    for(const mesh of fittings){mesh.removeFromParent();mesh.geometry.dispose();mesh.material.dispose();}
    fittings.clear();
  }};
}
