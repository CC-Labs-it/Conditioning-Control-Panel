const KEY = 'expo.camera.v1';
const BACKROOM_DRAG_KEY = 'br.dragLook.v1';

function valid(key, value) {
  if (key === 'arrowMode') return value === 'turn' || value === 'strafe';
  return (key === 'dragLook' || key === 'invertLook') && typeof value === 'boolean';
}

export function createCameraPreferences(win) {
  const options = { dragLook: false, invertLook: false, arrowMode: 'strafe' };
  try {
    const storage = win.localStorage;
    options.dragLook = storage.getItem(BACKROOM_DRAG_KEY) === 'true';
    const saved = JSON.parse(storage.getItem(KEY));
    for (const key of Object.keys(options)) {
      if (valid(key, saved?.[key])) options[key] = saved[key];
    }
  } catch { /* Storage can be disabled in an embedded ending. */ }

  return { options, set(key, value) {
    if (!valid(key, value) || options[key] === value) return false;
    options[key] = value;
    try { win.localStorage.setItem(KEY, JSON.stringify(options)); } catch { /* Keep the current visit's choice. */ }
    return true;
  } };
}
