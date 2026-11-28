import test from 'node:test';
import assert from 'node:assert/strict';
import {captionPages,captionAt} from '../ui/expo/caption-pages.js';
import {trailLines} from '../ui/expo/trail-lines.js';

test('phone captions keep every recorded word and page through to the end',()=>{
  for(const line of trailLines){
    const pages=captionPages(line.subtitle);
    assert.equal(pages.map(p=>p.value).join(' '),line.subtitle);
    assert.ok(pages.every(p=>p.value.length<=76));
    assert.equal(captionAt(pages,0),pages[0].value);
    assert.equal(captionAt(pages,1),pages.at(-1).value);
  }
});
