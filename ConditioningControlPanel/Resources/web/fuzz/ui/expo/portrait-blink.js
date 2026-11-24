// Pull the photographed upper lashes down over each eye, using the fur immediately above
// the lid. The paper, frame, and room light remain the same throughout the stare.
export function portraitBlink(T,portrait,figure,clean){
  let face;
  portrait?.traverse(n=>{if(n.isMesh&&n.material?.name==='art_portrait_lashes')face=n;});
  const level={value:0},fitted=new Map();
  const eyes=[],parts=[],points=[],p=new T.Vector3();
  figure?.updateWorldMatrix(true,true);
  figure?.traverse(n=>{
    if(!n.isMesh||!/^hero_herd_(eye|pupil)$/.test(n.material?.name))return;
    const position=n.geometry.attributes.position,world=[];
    for(let i=0;i<position.count;i++)world.push(p.fromBufferAttribute(position,i).applyMatrix4(n.matrixWorld).clone());
    parts.push([n,world]);if(n.material.name==='hero_herd_eye')points.push(...world);
  });
  if(points.length){
    const box=new T.Box3().setFromPoints(points),middle=box.getCenter(new T.Vector3());
    const axis=box.max.x-box.min.x>box.max.z-box.min.z?'x':'z';
    const bounds=[0,1].map(i=>new T.Box3().setFromPoints(points.filter(p=>Number(p[axis]>middle[axis])===i)));
    for(const [mesh,world] of parts){
      const old=mesh.geometry,original=mesh.material,material=original.clone(),geometry=old.clone(),height=new Float32Array(world.length);
      world.forEach((p,i)=>{const b=bounds[Number(p[axis]>middle[axis])];height[i]=(p.y-b.min.y)/(b.max.y-b.min.y);});
      geometry.setAttribute('expoLidHeight',new T.BufferAttribute(height,1));mesh.geometry=geometry;mesh.material=material;eyes.push(mesh);
      clean.push(()=>{mesh.geometry=old;mesh.material=original;geometry.dispose();material.dispose();});
    }
  }
  const warp=`
    uniform float lashClosure;
    vec2 closeLid(vec2 uv,vec2 center,float halfWidth,float slope,float height){
      float dx=uv.x-center.x;
      float shape=1.0-dx*dx/(halfWidth*halfWidth);
      if(shape<=0.0)return uv;
      float mid=center.y+dx*slope,upper=mid-height*shape,lower=mid+height*shape;
      float rise=(lower-upper)*lashClosure;
      float a=upper+rise*.77,b=lower-rise*.20;
      float brow=upper-.028,cheek=lower+.016;
      if(uv.y<=brow||uv.y>=cheek)return uv;
      if(uv.y<a)uv.y=mix(brow,upper,(uv.y-brow)/(a-brow));
      else if(uv.y<b)uv.y=mix(upper,lower,(uv.y-a)/(b-a));
      else uv.y=mix(lower,cheek,(uv.y-b)/(cheek-b));
      return uv;
    }
    vec2 portraitUV(vec2 uv){
      uv=closeLid(uv,vec2(.556,.391),.033,-.31,.014);
      return closeLid(uv,vec2(.388,.402),.022,.57,.008);
    }`;
  function fit(material,bronze=false){
    const compile=material.onBeforeCompile,programKey=material.customProgramCacheKey;
    material.onBeforeCompile=function(shader,renderer){
      compile.call(this,shader,renderer);
      shader.uniforms.lashClosure=level;
      if(bronze){
        shader.vertexShader='attribute float expoLidHeight;\nvarying float lidHeight;\n'+shader.vertexShader;
        shader.vertexShader=shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\nlidHeight=expoLidHeight;');
        shader.fragmentShader='uniform float lashClosure;\nvarying float lidHeight;\n'+shader.fragmentShader;
        shader.fragmentShader=shader.fragmentShader.replace('#include <map_fragment>',`#include <map_fragment>
          float lidCover=smoothstep(1.015-lashClosure,1.055-lashClosure,lidHeight)*smoothstep(0.,.035,lashClosure);
          float crease=1.-smoothstep(.015,.10,abs(lidHeight-(1.045-lashClosure)));
          diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.16,.078,.041)*(1.-crease*.71),lidCover);`)
          .replace('#include <roughnessmap_fragment>','#include <roughnessmap_fragment>\nroughnessFactor=mix(roughnessFactor,.42,lidCover);');
      }else shader.fragmentShader=shader.fragmentShader.replace('void main() {',warp+'\nvoid main() {')
          .replace('#include <map_fragment>',T.ShaderChunk.map_fragment.replaceAll('vMapUv','portraitUV(vMapUv)'));
    };
    material.customProgramCacheKey=()=>programKey.call(material)+(bronze?'-expo-lash-cast':'-expo-lash-portrait');
    material.needsUpdate=true;fitted.set(material,{compile,programKey});
  }
  clean.push(()=>{for(const [m,old] of fitted){m.onBeforeCompile=old.compile;m.customProgramCacheKey=old.programKey;m.needsUpdate=true;}});
  return value=>{
    level.value=Math.max(0,Math.min(1,value));
    if(value>0){
      if(face&&!fitted.has(face.material))fit(face.material);
      for(const n of eyes)if(!fitted.has(n.material))fit(n.material,true);
    }
  };
}
