// Fresh, public lexical mapping to physical letter mechanisms. No source phrases, recordings or timings.
// Numeric ids are shared with the shader and must remain stable. The first ten retain their original meaning.
export const WORD_EFFECTS = Object.freeze(['none', 'slide', 'spark', 'pink', 'sink', 'glass', 'rolling', 'reset',
  'empty', 'locked', 'burn', 'breath', 'echo', 'focus', 'liquid', 'fragment', 'elastic', 'rise', 'heavy', 'pulse',
  'fade', 'chain', 'code', 'gather', 'switch', 'orbit', 'rough', 'glow', 'ice', 'fold', 'split', 'rain', 'warm',
  'shiver', 'spectrum', 'metal', 'green']);
export const WORD_IDENTITY_COUNT = WORD_EFFECTS.length - 1;

// Whole lexical forms are deliberate. A shared physical meaning may share a mechanism; substring guessing
// must never turn a different word into an effect. Earlier rows retain ownership if a form has two senses.
const FORMS = [
  '',
  'slide slides sliding slid shift shifts shifting shifted back return returns returning right change changes changing changed move moves moving moved fast faster fastest speed speeds speeding rush rushes rushing rushed run runs running ran race races racing travel travels travelling traveling conveyor carry carries carrying carried like',
  'spark sparks sparked sparking electric electrical electricity voltage volt volts charge charged charging lightning laser lasers',
  'pink pinks pinkish rose rosy magenta fuchsia good reward rewards plastic gel goo gooey sticky glue',
  'sink sinks sinking sank sunk deep deeper deepest depth depths down below beneath underneath low lower lowest plunge plunges plunging plunged',
  'glass glassy glasses crystal crystals crystalline prism prisms diamond diamonds gem gems lens lenses',
  'rolling roll rolls rolled roller rollers wave waves waving waved ride rides riding rode',
  'reset resets resetting restart restarts restarted restarting reboot reboots rebooted rebooting',
  'empty empties emptied emptying blank blanks blanking blanked hollow hollows inside void voids nothing erase erases erased erasing',
  'locked lock locks locking latch latches latched latching sealed seal seals sealing restrain restrains restrained restraining restraint restraints bound bind binds binding obey obeys obeying obeyed',
  'burn burns burned burnt burning fire fires fiery flame flames blaze blazes blazing ash ashes ember embers hot hotter hottest heat heated heating warmth',
  'breath breaths breathe breathes breathing breathed inhale inhales inhaling inhaled exhale exhales exhaling exhaled calm calms calming calmed easy ease eases eased easing content patient patience rest rests resting rested quiet quietly silent silence slow slower slowly stay stays staying sleep sleeps sleeping asleep drowsy alive living life lives vital',
  'echo echoes echoed echoing repeat repeats repeated repeating again voice voices whisper whispers whispered whispering sound sounds sounding song songs sing sings singing sung call calls called calling hear hears heard hearing say says saying said talk talks talking talked speak speaks speaking spoken phrase phrases word words resonance resonant resonate',
  'focus focuses focused focusing eye eyes look looks looking looked see sees seeing seen watch watches watching watched scan scans scanned scanning read reads reading check checks checking checked detect detects detected detecting inspect inspects inspected inspecting clear clearer clearest sharpen sharpens sharpened notice notices noticed noticing know knows knowing known place places placed placing',
  'liquid liquids fluid fluids flow flows flowing flowed drip drips dripped dripping pour pours poured pouring wet wetter wettest melt melts melted melting leak leaks leaked leaking seep seeps seeped seeping mess messy honey syrup viscous',
  'fragment fragments fragmented fragmenting part parts piece pieces puzzle puzzles break breaks broken breaking broke fracture fractures fractured fracturing shatter shatters shattered shattering scatter scatters scattered scattering burst bursts bursting explode explodes exploded exploding explosion apart release releases released releasing free freed freeing freedom',
  'elastic elasticity rubber rubbery stretch stretches stretched stretching soft softer softest soften softens softened softening bounce bounces bounced bouncing spring springs sprung springing happy happier happiest happiness joy joyful enjoy enjoys enjoyed enjoying smile smiles smiled smiling grin grins grinning grinned pretty prettier prettiest play plays playing played playful',
  'rise rises rising risen up upward upwards lift lifts lifted lifting grow grows grown growing growth swell swells swelled swelling full fuller fullest expand expands expanded expanding large larger largest big bigger biggest better climb climbs climbed climbing stair stairs staircase step steps stepped stepping high higher highest float floats floating floated fly flies flying flew wing wings garden gardens leaf leaves bloom blooms blooming bloomed bud buds sprout sprouts sprouting',
  'heavy heavier heaviest weight weights weighted weighting pressure press presses pressed pressing hard harder hardest force forces forced forcing strong stronger strongest strength power powers powerful stone stones solid concrete cement cements fall falls fell fallen falling drop drops dropped dropping',
  'pulse pulses pulsed pulsing pulsate pulsates pulsating throbs throb throbbing stroke strokes stroking beat beats beating rhythm rhythmic rhythms clock clocks clockwork time times timing hour hours minute minutes second seconds past future tick ticks ticking heart hearts heartbeat kick kicks kicked kicking impact impacts',
  'fade fades faded fading gone vanish vanishes vanished vanishing disappear disappears disappeared disappearing dark darker darkest darkness shadow shadows hidden hide hides hiding hid invisible invisibility evaporate evaporates evaporated evaporating mist mists fog fogs smoke smokes smoky vapour vapor',
  'chain chains chained chaining cage cages caged caging link links linked linking rope ropes tether tethers tethered tethering tie ties tied tying knot knots cord cords collar collars leash leashes cuff cuffs shackle shackles anchor anchors anchored anchoring paired pair pairs same',
  'code codes coded coding decode decodes decoded decoding cipher ciphers brain brains mind minds think thinks thinking thought thoughts program programs programmed programming machine machines computer computers signal signals screen screens type types typed typing first next number numbers numeric count counts counted counting zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty hundred thousand million binary digital digit digits data algorithm algorithms token tokens script scripts',
  'gather gathers gathered gathering pull pulls pulled pulling hold holds held holding together collect collects collected collecting build builds building built beam beams frame frames framed framing assemble assembles assembled assembling join joins joined joining connect connects connected connecting tiny small smaller smallest shrink shrinks shrinking shrunk tighten tightens tightened tightening',
  'switch switches switched switching on off start starts started starting stop stops stopped stopping toggle toggles toggled toggling activate activates activated activating enable enables enabled enabling button buttons lever levers',
  'orbit orbits orbited orbiting around round rounds rounded circle circles circling circular spin spins spinning spun spiral spirals spiraling spiralling swirl swirls swirling turn turns turned turning wheel wheels moon moons planet planets world worlds galaxy galaxies universe cycle cycles loop loops looped looping',
  'rough rougher roughest edge edges edged edging rust rusty rusting corrode corrodes corroded corroding decay decays decayed decaying crumble crumbles crumbled crumbling grit gritty dust dusty sand sands grain grains granular',
  'glow glows glowing glowed light lights lit lighting bright brighter brightest shine shines shining shone glimmer glimmers glimmering glitter glitters glittering sparkle sparkles sparkling star stars starlight luminous radiant radiate radiates radiating neon wonder wonders wonderful magic magical',
  'ice icy frozen freeze freezes freezing froze cold colder coldest chill chills chilled chilling frost frosts frosty snow snows snowy still wait waits waited waiting',
  'fold folds folded folding unfold unfolds unfolded unfolding close closes closed closing shut shuts shutting curl curls curled curling wrap wraps wrapped wrapping bend bends bent bending',
  'split splits splitting open opens opened opening exit exits exiting gate gates door doors separate separates separated separating divide divides divided dividing peel peels peeled peeling',
  'rain rains rained raining raindrop raindrops water waters river rivers sea seas ocean oceans lake lakes shower showers tear tears teardrop teardrops droplet droplets splash splashes splashing flood floods flooded flooding storm storms waterfall waterfalls',
  'warm warms warmed warming love loves loved loving lover lovers care cares cared caring sweet sweeter sweetest pleasure pleasures pleasurable want wants wanted wanting desire desires desired desiring feel feels feeling felt touch touches touched touching arousal aroused fingers finger hands hand legs leg skin silk silky velvet tender tenderness',
  'shiver shivers shivered shivering shy shyer shyest worry worries worried worrying fear fears feared fearing afraid scared scare scares scary ache aches aching ached pain painful hurt hurts hurting sorrow sad sadness tremble trembles trembling trembled',
  'spectrum spectra rainbow rainbows prismatic colour colours color colors colourful colorful multicolour multicolor psychedelic iridescent iridescence chromatic',
  'metal metals metallic steel steely silver silvery chrome chromium gold golden gilded brass bronze mirror mirrors mirrored mirroring reflect reflects reflected reflecting reflection reflections polished polish polishing sheen',
  'green greens greenish emerald emeralds jade mint minty lime verdant',
].map(forms => forms ? forms.split(' ') : []);
for (let n = 0; n <= 100; n++) FORMS[22].push(String(n));
const IDENTITY = new Map();
for (const [id, forms] of FORMS.entries()) for (const form of forms) if (!IDENTITY.has(form)) IDENTITY.set(form, id);

function wordToken(text) {
  if (typeof text !== 'string') return '';
  const match = text.normalize('NFKC').trim().match(/^[\s("'\u2018\u201c\[]*([a-z0-9]+)[\s)"'\u2019\u201d\].,!?;:]*$/i);
  return match ? match[1].toLowerCase() : '';
}

/** Whole words only, allowing ordinary surrounding speech punctuation. Unknown text stays plain. */
export function wordIdentity(text) {
  return IDENTITY.get(wordToken(text)) || 0;
}

/** The same stable id for an approved display spelling; inflections and unknown words return zero. */
export function wordIdentityExact(text) {
  const token = wordToken(text), id = IDENTITY.get(token) || 0;
  return id && FORMS[id][0] === token ? id : 0;
}

/** A trigger keeps its complete phrase. Original signatures outrank incidental supporting words. */
export function phraseWordIdentity(text) {
  let first = 0;
  for (const token of String(text || '').split(/\s+/)) {
    const id = wordIdentity(token);
    if (id > 0 && id <= 10) return id;
    if (!first && id) first = id;
  }
  return first;
}
