import test from 'node:test';
import assert from 'node:assert/strict';
import { COURSES } from '../track/catalog.js';
import { courseTheme } from '../track/themes.js';
import { createTrackPicker } from '../hud/trackPicker.js';

function element() {
  const children=[], selectors=new Map();
  return { children, dataset:{}, style:{setProperty(){}},
    append(...items){children.push(...items);}, setAttribute(){}, addEventListener(){},
    querySelectorAll(){return [];}, remove(){},
    querySelector(key){if(!selectors.has(key)) selectors.set(key,element());return selectors.get(key);},
  };
}

test('the chooser discloses every course theme and its default lyrics before racing', t => {
  const previous=globalThis.document;
  globalThis.document={createElement:element};
  t.after(()=>{globalThis.document=previous;});
  const root=element(), picker=createTrackPicker({root,courses:COURSES,trackId:'threshold',change(){}});
  const tiles=root.children[0].querySelector('.g-track-grid').children;
  for(const [i,entry] of tiles.entries()) {
    const tile=entry.innerHTML ? entry : entry.children[0];
    const theme=courseTheme(COURSES[i].id);
    const [badge,lyrics]=tile.querySelector('.g-course-text').children;
    assert.equal(badge.textContent,'Theme: '+theme.label);
    assert.equal(lyrics.textContent,'Default lyrics: '+theme.lyrics);
  }
  assert.match(courseTheme('keep').lyrics,/cock/);
  assert.equal(courseTheme('unknown'),null);
  picker.dispose();
});
