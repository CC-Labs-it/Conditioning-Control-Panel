import * as T from 'three';

const tau=2*Math.PI;
const mistVertex=`
attribute vec4 cloud;
uniform float clock;
varying vec2 sheet;
varying float seed,fade;
void main(){
  seed=cloud.w;
  float sway=clock*.085+seed;
  vec3 p=cloud.xyz+vec3(.48*sin(sway),.19*cos(sway*.7+seed),.48*cos(sway));
  vec4 eye=modelViewMatrix*vec4(p,1.);
  sheet=uv*2.-1.;
  fade=smoothstep(.5,2.8,-eye.z);
  float size=1.3+fract(seed*.73)*1.6;
  eye.xy+=position.xy*size*vec2(1.35,.78);
  gl_Position=projectionMatrix*eye;
}`;
const mistFragment=`
uniform float density,clock;
varying vec2 sheet;
varying float seed,fade;
void main(){
  float r=dot(sheet,sheet);
  if(r>1.)discard;
  vec2 p=sheet*3.;
  float t=clock*.13+seed;
  float curl=sin(p.x*1.8+sin(p.y*1.9+t)+t)*cos(p.y*1.5-t*.6);
  float veil=(1.-smoothstep(.05,1.,r+.16*curl))*fade;
  vec3 tint=mix(vec3(.50,.10,.32),vec3(1.08,.46,.70),.5+.5*curl);
  gl_FragColor=vec4(tint,density*veil*(.095+.045*curl));
}`;

export function domeMist(group,lite){
  const quad=new T.PlaneGeometry(1,1),geometry=new T.InstancedBufferGeometry().copy(quad);
  quad.dispose();
  const clouds=[];
  const count=lite?100:190;
  for(let i=0;i<count;i++){
    const a=i*2.39996,r=7.85*Math.sqrt((i+.5)/count);
    const h=((i*73)%count)/count;
    clouds.push(r*Math.cos(a),.34+4.5*h*h,r*Math.sin(a),i*1.37);
  }
  geometry.setAttribute('cloud',new T.InstancedBufferAttribute(new Float32Array(clouds),4));
  geometry.instanceCount=count;
  const uniforms={clock:{value:0},density:{value:0}};
  const material=new T.ShaderMaterial({vertexShader:mistVertex,fragmentShader:mistFragment,uniforms,
    transparent:true,depthWrite:false,side:T.FrontSide});
  const mist=new T.Mesh(geometry,material);mist.name='Rose mist';mist.position.set(-22,0,-5);
  mist.frustumCulled=false;mist.renderOrder=1;mist.visible=false;group.add(mist);
  return {
    update(amount,time){mist.visible=amount>.005;uniforms.density.value=amount;uniforms.clock.value=time;},
    dispose(){mist.removeFromParent();geometry.dispose();material.dispose();}
  };
}

