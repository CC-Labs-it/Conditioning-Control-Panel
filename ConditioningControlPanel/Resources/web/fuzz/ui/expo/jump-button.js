/** A second finger can jump while the first finger holds the walking stick. */
export function mountJumpButton(root, jump) {
  const doc=root.ownerDocument;
  const button=doc.createElement('button');
  button.className='expo-touch expo-jump';
  button.type='button';
  button.textContent='Jump';
  const style=doc.createElement('style');
  style.textContent=`
    .expo-jump{right:max(22px,env(safe-area-inset-right));bottom:max(108px,calc(env(safe-area-inset-bottom) + 80px));min-width:80px;min-height:50px;padding:9px 14px;border:2px solid #d5e7df;border-radius:28px;background:#163c43ed;color:#fff;font:650 15px system-ui}
    @media(max-height:480px){.expo-jump{right:max(122px,calc(env(safe-area-inset-right) + 110px));bottom:max(42px,env(safe-area-inset-bottom))}}
  `;
  let lastTouch=-Infinity;
  button.onpointerdown=event=>{
    if (event.pointerType!=='touch') return;
    event.preventDefault(); event.stopPropagation();
    lastTouch=doc.defaultView.performance.now();
    jump();
  };
  button.onclick=event=>{
    event.preventDefault(); event.stopPropagation();
    if (!event.detail || doc.defaultView.performance.now()-lastTouch>700) jump();
    root.focus({preventScroll:true});
  };
  root.append(style,button);
  return ()=>{ button.remove(); style.remove(); };
}
