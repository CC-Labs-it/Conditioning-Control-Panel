import test from 'node:test';
import assert from 'node:assert/strict';
import { createFlashMedia, flashLayout } from '../fx/flashMedia.js';
import { createCardMedia } from '../host/cardMedia.js';

test('flash borrows live owners and immediately rejects expiry, source-off and disposal', async t => {
  t.mock.method(globalThis, 'fetch', async () => new Response(new Uint8Array([1,2,3]), { headers: { 'content-type': 'video/mp4' } }));
  let video;
  const media = { slots: [{ id: 'card-0', frame: 'card' }], setSource() {} };
  const adapter = createCardMedia(media, { origin: 'https://game.example', makeVideo: () => video = {
    readyState: 2, videoWidth: 640, pause() {}, removeAttribute() {}, load() {},
  } });
  t.after(() => adapter.dispose());
  const expiresAt = Date.now() + 60000;
  await adapter.apply({ enabled: true, sources: [{ kind: 'video', credit: 'scrolller', url: '/clip', expiresAt }] });
  assert.equal(adapter.sourceForFlash(1), video);
  video.error = true; assert.equal(adapter.sourceForFlash(), null); video.error = null;
  const realNow = Date.now; t.mock.method(Date, 'now', () => expiresAt + 1);
  assert.equal(adapter.sourceForFlash(), null);
  Date.now = realNow;
  await adapter.apply({ enabled: false });
  assert.equal(adapter.sourceForFlash(), null);
  adapter.dispose(); assert.equal(adapter.sourceForFlash(), null);
});

test('view card covers the racing line, Gentle shrinks it; pause stops copies, source-off clears pixels', () => {
  const r = flashLayout(16/9), gentle = flashLayout(16/9, { corner: true, scale: .35 });
  assert.ok(r.x < .5 && r.x + r.w > .5 && r.w * r.h > .55);
  assert.ok(gentle.w * gentle.h < r.w * r.h * .13);
  let copies = 0, clears = 0, src = { videoWidth: 640, videoHeight: 360 };
  const ctx = { fillRect() { clears++; }, drawImage() { copies++; }, fillText() {} };
  const canvas = { style: {}, setAttribute() {}, getContext: () => ctx, remove() {} };
  const fx = createFlashMedia({ parent: { appendChild() {} }, source: () => src, make: () => canvas, fallback: null });
  const screen = { kind: 'flash', k: 1, params: { seed: 1 } };
  fx.update(screen, .1); assert.equal(copies, 1);
  fx.update(screen, 0); assert.equal(copies, 1);
  screen.params.corner = true; screen.params.scale = .35;
  fx.update(screen, .1); const width = canvas.style.width;
  fx.update({ kind: 'pink', prevKind: 'flash', prevK: .5, params: { seed: 2 } }, .1);
  assert.equal(canvas.style.width, width, 'replaced Gentle flash retains its corner placement');
  src = null; const before = clears; fx.update(screen, 0); assert.ok(clears > before);
  fx.update({ kind: 'none' }, .1); assert.equal(canvas.style.display, 'none');
  fx.dispose();
});

test('scattered pictures preserve portrait and landscape proportions with bounded backing pixels', () => {
  let src = { videoWidth: 1920, videoHeight: 1080 };
  const ctx = { fillRect() {}, drawImage() {}, fillText() {} };
  const canvas = { style: {}, setAttribute() {}, getContext: () => ctx, remove() {} };
  const fx = createFlashMedia({ parent: { appendChild() {} }, source: () => src, make: () => canvas, fallback: null });
  const screen = { kind: 'flash', k: 1, params: { seed: 1, nativeAspect: true } };
  fx.update(screen, .1); assert.equal(fx.aspect, 16 / 9); assert.equal(canvas.width, 768);
  src = { videoWidth: 1080, videoHeight: 1920 };
  fx.update(screen, .1); assert.equal(fx.aspect, 9 / 16); assert.equal(canvas.height, 768);
  fx.dispose();
});
