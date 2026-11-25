/* ui/folder-files.js - whole folders as own files (owner 2026-11-08: "we should be able to select whole folders").
 * Two doors: a folder picker (<input webkitdirectory>, subfolders included) and a drop of any mix of files and folders.
 * Only the File objects are handed on; pictures.js keeps what is a picture, GIF or video. Nothing leaves the device. */

/** Walk a drop. Resolves the files in it, folders read to the bottom, stopping once `limit` are found. */
export async function filesFromDrop(dt, { limit = 20000 } = {}) {
  const out = [];
  const items = [...(dt?.items || [])].filter((it) => it.kind === 'file');
  const entries = items.map((it) => it.webkitGetAsEntry?.()).filter(Boolean);
  // No entry API (an old browser): the plain file list, folders left out.
  if (!entries.length) return [...(dt?.files || [])].slice(0, limit);
  const fileOf = (entry) => new Promise((res) => entry.file(res, () => res(null)));
  const readAll = (dir) => new Promise((res) => {
    const reader = dir.createReader(), all = [];
    // readEntries answers in batches (100 in Chrome) until an empty one.
    const more = () => reader.readEntries((batch) => { if (!batch.length) res(all); else { all.push(...batch); more(); } }, () => res(all));
    more();
  });
  const walk = async (entry, depth) => {
    if (out.length >= limit || depth > 12) return;
    if (entry.isFile) { const f = await fileOf(entry); if (f) out.push(f); return; }
    if (entry.isDirectory) for (const child of await readAll(entry)) { if (out.length >= limit) return; await walk(child, depth + 1); }
  };
  for (const e of entries) await walk(e, 0);
  return out;
}

/** True where a folder picker works: desktop browsers. Phones either lack it or show an empty sheet. */
export function folderPickerWorks(doc) {
  const probe = doc.createElement('input');
  probe.type = 'file';
  if (!('webkitdirectory' in probe)) return false;
  return !doc.defaultView?.matchMedia?.('(pointer: coarse)').matches;
}
