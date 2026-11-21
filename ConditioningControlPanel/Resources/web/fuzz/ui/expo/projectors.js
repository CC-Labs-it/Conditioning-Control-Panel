import * as T from 'three';

const quadVertex = `
varying vec2 slideUV;
void main(){slideUV=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}
`;
const quadFragment = `
uniform sampler2D oldSlide, newSlide, restingAtlas;
uniform vec2 cell;
uniform float blend, lamp, zoom, oldDetail, newDetail;
varying vec2 slideUV;
void main(){
  vec2 p=(slideUV-.5)/(1.+zoom)+.5;
  float edge=1.-.17*dot((slideUV-.5)*2.,(slideUV-.5)*2.);
  vec2 atlasUV=(cell+clamp(p,vec2(.004,.006),vec2(.996,.994)))/vec2(8.,6.);
  vec3 rest=texture2D(restingAtlas,atlasUV).rgb;
  vec3 print=mix(mix(rest,texture2D(oldSlide,p).rgb,oldDetail),mix(rest,texture2D(newSlide,p).rgb,newDetail),blend);
  vec3 light=vec3(.043,.037,.026)+print*vec3(1.,.925,.785)*edge*(.20+1.26*lamp);
  gl_FragColor=vec4(light,1.);
  #include <tonemapping_fragment>
  #include <colorspace_fragment>
}`;
const beamVertex = `
attribute vec3 beamUV;
varying vec3 vBeam;
void main(){vBeam=beamUV;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}
`;
const beamFragment = `
uniform float lamp;
varying vec3 vBeam;
void main(){
  float t=vBeam.z;
  float crossEdge=1.-smoothstep(.70,1.,abs(vBeam.x/max(.001,t)));
  float alpha=(.017+.057*lamp)*crossEdge*smoothstep(.0,.045,t)*(1.-.60*t);
  gl_FragColor=vec4(.87,.71,.43,alpha);
  #include <colorspace_fragment>
}`;

function meshIn(node) {
  let mesh;
  node?.traverse(item=>{if(!mesh&&item.isMesh)mesh=item;});
  return mesh;
}

export function centre(node) {
  const box=new T.Box3().setFromObject(node,true);
  return box.isEmpty()?node.getWorldPosition(new T.Vector3()):box.getCenter(new T.Vector3());
}

// The pull-down print may be oriented along either wall. Its actual UV corners determine the throw.
function screenCorners(mesh) {
  const uv=mesh.geometry.attributes.uv, pos=mesh.geometry.attributes.position;
  if(!uv||!pos)return [];
  return [[0,0],[1,0],[1,1],[0,1]].map(([u,v])=>{
    let best=Infinity, index=0;
    for(let i=0;i<uv.count;i++){
      const d=(u-uv.getX(i))**2+(v-uv.getY(i))**2;
      if(d<best){best=d;index=i;}
    }
    return new T.Vector3().fromBufferAttribute(pos,index).applyMatrix4(mesh.matrixWorld);
  });
}

function lightThrow(lens,corners,level,lite) {
  const group=new T.Group(), verts=[], coords=[];
  if(corners.length!==4)return group;
  for(let i=0;i<4;i++){
    verts.push(...lens.toArray(),...corners[i].toArray(),...corners[(i+1)%4].toArray());
    coords.push(0,0,0,-1,0,1,1,0,1);
  }
  const geo=new T.BufferGeometry();
  geo.setAttribute('position',new T.Float32BufferAttribute(verts,3));
  geo.setAttribute('beamUV',new T.Float32BufferAttribute(coords,3));
  const mat=new T.ShaderMaterial({uniforms:{lamp:level},vertexShader:beamVertex,fragmentShader:beamFragment,
    side:T.DoubleSide,transparent:true,depthWrite:false,blending:T.AdditiveBlending,forceSinglePass:true});
  const beam=new T.Mesh(geo,mat);beam.renderOrder=2;group.add(beam);
  const specks=[], random=i=>{const v=Math.sin(i*234.76+.3)*12345.123;return v-Math.floor(v);};
  for(let i=0;i<(lite?13:31);i++){
    const u=.12+.76*random(i+4),v=.12+.76*random(i+92),t=.10+.83*random(i+170);
    const upper=corners[0].clone().lerp(corners[1],u), lower=corners[3].clone().lerp(corners[2],u);
    specks.push(...upper.lerp(lower,v).lerp(lens,1-t).toArray());
  }
  const dustGeo=new T.BufferGeometry();
  dustGeo.setAttribute('position',new T.Float32BufferAttribute(specks,3));
  const dust=new T.Points(dustGeo,new T.PointsMaterial({color:0xe4cc9e,transparent:true,opacity:.18,
    size:.007,depthWrite:false,blending:T.AdditiveBlending}));
  group.add(dust);group.userData.dust=dust;
  return group;
}

export function createRestingAtlas(assetURL){
  let texture;
  const index=new Map();
  return {index,arrange(names){
    index.clear();names.forEach((name,i)=>index.set(name,i));
  },get texture(){
    if(!texture){
      texture=new T.TextureLoader().load(assetURL('narration/resting-atlas.webp'));
      texture.flipY=false;texture.colorSpace=T.SRGBColorSpace;
    }
    return texture;
  },dispose(){texture?.dispose();}};
}

