export function spitControls(root,active,spit,group,refresh){
  const button=root.ownerDocument.createElement('button');
  let input=null;
  function begin(name){if(input===null&&active()){input=name;spit.charge();}}
  function end(name){
    if(input!==name)return;
    input=null;if(active())spit.release();else spit.cancel();refresh();
  }
  function cancel(){input=null;spit.cancel();refresh();}
  button.type='button';button.className='expo-spit';button.textContent='Hold Spit';
  button.setAttribute('aria-label','Hold to aim spit, release to spit');
  button.onpointerdown=e=>{
    if(!active()||input!==null)return;
    e.preventDefault();e.stopPropagation();
    try{button.setPointerCapture(e.pointerId);}catch{}
    begin(e.pointerId);
  };
  button.onpointerup=e=>{e.preventDefault();end(e.pointerId);root.focus({preventScroll:true});};
  button.onpointercancel=button.onlostpointercapture=e=>{if(input===e.pointerId)cancel();};
  button.onclick=e=>{
    e.preventDefault();e.stopPropagation();
    if(e.detail===0&&active()){begin('activate');end('activate');}
  };
  group.append(button);
  return {button,begin,end,cancel,get touchHeld(){return typeof input==='number';}};
}
