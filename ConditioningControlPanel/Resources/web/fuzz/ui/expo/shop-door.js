// Earlier room exports merged the exit bar and the notice's frame into the static shop mesh.
// Keep their authored triangles and UVs attached to the moving leaf in locally cached editions.
function detachOlderTrim(T,room,hinge){
  const corners=new T.Box3(new T.Vector3(14.43,.98,-13.856),new T.Vector3(15.63,1.92,-13.737));
  const point=new T.Vector3(),inverse=new T.Matrix4(),parts=[];
  room.traverse(mesh=>{
    if(!mesh.isMesh||!(mesh.name==='Architecture'||mesh.parent?.name==='Architecture'))return;
    const geom=mesh.geometry,position=geom.attributes.position,index=geom.index;
    if(!index)return;
    const local=corners.clone().applyMatrix4(inverse.copy(mesh.matrixWorld).invert());
    const inside=new Uint8Array(position.count);
    for(let i=0;i<position.count;i++)inside[i]=Number(local.containsPoint(point.fromBufferAttribute(position,i)));
    const selected=[],remain=[];
    for(let i=0;i<index.count;i+=3){
      const a=index.getX(i),b=index.getX(i+1),c=index.getX(i+2);
      (inside[a]&&inside[b]&&inside[c]?selected:remain).push(a,b,c);
    }
    if(!selected.length)return;
    const trim=new T.BufferGeometry();
    for(const [name,attr]of Object.entries(geom.attributes))trim.setAttribute(name,attr);
    trim.setIndex(selected);trim.boundingBox=local;trim.boundingSphere=local.getBoundingSphere(new T.Sphere());
    geom.setIndex(remain);
    const metal=new T.Mesh(trim,mesh.material);metal.name='shop_exit_fittings';
    parts.push([mesh,metal]);
  });
  for(const [source,part] of parts){source.add(part);hinge.attach(part);}
  const sign=room.getObjectByName('Art_shop_door');if(sign)hinge.attach(sign);
}

export function mountShopDoor(T,room,api){
  const leaf=room.getObjectByName('shop_door');
  if(!leaf)return null;
  const hinge=new T.Group();hinge.name='shop_exit_hinge';room.add(hinge);
  hinge.position.copy(room.worldToLocal(new T.Vector3(14.32,.006,-13.93)));
  room.updateMatrixWorld(true);hinge.attach(leaf);
  if(!leaf.userData.swingIncludesTrim)detachOlderTrim(T,room,hinge);
  const free=api.registerDoorLeaf?.('shop','shop-exit',hinge,1.42,2.1);
  return {set(progress){hinge.rotation.y=-1.49*T.MathUtils.smootherstep(progress,0,1);},dispose(){free?.();}};
}
