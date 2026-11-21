// ll_end keeps its sticker and recap. The visit resolves only at the gift shop or on panic.
export const EXPO_PAPER = '#e9f4dc';

export async function expoEnding(ctx) {
  const parent = ctx.layer('ending'), doc = parent.ownerDocument;
  const cover = ctx.el('div', `position:absolute;inset:0;display:grid;place-content:center;gap:18px;background:${EXPO_PAPER};color:#163b29;font:700 24px system-ui;text-align:center;pointer-events:auto`, 'LLAMA EXPO 2037', parent);
  const status = doc.createElement('small'); status.textContent = 'Opening the Expo...'; cover.append(status);
  let scene = null, gone = false, finish, mediaOn = true;
  const done = new Promise(resolve => { finish = resolve; });
  const win = doc.defaultView;
  function dispose() {
    if (gone) return;
    gone = true; scene?.dispose(); cover.remove();
    if (mediaOn) { mediaOn = false; ctx.emit('ending-media', false); }
    if (win.__expo === scene) delete win.__expo;
    finish();
  }
  ctx.emit('ending-media', true);
  ctx.cleanup(dispose);
  ctx.on('suspend', event => { if (event?.on && event.reason === 'panic') dispose(); });
  try {
    const mod = await Promise.race([import('../../../expo/scene.js'), done]);
    if (gone || !mod) return;
    scene = mod.createExpo(ctx, parent, { onExit: dispose });
    if (['localhost', '127.0.0.1'].includes(win.location.hostname)) win.__expo = scene;
    // The cover is removed only after createExpo has rendered its first view.
    cover.remove();
    await done;
  } catch (error) {
    console.error(error);
    if (!gone) {
      scene?.dispose();
      status.textContent = 'The Expo could not open.';
      const exit = doc.createElement('button'); exit.textContent = 'Continue to recap'; exit.onclick = dispose; cover.append(exit);
      await done;
    }
  } finally { dispose(); }
}
