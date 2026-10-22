# Glassroad art: Wave 1 prop kit

21 static meshopt GLBs. The 7 LOD pairs are alternatives; use one LOD per instance.
Original cards, watch, bubble and spiral disc join reduced versions of the existing cabinet and approved chess models.
No images are embedded. Vertex colours and PBR factors provide the static colour work.

## Integration

Metres, Y up, forward -Z, base-centre origin. boundRadiusM encloses the actual packed mesh
from that origin, with a 1 mm pad rounded upward. It is conservative for scatter rotation.
Meshopt decode support and KHR_mesh_quantization support are required.
Every model is static. The slot lever, hands and other parts do not ship actions.
Most assets use one material draw; queen/king/watch/disc use two and the cabinet uses five.
Draw count, dimensions, LOD and material slots are listed in manifest.json.
Preserve COLOR_0: it carries the approved chess paint and the original card symbols.

Slot materials screen, credit and emi_screen are blank with 0..1 UVs.
The screen covers the display surfaces and the credit slot supports a runtime attribution tag.
No media is included. A host that displays media must populate the credit independently.
The disc uses spiral_mask on both sides, with clean 0..1 UVs; bind a shared neutral sky mask.
The bubble uses KHR_materials_iridescence plus clearcoat and alpha blending (alpha 0.14).
The shader owns all light intensity, motion and Gentle Visuals handling.

## Sources and licence

The owner explicitly required reuse of the existing CCP assets:
- backroom/stations/slot/assets/slot.glb
- piecebypiece/assets/pieces/bishop.glb
- piecebypiece/assets/pieces/king.glb
- piecebypiece/assets/pieces/knight.glb
- piecebypiece/assets/pieces/pawn.glb
- piecebypiece/assets/pieces/queen.glb
- piecebypiece/assets/pieces/rook.glb

Paths above are relative to ConditioningControlPanel/Resources/web.
Their existing CCP licence and rights remain unchanged. They are not relabelled CC0.
Chess LODs only decimate the approved geometry and retain its materials and vertex paint.
The knight/rook source contains zero-area triangles; only those invisible degenerate faces were removed.
The cabinet retains its source construction, simplified and grouped by material.
Cards, watch, bubble and spiral disc are original procedural work for CCP, under the project's existing asset terms.
No concepts, third-party pixels, real-person images or external geometry are redistributed.
The private master archive retains scripts, .blend sources, source hashes and the six-view checks.

## Assumptions and review

Use the six standard chess paint variants; alternate purple variants are not duplicated in this kit.
Cards are theatrical props, 0.66 x 1 m, with original Lens, Petal, Orbit and Drop symbols.
Watch height includes the bow (1.156 m); bubble diameter is 1 m; disc diameter is 0.92 m.
The source slot scale is retained, about 1.89 m tall. No functional slot behaviour is included.
Select LOD distances after Claude places props in the actual scene; no engine settings are changed here.
Review images show actual decoded runtime geometry, including every LOD.

## Triangles and texture memory

| Asset | Tris | Texture bytes | Bound radius (m) | Draws |
|---|---:|---:|---:|---:|
| bubble | 960 | 0 | 1.002 | 1 |
| card-drop | 464 | 0 | 1.043 | 1 |
| card-lens | 486 | 0 | 1.043 | 1 |
| card-orbit | 486 | 0 | 1.043 | 1 |
| card-petal | 476 | 0 | 1.043 | 1 |
| chess-bishop-lod0 | 5,798 | 0 | 1.004 | 1 |
| chess-bishop-lod1 | 1,448 | 0 | 1.010 | 1 |
| chess-king-lod0 | 5,799 | 0 | 1.288 | 2 |
| chess-king-lod1 | 1,432 | 0 | 1.285 | 2 |
| chess-knight-lod0 | 5,720 | 0 | 1.008 | 1 |
| chess-knight-lod1 | 1,370 | 0 | 1.008 | 1 |
| chess-pawn-lod0 | 5,088 | 0 | 0.552 | 1 |
| chess-pawn-lod1 | 1,450 | 0 | 0.552 | 1 |
| chess-queen-lod0 | 5,796 | 0 | 1.171 | 2 |
| chess-queen-lod1 | 1,443 | 0 | 1.171 | 2 |
| chess-rook-lod0 | 5,744 | 0 | 1.004 | 1 |
| chess-rook-lod1 | 1,393 | 0 | 1.006 | 1 |
| pocket-watch | 1,258 | 0 | 1.158 | 2 |
| slot-cabinet-lod0 | 5,571 | 0 | 1.959 | 5 |
| slot-cabinet-lod1 | 1,349 | 0 | 1.955 | 5 |
| spiral-disc | 572 | 0 | 0.922 | 2 |

Texture memory excludes already-shared sky masks; no new per-model texture residency is required.
Review PNGs are evidence only, not runtime inputs.

Each family branch starts directly from the same main snapshot. When integrating families,
append their distinct manifest rows and retain all family ART.md sections. The sampled palettes are identical.
