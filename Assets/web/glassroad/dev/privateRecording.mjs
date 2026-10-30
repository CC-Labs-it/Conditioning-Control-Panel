import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';

// Only explicit files are considered. No URL component is ever joined to a private directory.
export async function verifiedRecording(paths, sha256) {
  for (const file of paths) {
    try {
      const bytes = await readFile(file);
      if (createHash('sha256').update(bytes).digest('hex') === sha256) return { file, bytes };
    } catch (e) { if (!['ENOENT', 'ENOTDIR'].includes(e.code)) throw e; }
  }
  throw new Error('Exact selected recording unavailable');
}

export function recordingRange(value, length) {
  if (!value) return null;
  const m = /^bytes=(\d*)-(\d*)$/.exec(value);
  if (!m || (!m[1] && !m[2])) return false;
  const a = m[1] ? Number(m[1]) : null, b = m[2] ? Number(m[2]) : null;
  if ((a !== null && !Number.isSafeInteger(a)) || (b !== null && !Number.isSafeInteger(b))) return false;
  const start = a ?? Math.max(0, length - b), end = a === null ? length - 1 : Math.min(length - 1, b ?? length - 1);
  return start >= length || start > end ? false : { start, end };
}

export function sendRecording(req, res, bytes, headers) {
  const range = recordingRange(req.headers.range, bytes.length);
  const head = { ...headers, 'Accept-Ranges': 'bytes', 'Content-Type': 'audio/mpeg' };
  if (range === false) { res.writeHead(416, { ...head, 'Content-Range': `bytes */${bytes.length}` }); res.end(); return; }
  const body = range ? bytes.subarray(range.start, range.end + 1) : bytes;
  if (range) head['Content-Range'] = `bytes ${range.start}-${range.end}/${bytes.length}`;
  res.writeHead(range ? 206 : 200, { ...head, 'Content-Length': body.length });
  res.end(req.method === 'HEAD' ? undefined : body);
}