// Each burst stays at the point of contact while the next bubble grows elsewhere.
export function bubbleBursts(group){
  const ringGeometry=new T.TorusGeometry(1,.019,6,56),beadGeometry=new T.SphereGeometry(1,9,6);
  const dummy=new T.Object3D(),velocity=[],colour=new T.Color(),pool=[];
  const count=20;
  for(let i=0;i<count;i++){
    const y=1-2*(i+.5)/count,a=i*2.39996,r=Math.sqrt(1-y*y);
    velocity.push(new T.Vector3(r*Math.cos(a),y,r*Math.sin(a)).multiplyScalar(1.2+(i%4)*.23));
  }
  for(let i=0;i<4;i++){
    const root=new T.Group();
    root.name='Bubble burst';root.visible=false;group.add(root);
    const ringMat=new T.MeshBasicMaterial({color:0xffc9df,transparent:true,depthWrite:false});
    const beadMat=new T.MeshBasicMaterial({transparent:true,depthWrite:false});
    const ring=new T.Mesh(ringGeometry,ringMat),beads=new T.InstancedMesh(beadGeometry,beadMat,count);
    for(let j=0;j<count;j++)beads.setColorAt(j,colour.setHex([0xffb5e7,0xaff7f3,0xffe5b5,0xd8bcff][j%4]));
    beads.instanceMatrix.setUsage(T.DynamicDrawUsage);beads.frustumCulled=false;
    root.add(ring,beads);pool.push({root,ring,beads,age:0,quiet:false});
  }
  let cursor=0;
  return {
    start(mesh,camera,quiet){
      const burst=pool[cursor++%pool.length],root=burst.root;
      root.position.copy(mesh.position);root.scale.setScalar(mesh.scale.x);root.visible=true;
      burst.age=0;burst.quiet=quiet;burst.ring.quaternion.copy(camera.quaternion);
      burst.ring.scale.setScalar(1);burst.ring.material.opacity=.75;burst.beads.visible=false;
    },
    update(dt){
      for(const b of pool){
        if(!b.root.visible)continue;
        b.age+=dt;const t=b.age/(b.quiet?.31:.67);
        if(t>=1){b.root.visible=false;continue;}
        b.ring.scale.setScalar(b.quiet?1:1+1.03*(1-(1-t)**3));
        b.ring.material.opacity=.84*(1-t)**2;
        if(b.quiet)continue;
        b.beads.visible=true;b.beads.material.opacity=1-t*t;
        for(let i=0;i<count;i++){
          dummy.position.copy(velocity[i]).multiplyScalar(.62+1.65*t);
          dummy.position.y-=1.7*t*t;
          const size=(.043+.025*(i%3))*(1-t*.72);
          dummy.scale.set(size,size*(1.5-t*.4),size);dummy.updateMatrix();
          b.beads.setMatrixAt(i,dummy.matrix);
        }
        b.beads.instanceMatrix.needsUpdate=true;
      }
    },
    clear(){for(const b of pool)b.root.visible=false;},
    dispose(){for(const b of pool){b.root.removeFromParent();b.ring.material.dispose();b.beads.material.dispose();b.beads.dispose();}
      ringGeometry.dispose();beadGeometry.dispose();}
  };
}

export function driftWords(group){
  const words=['drop','sink','drift','float'];
  const items=words.map(word=>{
    const canvas=document.createElement('canvas');canvas.width=512;canvas.height=192;
    const g=canvas.getContext('2d');g.textAlign='center';g.textBaseline='middle';g.font='italic bold 146px Georgia';
    g.fillStyle='#332544';g.fillText(word,258,103);
    const ink=g.createLinearGradient(0,29,0,157);ink.addColorStop(0,'#fff3d7');ink.addColorStop(.5,'#ffc4e0');ink.addColorStop(1,'#d2b8ef');
    g.fillStyle=ink;g.fillText(word,256,99);
    const map=new T.CanvasTexture(canvas);map.colorSpace=T.SRGBColorSpace;
    const mat=new T.SpriteMaterial({map,transparent:true,depthWrite:false});
    const sprite=new T.Sprite(mat);sprite.name='Dome word '+word;sprite.scale.set(2.1,.79,1);
    group.add(sprite);sprite.visible=false;return sprite;
  });
  return {
    update(amount,t,quiet,camera){
      items.forEach((s,i)=>{
        const phase=i*tau/4+.38+t*(quiet?0:.095);
        const beat=.5+.5*Math.sin(t*(quiet?0:.68)+i*1.81);
        s.position.set(-22+Math.cos(phase)*4.8,1.35+i*.32+(i<2?1-beat:beat)*1.45,-5+Math.sin(phase)*4.8);
        s.material.rotation=quiet?0:.10*Math.sin(phase*2);
        const distance=s.position.distanceTo(camera.position);
        s.material.opacity=amount*(.68+.3*beat)*T.MathUtils.smoothstep(distance,1.2,2.6);s.visible=s.material.opacity>.01;
      });
    },
    dispose(){for(const s of items){s.removeFromParent();s.material.map.dispose();s.material.dispose();}}
  };
}
