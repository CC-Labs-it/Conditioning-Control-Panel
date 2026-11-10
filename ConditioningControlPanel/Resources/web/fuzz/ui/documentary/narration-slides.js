import { NARRATION_SUBTITLES } from './subtitles.js';

// Phrase matches select the existing subtitle cue. No independent slideshow clock.
const SLIDES = {
  r1: [
    { phrase: 'This is the story of a person', projector: 'booth', slot: 'intro-subject', title: 'The Subject' },
    { phrase: 'you will be known as the Subject', beat: 'booth.stamp' },
  ],
  r2: [
    { phrase: 'The cart before you moves', projector: 'lever', slot: 'pump-documentary', title: 'Pumping research' },
  ],
  r3: [
    { phrase: 'The cheetah reaches', projector: 'reflex', slot: 'reflex-cheetah', title: 'The cheetah' },
    { phrase: 'The mantis shrimp strikes', projector: 'reflex', slot: 'reflex-shrimp', title: 'The mantis shrimp' },
    { phrase: "moves quite as fast as the Subject's", projector: 'reflex', slot: 'subject-habitat', title: 'The Subject' },
    { phrase: 'on hearing a floorboard creak', projector: 'reflex', slot: 'floorboard-warning', title: 'A floorboard creak', beat: 'floor.creak' },
    { phrase: 'gone in under a third of a second', beat: 'floor.snap' },
    { phrase: 'and replaced, seamlessly, with a spreadsheet', projector: 'reflex', slot: 'subject-spreadsheet', title: 'A perfectly ordinary spreadsheet' },
    { phrase: 'Again, in slow motion', projector: 'reflex', slot: 'subject-spreadsheet', title: 'Again, in slow motion', beat: 'floor.replay' },
    { phrase: 'Magnificent.', beat: 'floor.freeze' },
  ],
  r4: [
    { phrase: 'to a world of daylight', projector: 'wakeup', slot: 'outside-daylight', title: 'The outside world' },
    { phrase: 'and "we need to talk"', projector: 'conversation', slot: 'conversation-chairs', title: 'We need to talk' },
  ],
};
const normalize = text => String(text).replace(/\s+/g, ' ').trim().toLowerCase();

export function projectionTimeline(id, subtitles = NARRATION_SUBTITLES) {
  const cues = subtitles[id] || [];
  return (SLIDES[id] || []).flatMap((rule, index) => {
    const cue = cues.find(([, , text]) => normalize(text).includes(normalize(rule.phrase)));
    return cue ? [{ ...rule, at: cue[0], key: `${id}:${index}` }] : [];
  }).sort((a, b) => a.at - b.at);
}
