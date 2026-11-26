// The recap keeps a print of the particular room where this visit ended.
export async function departurePrint(api,state,title){
  const {ctx,renderer,draw}=api;
  const saved=ctx.store.get('souvenirPrint');
  const out=document.createElement('canvas'),g=out.getContext('2d');out.width=800;out.height=900;
  g.fillStyle='#f2ebd7';g.fillRect(0,0,800,900);g.fillStyle='#1b4339';g.fillRect(0,0,800,90);
  g.fillStyle='#fff4d2';g.font='29px Georgia';g.fillText('Llama Expo 2037',32,57);
  g.textAlign='right';g.font='17px Georgia';g.fillText('VISITOR SERVICES',768,55);g.textAlign='left';
  if(saved){
    const im=new Image();im.src=saved;await im.decode();g.drawImage(im,30,72,480,480,56,100,688,546);
  }else{
    draw();const view=renderer.domElement,w=view.width,h=view.height;
    const scale=Math.min(w/736,h/536);g.drawImage(view,(w-736*scale)/2,(h-536*scale)/2,736*scale,536*scale,32,108,736,536);
  }
  g.strokeStyle='#a78d58';g.strokeRect(32,108,736,536);g.fillStyle='#193a32';g.font='bold 44px Georgia';g.fillText(title,32,709,736);
  g.font='22px Georgia';g.fillText(ctx.store.get('souvenirLabel')||state.file.name,33,750,736);
  g.fillStyle='#6e795f';g.fillRect(32,777,736,1);g.fillStyle='#344b3c';g.font='19px Consolas,monospace';
  g.fillText('TICKET   '+state.ticket.count+' / 12     REGISTER   '+(state.file.taps+state.presses),32,814);
  const total=api.recordTotal?.();
  g.fillText('HISTORY  '+api.records()+(total?' / '+total:'')+'     AGREEMENTS   '+(state.ticket.yes+state.ticket.deviations),32,852);
  for(const quality of [.76,.58,.42]){const url=out.toDataURL('image/webp',quality);if(url.length<=240023){ctx.store.set('departurePrint',url);break;}}
}
