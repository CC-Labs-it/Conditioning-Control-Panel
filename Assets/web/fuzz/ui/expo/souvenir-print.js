export function souvenirPrint({THREE:T,renderer,draw,ctx},item,title){
  if(!item)return;
  const stage=new T.Scene(),camera=new T.PerspectiveCamera(32,1,.01,10);
  const copy=item.clone(true);copy.visible=true;copy.position.set(0,0,0);copy.rotation.y=-.28;
  stage.add(copy);const box=new T.Box3().setFromObject(copy,true),center=box.getCenter(new T.Vector3()),size=box.getSize(new T.Vector3()).length();
  stage.background=new T.Color('#e7e2cc');
  stage.add(new T.HemisphereLight(0xfff5d8,0x627266,2));
  const light=new T.DirectionalLight(0xffefcb,2);light.position.set(-2,3,2);stage.add(light);
  camera.position.copy(center).add(new T.Vector3(.32,.42,1).multiplyScalar(size*1.7));camera.lookAt(center);
  const out=document.createElement('canvas');out.width=540;out.height=640;const g=out.getContext('2d');
  const dims=renderer.getSize(new T.Vector2()),ratio=renderer.getPixelRatio();
  try{
    renderer.setDrawingBufferSize(480,480,1);renderer.render(stage,camera);
    const sticker=document.createElement('canvas');sticker.width=sticker.height=256;const s=sticker.getContext('2d');
    s.beginPath();s.arc(128,128,125,0,Math.PI*2);s.clip();s.drawImage(renderer.domElement,0,0,256,256);
    s.strokeStyle='#f9f1d4';s.lineWidth=12;s.stroke();ctx.store.set('souvenirSticker',sticker.toDataURL('image/webp',.78));
    g.fillStyle='#f5eedb';g.fillRect(0,0,540,640);g.fillStyle='#254838';g.fillRect(0,0,540,56);
    g.fillStyle='#fff6d9';g.font='bold 22px Georgia';g.fillText('LLAMA EXPO 2037',24,36);
    g.drawImage(renderer.domElement,30,72,480,480);g.fillStyle='#244537';g.font='bold 30px Georgia';g.fillText(title,30,593);
    g.font='17px Georgia';g.fillText('Visitor Services   /   '+(ctx.identity?.displayName?.trim().slice(0,36)||'Admit one human'),30,621);
    ctx.store.set('souvenirPrint',out.toDataURL('image/webp',.77));ctx.store.set('souvenirLabel',title);
  }finally{renderer.setDrawingBufferSize(dims.x,dims.y,ratio);draw();}
}
