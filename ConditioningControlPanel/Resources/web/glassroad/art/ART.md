# Glassroad art

Wave 1 runtime assets by Astra, one section per family (PRs #2557-#2561).
Contact sheets and masters live in the private art folder, not in this tree.

## road: road set (#2557)

Six texture inputs for the engine-generated road, rails, boost pads and jump markers.
These are texture assets, so no GLB or extra road triangles are shipped.

### Sampling

| Asset | Tile in metres (U x V) | Sampler | Interpretation |
|---|---:|---|---|
| road-bands-mask | 12 x 16 | repeat both | Three broad lateral crests with a slow along-track warp. Map R through the palette. |
| road-glass-roughness | 4 x 4 | repeat both | Subtle roughness, about 0.18-0.25. |
| road-glass-normal | 4 x 4 | repeat both | OpenGL tangent-space normal, shallow glass surface. |
| rail-glow-mask | 0.9 x 8 | clamp U, repeat V | U follows rail height; one broad crest and a weaker low edge. |
| boost-chevron-mask | 4 x 6 | clamp U, repeat V | Add 0.5 to U after dividing lateral metres by 4. Tip points along +V. |
| jump-lip-mask | 12 x 3 | repeat U, clamp V | Last 3 m before the jump lip, with a broad paired curl. |

For road U=0 at the centre, repeating U supports either signed metre values or a half-tile phase shift.
Rail U=0 at the bottom. Boost V=0 at the pad start. Jump V=0 starts the lip patch.
The normal and roughness maps describe the glass; band, rail and marker masks describe shader inputs.

### Sources and licence

Original deterministic procedural work created for CCP. No third-party pixels or geometry.
The supplied concept images informed mood and palette only. They are not redistributed or traced.
Dawn was sampled from 01-key-art_1, deep from 04-rabbit-hole_2, afterglow from 11-afterglow-finish_1.
The private master archive retains exact sampling coordinates, source PNGs, scene and generation scripts.
Rights follow the project's existing asset terms; these assets are not claimed to be CC0.

### Validation

All six packaged KTX2 files pass Khronos strict glTF BasisU validation and decode successfully.
All tileable source edges match exactly. ETC1S introduces small block differences at some edges;
review uses the decoded runtime files, not the source PNGs. Normal map uses UASTC.
The road shader must own intensity and contrast; do not display a raw black/white mask as lighting.
Front swatches, oblique material views and a sky-coloured scene are in review/road-contact.png.

### Contract

Runtime assets only. No scene, renderer, physics, track or host code changes.
All geometry uses metres, Y up, base-centre origin, forward -Z.
Textures are KTX2 with complete mip chains. Mask and data textures are linear; colour layers are sRGB.
Read scalar masks from the red channel. They are not coloured pictures or final luminance.
Texture origin is top-left (KTX orientation rd); V increases forward for road surfaces.
Use the manifest sampler modes and scale metre UVs by uvSizeM before sampling.

texBytes is conservative RGBA8 resident memory, including every mip, not download size.
texBytesBlock16 is a 16-byte-per-4x4-block GPU allocation including all mips.
The actual Basis transcode target depends on the device. Budget against texBytes.
A texture row has tris 0 and boundRadiusM 0 because it contributes no geometry.
Every model/prop row carries a radius measured from the export origin including all its vertices.

Palettes contain five concept-sampled sRGB colours in this order:
background, primary, secondary, accent, pearl. Choose primary through pearl for road bands.
No motion or lighting changes are baked. Engine Gentle Visuals and flash limiting control
contrast, scroll, bloom and transitions. The contact sheet is static material review, not a game capture.
Review PNGs are evidence and are not runtime texture inputs.

Each family branch starts from the same main snapshot. When integrating multiple families,
append unique rows from each manifest and retain all family sections in ART.md.
The shared palettes.json is identical across branches.

### Triangles and texture memory

| Asset | Tris | RGBA8 with mips (MiB) | Block16 with mips (MiB) |
|---|---:|---:|---:|
| road-bands-mask | 0 | 5.333 | 1.333 |
| road-glass-roughness | 0 | 1.333 | 0.333 |
| road-glass-normal | 0 | 1.333 | 0.333 |
| rail-glow-mask | 0 | 0.667 | 0.167 |
| boost-chevron-mask | 0 | 1.333 | 0.333 |
| jump-lip-mask | 0 | 0.667 | 0.167 |
| Total | 0 | 10.667 | 2.667 |

## sky: sky set (#2558)

Three palette families of static galaxy haze and separate sparse star fields, plus two reusable
grayscale spiral masks. All motion and spiral colouring remain in the engine shader.

### Sampling and composition

Galaxy textures are 2048 x 1024 equirectangular maps. Star fields are 1024 x 512 equirectangular maps.
U wraps longitude, V clamps latitude; the north pole is V=0. Use sphere direction to derive the UVs.
The separate star layer has black background and adds light after sRGB decoding.
Each palette has its own colour layers; only the active palette and a crossfade partner need residency.

The 1024-square masks use disc UVs 0..1, centre (0.5, 0.5), transparent-by-zero outside the disc.
Sample R, multiply by the selected palette colour and apply the engine's contrast/intensity controls.
open-mask has two broad arms; deep-mask has three. Both have a smooth core and edge.
They deliberately avoid fine high-contrast central rings. Rotate their UVs, never the static nebula.
If projected on a dome, keep angular extent and orientation under the sky shader's control.
The contact sheet combines these actual decoded layers with a static review recipe; it is not baked runtime art.

### Sources and licence

Original deterministic spherical cloud fields, spherical star sampling and spiral-mask construction for CCP.
No external sky photography, HDRI, generated image or third-party texture was used.
The supplied concepts informed mood and the shared sampled palettes only.
The private master archive retains generation inputs, source images and review composition source.
Rights follow the project's existing asset terms; these assets are not claimed to be CC0.

### Validation

All eight KTX2 files pass Khronos strict glTF BasisU validation and decode successfully.
Source longitude edges match exactly. Hero galaxy gradients use UASTC to avoid dark-field block banding.
The bulk star fields and neutral masks use ETC1S; decoded differences are recorded privately.
Mask borders decode to zero and can clamp without a visible rectangular edge.
The whole sky set's conservative memory is listed below, although normal play needs one palette at a time.

### Contract

Runtime assets only. No scene, renderer, physics, track or host code changes.
All geometry uses metres, Y up, base-centre origin, forward -Z.
Textures are KTX2 with complete mip chains. Mask and data textures are linear; colour layers are sRGB.
Read scalar masks from the red channel. They are not coloured pictures or final luminance.
Texture origin is top-left (KTX orientation rd); V increases forward for road surfaces.
Use the manifest sampler modes and scale metre UVs by uvSizeM before sampling.

texBytes is conservative RGBA8 resident memory, including every mip, not download size.
texBytesBlock16 is a 16-byte-per-4x4-block GPU allocation including all mips.
The actual Basis transcode target depends on the device. Budget against texBytes.
A texture row has tris 0 and boundRadiusM 0 because it contributes no geometry.
Every model/prop row carries a radius measured from the export origin including all its vertices.

Palettes contain five concept-sampled sRGB colours in this order:
background, primary, secondary, accent, pearl. Choose primary through pearl for road bands.
No motion or lighting changes are baked. Engine Gentle Visuals and flash limiting control
contrast, scroll, bloom and transitions. The contact sheet is static material review, not a game capture.
Review PNGs are evidence and are not runtime texture inputs.

Each family branch starts from the same main snapshot. When integrating multiple families,
append unique rows from each manifest and retain all family sections in ART.md.
The shared palettes.json is identical across branches.

### Triangles and texture memory

| Asset | Tris | RGBA8 with mips (MiB) | Block16 with mips (MiB) |
|---|---:|---:|---:|
| sky-dawn-galaxy | 0 | 10.667 | 2.667 |
| sky-dawn-stars | 0 | 2.667 | 0.667 |
| sky-deep-galaxy | 0 | 10.667 | 2.667 |
| sky-deep-stars | 0 | 2.667 | 0.667 |
| sky-afterglow-galaxy | 0 | 10.667 | 2.667 |
| sky-afterglow-stars | 0 | 2.667 | 0.667 |
| sky-spiral-open-mask | 0 | 5.333 | 1.333 |
| sky-spiral-deep-mask | 0 | 5.333 | 1.333 |
| Total | 0 | 50.667 | 12.667 |

## kart: EMI teacup kart (#2559)

A porcelain cup and saucer carrying the existing EMI mascot.
Runtime: one skinned mesh, two material primitives, 13 rigid joints, 4,722 triangles.
Eight karts therefore contribute 16 material draws before engine-specific passes.

### Integration

Metres, Y up, forward -Z. Origin is the saucer's underside centre.
The saucer radius is 0.72 m; the handle extends the full width to about 1.528 m.
The model is about 1.263 m tall. Manifest radius is padded upward from measured decoded vertices.
Meshopt decode support and KTX2 Basis transcode support are required.
Position values remain full float under lossless meshopt compression to preserve the smallest inherited faces.

The blank screen is a separate material named emi_screen with a clean 0..1 UV island.
It has no image, no face atlas and no painted expression. Bind the runtime face texture there.
kart_surface uses two embedded 512-square UASTC atlases:
base colour (sRGB) and packed occlusion/roughness/metalness (linear), each with ten mip levels.
All visible colour and finish changes outside the screen share that surface material.
No separate external texture files need loading for this kart.

The model has a real rigid-weight skin: every vertex follows exactly one joint.
Named joints preserve the source control points:
kart_root, rider_mount, EMI_root, shoulderL, shoulderR, ant0, ant1, ant2,
ballpiv, button, footL, footR, EMI_glass.
Animate the named bone transforms for arms and antenna. Drive the whole kart from its scene root.
Keep the mesh and skeleton together; use SkeletonUtils.clone for independent kart instances.
The source's five selection/showcase clips were omitted because full-body hops and stretches
do not fit a seated rider. No new race animations are claimed or included.

### Sources and licence

EMI starts from ConditioningControlPanel/Resources/web/dtrh/race/assets/emi.glb.
Her approved body, screen, arms, antenna and identity are retained. The inverted outline hull
and fully concealed footwear geometry were removed to make room for the cup within the triangle budget.
Only zero-area faces were removed after the rigid bind; no visible character forms were redrawn.
The cup, saucer, connected handle, inlaid motifs and shared surface atlas are original work for CCP.
The existing EMI asset rights remain unchanged under the project's existing terms; no CC0 claim is made.
The supplied concepts guided porcelain, trim and composition only; no reference pixels were reused.
Private sources retain the accepted rigid version, .blend masters, atlas source and reproducible scripts.

### Validation and assumptions

Fresh import of the final decoded compressed GLB preserves one mesh, two primitives, 13 bones,
the named materials, full screen UV range and zero degenerate triangles.
Bind conversion moved vertices by at most 0.000000121 m.
A 25-degree shoulder test moves the arm while keeping the cup and screen fixed.
The contact sheet and private pose views were rendered from the actual decoded packed export.
Three.js r186 CPU skinning also confirms that translating, rotating and scaling the common root
moves all 3,063 vertices correctly. The validator non-root-skinned-mesh warning is accounted for:
mesh and joints share identity-transform parents, with no independent mesh offset.
The engine still needs to supply faces, race poses, floating motion and Gentle Visuals behaviour.
The original source's +Z facing was rotated to the required -Z direction.
The cup's 0.72 m saucer radius follows the current engine's visual footprint.

### Triangles and texture memory

| Asset | Tris | RGBA8 with all mips | Block16 with all mips | Draws |
|---|---:|---:|---:|---:|
| emi-teacup-kart | 4,722 | 2,796,200 bytes (2.667 MiB) | 699,104 bytes (0.667 MiB) | 2 |

texBytes uses conservative RGBA8 allocation including every mip, not compressed download size.
Textures can be shared by every kart instance; do not count their memory once per racer.
Review PNGs are evidence only and are not runtime texture inputs.
Each family branch starts directly from the same main snapshot. When integrating families,
append their distinct manifest rows and retain every family ART.md section. Palettes are identical.

## props: prop kit (#2560)

21 static meshopt GLBs. The 7 LOD pairs are alternatives; use one LOD per instance.
Original cards, watch, bubble and spiral disc join reduced versions of the existing cabinet and approved chess models.
No images are embedded. Vertex colours and PBR factors provide the static colour work.

### Integration

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

### Sources and licence

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

### Assumptions and review

Use the six standard chess paint variants; alternate purple variants are not duplicated in this kit.
Cards are theatrical props, 0.66 x 1 m, with original Lens, Petal, Orbit and Drop symbols.
Watch height includes the bow (1.156 m); bubble diameter is 1 m; disc diameter is 0.92 m.
The source slot scale is retained, about 1.89 m tall. No functional slot behaviour is included.
Select LOD distances after Claude places props in the actual scene; no engine settings are changed here.
Review images show actual decoded runtime geometry, including every LOD.

### Triangles and texture memory

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

## portals: countdown and finish (#2561)

Three static meshopt GLBs: an instanced countdown ring and two LODs of an open finish arch.
No textures are embedded. The spiral faces use the shared sky masks.

### Integration

Metres, Y up, base-centre origin, forward -Z. The countdown ring stands upright with its bottom at Y=0.
Instance three rings above the grid. Geometry contains no numerals or baked count sequence.
The countdown ring is 2.91 m across including trim; position each instance by its base, not its eye.
The finish arch is about 16.21 m wide and 10.92 m tall.
Its minimum interior width over the first 2.8 m above the road is 14.4 m, clearing the nominal 12 m road.
There is no face across the driving opening. The spiral_mask material occupies only the arch accent strip.

Materials: portal_glass, portal_trim, spiral_mask. Static surfaces are combined to three material draws per asset.
spiral_mask initially has zero alpha and no image. Bind the shared sky spiral mask and explicitly configure
the shader material's alpha and emission. A raw GLB preview intentionally shows only the ring and trim.
The host owns countdown timing, colour, contrast, transitions and Gentle Visuals controls.
Apply sky-spiral-open-mask to the countdown disc UVs. The finish accent uses its local 0..1 strip UVs.
Do not rotate the whole finish geometry to create a spiral effect; animate the mask in its material.

boundRadiusM encloses packed vertices from the base-centre origin, including a 1 mm upward safety pad.
Meshopt decoding and KHR_mesh_quantization are required. LODs are alternatives, not simultaneous geometry.
Other surface UVs can exceed 0..1 because they are metre-based; spiral_mask UVs stay 0..1.

### Sources and licence

Original procedural glass bands, inset fillets, trim and annular shader faces created for CCP.
The supplied start/finish concepts guided mood only, with no tracing or copied pixels.
No third-party meshes, textures, photography or media are included.
Rights follow the project's existing asset terms; no CC0 claim is made.
The private master archive retains the generator, .blend masters and independent import evidence.

### Assumptions and review

One instanced countdown ring supplies all countdown lights; the engine chooses placement and count.
Portal opening was chosen for the plan's 12 m road plus lateral clearance.
The LOD1 removes decorative fillets but retains the double-arch silhouette and clearance.
Review images include front, three-quarter and a sky-coloured material preview, plus six fixed views.
The context view temporarily binds the shared neutral mask; no coloured or moving pattern is baked.

### Triangles and texture memory

| Asset | Tris | Texture bytes | Bound radius (m) | Draws |
|---|---:|---:|---:|---:|
| countdown-ring | 1,224 | 0 | 2.912 | 3 |
| finish-portal-lod0 | 2,876 | 0 | 10.923 | 3 |
| finish-portal-lod1 | 780 | 0 | 10.860 | 3 |

Texture memory excludes already-shared sky masks; no new per-model texture residency is required.
Review PNGs are evidence only, not runtime inputs.

Each family branch starts directly from the same main snapshot. When integrating families,
append their distinct manifest rows and retain all family ART.md sections. The sampled palettes are identical.

## fx: effect bubble icons (B1, lifted from DtRH)

Source: our own DtRH art, `Resources/web/dtrh/assets/bubbles/effects/*.png` and `bubbles/bubble.png`
(CCP originals, same rights as the DtRH tree). Copied into `art/fx/` and re-encoded as RGBA PNG at
256 px or less with PIL (the sources are already 256 x 256, so no resize). Plain PNG, sRGB, straight
alpha; NOT in `manifest.json` and not loaded by `render/art.js` (KTX2 only): the B1 lanes load them with
a three.js `TextureLoader` (render) or as `<img>` (HUD). Each icon is a whole glass bubble with the
effect drawn inside; `bubble.png` is the empty bubble.

| Item id (sim/itemTable.js) | File | Notes |
|---|---|---|
| flash | flash.png | white star |
| spiral | spiral.png | teal spiral |
| brainDrain | braindrain.png | green static |
| freeze | bambifreeze.png | snowflake |
| blackout | blackout.png | black sphere |
| pink | pinkfilter.png | pink level |
| melt | melt.png | dripping drop |
| shatter | prism.png | rainbow swirl |
| surge | gold_droplet.png | gold drop |
| glass | bubble.png | the empty bubble |
| echo | glitch.png | broken copies |
| golden | golden.png | gold dust spiral |
| (later: Glitch, Lock, Subliminal, Video) | gifwash.png, htlink.png, lock.png, subliminal.png, video.png | unused in B1 |

17 files, 1.2 MB together. The phone tier uses them as is (256 px).
## Wave 2: particle atlas

One 1024-square UASTC atlas with a 4 x 4 grid of 256-square cells.
The manifest cells map uses zero-based [column,row], with row zero at the top.
Cells are disc, spark, glint, glitter, petal, heart, ring, smoke, drip, shard,
frost, swirl, bubble, star and two empty spares. A cell has at least 20 px of empty gutter.

Use the alpha channel as coverage. RGB is neutral white, including transparent pixels;
this prevents dark filtering fringes. Storage is straight alpha: premultiply once in the
host when using premultiplied blending. Tint, brightness, lifetime and motion belong to the engine.
Do not use this atlas as an sRGB colour input. Clamp sampling to LOD 4 to avoid unrelated
cells mixing in the smallest atlas mips. Apply the half-texel inset inside each cell.

Source: original deterministic procedural silhouettes, no external image or font input.
Private masters and decoded-file evidence are in glassroad-art/w2/particles.
Contact sheets are hosted as PR attachments, never included in the installer tree.

| Asset | Tris | RGBA8 residency including mips |
|---|---:|---:|
| particles-atlas | 0 | 5,592,404 bytes (5.333 MiB) |

Running manifest total after this family: 69.333 MiB, compared with 64 MiB in Wave 1.
This includes every palette and mip, not just the active sky. Runtime render targets and
engine-owned effect icons need their own allowance. KTX strict validation and decoded-file
visual inspection passed. Engine motion and device integration are Claude's lane.
## Wave 2: screen-effect masks

Six neutral ETC1S scalar fields. Sample .r in linear space; .g and .b are not data.
White is effect strength, except iris where white is the visible aperture.
Melt flow uses 0.5 as neutral displacement. Controls, strength, duration, tint and
Gentle Visuals remain with the host. No timing, flash or full-screen white fill is baked.

Frost has a clear central window and branches growing in from each edge.
Melt flow repeats both axes, with long vertical runs. Melt spiral is a clamped disc with
broad warped arms and attached drips. Crack is sparse, soft branching glass lines.
Iris is a soft circular aperture; adjust aspect in the host to keep the window round.
Noise is toroidal ranked high-pass blue noise for dissolves and dithering. Neighbouring
samples differ across its wrap seam just as they do elsewhere; no edge stripe is forced.

Wrap Melt flow and noise; clamp the other four. For noise, sample the base mip with nearest
filtering when used as a threshold. Mips are included for consistent asset handling,
not for a dither threshold. The decoded noise low/mid spatial power ratio is 0.063.

Source: original seeded procedural fields. No external image, media or font input.
Private masters, decoded previews and quantitative checks are in glassroad-art/w2/masks.
Contact sheets are PR attachments only.

| Asset | Size | Tris | RGBA8 residency including mips |
|---|---|---:|---:|
| fx-frost-edge | 1024 square | 0 | 5.333 MiB |
| fx-melt-flow | 1024 square | 0 | 5.333 MiB |
| fx-melt-spiral | 512 square | 0 | 1.333 MiB |
| fx-crack | 512 square | 0 | 1.333 MiB |
| fx-iris | 512 square | 0 | 1.333 MiB |
| fx-noise | 256 square | 0 | 0.333 MiB |

Family adds 15 MiB; running Wave 1 plus Wave 2 family total is 84.333 MiB including
all palettes and mips. Runtime render targets and engine-owned icons are additional.
KTX strict validation, periodic-flow checks and decoded-file visual review passed.
## Wave 2 item card and effect bubble

Two original procedural GLBs, with no embedded textures, no words and no effect icons.
The existing DtRH effect art remains external and unchanged.

### Integration

Both assets use metres, Y up, forward -Z and a base-centred origin.
Use the card as the spinning pickup carrier; its eye motif is static geometry.
The body is item_surface, with retained COLOR_0; item_edge is a separate material.
The edge starts with a low, constant emissive value. The host owns its colour,
intensity, transitions, pickup spin and reveal. No animation is embedded.

The bubble is 1.1 m across. effect_shell uses alpha blending, clearcoat and
iridescence for a clear centre with view-dependent reflections. It is a single
surface with back-face culling, not a nested volume or a transparent solid ball.
A host Fresnel shader may replace it while retaining the shape and UVs.

The separate mesh node and material effect_icon form a 0.70 m square billboard.
Its local vertices are centred on its object origin, with full 0..1 UVs, front -Z.
The node is placed at (0, 0.55, 0) relative to the asset base. Rotate this node
about its centre to face the camera; keep the shell rooted independently.
Its four corners leave at least 0.055 m clearance inside the shell at any rotation.
The material starts at alpha 0 and has no image. Bind the existing animated DtRH
icon, enable alpha blending and disable billboard depth writing at runtime.
No icon, motes, pop sequence or screen effect is baked into the asset.
The engine owns emission, motion, contrast and Gentle Visuals handling.

### Sources and review

Original work for CCP, developed from the private W1 card and bubble helpers.
The design board's effect section guided a clear centre and thin reflective rim;
W1 cards guided the porcelain, plum, gold and pink vocabulary. No concept pixels
were copied, no third-party geometry was used, and no existing icon was redrawn.
Rights follow the project's existing asset terms. No CC0 claim is made.
Sources, editable .blend masters and review renders stay in the private archive.
Contact sheets belong in the PR description, never the runtime tree.

The card's width and thickness, 1.1 m bubble diameter and 0.70 m icon carrier are
art assumptions. Collision radii and pickup placement remain engine decisions.
Use manifest boundRadiusM for clearance around the asset's base-centred origin.
Final triangle counts, bounds and fresh-import evidence accompany the exports.
Both models add zero texture bytes to the running game budget.

### Measured export budget

| Asset | Triangles | Draws | Texture bytes | Bound radius |
|---|---:|---:|---:|---:|
| item-card | 814 | 2 | 0 | 1.245 m |
| effect-bubble | 962 | 2 | 0 | 1.102 m |

Fresh-imported final meshopt roundtrips pass finite geometry, zero degenerate triangles and material
separation checks. The icon UVs span 0..1 and measured corner clearance is
0.055025 m. Front, three-quarter, rear, both sides and top views were opened for
review. The separate runtime example binds the existing spiral icon only in the
review scene; that image is not embedded in either GLB.
## Rivals: porcelain colourways and head accessories

The accepted EMI kart keeps its geometry, UVs, normals, weights, 13 joints and two material draws.
This replaces models/emi-teacup-kart.glb in place; it is not a second kart mesh.
Seven karts palette entries carry cup/saucer colours, an accessory id and cosmetic personality copy.
No gameplay behaviour is implied by the personality text.

### Tint binding

Two glTF custom scalar vertex attributes contain binary masks:
_CUP_TINT selects the bowl and handle; _SAUCER_TINT selects porcelain on the saucer.
three.js GLTFLoader exposes them as geometry attributes _cup_tint and _saucer_tint.
Gold rims, tea, EMI, and emi_screen are zero in both. Masks never overlap.
The existing base-colour atlas now has alpha reserved for detail protection:
alpha is zero on the four tiny gold decal dots and one on the surrounding porcelain.
Keep kart_surface OPAQUE. This alpha is data, not opacity.

Decode the base-colour texture RGB from sRGB, then apply each tint in linear space.
Let porcelainReferenceLinear = [0.95597335, 0.83076988, 0.87962240].
For each channel, weight = semanticMask * baseColourSample.a.
Multiply base colour by mix([1,1,1], tintLinear / porcelainReferenceLinear, weight).
Apply the cup and saucer multipliers in sequence, then use output alpha 1.
Palette hex values are sRGB target porcelain colours, not linear multipliers.
Read tint alpha before any material opacity multiplication.
The two KTX2 atlases remain 512 square UASTC with full mip chains.
No additional texture, material or draw is introduced by the tint system.

### Attachment binding

The kart adds a non-joint Empty named head_attach beneath EMI_root.
In the bind pose it is at [0, 1.111116, -0.176895] metres with identity world rotation.
Its local transform follows EMI_root automatically; do not add a fourteenth skin joint.
Every accessory exports a root named head_attach at its own attachment origin.
Parent the complete accessory scene to the kart's head_attach and reset its local
position/rotation/scale to zero/identity/one. Do not add the bind-pose position again.
Accessories face -Z and have a narrow fitted saddle touching the forward head roof.
The halo ring has two visible supports. Spiral crown curls have continuous stems.
EMI geometry is unchanged, and the screen and antenna remain visible.
Use SkeletonUtils.clone for each complete kart rig, then attach that instance's accessory.
Find the blank emi_screen by material name; it retains exact 0..1 UVs.

### Budget

| Asset | Tris | Draws | Texture bytes, RGBA8 with mips | Radius m |
|---|---:|---:|---:|---:|
| EMI kart, replacement | 4722 | 2 | 2796200 | 1.274 |
| Bow | 180 | 1 | 0 | 0.205 |
| Cat ears | 216 | 1 | 0 | 0.256 |
| Halo | 376 | 1 | 0 | 0.276 |
| Spiral crown | 350 | 1 | 0 | 0.199 |

Kart texture residency is unchanged from Wave 1: 2.667 MiB conservative RGBA8,
or 699104 bytes for 16-byte 4x4 GPU blocks, both including all mips.
Rivals adds 0 texture bytes to the running wave budget. Seven colourways share one kart.
An equipped accessory adds one draw and its listed triangles per racer.
Accessory radii are measured from their head attachment origin, not the kart origin.

### Sources, validation and assumptions

Original accessory geometry and colourways created for CCP; existing project asset rights apply.
The approved dtrh/race/assets/emi.glb remains the source of EMI's unchanged visible geometry.
The accepted Wave 1 teacup master is reused. No third-party geometry, pixels or concept tracing.
Masters, scripts and contact sheets stay in the private glassroad-art/w2/rivals folder.
All five final GLBs use meshopt. Both embedded KTX2 textures pass strict Khronos validation.
Fresh imports retain the 4722-triangle kart, one skin, 13 joints, screen UVs and zero degenerates.
All 3063 original exported position/normal/UV/joint/weight signatures are preserved.
Tint checks find 1066 masked vertices, zero masked EMI vertices and no overlapping masks.
Each accessory passes the strict under-400-triangle budget and has no texture allocation.
Six packed-asset views, fitted front/three-quarter closeups and seven colourway views were reviewed.
Cup target colours, personality lines and the forward roof attachment position are art choices.
The existing omitted selection clips remain omitted; no new animation ships.
