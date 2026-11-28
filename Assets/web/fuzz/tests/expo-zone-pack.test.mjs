import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readdir,readFile} from 'node:fs/promises';
import {gunzipSync} from 'node:zlib';
import {relative,join,sep} from 'node:path';
import {fileURLToPath} from 'node:url';
import {roomAsset} from '../ui/expo/entrance.js';

const folder=new URL('../assets/expo/zones/',import.meta.url);

test('the desktop zone pack contains exactly the current geometry, its maps, and collision',async()=>{
  const plan=JSON.parse(await readFile(new URL('zones.json',folder)));
  const needed=new Set(['zones.json']);
  for(const def of plan.zones){
    needed.add(def.file);needed.add(def.collision);
    needed.add(roomAsset(def,'-light.webp'));
    for(const side of ['nx','ny','nz','px','py','pz'])needed.add(roomAsset(def,'-probe-'+side+'.webp'));
    if(def.arrivalGzip){
      needed.add(def.arrivalGzip);
      assert.deepEqual(gunzipSync(await readFile(new URL(def.arrivalGzip,folder))),await readFile(new URL(def.file,folder)));
    }
    for(const name of [def.file,def.collision]){
      const bytes=await readFile(new URL(name,folder));
      assert.equal(bytes.toString('ascii',0,4),'glTF');
      const model=JSON.parse(bytes.toString('utf8',20,20+bytes.readUInt32LE(12)));
      for(const {uri} of [...(model.images||[]),...(model.buffers||[])]){
        if(!uri||uri.startsWith('data:'))continue;
        const resolved=new URL(uri,new URL(name,folder));
        assert(resolved.href.startsWith(folder.href));
        needed.add(resolved.href.slice(folder.href.length));
      }
    }
  }
  const files=await readdir(folder,{recursive:true,withFileTypes:true});
  const base=fileURLToPath(folder);
  const shipped=files.filter(entry=>entry.isFile()).map(entry=>relative(base,join(entry.parentPath,entry.name)).split(sep).join('/'));
  assert.deepEqual(shipped.sort(),[...needed].sort());
});
