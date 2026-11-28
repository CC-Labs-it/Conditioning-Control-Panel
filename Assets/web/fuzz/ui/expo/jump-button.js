/** A second finger can jump while the first finger holds the walking stick. */
export function mountJumpButton(root,jump,group) {
  const button=root.ownerDocument.createElement('button');
  button.className='expo-jump';button.type='button';button.textContent='Jump';
  let lastTouch=-Infinity;
  button.onpointerdown=event=>{
    if(event.pointerType!=='touch')return;
    event.preventDefault();event.stopPropagation();
    lastTouch=root.ownerDocument.defaultView.performance.now();
    jump();
  };
  button.onclick=event=>{
    event.preventDefault();event.stopPropagation();
    if(!event.detail||root.ownerDocument.defaultView.performance.now()-lastTouch>700)jump();
    root.focus({preventScroll:true});
  };
  group.append(button);return button;
}
