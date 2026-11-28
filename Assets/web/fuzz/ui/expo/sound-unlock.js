/** Controller buttons can operate the exhibition while a browser still requires a trusted press for audio. */
export function bindExpoSoundUnlock(ctx, parent, menus) {
  const doc = parent.ownerDocument, win = doc.defaultView;
  const button = doc.createElement('button');
  button.type = 'button';
  button.className = 'expo-sound-unlock';
  button.textContent = 'Click or press a key for sound';
  let offered = false, disposed = false;
  function refresh() {
    if (disposed) return;
    if (!ctx.audio.needsGesture || ctx.paused || doc.hidden) { button.remove(); return; }
    if (menus.mode === 'gamepad') offered = true;
    if (offered && !button.isConnected && parent.querySelector('.expo-root,.expo-worm')) parent.append(button);
  }
  function unlock(event) {
    if (!button.isConnected || !event.isTrusted || ctx.paused || doc.hidden) return;
    if (event.target?.closest?.('#mute,#audio-mute,#wake')) return;
    if (event.currentTarget !== doc && event.target !== win.frameElement &&
        event.target !== event.currentTarget.body && event.target !== event.currentTarget.documentElement) return;
    ctx.audio.armFromUserPress(event);
    if (event.type === 'click' && event.target === button) {
      const current = parent.querySelector('.expo-return-recap') || parent.querySelector('.expo-worm #btn,.expo-root');
      current?.focus({preventScroll:true});
    }
  }
  // A same-origin preview may still own keyboard focus after handing input to its game frame.
  const documents = [doc];
  try {
    const host = win.frameElement?.ownerDocument;
    if (host?.location.pathname.endsWith('/dev/play-ending.html')) documents.push(host);
  } catch {}
  for (const target of documents) {
    target.addEventListener('click', unlock, true);
    target.addEventListener('keydown', unlock, true);
  }
  const off = [ctx.every(180, refresh), ctx.on('pause', refresh), ctx.on('resume', refresh), ctx.on('suspend', refresh)];
  doc.addEventListener('visibilitychange', refresh);
  refresh();
  return () => {
    if (disposed) return;
    disposed = true;
    button.remove();
    for (const stop of off) stop();
    for (const target of documents) {
      target.removeEventListener('click', unlock, true);
      target.removeEventListener('keydown', unlock, true);
    }
    doc.removeEventListener('visibilitychange', refresh);
  };
}
