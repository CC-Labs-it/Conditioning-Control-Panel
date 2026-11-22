import {Box3,Group,InstancedMesh,MathUtils,Matrix4,Vector3} from 'three';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {MeshoptDecoder} from 'three/addons/libs/meshopt_decoder.module.js';

import {door,doorways} from './doorways.js';
export {doorways};
const openGalleries=[
  ['chronology','forum',3.4,0,103],['forum','commons',-66.2,0,98.6],['commons','aquarium',-61.8,0,45.5]
];
const includesPair=(rooms,from,to)=>rooms.length===2&&rooms.includes(from)&&rooms.includes(to);
const distanceTo=(p,x,y,z)=>Math.hypot(p.x-x,p.z-z,Math.max(0,Math.abs(p.y-y)-.45)*2.2);
export function streamingPriority(from,to,p,lite,forward){
  // The crown and rotunda stair are open sightlines across vertically overlapping room bounds.
  if((from==='dome'&&(to==='catwalk'||to==='hall'))||(from==='twelve'&&to==='hall'))return {distance:0,near:1,keep:1};
  if(from==='catwalk'&&to==='dome'&&(p.z<14.5||p.y>9.2))return {distance:0,near:1,keep:1};
  if(from==='catwalk'&&to==='atrium'&&p.x>=-16&&Math.abs(p.z-28)<2.5)return {distance:0,near:1,keep:1};
  if(from==='service'&&to==='atrium'&&p.y>4.65&&p.z<25.6)return {distance:0,near:1,keep:1};
  if(from==='atrium'&&to==='hall'&&p.z<35.4&&p.y<1.15&&Math.abs(p.x)<5.2&&forward?.z<-.5){
    // A visitor who has cleared the turnstile can see the hall all along the axial aisle.
    return {distance:distanceTo(p,0,0,16)*.35,near:7.0,keep:27};
  }
  if(from==='hall'&&to==='twelve'){
    // The axial stair gives a long, straight view of the museum's largest download.
    // Start it when the visitor walks toward that end, before the side booths can occupy the queue.
    const d=distanceTo(p,0,0,-16),headingNorth=forward?.z<-.55&&Math.abs(p.x)<7.8&&p.y<1.1;
    return {distance:headingNorth?d*.28:d,near:headingNorth?9.8:(lite?11:13),keep:38};
  }
  const gallery=openGalleries.find(([a,b])=>includesPair([a,b],from,to));
  if(gallery)return {distance:distanceTo(p,...gallery.slice(2)),near:lite?15:17,keep:lite?22:26};
  let best=Infinity;
  for(const d of doorways)if(includesPair(d.rooms,from,to))best=Math.min(best,distanceTo(p,d.x,d.y,d.z));
  if(!Number.isFinite(best))return {distance:Infinity,near:0,keep:0};
  const westGallery=to==='aquarium',eastGallery=to==='chronology';
  if(from==='atrium'&&(eastGallery||westGallery)){
    // Crossing the admission plaza toward the main aisle passes within ten metres of both wings.
    // Reserve that bandwidth for the hall until the visitor actually picks a side entrance.
    const approach=p.y<1.3&&p.z>35.80&&(eastGallery?p.x>3.2:p.x<-3.2);
    return {distance:best,near:approach?12:3.1,keep:18};
  }
  // With the downstairs staff exit in view, its upper partner must not pull in an unrelated level.
  return {distance:best,near:from==='atrium'?(lite?9.7:10.9):(lite?10.8:12.8),keep:lite?17:22};
}

export async function loadDoorHardware(url,signal){
  const response=await fetch(url,{signal});
  if(!response.ok)throw Error('Door hardware HTTP '+response.status);
  const {scene}=await new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).parseAsync(await response.arrayBuffer(),new URL('.',url).href);
  scene.updateMatrixWorld(true);
  const parts=new Map();
  scene.traverse(node=>{
    if(!node.isMesh||!node.userData.assembly)return;
    const name=node.userData.assembly;
    if(!parts.has(name))parts.set(name,[]);
    parts.get(name).push(node);
  });
  for(const key of ['Leaf_civic','Hinge_civic','Leaf_staff','Hinge_staff'])if(!parts.get(key)?.length)throw Error('Missing physical door assembly '+key);
  return {parts,dispose(){scene.traverse(m=>{if(m.isMesh){m.geometry.dispose();m.material.dispose();}});}};
}

