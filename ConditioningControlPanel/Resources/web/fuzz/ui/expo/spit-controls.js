export function spitControls(root,active,spit){
  const doc=root.ownerDocument,button=doc.createElement('button'),style=doc.createElement('style');
  let input=null;
  function begin(name){if(input===null&&active()){input=name;spit.charge();}}
  function end(name){if(input!==name)return;input=null;if(active())spit.release();else spit.cancel();}
  function cancel(){input=null;spit.cancel();}
  button.type='button';button.className='expo-touch expo-spit';button.textContent='Spit';
  button.setAttribute('aria-label','Hold to aim spit, release to spit');
  style.textContent='.expo-spit{right:max(112px,env(safe-area-inset-right));bottom:max(110px,calc(env(safe-area-inset-bottom) + 82px));min-height:46px;padding:8px 14px;border:2px solid #d5e7df;border-radius:24px;background:#163c43ed;color:white;font:650 15px system-ui}.expo-spit:active{background:#356c5e}@media(max-height:480px){.expo-spit{right:max(122px,calc(env(safe-area-inset-right) + 110px));bottom:max(100px,env(safe-area-inset-bottom))}}';
  button.onpointerdown=e=>{e.preventDefault();e.stopPropagation();button.setPointerCapture(e.pointerId);begin(e.pointerId);};
  button.onpointerup=e=>{e.preventDefault();end(e.pointerId);root.focus({preventScroll:true});};
  button.onpointercancel=button.onlostpointercapture=e=>{if(input===e.pointerId)cancel();};
  root.append(style,button);
  return {begin,end,cancel,dispose(){cancel();style.remove();button.remove();}};
}
