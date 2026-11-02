/* Garage-only ink and cel paint. Shared source assets, accepted faces and race materials stay intact. */
import {MeshToonNodeMaterial,MeshBasicNodeMaterial,Mesh,SkinnedMesh,BackSide,DataTexture,RGBAFormat,NearestFilter} from 'three/webgpu';
import {texture,vertexColor,saturation,uniform,positionLocal,normalLocal} from 'three/tsl';

export function createGarageStyle() {
  const cache=new WeakMap(),owned=new Set();
  const ramp=new DataTexture(new Uint8Array([96,96,96,255, 157,157,157,255, 216,216,216,255, 255,255,255,255]),4,1,RGBAFormat);
  ramp.minFilter=ramp.magFilter=NearestFilter;ramp.generateMipmaps=false;ramp.needsUpdate=true;
  const ink=new MeshBasicNodeMaterial({color:0x120f19,side:BackSide,name:'garage_ink'});
  // setupPosition skins the original normal and position first, so the hull follows every accepted bone.
  ink.positionNode=positionLocal.add(normalLocal.normalize().mul(.0035));ink.toneMapped=false;
  function paint(source) {
    if(cache.has(source))return cache.get(source);
    const m=new MeshToonNodeMaterial({name:source.name,gradientMap:ramp});
    let base=source.colorNode||(source.map?texture(source.map).rgb.mul(uniform(source.color)):uniform(source.color));
    if(source.vertexColors)base=base.mul(vertexColor().rgb);
    m.colorNode=saturation(base,1.38).clamp(0,1);m.vertexColors=false;
    m.side=source.side;m.transparent=source.transparent;m.opacity=source.opacity;m.alphaTest=source.alphaTest;
    m.depthWrite=source.depthWrite;m.userData={...source.userData,garagePaint:true};
    cache.set(source,m);owned.add(m);return m;
  }
  return {
    apply(root,{locked=false}={}) {
      if(locked)return;
      const meshes=[];root.traverse(m=>{if(m.isMesh&&!m.userData.garageInk)meshes.push(m);});
      for(const m of meshes){
        const source=m.material;
        if(!source||Array.isArray(source)||source.transparent||source.name==='emi_screen'||m.userData.garageOutlined)continue;
        // Self-lit mechanisms retain their moving Loom materials. Painted shell and outfits receive cel bands.
        if(!locked&&(source.isMeshStandardMaterial||source.isMeshStandardNodeMaterial||source.isMeshPhysicalNodeMaterial))m.material=paint(source);
        if(!m.geometry.getAttribute('normal'))continue;
        const hull=m.isSkinnedMesh?new SkinnedMesh(m.geometry,ink):new Mesh(m.geometry,ink);
        if(m.isSkinnedMesh){hull.bindMode=m.bindMode;hull.bind(m.skeleton,m.bindMatrix);}
        hull.name='garage_ink';hull.userData.garageInk=true;hull.frustumCulled=false;
        hull.raycast=()=>{};hull.renderOrder=m.renderOrder;hull.layers.mask=m.layers.mask;
        m.add(hull);m.userData.garageOutlined=true;
      }
    },
    dispose(){for(const m of owned)m.dispose();ink.dispose();ramp.dispose();},
  };
}
