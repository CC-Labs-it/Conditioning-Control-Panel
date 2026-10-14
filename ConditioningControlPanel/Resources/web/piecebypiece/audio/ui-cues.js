// Menu and screen sounds for the front door, the ladder and the pictures picker (2026-10-14 polish pass).
// CONTRACT (fixed): the door lane calls these names only; the sound lane fills the bodies. Every call is
// safe before the audio context exists and while sound is off (a no-op), never throws, never speaks.
//   hover()        a soft tick when the pointer lands on a button
//   press()        the click of a button going down
//   open()         a screen arrives (menu, lobby, profile, games, ladder, pictures, end card)
//   back()         the Back button / Escape leaves a screen
//   select()       a choice chip turns on (difficulty, flavour, toggle)
//   tick()         one step of a number roll (ratings, stats)
//   stamp()        a stamp lands (+N, new best, a result word)
//   error()        a refused action (offline, not signed in)
//   setVolume(v)   0..1, follows the page's sfx volume
export const ui = {
  hover() {}, press() {}, open() {}, back() {}, select() {},
  tick() {}, stamp() {}, error() {}, setVolume() {},
};
export default ui;
