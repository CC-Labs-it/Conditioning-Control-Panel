import * as T from 'three';
import {mergeGeometries} from 'three/addons/utils/BufferGeometryUtils.js';
import {railPlate} from './rail-lines.js';

function plateMap(){
  const canvas=document.createElement('canvas');canvas.width=768;canvas.height=384;
  const g=canvas.getContext('2d'),metal=g.createLinearGradient(0,0,768,384);
  metal.addColorStop(0,'#b99b5a');metal.addColorStop(.43,'#f4df9f');metal.addColorStop(1,'#c4a46b');
  g.fillStyle=metal;g.fillRect(0,0,768,384);g.strokeStyle='#554028';g.lineWidth=5;g.strokeRect(12,12,744,360);
  g.fillStyle='#332616';g.font='bold 34px Georgia';g.textAlign='center';
  const words=railPlate.split(' ');let line='',y=65;
  for(const word of words){
    const next=line?line+' '+word:word;
    if(g.measureText(next).width>696){g.fillText(line,384,y);line=word;y+=45;}else line=next;
  }
  g.fillText(line,384,y);
  for(const x of [26,742])for(const y of [26,358]){g.beginPath();g.arc(x,y,5,0,Math.PI*2);g.fill();}
  const map=new T.CanvasTexture(canvas);map.colorSpace=T.SRGBColorSpace;map.anisotropy=4;return map;
}
export function railModels(scene){
  const plate=plateMap(),canvas=document.createElement('canvas');canvas.width=canvas.height=128;
  const g=canvas.getContext('2d'),fill=g.createRadialGradient(43,29,2,65,67,87);
  for(const [at,color] of [[0,'#fffdf0'],[.16,'#fff4de'],[.27,'#e4deca'],[.49,'#b9b5aa'],[.62,'#77796f'],[.78,'#b5b1a1'],[1,'#646158']])fill.addColorStop(at,color);
  g.fillStyle=fill;g.fillRect(0,0,128,128);
  const matcap=new T.CanvasTexture(canvas);matcap.colorSpace=T.SRGBColorSpace;
  function build(parts){
    const root=new T.Group(),moving=new T.Group();root.name='The Rail™';scene.add(root);root.add(moving);
    root.position.y=parts[0].y;
    const orange=new T.MeshMatcapMaterial({color:0xff7c13,matcap});
    const dark=new T.MeshMatcapMaterial({color:0x504a41,matcap});
    const brass=new T.MeshMatcapMaterial({color:0xe4bb70,matcap});
    const printed=new T.MeshBasicMaterial({map:plate});
    const groups=[[],[],[],[]],geometries=[],posts=new Set();
    const up=new T.Vector3(0,1,0);
    function pipe(which,a,b,r){
      const direction=b.clone().sub(a),len=direction.length();
      const geom=new T.CylinderGeometry(r,r,len,12);
      geom.applyQuaternion(new T.Quaternion().setFromUnitVectors(up,direction.normalize()));
      geom.translate((a.x+b.x)/2,(a.y+b.y)/2,(a.z+b.z)/2);groups[which].push(geom);
    }
    for(const s of parts){
      const a=new T.Vector3(s.ax,1.055,s.az),b=new T.Vector3(s.bx,1.055,s.bz);
      pipe(0,a,b,.045);
      const pieces=Math.ceil(a.distanceTo(b)/1.65);
      for(let i=0;i<=pieces;i++){
        const p=a.clone().lerp(b,i/pieces),id=p.x.toFixed(3)+','+p.z.toFixed(3);
        if(posts.has(id))continue;posts.add(id);
        const low=p.clone();low.y=.018;pipe(0,low,p,.039);
        const collar=p.clone();collar.y=.94;pipe(2,collar,p,.051);
        const cap=p.clone();cap.y=1.10;pipe(2,p,cap,.052);
        const rim=low.clone();rim.y=.026;pipe(3,low,rim,.123);
      }
      const slot=new T.BoxGeometry(a.distanceTo(b)+.09,.016,.105);
      slot.rotateY(-Math.atan2(b.z-a.z,b.x-a.x));slot.translate((a.x+b.x)/2,.014,(a.z+b.z)/2);groups[3].push(slot);
    }
    const s=parts.length===16?parts[3]:parts[Math.floor(parts.length/2)],cx=(s.ax+s.bx)/2,cz=(s.az+s.bz)/2;
    const angle=-Math.atan2(s.bz-s.az,s.bx-s.ax),plateWidth=.55;
    const bracket=new T.BoxGeometry(plateWidth,.275,.11);bracket.rotateY(angle);bracket.translate(cx,.91,cz);groups[2].push(bracket);
    const face=new T.PlaneGeometry(plateWidth,.275);geometries.push(face);
    for(const side of [1,-1]){
      const m=new T.Mesh(face,printed);m.rotation.y=angle+(side===1?0:Math.PI);
      m.position.set(cx+side*.057*Math.sin(angle),.91,cz+side*.057*Math.cos(angle));moving.add(m);
    }
    groups.forEach((list,i)=>{
      if(!list.length)return;
      const geometry=mergeGeometries(list);list.forEach(g=>g.dispose());geometries.push(geometry);
      (i===3?root:moving).add(new T.Mesh(geometry,[orange,dark,brass,dark][i]));
    });
    const materials=[orange,dark,brass,printed];
    return {root,pose(f,calm){
      root.visible=f>0;const t=f*f*(3-2*f);
      moving.scale.y=calm?1:Math.max(.001,t);
      for(const m of materials){const fade=calm&&f<1;if(m.transparent!==fade){m.transparent=fade;m.needsUpdate=true;}m.opacity=calm?t:1;}
    },dispose(){root.removeFromParent();geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());}};
  }
  return {build,dispose(){plate.dispose();matcap.dispose();}};
}
