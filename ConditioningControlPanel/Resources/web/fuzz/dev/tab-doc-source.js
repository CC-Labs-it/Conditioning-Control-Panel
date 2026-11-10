const KEY='td-preview-picture-source-v1';
const SOURCES=['scrolller','own','none'];

export function previewPictureSource(search, storage) {
  const query=new URLSearchParams(search);
  const requested=query.get('pics');
  // Fixture runs stay offline, even when a normal visit previously selected Scrolller.
  if(query.get('manual')==='1'||query.get('test')==='1') return SOURCES.includes(requested)?requested:'none';
  if(SOURCES.includes(requested)) return requested;
  let saved;
  try { saved=storage?.getItem(KEY); } catch { /* Private browsing may disable storage. */ }
  return SOURCES.includes(saved)?saved:'scrolller';
}

export function rememberPreviewPictureSource(source, storage) {
  if(!SOURCES.includes(source)) return;
  try { storage?.setItem(KEY,source); } catch { /* The current visit still honors the choice. */ }
}
