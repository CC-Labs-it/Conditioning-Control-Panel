import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {mkdtemp, mkdir, copyFile, readFile, writeFile, rm} from 'node:fs/promises';
import {join} from 'node:path';
import {tmpdir} from 'node:os';

test('a repeat build ships the staged radio, exact manifest and identical archive bytes', async t => {
  const temp = await mkdtemp(join(tmpdir(), 'expo-pack-'));
  t.after(() => rm(temp, {recursive: true, force: true}));
  const repo = join(temp, 'game-repo');
  const game = join(repo, 'game');
  const out = join(temp, 'outputs');
  const assets = join(game, 'assets/expo');
  const radio = join(game, 'assets/lofi/audio');
  for (const dir of [join(repo,'.git'), join(game,'dev'), join(assets,'audio/radio'), join(assets,'zones'), radio]) {
    await mkdir(dir, {recursive:true});
  }
  for (const name of ['expo-pack.mjs', 'expo-pack.py']) {
    await copyFile(new URL('../dev/' + name, import.meta.url), join(game, 'dev', name));
  }
  await writeFile(join(assets,'zones/zones.json'), '{}');
  await writeFile(join(assets,'audio/radio/.gitignore'), '*.mp3');
  const tracks = ['breathe-in.mp3','idle-cycle.mp3','sealed.mp3'];
  for (const track of tracks) {
    await writeFile(join(radio,track), track);
    await writeFile(join(assets,'audio/radio',track), 'obsolete local copy');
  }
  const build = (...extra) => JSON.parse(execFileSync(process.execPath, [
    'dev/expo-pack.mjs','--out',out,'--version','test-1',...extra], {cwd:game, encoding:'utf8', stdio:'pipe'}));
  const first = build('--compare');
  const manifest = JSON.parse(await readFile(join(out,'expo-manifest.json')));
  assert.equal(first.files, 4);
  assert.equal(first.formats.deflated.bytes, manifest.bytes);
  const zip = join(out, manifest.file);
  const bytes = await readFile(zip);
  assert.deepEqual(manifest, {v:1, file:first.manifest.file, bytes:bytes.length,
    sha256:createHash('sha256').update(bytes).digest('hex'), version:'test-1', minApp:'7.4.0'});
  const python = process.env.PYTHON || (process.platform === 'win32' ? 'python' : 'python3');
  const contents = JSON.parse(execFileSync(python, ['-c',
    'import json, sys, zipfile\nwith zipfile.ZipFile(sys.argv[1]) as z: print(json.dumps({n:z.read(n).decode() for n in z.namelist()}))', zip]));
  assert.deepEqual(contents, Object.fromEntries([['zones/zones.json','{}'], ...tracks.map(name=>['audio/radio/'+name,name])]));
  assert.deepEqual(build().manifest, manifest);
  assert.throws(() => build('--out', join(game,'bad-output')), /outside the repository/);
  for (const name of tracks) {
    await rm(join(radio,name));
    assert.throws(() => build(), error => error.stderr.includes('Missing licensed radio') && error.stderr.includes(name));
    await writeFile(join(radio,name),name);
  }
  assert.deepEqual(JSON.parse(await readFile(join(out,'expo-manifest.json'))), manifest);
});
