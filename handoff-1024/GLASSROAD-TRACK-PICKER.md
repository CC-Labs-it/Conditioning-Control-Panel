# Visible course selection, 2026-10-24

Owner could not select an individual track from the approved garage. The game branch carried only Threshold, while the separate course preview had the catalog and other authored tracks behind an older menu.

Preview remains http://127.0.0.1:18892/. It now combines the approved menu/models with saved versions of all four courses. This is an isolated integration preview, not a game-branch landing.

- Base branch `codex/glassroad-menu-courses`, integration commit `8bc70b523`: game `f99fa4dfc` plus saved combined/Mainframe/Keep `73f0c6ceb` and saved Boudoir `14ba8b151`. Both parent histories and the approved menu remain intact. Active track checkouts were read only; ongoing immersion and Boudoir fork work was not copied.
- Feature branch `codex/glassroad-course-picker` adds a visible Choose track control directly below Race. A compact native dialog shows Threshold, Mainframe, The Keep and Boudoir, actual route outlines, distance, laps and solo/online availability.
- Native dialog focus isolation prevents carousel clicks underneath. Back restores focus to Choose track. Keyboard and controller navigation use the existing garage route.
- Selecting a different card fades into the opaque loading cover, changes the validated `track` query and retains same-origin garage appearance. The existing catalog clears online room/invite parameters when switching courses. Only Threshold advertises online admission.
- Integration retains reactive water, Mainframe floor/contact refinements, Keep glide/landing support, Boudoir water palette and complete native mirror warm-up. The warm-up stays behind the approved opaque cover.

Validation: complete integrated suite passed 1,259 tests. Two new actual-course sketch tests and the two existing catalog/navigation tests passed afterward. Muted Chromium/WebGPU passed 23 UI/course checks, including all four loads, actual selected IDs and labels, outfit retention, phone layout, Back focus and race start. Muted Firefox/WebGL2 with Gentle also passed picker opening, selecting The Keep and race start without errors. Browser captures and reports are private under this chat's `track-picker/` folder.

No manual deployment, server change or online admission change. The game branch remains `f99fa4dfc` until this preview and its course integration are approved for landing.
