// Surface-bound wearables share the native body's existing animation weights.
export function expandWardrobe({THREE,avatar,nodes,materials,info}) {
  const source=nodes.get('Foundation.Hazmat.Complete'),body=nodes.get('GEO-rain-body');
  if(!source?.geometry||!body)throw Error('Jewelry requires the complete animated native body.');
  const base=source.geometry,p=base.attributes.position,n=base.attributes.normal,region=base.attributes.wardrobeLowerBody,idx=base.index,wearables=[],cache=new Map();
  const V=()=>new THREE.Vector3(),a=V(),b=V(),c=V(),hit=V(),ray=new THREE.Ray(),tri=new THREE.Triangle();
  function section(y,side,arm){
    const key=[y,side,arm].join(':');if(cache.has(key))return cache.get(key);
    const triangles=[],bounds=new THREE.Box3();
    for(let i=0;i<idx.count;i+=3){
      const ids=[idx.getX(i),idx.getX(i+1),idx.getX(i+2)],q=ids.map(j=>V().fromBufferAttribute(p,j));
      if(Math.min(...q.map(v=>v.y))>y||Math.max(...q.map(v=>v.y))<y)continue;
      if(q.some(v=>v.x*side<(arm?.09:.015)))continue;
      if(arm&&ids.reduce((s,j)=>s+region.getX(j),0)>1.5)continue;
      triangles.push(ids);
      for(let j=0;j<3;j++){const u=q[j],v=q[(j+1)%3];if((u.y-y)*(v.y-y)<=0&&Math.abs(v.y-u.y)>.000001)bounds.expandByPoint(u.clone().lerp(v,(y-u.y)/(v.y-u.y)));}
    }
    if(!triangles.length||bounds.isEmpty())throw Error(`No native jewelry section at ${key}`);
    const center=bounds.getCenter(V());center.y=y;
    const result={center,triangles};cache.set(key,result);return result;
  }
  function surface(y,angle,side,arm){
    const sectionData=section(y,side,arm),dir=new THREE.Vector3(Math.cos(angle),0,Math.sin(angle)),origin=sectionData.center.clone().addScaledVector(dir,.3);
    ray.set(origin,dir.clone().negate());let best=Infinity,binding;
    for(const ids of sectionData.triangles){a.fromBufferAttribute(p,ids[0]);b.fromBufferAttribute(p,ids[1]);c.fromBufferAttribute(p,ids[2]);if(!ray.intersectTriangle(a,b,c,false,hit))continue;const distance=origin.distanceToSquared(hit);if(distance>=best)continue;best=distance;tri.set(a,b,c);const weights=tri.getBarycoord(hit,V()).toArray(),normal=V();ids.forEach((j,k)=>normal.addScaledVector(V().fromBufferAttribute(n,j),weights[k]));binding={ids,weights,point:hit.clone(),normal:normal.normalize()};}
    if(!binding)throw Error(`Jewelry surface fit failed at ${y}, ${angle}`);return binding;
  }
  const palette={gold:['#c59c53',.65,.32],silver:['#c6cdd3',.8,.28],pearl:['#eee1ca',.12,.27],leather:['#65424a',.04,.72],rose:['#b97882',.38,.42],gem:['#338f8c',.35,.25]};
  function material(name,key){const [color,metalness,roughness]=palette[key];const m=new THREE.MeshStandardMaterial({name,color,metalness,roughness,flatShading:true,side:THREE.DoubleSide});m.userData.wardrobeColorChannel='accessories';materials.set(name,[m]);return m;}
  function builder(){return {positions:[],indices:[],bindings:[],offsets:[],add(binding,offset){const i=this.bindings.length;this.bindings.push(binding);this.offsets.push(offset.clone());this.positions.push(...binding.point.clone().add(offset).toArray());return i;}};}
  function loop(g,y,side,arm,{radius=.0025,pad=.004,steps=40,from=0,to=Math.PI*2}={}){
    const start=g.bindings.length,closed=Math.abs(to-from-Math.PI*2)<.0001,count=closed?steps:steps+1;
    for(let i=0;i<count;i++){const angle=from+(to-from)*i/steps,binding=surface(y,angle,side,arm);for(let j=0;j<6;j++){const t=j/6*Math.PI*2;g.add(binding,binding.normal.clone().multiplyScalar(pad+radius*Math.cos(t)).add(new THREE.Vector3(0,radius*Math.sin(t),0)));}}
    for(let i=0;i<steps;i++)for(let j=0;j<6;j++){const u=start+i*6+j,v=start+i*6+(j+1)%6,w=start+((i+1)%count)*6+j,x=start+((i+1)%count)*6+(j+1)%6;g.indices.push(u,w,v,v,w,x);}
  }
  function cuff(g,y,width,side,arm,pad=.004,open=false){
    const start=g.bindings.length,steps=40,from=open?.45:0,to=open?Math.PI*2-.45:Math.PI*2,count=open?steps+1:steps;
    for(let i=0;i<count;i++)for(const [dy,d] of [[-width/2,pad],[-width/2,pad+.002],[width/2,pad+.002],[width/2,pad]]){const binding=surface(y+dy,from+(to-from)*i/steps,side,arm);g.add(binding,binding.normal.clone().multiplyScalar(d));}
    for(let i=0;i<steps;i++)for(let j=0;j<4;j++){const u=start+i*4+j,v=start+i*4+(j+1)%4,w=start+((i+1)%count)*4+j,x=start+((i+1)%count)*4+(j+1)%4;g.indices.push(u,w,v,v,w,x);}
  }
  function bead(g,binding,size,offset=new THREE.Vector3()){
    const start=g.bindings.length,rings=5,segments=8;
    for(let j=0;j<=rings;j++)for(let i=0;i<segments;i++){const phi=j/rings*Math.PI,t=i/segments*Math.PI*2;g.add(binding,new THREE.Vector3(Math.sin(phi)*Math.cos(t)*size.x,Math.cos(phi)*size.y,Math.sin(phi)*Math.sin(t)*size.z).add(offset));}
    for(let j=0;j<rings;j++)for(let i=0;i<segments;i++){const u=start+j*segments+i,v=start+j*segments+(i+1)%segments;g.indices.push(u,v,u+segments,v,v+segments,u+segments);}
  }
  function finish(root,g,key,suffix){
    const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(g.positions,3));geometry.setIndex(g.indices);geometry.computeVertexNormals();geometry.morphTargetsRelative=true;
    // Transport every offset in its bound triangle frame, including tangential
    // charm and bead offsets. A normal-only offset would drift during arm turns.
    const offsetFrames=g.bindings.map((bind,i)=>{const q=bind.ids.map(j=>V().fromBufferAttribute(p,j)),u=q[1].sub(q[0]),v=q[2].sub(q[0]),w=bind.normal;return g.offsets[i].clone().applyMatrix3(new THREE.Matrix3().set(u.x,v.x,w.x,u.y,v.y,w.y,u.z,v.z,w.z).invert());});
    geometry.morphAttributes.position=base.morphAttributes.position.map((attribute,k)=>{const values=new Float32Array(g.positions.length),normal=base.morphAttributes.normal?.[k];for(let i=0;i<g.bindings.length;i++){const bind=g.bindings[i],delta=V(),dn=V(),q=bind.ids.map(j=>V().fromBufferAttribute(attribute,j)),offset=offsetFrames[i];bind.ids.forEach((j,t)=>{delta.addScaledVector(q[t],bind.weights[t]);if(normal)dn.addScaledVector(V().fromBufferAttribute(normal,j),bind.weights[t]);});delta.addScaledVector(q[1].sub(q[0]),offset.x).addScaledVector(q[2].sub(q[0]),offset.y).addScaledVector(dn,offset.z);values.set(delta.toArray(),i*3);}return new THREE.BufferAttribute(values,3);});
    const mesh=new THREE.Mesh(geometry,material(`${root.name}.${suffix}`,key));mesh.name=`${root.name}.${suffix}`;mesh.matrixAutoUpdate=false;mesh.frustumCulled=false;mesh.morphTargetInfluences=body.morphTargetInfluences;root.add(mesh);nodes.set(mesh.name,mesh);wearables.push(mesh);
  }
  function item(slot,id,name,swatch,build){const root=new THREE.Group();root.name=`Jewelry.${slot}.${id}`;root.visible=false;avatar.add(root);nodes.set(root.name,root);build(root);info.slots[slot].push({id,name,swatch,nodes:[root.name],note:slot==='ankles'?'Fitted above the ankle. Covered by tall boots and full suits.':'Follows the arm. Covered by full suits.'});}
  for(const slot of ['arms','ankles']){info.slots[slot]=[{id:'none',name:'None',nodes:[]}];info.defaultLook[slot]='none';}
  const wrist=.925,ankle=.225;
  for(const [slot,y,arm] of [['arms',wrist,true],['ankles',ankle,false]]){
    const pair=build=>root=>{for(const side of [-1,1])build(root,side);};
    item(slot,'fine-gold',arm?'Fine bangles':'Fine anklets','#c59c53',pair((root,side)=>{const g=builder();loop(g,y,side,arm,{radius:.002});finish(root,g,'gold',`Fine${side}`);}));
    item(slot,'double-silver',arm?'Stacked bangles':'Double chains','#c6cdd3',pair((root,side)=>{const g=builder();for(const dy of [-.006,.006])loop(g,y+dy,side,arm,{radius:.0022});finish(root,g,'silver',`Double${side}`);}));
    item(slot,'pearl-strand',arm?'Pearl bracelets':'Pearl anklets','#eee1ca',pair((root,side)=>{const g=builder();loop(g,y,side,arm,{radius:.0007,pad:.005});for(let i=0;i<16;i++){const bind=surface(y,i/16*Math.PI*2,side,arm);bead(g,bind,new THREE.Vector3(.0035,.0035,.0035),bind.normal.clone().multiplyScalar(.005));}finish(root,g,'pearl',`Pearls${side}`);}));
    item(slot,'open-cuff',arm?'Sculpted cuffs':'Sculpted ankle cuffs','#c59c53',pair((root,side)=>{const g=builder();cuff(g,y,.018,side,arm,.004,true);finish(root,g,'gold',`Cuff${side}`);}));
    item(slot,'gem-charm',arm?'Drop charm bracelets':'Drop charm anklets','#338f8c',pair((root,side)=>{const g=builder();loop(g,y,side,arm,{radius:.0018});finish(root,g,'gold',`Chain${side}`);const charm=builder(),bind=surface(y,Math.PI/2,side,arm);bead(charm,bind,new THREE.Vector3(.005,.009,.003),bind.normal.clone().multiplyScalar(.008).add(new THREE.Vector3(0,-.009,0)));finish(root,charm,'gem',`Charm${side}`);}));
    item(slot,arm?'forearm-wrap':'ribbon-bow',arm?'Leather forearm wraps':'Ribbon bow anklets','#65424a',pair((root,side)=>{const g=builder();cuff(g,arm?1.005:y,arm?.045:.012,side,arm,.004);finish(root,g,arm?'leather':'rose',`Wrap${side}`);if(!arm){const bow=builder(),bind=surface(y,Math.PI/2,side,arm);for(const x of [-.007,.007])bead(bow,bind,new THREE.Vector3(.008,.004,.003),bind.normal.clone().multiplyScalar(.008).add(new THREE.Vector3(x,0,0)));bead(bow,bind,new THREE.Vector3(.003,.0045,.004),bind.normal.clone().multiplyScalar(.009));finish(root,bow,'rose',`Bow${side}`);}}));
    item(slot,arm?'upper-arm-bands':'spiral-anklets',arm?'Upper-arm bands':'Spiral anklets','#b97882',pair((root,side)=>{const g=builder();if(arm){for(const height of [1.20,1.22])loop(g,height,side,true,{radius:.003});}else{for(let i=0;i<3;i++)loop(g,y+(i-1)*.009,side,false,{radius:.0025,from:i*.35,to:Math.PI*2+i*.35-.22});}finish(root,g,'rose',`Bands${side}`);}));
  }
  for(const dress of info.slots.dress)if(['hazmat-yellow','action-yellow'].includes(dress.id)){dress.coverage??={};dress.coverage.hideSlots=[...new Set([...(dress.coverage.hideSlots||[]),'arms','ankles'])];}
  for(const shoes of info.slots.shoes)if(['midcalf-boots','knee-high-boots'].includes(shoes.id)){shoes.coverage??={};shoes.coverage.hideSlots=[...new Set([...(shoes.coverage.hideSlots||[]),'ankles'])];}
  info.jewelry={revision:'surface-2',method:'Barycentric native surface binding with shared body morph weights',arms:8,ankles:8};
  function update(){for(const mesh of wearables){mesh.matrix.copy(body.matrix);mesh.morphTargetInfluences=body.morphTargetInfluences;}}update();return {update};
}
