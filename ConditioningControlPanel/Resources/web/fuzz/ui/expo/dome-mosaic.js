import * as T from 'three';
import {assetURL} from './asset-base.js';

const prints=[
  ...['reginald','margaret','lashes','bob','greg','henry'].map(id=>'dome/'+id),
  ...['black','piebald','chestnut','gray'].map(id=>'hoof-photos/llama-'+id),
  ...['plate','survey','rematch','coup','chew','opening'].map(id=>'narration/slides/'+id)
];
const width=14, pitch=1.26, cell=256;

// Both maps are sampled by the floor itself so open squares retain the parallax of the shaft.
export const mosaicShader=`
  vec2 grid=local.xz/${pitch}+vec2(${width/2}.);
  vec2 index=floor(grid);
  vec4 card=texture2D(tileIndex,(clamp(index,0.,${width-1}.)+.5)/${width}.);
  if(card.g>.5 && depth>.5 && mosaicReady>.5){
    float slot=floor(card.r*255.+.5);
    vec2 uv=1.-fract(grid).yx;
    vec2 corner=vec2(mod(slot,4.),3.-floor(slot/4.));
    vec3 photograph=texture2D(printSheet,(corner+mix(vec2(.008),vec2(.992),uv))*.25).rgb;
    float margin=min(min(uv.x,1.-uv.x),min(uv.y,1.-uv.y));
    vec3 mounted=mix(vec3(.59,.32,.32),photograph*(.86+.14*pulse),smoothstep(.024,.03,margin));
    float opacity=smoothstep(.50,.76,depth)*smoothstep(.008,.017,margin)*(1.-smoothstep(7.7,8.35,length(local.xz)));
    outgoingLight=mix(outgoingLight,mounted,opacity*.94);
  }`;

export function domeMosaic(){
  const canvas=document.createElement('canvas');canvas.width=canvas.height=cell*4;
  const sheet=new T.CanvasTexture(canvas);sheet.colorSpace=T.SRGBColorSpace;
  const data=new Uint8Array(width*width*4);
  const index=new T.DataTexture(data,width,width);index.needsUpdate=true;
  const uniforms={tileIndex:{value:index},printSheet:{value:sheet},mosaicReady:{value:0}};
  let requested=false,disposed=false,elapsed=0,turn=0;
  const slots=[],tiles=[],forward=new T.Vector3(),frameMaps=[];
  for(let z=0;z<width;z++)for(let x=0;x<width;x++){
    const dx=(x+.5-width/2)*pitch,dz=(z+.5-width/2)*pitch;
    if((x+z)%2===0 && Math.hypot(dx,dz)<8.4)tiles.push({offset:4*(z*width+x),dx,dz});
  }
  function set(tile,slot,on){
    data[tile.offset]=slot;data[tile.offset+1]=on?255:0;
  }
  async function load(){
    requested=true;
    const loader=new T.ImageLoader(),g=canvas.getContext('2d');
    await Promise.all(prints.map(async(path,i)=>{
      try{
        const img=await loader.loadAsync(assetURL(path+'.webp'));
        if(disposed)return;
        const size=Math.min(img.width,img.height);
        g.drawImage(img,(img.width-size)/2,(img.height-size)/2,size,size,(i%4)*cell,Math.floor(i/4)*cell,cell,cell);
        slots.push(i);
      }catch{/* Leave unavailable prints out of the deck. */}
    }));
    if(disposed||!slots.length)return;
    slots.sort((a,b)=>a-b);
    tiles.forEach((tile,i)=>set(tile,slots[(i*7+Math.floor(i/8))%slots.length],i%6!==0));
    sheet.needsUpdate=true;index.needsUpdate=true;uniforms.mosaicReady.value=1;
  }
  function framePrint(material,first){
    const map=sheet.clone();map.flipY=false;map.repeat.set(.25,.25);frameMaps.push(map);
    let cursor=first,next=0,showing=false;
    return (time,behind,quiet)=>{
      if(!uniforms.mosaicReady.value||showing&&(quiet||!behind||time<next))return;
      const slot=slots[cursor%slots.length];
      map.offset.set((slot%4)*.25,Math.floor(slot/4)*.25);
      if(!showing){material.map=map;material.needsUpdate=true;map.needsUpdate=true;showing=true;}
      next=time+6;cursor+=5;
    };
  }
  return {uniforms,framePrint,update(depth,dt,camera,quiet){
    if(depth>.28&&!requested)void load();
    if(depth<.5||!uniforms.mosaicReady.value||quiet)return;
    elapsed+=dt;if(elapsed<1.35)return;elapsed=0;
    camera.getWorldDirection(forward);
    // A complete square must be behind the eye plane before its photograph can change.
    for(let i=0;i<tiles.length;i++){
      const k=(turn*17+i*13)%tiles.length,tile=tiles[k];
      const behind=(tile.dx-22-camera.position.x)*forward.x-camera.position.y*forward.y+(tile.dz-5-camera.position.z)*forward.z;
      if(behind>=-.95)continue;
      const old=slots.indexOf(data[tile.offset]);
      set(tile,slots[(old+1+turn%Math.max(1,slots.length-1))%slots.length],!data[tile.offset+1]||turn%4!==0);
      index.needsUpdate=true;break;
    }
    turn++;
  },dispose(){disposed=true;for(const map of frameMaps)map.dispose();sheet.dispose();index.dispose();canvas.width=canvas.height=1;}};
}