export function createProjector({group,id,scene,assetURL,lite,atlas}) {
  const face=meshIn(group.getObjectByName('expo_screen_'+id)), lens=meshIn(group.getObjectByName('expo_lens_'+id));
  if(!face||!lens)return null;
  const original=face.material, lensOriginal=lens.material;
  const archive=id.startsWith('slot_');
  const viewPoint=centre(face);
  const viewNormal=new T.Vector3().fromBufferAttribute(face.geometry.attributes.normal,0)
    .applyNormalMatrix(new T.Matrix3().getNormalMatrix(face.matrixWorld));
  const level={value:0}, blank=atlas.texture;
  const uniforms={oldSlide:{value:blank},newSlide:{value:blank},restingAtlas:{value:blank},cell:{value:new T.Vector2(7,5)},
    oldDetail:{value:0},newDetail:{value:0},blend:{value:1},lamp:level,zoom:{value:0}};
  const print=new T.ShaderMaterial({uniforms,vertexShader:quadVertex,fragmentShader:quadFragment,side:T.DoubleSide});
  face.material=print;
  const light=lensOriginal?.isMaterial?lensOriginal.clone():null;
  if(light?.emissive){light.emissive.set(0xffcb84);light.emissiveIntensity=.18;lens.material=light;}
  const pos=centre(lens), beam=lightThrow(pos,screenCorners(face),level,lite);
  scene.add(beam);beam.visible=false;
  let active=false, paused=false, dead=false, key='', shown='', restKey='', failed='', retryAt=0, request=0, previous=null, current=null, age=0;
  let nextPath='',nextPromise=null;
  const loader=new T.TextureLoader();
  const fetchTexture=path=>loader.loadAsync(assetURL('narration/'+path)).then(texture=>{
    texture.flipY=false;texture.colorSpace=T.SRGBColorSpace;texture.userData.expoSlidePath=path;return texture;
  });
  function dropNext(){
    if(nextPromise)nextPromise.then(texture=>texture?.dispose());
    nextPromise=null;nextPath='';
  }
  function prime(path){
    if(!path||path===nextPath)return;
    dropNext();nextPath=path;nextPromise=fetchTexture(path).catch(()=>null);
  }
  function image(path){
    if(dead||!path||path===key||(path===failed&&age<retryAt))return;
    const token=++request;key=path;
    let promise;
    if(nextPath===path){promise=nextPromise;nextPromise=null;nextPath='';}
    else promise=fetchTexture(path);
    promise.then(texture=>{
      if(!texture)throw Error('Preloaded slide unavailable');
      if(dead||token!==request){texture.dispose();return;}
      failed='';previous?.dispose();previous=current;current=texture;
      uniforms.oldSlide.value=previous||blank;uniforms.newSlide.value=current;uniforms.blend.value=0;
      uniforms.oldDetail.value=previous?1:0;uniforms.newDetail.value=1;
    }).catch(()=>{if(token===request){key='';failed=path;retryAt=age+4;}});
  }
  function clearSlides(){
    request++;key='';previous?.dispose();current?.dispose();previous=null;current=null;
    uniforms.oldSlide.value=blank;uniforms.newSlide.value=blank;
    uniforms.oldDetail.value=0;uniforms.newDetail.value=0;
  }
  function step(dt,gentle,distance,slide,progress,rest,following){
    if(paused)return;
    age+=dt;
    shown=active?slide:rest;
    if(rest!==restKey){
      // A new record changes the digital plaque and its idle picture on the same frame.
      if(!active && key)clearSlides();
      restKey=rest;const i=atlas.index.get(rest)??47;uniforms.cell.value.set(i%8,Math.floor(i/8));
    }
    const closePrint=archive && !active && rest && distance<(key?7:5.5);
    if(slide&&active&&distance<13){image(slide);prime(following);}
    if(closePrint){image(rest);if(nextPromise)dropNext();}
    if(!active && !closePrint && key){
      request++;key='';dropNext();previous?.dispose();previous=current;current=null;
      uniforms.oldSlide.value=previous||blank;uniforms.oldDetail.value=previous?1:0;
      uniforms.newSlide.value=blank;uniforms.newDetail.value=0;uniforms.blend.value=0;
    }
    if(distance>15&&(key||nextPromise)){clearSlides();dropNext();}
    const fade=1-Math.exp(-dt*(gentle?2.1:3.4));
    level.value+=((active?1:0)-level.value)*fade;
    uniforms.blend.value=Math.min(1,uniforms.blend.value+dt/(gentle?1.15:.68));
    if(uniforms.blend.value===1 && previous){previous.dispose();previous=null;uniforms.oldSlide.value=current||blank;uniforms.oldDetail.value=current?1:0;}
    uniforms.zoom.value=gentle||!active?0:Math.min(.04,Math.max(0,progress)*.04);
    if(light?.emissive)light.emissiveIntensity=.18+2.4*level.value;
    beam.visible=distance<13&&(level.value>.012||distance<5);
    const dust=beam.userData.dust;
    if(dust){dust.visible=level.value>.03;
      dust.material.opacity=.23*level.value;
      dust.position.set(gentle?0:.009*Math.sin(age*.28),gentle?0:.012*Math.sin(age*.21),0);}
  }
  function setActive(on,slide){
    active=on;
    // The audio can start between render frames while the idle wide shot is still displayed.
    if(on && slide){shown=slide;image(slide);}
  }
  return {pos,viewPoint,viewNormal,step,setPaused(on){paused=on;},setActive,
    get stats(){return {active,paused,slide:shown,texture:current?.userData.expoSlidePath,resting:restKey,
      full:!!current&&current.userData.expoSlidePath===shown,lamp:level.value,zoom:uniforms.zoom.value};},
    dispose(){dead=true;clearSlides();dropNext();face.material=original;lens.material=lensOriginal;light?.dispose();print.dispose();
      beam.removeFromParent();beam.traverse(o=>{o.geometry?.dispose();o.material?.dispose();});}};
}