export function createDoors(scene,walker,ctx,camera,play){
  const rooms=new Map(),active=new Map(),scratch=new Vector3(),heading=new Vector3(),last=new Vector3();
  let hardware=null,crown=null,inspectionWall=null;
  function appearance(zone,source,finish=source.userData.finish){
    const existing=zone.materials.get(finish)||source.material;
    if(zone.finishes.has(existing))return zone.finishes.get(existing);
    const m=existing.clone();
    m.lightMap=null;m.envMap=zone.environment;m.envMapIntensity=1.45;
    zone.finishes.set(existing,m);return m;
  }
  function build(d){
    const root=new Group();root.name='swing_'+d.id;root.position.set(d.x,d.y,d.z);
    root.rotation.y=d.axis==='x'?0:-Math.PI/2;scene.add(root);
    const state={d,root,leaves:[],progress:0,side:1,hold:0,finishRoom:null};
    const shared=d.rooms.length===2,roughWidth=d.roughWidth??d.width,roughHeight=d.roughHeight??d.height;
    // Offset swinging pins from the deep casing so the heel of each double-action leaf clears it.
    const pinInset=d.lift?0:.075,undercut=shared&&!d.lift?.036:0;
    if(shared){
      state.backing=new Group();
      // The fixed hinge tongues already close the outer strips of the opening.
      state.backing.scale.set(d.width - 2 * pinInset,d.height/2.18,1);
      for(const part of hardware.parts.get('Backing')||[])state.backing.add(part.clone(false));
      root.add(state.backing);
    }
    if(shared||d.roughWidth||pinInset){
      // The two rooms can have different rough openings. A fixed head and jamb liner fills the
      // rebate beyond the moving leaves. One instanced draw uses the authored gasket profile.
      const profile=hardware.parts.get('Backing')?.[0];
      if(profile){
        const outerWidth=d.reveal?.width??(roughWidth+(shared?.38:0));
        const outerHeight=d.reveal?.height??(roughHeight+(shared?.34:0));
        const depth=d.reveal?.depth??.055,offset=d.reveal?.offset??0;
        const edge=(outerWidth-d.width)*.5,top=outerHeight-d.height,inner=d.width*.5;
        const bands=[];
        if(edge>0)bands.push([-inner-edge*.5,0,edge,d.height,depth,offset],[inner+edge*.5,0,edge,d.height,depth,offset]);
        if(top>0)bands.push([0,d.height,outerWidth,top,depth,offset]);
        // Thin metal tongues bridge the casing to the fixed hinge plates, clear of the rotating heel.
        if(pinInset)for(let i=0;i<d.leaves;i++)bands.push([(i===0?-1:1)*(inner-pinInset*.5),0,pinInset,d.height,.011]);
        const trim=new InstancedMesh(profile.geometry,profile.material,bands.length),m=new Matrix4();
        state.fixed=[];
        for(const [i,[x,y,width,height,bandDepth,z=0]] of bands.entries()){
          m.makeScale(width,height/2.18,bandDepth/.011).setPosition(x,y,z).multiply(profile.matrix);
          trim.setMatrixAt(i,m);
          const jamb=new Group();jamb.position.set(x-width/2,y,z);root.add(jamb);
          state.fixed.push(registerLeaf(d.rooms[0],d.id+'-jamb-'+i,jamb,width,height,true,bandDepth*.5));
        }
        trim.name='threshold_liner_'+d.id;root.add(trim);state.trim=trim;state.trimSource=profile;
      }
    }
    for(let i=0;i<d.leaves;i++){
      const hand=i===0?1:-1;
      const fixed=new Group(),rotor=new Group(),width=d.width/d.leaves-pinInset;
      fixed.position.x=rotor.position.x=-hand*(d.width*.5-pinInset);
      fixed.position.y=undercut;fixed.scale.set(hand*width,(d.height-undercut)/2.18,1);root.add(fixed,rotor);
      const panel=new Group();panel.position.y=undercut;panel.scale.copy(fixed.scale);rotor.add(panel);
      const handle=new Group(),handleSize=Math.min(1,d.height/2.1);
      handle.scale.set(hand*handleSize,handleSize,1);
      handle.position.set(hand*(width-.20*handleSize),Math.min(1.04,d.height*.49),0);rotor.add(handle);
      const surfaces=[];
      const assemblies=d.lift?[['Leaf',panel]]:[['Leaf',panel],['Hinge',fixed],['Handle',handle]];
      for(const [part,object]of assemblies)for(const source of hardware.parts.get(part+'_'+d.kind)||[]){
        const geom=source.clone(false);object.add(geom);surfaces.push({geom,source});
      }
      state.leaves.push({rotor,hand,width,surfaces,hx:0,hz:0,tx:0,tz:0});
    }
    active.set(d.id,state);pose(state);return state;
  }
  function pose(s){
    const ease=MathUtils.smootherstep(s.progress,0,1),angle=ease*Math.PI*(84/180);
    if(s.backing)s.backing.visible=s.progress===0;
    for(const leaf of s.leaves){
      if(s.d.lift){
        // The little authored door is already open on its outside jamb. Its protective grille
        // runs straight up inside the head so only that original door remains in the sightline.
        leaf.rotor.position.y=s.d.lift*ease;leaf.rotor.visible=s.progress<1;
      }else leaf.rotor.rotation.y=leaf.hand*s.side*angle;
      leaf.rotor.getWorldPosition(scratch);leaf.hx=scratch.x;leaf.hy=scratch.y;leaf.hz=scratch.z;
      scratch.set(leaf.hand,0,0).transformDirection(leaf.rotor.matrixWorld);
      leaf.tx=scratch.x;leaf.tz=scratch.z;
    }
  }
  function sync(){
    if(!hardware)return;
    syncCrown();
    syncStairEnd();
    syncInspectionWall();
    for(const d of doorways){
      let s=active.get(d.id);
      const source=rooms.has(s?.finishRoom)?s.finishRoom:d.rooms.find(id=>rooms.has(id));
      if(!source){if(s){for(const remove of s.fixed||[])remove();s.root.removeFromParent();s.trim?.dispose();active.delete(d.id);}continue;}
      if(!s)s=build(d);
      if(s.progress>0&&!d.rooms.every(id=>rooms.has(id))){s.progress=0;s.hold=0;pose(s);}
      if(s.finishRoom!==source){
        const zone=rooms.get(source);
        for(const leaf of s.leaves)for(const {geom,source:mesh}of leaf.surfaces)geom.material=appearance(zone,mesh);
        if(s.trim)s.trim.material=appearance(zone,s.trimSource,d.roughWidth?'alloy':s.trimSource.userData.finish);
        s.finishRoom=source;
      }
    }
  }
  function syncInspectionWall(){
    const surface=rooms.get('catwalk')?.wallSurface;
    if(!surface){
      inspectionWall?.();inspectionWall=null;return;
    }
    if(inspectionWall)return;
    // The corridor has no east wall of its own. Its permanent plaster return keeps the same
    // finish whether the atrium is resident or not, with both openings cut into it.
    const bands=[
      [19.80,22.22,8.98,9.62],[22.22,27.69,6.80,9.62],[27.69,28.31,6.80,7.74],
      [27.69,28.31,8.58,9.62],[28.31,29.55,6.80,9.62]
    ];
    const {geometry,material}=surface;
    geometry.computeBoundingBox();
    const center=geometry.boundingBox.getCenter(new Vector3()),size=geometry.boundingBox.getSize(new Vector3());
    const panels=new InstancedMesh(geometry,material,bands.length),matrix=new Matrix4(),offset=new Vector3();
    panels.name='catwalk_inspection_wall_return';
    const release=[];
    for(const [index,[start,end,floor,head]] of bands.entries()){
      const sx=.18/size.x,sy=(head-floor)/size.y,sz=(end-start)/size.z;
      offset.set(-12.108-center.x*sx,(floor+head)*.5-center.y*sy,(start+end)*.5-center.z*sz);
      matrix.makeScale(sx,sy,sz).setPosition(offset);panels.setMatrixAt(index,matrix);
      const plane=new Group();plane.position.set(-12.108,floor,start);plane.rotation.y=-Math.PI/2;
      release.push(registerLeaf('catwalk','inspection-wall-'+index,plane,end-start,head-floor,true,.09));
    }
    scene.add(panels);
    inspectionWall=()=>{for(const remove of release)remove();panels.removeFromParent();panels.dispose();};
  }
  function syncCrown(){
    const source=rooms.has(crown?.finishRoom)?crown.finishRoom:['catwalk','dome'].find(id=>rooms.has(id));
    if(!source){crown?.root.removeFromParent();crown=null;return;}
    const native=hardware.parts.get('CrownShutter');
    if(!native)return;
    if(!crown){
      const root=new Group();root.name='crown_blackout_shades';root.position.set(-22,11.323,-5);scene.add(root);
      const leaves=[-1,1].map(hand=>{
        const runner=new Group();runner.scale.x=hand;root.add(runner);
        for(const part of native)runner.add(part.clone(false));
        return {runner,hand};
      });
      crown={root,leaves,progress:0};
    }
    if(crown.finishRoom!==source){
      for(const {runner} of crown.leaves)native.forEach((part,i)=>runner.children[i].material=appearance(rooms.get(source),part));
      crown.finishRoom=source;
    }
  }
  function tickCrown(travel,calm){
    if(!crown)return;
    const p=walker.feet;
    const viewing=walker.zone==='dome'||walker.zone==='catwalk'&&(p.z<14.5||p.y>9.2);
    const open=viewing&&rooms.has('dome')&&rooms.has('catwalk');
    const progress=MathUtils.clamp(crown.progress+(open?1:-1)*travel/(calm?1.05:.82),0,1);
    if(progress===crown.progress)return;
    crown.progress=progress;
    for(const {runner,hand} of crown.leaves)runner.position.x=hand*MathUtils.smootherstep(progress,0,1)*1.57;
    crown.root.visible=progress<1;
  }
  function fitFloorInlay(node){
    // Keep the printed face on its original baked footprint. The raised metal arrows need to clear
    // the bottom rail when the leaf passes over this floor marker.
    const bounds=new Box3().setFromObject(node),height=bounds.max.y-bounds.min.y;
    if(height<=.019)return;
    const scale=.018/height,printHeight=.027;
    node.position.y=printHeight+(node.position.y-printHeight)*scale;
    node.scale.y*=scale;node.updateMatrixWorld(true);
  }
  function mount(id,zone){
    const materials=new Map();let endWall=false;
    zone.view.traverse(n=>{
      if(n.userData.expoGuideZone)fitFloorInlay(n);
      if(n.userData.portal||/^door_([a-z]+)$/.test(n.name))n.visible=false;
      if(n.userData.solidStairEnd)endWall=true;
      if(n.isMesh)for(const m of [n.material].flat())if(m.isMeshStandardMaterial&&!materials.has(m.name))materials.set(m.name,m);
    });
    const room={materials,finishes:new Map(),environment:zone.environment.texture,endWall};
    if(id==='catwalk'){
      // Share the real plaster mesh and its finish with the existing stair-end wall.
      zone.view.getObjectByName('catwalk_corridor_endcap')?.traverse(node=>{
        if(node.isMesh&&node.material.name==='pale_shell')room.wallSurface=node;
      });
    }
    rooms.set(id,room);sync();
  }
  function syncStairEnd(){
    const room=rooms.get('catwalk'),parts=hardware.parts.get('StairEnd');
    if(!room||room.endWall||room.releaseEnd||!parts)return;
    // The older immutable room ends one wall short beside the first maintenance tread.
    const root=new Group(),surface=new Group();root.name='catwalk_corridor_endcap';
    root.position.set(-23.04,6.8,19.755);root.rotation.y=-Math.PI/2;
    surface.position.x=1.245;root.add(surface);scene.add(root);
    for(const source of parts){
      const part=source.clone(false);part.material=appearance(room,source);surface.add(part);
      if(source.userData.finish==='plaster')room.wallSurface=part;
    }
    const remove=registerLeaf('catwalk','catwalk-stair-end',root,2.49,3.07,true);
    room.releaseEnd=()=>{remove();root.removeFromParent();};
  }
  function unmount(id){
    const room=rooms.get(id);room?.releaseEnd?.();rooms.delete(id);sync();for(const m of room?.finishes.values()||[])m.dispose();
  }
  function sampleExternal(state){
    if(!state.external)return;
    const leaf=state.leaves[0],angle=leaf.rotor.rotation.y;
    if(leaf.angle===angle)return;
    leaf.angle=angle;
    leaf.rotor.getWorldPosition(scratch);
    leaf.hx=scratch.x;leaf.hy=scratch.y;leaf.hz=scratch.z;
    scratch.set(1,0,0).transformDirection(leaf.rotor.matrixWorld);
    leaf.tx=scratch.x;leaf.tz=scratch.z;
    state.progress=state.fixed?0:Math.min(1,Math.abs(angle)/1.49);
  }
  function tick(dt){
    const p=walker.feet,calm=ctx.gentle||ctx.reduced;
    const travel=Math.min(dt,1/18);
    tickCrown(travel,calm);
    const mx=p.x-last.x,mz=p.z-last.z,span=Math.hypot(mx,mz);
    camera?.getWorldDirection(heading);last.copy(p);
    for(const s of active.values()){
      if(s.external){sampleExternal(s);continue;}
      const {d}=s,dx=p.x-d.x,dz=p.z-d.z;
      const normal=d.axis==='x'?dz:-dx,along=d.axis==='x'?dx:dz;
      const level=(!walker.zone||d.rooms.includes(walker.zone))&&Math.abs(p.y-d.y)<1.08,ready=d.rooms.every(id=>rooms.has(id));
      const towards=d.axis==='x'?heading.z:-heading.x,motion=(d.axis==='x'?mz:-mx)/Math.max(.0001,span);
      const approaching=-Math.sign(normal)*towards>.42||span>.0001&&-Math.sign(normal)*motion>.42;
      const near=level&&Math.abs(normal)<3.7&&Math.abs(along)<d.width*.5+.65&&
        (approaching||s.progress>0||Math.abs(normal)<.75);
      const inSweep=level&&Math.abs(normal)<d.width/d.leaves+.45&&Math.abs(along)<d.width*.5+.46;
      if(near&&ready){
        if(s.progress===0&&s.hold===0){
          s.side=d.swingSide||(normal>=0?1:-1);
          const dx=d.x-p.x,dz=d.z-p.z,len=Math.max(1,Math.hypot(dx,dz));
          play?.('door',calm ? .15 : .26,(-dx*heading.z+dz*heading.x)/len);
        }
        s.hold=1.45;
      }
      else s.hold=Math.max(0,s.hold-dt);
      const wants=ready&&(near||s.hold>0||(s.progress>0&&inSweep));
      const velocity=wants ? 1/(calm ? .80 : .65) : -1/(calm ? 1.05 : .86);
      const next=MathUtils.clamp(s.progress+travel*velocity,0,1);
      if(next!==s.progress){
        if(inSweep&&!d.lift&&swingsIntoVisitor(s,next,along,normal))continue;
        s.progress=next;pose(s);
      }
    }
  }
  function swingsIntoVisitor(s,next,along,normal){
    const angle=MathUtils.smootherstep(next,0,1)*Math.PI*(84/180),c=Math.cos(angle),sn=Math.sin(angle);
    const n=-normal*s.side;
    for(const leaf of s.leaves){
      const u=(along-leaf.rotor.position.x)*leaf.hand;
      const distance=(x,y)=>{
        const at=MathUtils.clamp(u*x+n*y,0,leaf.width);
        return Math.hypot(u-at*x,n-at*y);
      };
      const to=distance(c,sn);
      if(to<.24+.075){
        const old=Math.abs(leaf.rotor.rotation.y);
        if(to<distance(Math.cos(old),Math.sin(old))-1e-8)return true;
      }
    }
    return false;
  }
  function pushOut(p,radius,height,prior){
    for(const s of active.values()){
      const d=s.d,thickness=s.halfDepth??.046;
      if(p.y+height<=d.y+.014||p.y>=d.y+d.height+(d.lift||0)||Math.abs(p.x-d.x)>d.width+.55||Math.abs(p.z-d.z)>d.width+.55)continue;
      sampleExternal(s);
      for(const l of s.leaves){
        if(p.y+height<=l.hy+.014||p.y>=l.hy+d.height)continue;
        const dx=p.x-l.hx,dz=p.z-l.hz,u=dx*l.tx+dz*l.tz,n=dx*(-l.tz)+dz*l.tx;
        const a=u-MathUtils.clamp(u,0,l.width),b=n-MathUtils.clamp(n,-thickness,thickness);
        const distance=Math.hypot(a,b);if(distance>=radius)continue;
        const previousN=(prior.x-l.hx)*(-l.tz)+(prior.z-l.hz)*l.tx;
        const previousU=(prior.x-l.hx)*l.tx+(prior.z-l.hz)*l.tz;
        const crossed=u>=0&&u<=l.width&&previousU>=0&&previousU<=l.width&&previousN*n<0;
        if(distance>.00001&&!crossed){
          const k=(radius-distance+.00005)/distance;p.x+=(a*l.tx-b*l.tz)*k;p.z+=(a*l.tz+b*l.tx)*k;
        }else{
          const side=Math.sign(previousN)||1;
          const v=side*(radius+thickness)-n;p.x-=l.tz*v;p.z+=l.tx*v;
        }
      }
    }
  }
  function blocked(ray,limit){
    for(const s of active.values()){
     sampleExternal(s);
     const thickness=s.halfDepth??.046;
     for(const l of s.leaves){
      const px=ray.origin.x-l.hx,pz=ray.origin.z-l.hz;
      if(Math.hypot(px,pz)>limit+l.width+.15)continue;
      const o=[px*l.tx+pz*l.tz,ray.origin.y-l.hy,-px*l.tz+pz*l.tx];
      const v=[ray.direction.x*l.tx+ray.direction.z*l.tz,ray.direction.y,-ray.direction.x*l.tz+ray.direction.z*l.tx];
      const bounds=[[0,l.width],[.012,s.d.height],[-thickness,thickness]];
      let begin=0,end=limit;
      for(let axis=0;axis<3;axis++){
        const [min,max]=bounds[axis];
        if(Math.abs(v[axis])<1e-7){if(o[axis]<min||o[axis]>max){end=-1;break;}continue;}
        const a=(min-o[axis])/v[axis],b=(max-o[axis])/v[axis];
        begin=Math.max(begin,Math.min(a,b));end=Math.min(end,Math.max(a,b));
        if(end<begin)break;
      }
      if(end>=begin)return true;
     }
    }
    return false;
  }
  function debug(){const rows=[...active.values()].map(({d,progress,leaves})=>({id:d.id,progress,
    ready:d.rooms.every(id=>rooms.has(id)),distance:distanceTo(walker.feet,d.x,d.y,d.z),rooms:d.rooms,
    leaves:leaves.map(l=>({hinge:[l.hx,l.hy,l.hz],tip:[l.hx+l.tx*l.width,l.hy,l.hz+l.tz*l.width],degrees:l.rotor.rotation.y*180/Math.PI}))}));
    if(crown)rows.push({id:'crown-shutter',progress:crown.progress,ready:rooms.has('dome')&&rooms.has('catwalk'),
      distance:distanceTo(walker.feet,-22,11.323,-5),rooms:['catwalk','dome'],leaves:[]});
    return rows;
  }
  function registerLeaf(room,id,rotor,width,height,fixed=false,halfDepth=.046){
    rotor.getWorldPosition(scratch);
    const d=door(id,[room],scratch.x,scratch.y,scratch.z,'x',width,height,'staff',1);
    const state={d,external:true,fixed,halfDepth,progress:0,leaves:[{rotor,width,hx:d.x,hz:d.z,tx:1,tz:0}]};
    sampleExternal(state);active.set(id,state);
    return ()=>active.delete(id);
  }
  function connectVisibleRooms(visible){
    let previous;
    do {
      previous=visible.size;
      for(const d of doorways){
        if(d.rooms.length!==2||!active.get(d.id)?.progress)continue;
        const [a,b]=d.rooms;
        if(visible.has(a)||visible.has(b)){visible.add(a);visible.add(b);}
      }
      if(crown?.progress>0&&(visible.has('dome')||visible.has('catwalk'))){visible.add('dome');visible.add('catwalk');}
    }while(visible.size!==previous);
    return visible;
  }
  return {setHardware(asset){hardware=asset;sync();},mount,unmount,tick,pushOut,blocked,debug,registerLeaf,connectVisibleRooms,
    holds(id){
      if(crown?.progress>0&&(id==='catwalk'||id==='dome'))return true;
      for(const s of active.values())if(s.progress>0&&s.d.rooms.length===2&&s.d.rooms.includes(id))return true;
      return false;
    },
    dispose(){for(const id of [...rooms.keys()])unmount(id);hardware?.dispose();}};
}
