// One local scene probe, allocated before warmup and refreshed a single face at a time.
import { CubeCamera, CubeRenderTarget, HalfFloatType, LinearFilter } from 'three/webgpu';

export function createWaterReflections({ scene, renderer, exclude, quality='mid', track }={}) {
  const three=renderer?.three||renderer, size=quality==='phone'?32:64;
  const target=new CubeRenderTarget(size,{type:HalfFloatType,generateMipmaps:false,minFilter:LinearFilter,magFilter:LinearFilter});
  target.texture.name='water-local-scene-reflection';
  const camera=new CubeCamera(.5,quality==='phone'?100:160,target);
  const stats={ready:false,failed:false,error:'',captures:0,size,face:0};
  let elapsed=0,disposed=false;
  function position(playerPos,playerS) {
    const p=playerPos||track?.frameAt(playerS||0)?.p||[0,0,0];
    camera.position.set(p.x??p[0],(p.y??p[1])+2.4,p.z??p[2]);
    if(camera.coordinateSystem!==three.coordinateSystem){camera.coordinateSystem=three.coordinateSystem;camera.updateCoordinateSystem();}
    camera.updateMatrixWorld(true);
  }
  function withCapture(fn) {
    const visible=exclude?.visible,oldTarget=three.getRenderTarget(),oldFace=three.getActiveCubeFace(),oldMip=three.getActiveMipmapLevel();
    const xr=three.xr?.enabled,shadow=three.shadowMap?.enabled,autoClear=three.autoClear;
    const mrt=three.getMRT?.();
    if(exclude)exclude.visible=false;
    if(three.xr)three.xr.enabled=false;
    if(three.shadowMap)three.shadowMap.enabled=false;
    three.autoClear=true;
    three.setMRT?.(null);
    const restore=()=>{
      three.setRenderTarget(oldTarget,oldFace,oldMip);
      if(three.setMRT)three.setMRT(mrt);
      if(exclude)exclude.visible=visible;
      if(three.xr)three.xr.enabled=xr;
      if(three.shadowMap)three.shadowMap.enabled=shadow;
      three.autoClear=autoClear;
    };
    try { const result=fn();if(result?.then)return result.finally(restore);restore();return result; }
    catch(error){restore();throw error;}
  }
  function fail(error) {
    stats.failed=true;stats.ready=false;stats.error=String(error?.message||error);
    console.warn('[glassroad/water] scene reflections unavailable: '+stats.error);
  }
  return { texture:target.texture,stats,target,camera,
    /** overdraw(): called in the same task as the staging draw (garage first: the menu's own frame goes over it). */
    async warm({playerPos,playerS=0,onProgress=null,overdraw=null}={}) {
      if(!scene||!three){stats.error='Scene renderer not supplied';return false;}
      if(disposed)return false;
      // the canvas (or whatever target the caller had) goes back before every await: a menu frame drawn while
      // the pipelines build or during the painted gap must never land in a cube face
      const outer=three.getRenderTarget(),outerFace=three.getActiveCubeFace(),outerMip=three.getActiveMipmapLevel();
      const handBack=()=>three.setRenderTarget(outer,outerFace,outerMip);
      try {
        position(playerPos,playerS);
        await withCapture(async()=>{
          for(let face=0;face<6;face++) {
            three.setRenderTarget(target,face);
            const saved=[],materials=[],lods=[],excluded=new Set(),faceCamera=camera.children[face];
            exclude?.traverse?.(object=>excluded.add(object));
            try {
              // Cube captures have their own no-shadow/MRT pipeline. Warm its hidden,
              // distant and zero-count variants too, before the loading cover lifts.
              // All six cube views share perspective/layers/target format and no-shadow shaders.
              // One unculled compile covers every hidden variant; orientation is camera data.
              if(face===0) scene.traverse?.(object=>{
                if(excluded.has(object))return;
                // The cube draw's projection selects one LOD level and hides the rest, so the far finish-portal
                // levels linked mid-race in this pass (Firefox, 2026-10-27). Hold every level shown, as warmAll does.
                if(object.isLOD){lods.push([object,object.autoUpdate]);object.autoUpdate=false;}
                saved.push([object,object.visible,object.frustumCulled,object.count]);
                object.visible=true;object.frustumCulled=false;
                if(object.count===0&&(object.isInstancedMesh||object.isSprite||object.isBatchedMesh))object.count=1;
                for(const material of [].concat(object.material||[]))if(material&&material.visible===false){materials.push(material);material.visible=true;}
              });
              // Keep the real cube layer mask. Layer-2 road shadow catchers never enter
              // this no-shadow capture; compiling ShadowNodeMaterial without shadows
              // asks its shadow-mask lighting model for a float that does not exist.
              scene.updateMatrixWorld?.(true);
              if(face===0) {
                // the compile takes its render list and target in this task; the pipelines build afterwards
                const compiling=three.compileAsync(scene,faceCamera);
                handBack();
                await compiling;
                three.setRenderTarget(target,face);
                // r186's async compiler sees a transparent material after its temporary
                // front/back side is restored. An actual unculled draw warms both cull
                // pipelines, including distant media that first enter a later cube view.
                // The clean capture below overwrites this staging face before it is used.
                three.render(scene,faceCamera);
                if(overdraw){handBack();overdraw();three.setRenderTarget(target,face);}
              }
            } finally {
              for(const [object,autoUpdate] of lods)object.autoUpdate=autoUpdate;
              for(const [object,visible,culled,count] of saved){object.visible=visible;object.frustumCulled=culled;if(object.count!==count)object.count=count;}
              for(const material of materials)material.visible=false;
              scene.updateMatrixWorld?.(true);
            }
            // Capture normal visibility so hidden warmup props do not enter the reflection.
            three.render(scene,camera.children[face]);stats.captures++;
            handBack();
            onProgress?.(face+1,6);
            await new Promise(resolve=>globalThis.requestAnimationFrame?requestAnimationFrame(()=>setTimeout(resolve,0)):setTimeout(resolve,0));
          }
        });
        stats.ready=true;stats.failed=false;stats.error='';return true;
      } catch(error){fail(error);return false;}
    },
    update(dt,{paused=false,playerPos,playerS=0,wet=true}={}) {
      if(paused||disposed||!stats.ready||stats.failed||!wet)return;
      elapsed+=Math.max(0,dt);
      if(elapsed<(quality==='phone'?.4:.16))return;
      elapsed=0;
      try {
        // Hold the probe origin for all six faces, avoiding a different space on each cube side.
        if(stats.face===0)position(playerPos,playerS);
        withCapture(()=>{three.setRenderTarget(target,stats.face);three.render(scene,camera.children[stats.face]);});
        stats.face=(stats.face+1)%6;stats.captures++;
      } catch(error){fail(error);}
    },
    dispose(){disposed=true;target.dispose();},
  };
}
