import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';

export function exhibitProps(assetURL, filename='renewal-collection.glb') {
  const controller=new AbortController(), loader=new GLTFLoader();let source,promise;
  function free(root){
    const resources=new Set();
    root?.traverse(n=>{if(!n.isMesh)return;resources.add(n.geometry);
      for(const mat of [n.material].flat()){resources.add(mat);for(const val of Object.values(mat))if(val?.isTexture)resources.add(val);}});
    for(const item of resources){item.source?.data?.close?.();item.dispose();}
  }
  function mount(room,setup){
    let removed=false,cleanup;const made=[],surfaces=new Map();
    if(!promise)promise=(async()=>{
      const r=await fetch(assetURL(filename),{signal:controller.signal});if(!r.ok)throw Error(filename+' '+r.status);
      const model=await loader.parseAsync(await r.arrayBuffer(),'');source=model.scene;
      if(controller.signal.aborted){free(source);source=null;}
      return source;
    })().catch(err=>{promise=null;if(!controller.signal.aborted)console.warn(err);return null;});
    promise.then(kit=>{
      if(!kit||removed)return;
      let probe;room.traverse(n=>{if(!probe&&n.isMesh)probe=[n.material].flat().find(m=>m.envMap)?.envMap;});
      function finish(original){
        if(!surfaces.has(original)){const m=original.clone();if(probe&&m.isMeshStandardMaterial){m.envMap=probe;m.envMapIntensity=1.75;}surfaces.set(original,m);}
        return surfaces.get(original);
      }
      function place(name,position,yaw=0){
        const original=kit.getObjectByName(name);if(!original)throw Error('Missing Expo prop '+name);
        const node=original.clone(true);node.traverse(n=>{if(n.isMesh)n.material=Array.isArray(n.material)?n.material.map(finish):finish(n.material);});
        node.position.fromArray(position);node.rotation.y=yaw;room.add(node);node.updateWorldMatrix(true,true);made.push(node);return node;
      }
      cleanup=setup(place);
    }).catch(err=>{if(!removed)console.error('Expo prop assembly',err);});
    return ()=>{removed=true;cleanup?.();for(const n of made)n.removeFromParent();for(const m of surfaces.values())m.dispose();};
  }
  return {mount,dispose(){controller.abort();free(source);source=null;}};
}
