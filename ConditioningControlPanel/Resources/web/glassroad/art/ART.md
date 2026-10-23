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
## Wave 2 HUD kit

26 original SVGs, 19,396 bytes total. No embedded raster images, text nodes, font
references, filters, scripts, remote references or animation. No baked texture
allocation; each manifest row has texBytes: 0, tris: 0, boundRadiusM: 0.

| Assets | Count | ViewBox | Use |
|---|---:|---|---|
| place-1-white.svg through place-8-white.svg | 8 | 104 x 112 | Main place numeral |
| place-1-pink.svg through place-8-pink.svg | 8 | 104 x 112 | CCP pink place variant |
| item-slot-frame.svg | 1 | 112 x 112 | Round item frame |
| drift-pip-pink.svg, drift-pip-white.svg, drift-pip-gold.svg | 3 | 32 x 32 | Drift stages in that order |
| lap-time-divider.svg | 1 | 192 x 16 | Separator only |
| minimap-frame.svg | 1 | 200 x 152 | Empty map frame |
| flag-start.svg, flag-finish.svg | 2 | 128 x 120 | Original spiral-stamp flags |
| menu-race-frame.svg, menu-online-frame.svg | 2 | 256 x 168 | Icon frames with empty label areas |

### Binding

- Render place numerals at a minimum height of 48 CSS px, with their native aspect
  ratio. All 16 were opened at that exact height on dark and light backgrounds.
  Common numeral contentBox is [12,5,87,105], with one pixel of padding around
  the combined nonzero-alpha bounds [13,6,85,103] at native size.
  The engine supplies any localized ordinal suffix separately.
- Palette: ink #17151c, paper #fff8ef, pink #ff5fa8, gold #ffd36b.
  Paper is intentionally warm. Pink and gold follow the supplied design board.
- The item frame's safe content rectangle is [28,28,56,56].
- The minimap's safe content rectangle is [24,24,152,104].
- Menu labels belong in [27,120,196,25]; optional descriptions fit in
  [98,38,109,61]. The frames contain icons only. Render text in the engine.
- SVG width and height match the viewBox and manifest dimensions. Preserve
  aspect ratio. Frames are not nine-slice assets.
- The three drift fills also carry one, two and three ink ticks. Use host opacity
  for inactive pips; the kit adds no pulse, transition or glow.
- Start uses a two-by-two alternating spiral stamp and a pink forward pennant.
  Finish uses a three-by-two spiral stamp and a small gold seal. Neither uses a
  conventional square checker fill.
- The SVGs work as ordinary images. An inline renderer may override fills if
  needed, but no CSS variables or external stylesheet is required.

### Source and review

Original path geometry authored for this project. No external font outlines,
stock icons, traced concept shapes or third-party artwork were used. Reuse is
under the repository's existing licence and owner-directed asset policy.

The supplied design board was read and opened in an isolated browser at its HUD
section. Its clear centre, corner placement, round item frame, pink/white/gold
drift sequence and two-door menu guided the kit. Localizable text remains outside
the art.

Validation rasterized every SVG using Sharp, checked nonempty alpha and clear
viewBox margins, and scanned for text, fonts, images, filters, scripts and external
references. The contact sheet and separate 48px proof were opened and reviewed.
Private review PNGs are excluded from the runtime tree.

Assumptions: one white and one pink variant per place; three filled drift states
with inactive opacity supplied by the host; empty minimap and menu text areas;
flags are HUD emblems, not track geometry. Placement, focus states, text and motion
remain the engine's responsibility.
## Wave 2 sky textures

These three static galaxies replace the Wave 1 haze at the existing asset IDs and paths.
Each has a defined core, branching dust lanes and small embedded stellar clusters.
The separate star overlays and grayscale spiral masks remain independent engine layers.

| Asset ID | Size | Tris | Texture bytes, RGBA8 with all mips | Packed bytes |
|---|---|---:|---:|---:|
| sky-dawn-galaxy | 2048 x 1024 | 0 | 11,184,812 | 303,074 |
| sky-deep-galaxy | 2048 x 1024 | 0 | 11,184,812 | 298,572 |
| sky-afterglow-galaxy | 2048 x 1024 | 0 | 11,184,812 | 285,153 |
| Total | 3 textures | 0 | 33,554,436 | 886,799 |

The conservative total is 32.000004 MiB, replacing the same Wave 1 allocation.
A 16-byte block transcode needs 2,796,240 bytes per texture including all mips.
There is no additional sky texture allocation in Wave 2.

Runtime contract:
- KTX2, UASTC, sRGB, 12 mip levels.
- Equirectangular mapping, repeat U, clamp V.
- Keep the current IDs and material slots.
- These are stationary environmental backgrounds. Any rotation, brightness or motion belongs to the engine.
- The galaxy core is at the equator; longitudinal orientation can be set by the sky transform.
- Dawn core longitude is -14 degrees, deep +9 degrees, afterglow -5 degrees.
- Galactic plane tilts are +29, -34 and +22 degrees respectively.
- Values are authored from the existing five-colour palettes. No palette entries change.

Source and licence:
- Original procedural artwork, authored for this project.
- Spherical density fields, authored dust lanes and deterministic stellar clusters.
- No external images, downloaded textures, fonts or third-party artwork in the runtime assets.
- No words, media images, baked animated spiral or flashing pattern.

Validation:
- All three KTX2 files pass Khronos validation with warnings treated as errors.
- Packed output was decoded and visually inspected, including panoramas, core closeups,
  a direct Wave 1 comparison and the longitude seam at base level and mip 5.
- Source edge samples match exactly and both source poles are longitude invariant.
- Packed base-level edge differences are at most 3 bytes; packed pole range is zero.
- Mip 3 and mip 5 were checked. Repeat filtering keeps the longitude seam continuous.
  At mip 5 the adjacent border samples differ by up to 20 bytes because they represent
  different angular areas; pole variation is at most 1 byte.
- Whole-image mean absolute packing error is at most 0.033 byte including alpha.
- About 96-98 percent of panorama pixels remain below luma 0.12.
- Evidence views use the actual packed textures with horizontal repeat sampling.
  They are static review views, not an in-game device test.

Private evidence:
- sky-before-after.png: identical camera and exposure for Wave 1 and Wave 2.
- sky-packed-contact.png: each decoded panorama and a core closeup.
- sky-seam-poles.png: decoded longitude seam at full size and mip 5.
- sky-validation.json: edge, pole and mip measurements.
- sky-checks.json: packed SHA256 values, bytes and reconstruction error.

## Wave 2 finish arch rework

This replaces the two W1 finish-portal GLBs, retaining their IDs and base-centred
placement. The countdown ring is unchanged. Galaxy texture replacements are
listed separately by the sky lane.

### Integration

Metres, Y up, forward -Z. The main frame is now about 0.95 m thick radially,
with greater depth, an outer ribbon and five supported crown discs. LOD0 is
17.24 m wide, 12.85 m tall and 1.02 m deep including the front inlay.
The rigid opening remains 14.4 m wide through the first 2.8 m above the road.
Both LODs retain the crown, supports and membrane; use one LOD per instance.

Materials remain portal_glass, portal_trim and spiral_mask, with the added
finish_membrane material on its own named mesh node. Both spiral_mask crown
faces and finish_membrane have clean 0..1 UVs. All other UVs are ordinary mesh UVs.
The membrane fills the opening from the road to the inner arch, with centre
UV (0.5, 0.5), U increasing right and V upward before glTF texture convention.
It is visual geometry, never a collision barrier. Do not use it for road clearance.

Both mask materials start at alpha 0 with no image and glTF alphaMode MASK.
For a soft or fading spiral, explicitly select blended transparency, set alphaTest
to 0 and disable depth writing, or replace the material with the host shader.
Bind the shared grayscale
spiral texture, sample its red channel and set palette colour, alpha and emission
in the engine. Do not treat a decoded single-channel image as coloured RGB.
The host owns rotation, intensity, contrast, transitions and Gentle Visuals.
No coloured or moving spiral is baked into either GLB.

The two nodes cost four material draws: three for the static frame/crown and
one for the membrane. No new texture allocation is required; masks are shared.
The manifest bounds include all crown discs and a small numerical safety pad.

### Sources and review

Original CCP geometry, refined from the private W1 portal source. The main inner
radius and leg clearance remain unchanged. Crown supports overlap the frame and
disc bezels, providing a continuous physical connection. No reference pixels,
third-party meshes, words, numerals or media were added. Rights follow the
project's existing asset terms; no CC0 claim is made.

Native .blend masters reopen successfully. Final meshopt roundtrips preserve names,
materials, dimensions and 0..1 mask UVs, with finite geometry and zero degenerate
triangles. Measured rigid clearance is 14.3999996 m from float storage, or 14.4 m.
Private evidence includes both LODs from front, three-quarter, back, both sides
and top, plus a contextual temporary shader binding. Those sheets were opened
and reviewed. The context preview is not baked into runtime art.

| Asset | Triangles | Draws | Texture bytes | Bound radius |
|---|---:|---:|---:|---:|
| finish-portal-lod0 | 4,695 | 4 | 0 | 12.852 m |
| finish-portal-lod1 | 1,207 | 4 | 0 | 12.838 m |

The crown spacing, disc sizes and frame thickness are art assumptions. Engine
placement, collision decisions and the final membrane shader remain Claude's lane.
Contact sheets stay in the private archive and PR description, not the game tree.

## EMI faces: approved Back Room identity

Replaces the Wave 3 redraw with crops from the approved Back Room EMI atlas.
The slot and wheel source images have identical SHA-256
`6398c8fbd533cf378344d34c814c9c5eedeffd11fb2b5b1f62e19e4e3ceab053`.
Canonical happy keeps the original caret eyes and flat underscore mouth.
No new face glyphs, palettes, moving patterns or screen geometry are authored.
The owner rejected the melty face. Both sad and sleepy now use classic 0_0
(idle0_0), a quiet wide-eyed pause. No runtime state uses melt.

The same 1024 x 1024 sRGB UASTC atlas has sixteen 256px cells and 11 mips.
Budget stays 5,592,404 bytes (5.333 MiB) as RGBA8 with mips; extra allocation is 0.
Original 152 x 137 crops use one uniform 224/152 scale, centered on the original
RGB (62,46,92) background with bicubic reconstruction and a clear 16px border.
The original face aspect is preserved. The blank cell samples that background.

| State ID | Approved expression | Source frame |
|---|---|---|
| neutral | idle0_0 | 3 |
| happy | happy | 0 |
| grin | cute | 1 |
| wink | cute | 1 |
| determined | squeeze | 2 |
| drift-focus | squeeze | 2 |
| dizzy | dizzy | 6 |
| sleepy | idle0_0 | 3 |
| frozen | idle0_0 | 3 |
| heart-eyes | hearts | 7 |
| x-eyes | squeeze | 2 |
| smug | cute | 1 |
| sad | idle0_0 | 3 |
| sparkle | stars | 5 |
| cheer | happy | 0 |
| blank | sampled background | none |

These are state aliases, not claims that the source includes a literal wink,
frozen face or cross-shaped eyes. Existing contextual motion and head marks
carry those distinctions. The runtime alias `heart` resolves to `heart-eyes`,
cell (1,2), retaining all sixteen manifest IDs and their existing positions.

Bind to `emi_screen` without changing its full 0..1 UVs. Grid origin is top left;
glTF v=0 addresses the top. Use flipY=false for PNG review textures. KTX2 uses
the existing atlas path and cell order. Clamp wrapping, retain the documented
1px UV inset and max safe selected mip 2. Engine currently ignores the latter
two metadata hints; clear cell borders keep expression edges inside their cells.

Warm-load the atlas before judging identity: the current engine still draws
its own procedural fallback if the atlas is missing. That fallback and runtime
loading are engine work. This asset update does not edit them.

Every master cell exactly matches its declared resampled source reference.
Packed cells were decoded, compared numerically and reviewed at large and
64px display sizes. Worst cell mean absolute channel error is 0.149/255.
Strict Khronos KTX validation passes. Masters, scripts, source crops,
comparison and audit evidence stay private; project asset terms apply.

### Running residency after the classic face replacement

| Residency | Desktop MiB | Phone MiB |
|---|---:|---:|
| Face atlas, replacing existing allocation | 5.333332 | 5.333332 |
| This family added allocation | 0 | 0 |
| W4 additions, unchanged | 87.083347 | 9.770847 |
| Persistent total including W3/W4 art families | 196.416664 | 119.104164 |
| With selected menu backdrop | 205.416664 | 127.014320 |

These are conservative RGBA8 mip delivery totals, not measured live-engine memory.
Only the active sky palette nebula is resident. Render targets, decode buffers
and 63,154 bytes of optional vertex sidecars are excluded. The 17-icon allowance
is counted once. This face replacement adds no geometry or texture allocation.


## Wave 3 input and option glyphs

63 original SVGs under ui/glyphs, 35,776 bytes total. Each is 56 x 56 and
was reviewed at an actual rendered height of 28 px on dark and paper backgrounds.
Every row has tris 0, texBytes 0 and boundRadiusM 0.

| Group | Count | IDs |
|---|---:|---|
| Xbox face and shoulder controls | 10 | xbox-a, b, x, y, lb, rb, lt, rt, start, view |
| PlayStation face and shoulder controls | 10 | ps-cross, circle, square, triangle, l1, r1, l2, r2, options, create |
| Generic numbered pad buttons | 12 | pad-1 through pad-12 |
| Shared sticks and d-pad | 5 | pad-stick-left, right, press-left, press-right; pad-dpad |
| Keyboard | 13 | key-w, a, s, d, e, up, right, down, left, space, shift, esc, enter |
| Mouse | 4 | mouse-body, left, right, wheel |
| Touch | 3 | touch-tap, hold, drag |
| Options | 6 | option-gentle-visuals, steering-assist, music, effects, controls, back |

Binding:
- Preserve the square aspect ratio. Use a minimum 28 CSS px height.
- Shared stick/d-pad glyphs work with all three pad families.
- Numbered generic labels are human-facing 1 to 12. The engine owns the mapping to
  device button indices; these are not zero-based Gamepad API indices.
- Stick-click glyphs use a downward pink mark. Touch hold uses a clock,
  tap uses three contact marks, and drag uses a horizontal two-way arrow.
- Space uses a spacebar bracket, Shift an upward key arrow, Enter a return arrow,
  Start/Options menu bars and View overlapping panels.
- Device detection, action mapping, focused/disabled states and motion belong to the engine.
- Accessible names and every localized action label remain external to the SVG.
- Ink #17151c, paper #fff8ef and pink #ff5fa8 match the Wave 2 HUD.
- Functional button letters and digits are original hand-authored paths.
  There are no font outlines, text nodes, linked fonts, images, filters or scripts.

Original design for this project. Standard functional identifiers describe the
controls; no platform logos, stock icon files or third-party artwork are included.
The design board HUD, menu, pause and results sections were read and opened in a
private browser. Sources and review images remain in the private art archive.

Validation:
- All 63 exported SVGs rasterize and contain visible pixels.
- Every source viewBox has a clear outermost pixel border.
- SHA256, file bytes and measured alpha bounds are recorded in private checks.json.
- The final contact and actual 28px proof were opened and visually reviewed.
- Shoulder labels were centred within their key shapes; touch marks have contrast
  on both the dark and paper proof backgrounds.
- Runtime SVGs and manifest rows are compact; no source or review file ships.

No baked texture allocation is added. Host SVG raster caches are implementation
dependent and are not included in texBytes. The shared pre-W3 estimate remains
about 90 MiB including the existing effect icons; these glyphs add 0 MiB.
## Wave 3 paper cards and result stamps

9 original text-free SVGs under ui/cards, 9,882 bytes total.
Every row has tris 0, texBytes 0 and boundRadiusM 0.
Frames extend the Wave 2 ink, warm paper and pink-tab vocabulary.

| Asset | ViewBox | Purpose |
|---|---|---|
| card-pause | 480 x 560 | Pause actions and context |
| card-options | 640 x 640 | Options and input preferences |
| card-results | 880 x 640 | Race result content |
| card-controls | 760 x 620 | Control mapping content |
| results-row | 600 x 80 | Place stamp, name and time |
| stamp-new-best | 128 x 128 | Wordless personal-best mark |
| podium-gold | 128 x 144 | First-place spiral medal |
| podium-silver | 128 x 144 | Second-place spiral medal |
| podium-bronze | 128 x 144 | Third-place spiral medal |

Binding:
- Preserve aspect ratios. Frames are complete panels, not nine-slice borders.
- Place all text, buttons, media and interaction in engine layers.
- Card title, body and footer rectangles are named safeTitleBox, safeContentBox
  and safeFooterBox in the manifest. Coordinates use source viewBox pixels.
- The results row has placeStampBox [27,23,34,34], safeNameBox [89,19,277,42]
  and safeTimeBox [411,19,153,42]. The place slot is empty.
- Scale every rectangle by the same factor as its SVG. Do not infer bounds from
  the visible border or crop the panel to a different aspect ratio.
- Pink tabs, corner stamps and divider rules remain outside all declared areas.
- The personal-best stamp uses an upward arrow, star and laurel, with no letters.
  The engine supplies the localized meaning and any accessible name.
- Podium rank is conveyed by the gold/silver/bronze palette and manifest rank field.
  The engine supplies accessible rank labels. No numbers are baked into the badges.
- Render standalone stamps at a minimum height of 48 CSS px.
- Motion, transitions, focus, disabled states and any new-best animation are external.

Ink #17151c, paper #fff8ef and pink #ff5fa8 match the Wave 2 HUD.
Medal fills are gold #ffd36b, silver #ced4df and bronze #c89471.
These are local SVG fills; the shared environment palettes are unchanged.

Original geometry and symbols authored for this project. No fonts, stock icons,
traced concept pixels, third-party artwork or media images are embedded.
The design board menu, pause and results layouts guided the clear content areas.
Sources and review PNGs remain private; contact images do not ship.

Validation rasterized every final SVG and checked a clear outermost pixel border.
Every pixel inside every declared text/stamp rectangle matches the exact opaque
paper fill, so no border, tab, divider or accent crosses a safe area.
SHA256 values and per-area results are in private checks.json.
The contact sheet and actual 48px stamp proof were opened and reviewed.

Assumptions: fixed panel aspect ratios, opaque paper, shared result-row geometry,
and engine-owned localized content. No media frame or credit treatment is supplied
by these cards. No baked texture memory is added; host SVG raster caches are
implementation dependent. Shared pre-W3 estimate remains about 90 MiB including
existing effect icons, and this family adds 0 MiB.
## Wave 3 threshold landmarks

All models use metres, Y up and -Z forward, with base-centre roots and measured conservative bounds. New geometry is original. The cabinet row derives only from the approved Wave 1 slot cabinet.

| Asset stem | LOD0 tris | LOD1 tris | Texture memory |
| --- | ---: | ---: | ---: |
| threshold-start-gantry | 2252 | 668 | 0 |
| threshold-slot-row | 5410 | 1402 | 0 |
| threshold-bubble-arch-a | 5272 | 1276 | 0 |
| threshold-bubble-arch-b | 4824 | 1180 | 0 |
| threshold-bubble-arch-c | 5720 | 1372 | 0 |
| threshold-chevron-stalk | 480 | 336 | 0 |
| threshold-checkpoint-ring | 1144 | 620 | 0 |

- The start gantry has a 14.4 m minimum opening and three blank displays. Mesh nodes start_screen_left, start_screen_centre and start_screen_right use screen; the matching start_credit_* nodes use credit. Each has full 0..1 UVs. Clone shared materials per display before assigning different feeds.
- countdown_mount_left, countdown_mount_centre and countdown_mount_right are attachment empties at x = -4.3, 0 and 4.3 m, y = 4.1 m, z = -0.15 m. Place the approved countdown-ring model at each anchor using its existing base origin. Rings are separate assets and are not duplicated into the gantry file.
- Six original cabinets share one 12.3 m glass plinth. slot_lamps stays a separate material for host emission. screen, credit and emi_screen remain blank. LOD1 removes small original detail islands before material-preserving decimation; the silhouette is retained. This row repeats a shared feed across the cabinet screens.
- Bubble arches use 21, 19 and 23 clear shells on a thin gold support. Their nominal low passage widths are 14.4, 15.1 and 14.6 m. The transparent bubble_glass material uses iridescence; there is no baked colour motion.
- The chevron points along local +X. Rotate or mirror placement for track direction. Its geometric symbol has no text or numerals.
- The checkpoint has a thin glass rim, gold inlay and 14.4 m minimum spacing between its feet.
- Material colours and lamp emission are controlled by the host. There is no flash, strobe, animation or media image in these exports.
- Contact sheets show neutral views and clearly labelled host examples. Review-only countdown rings and shader mask bindings are excluded from the landmark runtime files.

Validation covers reopened native masters, fresh GLB imports, triangle caps, zero-area faces, finite coordinates, all screen/credit UVs and named mounts. All models add zero texture memory.
## Wave 3 far horizon atlas

far-landmarks.ktx2 is a 2048 x 1024 RGBA atlas with eight static silhouettes.
The source is neutral grayscale in RGB with straight alpha, soft upper-left light
and reduced contrast for distance. The engine applies the track palette.

| Asset | Runtime tris | Texture bytes, RGBA8 with all mips | Packed bytes |
|---|---:|---:|---:|
| far-landmarks | 0 | 11,184,812 | 405,800 |

The atlas adds 10.666668 MiB to the running texture budget.
A 16-byte block GPU transcode needs 2,796,240 bytes including all mips.
Against the approximately 90 MiB pre-W3 total, this family alone brings usage to
100.667 MiB. Other Wave 3 allocations must be added by the combined manifest.

| Cell key | Column, row | Source |
|---|---|---|
| king | 0, 0 | Approved king.glb, including its crown |
| pocketWatch | 1, 0 | Wave 1 pocket-watch.glb |
| cardTower | 2, 0 | Twelve transformed instances of Wave 1 card-orbit.glb |
| slotTower | 3, 0 | Three transformed instances of Wave 1 slot-cabinet-lod0.glb |
| teacupMoon | 0, 1 | Accepted kart's rigid cup, saucer, handle and tea geometry |
| spiralLighthouse | 1, 1 | Original column, architectural helix and lantern cage |
| bubbleCloud | 2, 1 | Seven transformed instances of Wave 1 bubble.glb |
| floatingStaircase | 3, 1 | Fourteen original floating treads |

Runtime contract:
- KTX2 UASTC, sRGB, straight alpha, 12 supplied mip levels.
- Four columns and two rows; cells are 512 square with origin at the top left.
- Each cell reserves at least 32 transparent pixels on every edge.
- Sample the selected cell only. Clamp atlas sampling and retain the gutters.
- Clamp the sampled mip level to maxSafeLod 3, including trilinear upper levels.
  Coarser atlas mips begin mixing content into the gutters.
- Decode RGB as sRGB and multiply the desired palette tint in linear space.
  Keep the authored alpha for haze and the translucent bubble centres.
- Intended placement is 800 to 2000 metres away. Billboard dimensions, facing,
  placement, fog and any transitions belong to the engine.
- A cell's contents retain their natural aspect ratio inside the square.
  Do not stretch its visible alpha bounds to fill a square.
- The architectural lighthouse helix is a static silhouette, not a moving spiral
  texture. No animation, flash, strobe, text or media imagery is baked in.

Sources and rights:
- Original scene arrangements and two original meshes created for this project.
- Existing approved project assets retain their existing rights.
- The exact approved piecebypiece/assets/pieces/king.glb supplied all 12,364 king
  and crown triangles. No king vertices were decimated, regenerated or remodelled.
  Its source SHA256 was checked before and after production and is unchanged.
- Watch, cards, cabinet and bubbles use unchanged source triangles with instance
  transforms. The cup selects the existing rigid kart_root triangles, excluding EMI.
- Source meshes are projected by a private CPU rasterizer with interpolated normals
  and a depth buffer. No image generator or downloaded art was used.
- Geometry sources, new mesh data, scripts, masters and review PNGs stay private.

Validation:
- Strict Khronos KTX2 validation passes with warnings treated as errors.
- The decoded base-level alpha is zero throughout every 32px gutter.
- At decoded mip 3, gutter alpha is at most 1/255 in every cell.
- Visible RGB mean packing error is 0.311 byte or less.
- The actual decoded atlas contact and actual decoded mip-3 cells were opened
  and visually reviewed. The latter are displayed at native 64px cell size.
- Private checks.json records hashes, codec errors and every cell's gutter result.
  source-checks.json records content bounds and triangle counts used for projection.

Assumptions: the teacup moon is an oversized suspended teacup, the cabinet tower
has three tiers, the card tower has three tiers and the bubble cloud has seven
bubbles. These are distant billboards, not additional runtime GLB props.
## Wave 3 menu and results stage model

menu-results-stage.glb is an original 12576-triangle glass balcony with 11 material draws and zero added texture memory. Dimensions are 10.03 x 1.44445 x 6.83 m. Conservative root boundRadiusM is 5.02.

- Metres, Y up, -Z forward; root is the horizontal centre at the underside minimum.
- balcony_frame is static. The rear glass balustrade leaves the camera-facing edge open.
- podium_high, podium_mid and podium_low are separate mesh nodes with base-level origins. Their raised heights are 1.0, 0.65 and 0.4 m. Their glTF local bases are [0, 0.41945, 0.55], [-2.35, 0.41945, 0.55] and [2.35, 0.41945, 0.55]. Lower each along local Y by its own height, then hide it for the menu. The engine owns rise timing, easing and visibility.
- Each podium mesh has a spiral_mask material section with a full 0..1 UV disc. It ships with alpha 0 and no texture. Bind a shared grayscale spiral mask and palette in the host shader; set the desired blending there. No moving spiral is baked.
- podium_surface uses vertex colours for gold, silver and bronze. stage_glass and stage_trim are shared. No numbers or words are modelled.
- menu_park is a placement anchor at [0, 0.41945, -1.75]. It fits the approved teacup kart in the open foreground. The kart is a separate runtime asset.
- Review context shows the approved kart and an existing grayscale mask bound by the preview only. Neither is duplicated in this GLB.

Fresh-import checks verify zero degenerate triangles, matching native-master counts, full mask UVs, transparent default inlays and each podium pivot exactly on its base. The stage is below the 15000-triangle cap. There is no added texture allocation for the model.

## Wave 3 painted key art

ui/key-art.webp is 2048 x 1152; ui/key-art-phone.webp is 1080 x 1920.
Both are static, text-free sRGB WebP backdrops. The wide scene follows the supplied
key-art mood reference and the accepted EMI kart. The portrait adaptation extends
that composition vertically so the whole kart remains visible.

Created with the built-in image generator using the supplied concept as mood and
the actual W2 kart contact sheet as identity reference. Private prompts, generated
masters and packing checks remain under glassroad-art/w3/stage. Existing project
references and the resulting artwork follow the owner's project asset policy.
No third-party asset, font, real-person image or downloaded texture was added.

Use as a DOM backdrop without mipmaps. Decoded RGBA8 memory is 9 MiB for wide and
7.910156 MiB for phone. These are mutually exclusive viewport variants in the
menu-backdrop residency group. Load only the current variant; release it when
leaving that screen. Do not preload both or upload them as mipmapped race textures.
The running budget reports these presentation images separately from persistent
race atlases, since the brief's approximately 25 MiB estimate omits the stills.

Both final WebP files were reopened at their exact dimensions and reviewed.
The separate desktop launcher tile is prepared at the existing race.png size,
1024 x 572, and does not enter this art family. Composition and phone reframing
are art assumptions. No game or launcher code is changed.

Generated source images are 1672 x 941 and 940 x 1672; export resizing produces the requested wide and phone dimensions.
## Wave 3 section road bands and rim

Three linear R8 ETC1S fields extend the existing road material. Sample .r.
road-bands-deep and road-bands-afterglow are 512 square; road-rim-mask is
1024 x 128. Total conservative RGBA8 residency with mips is 3.333 MiB.

Keep the Wave 1 coordinate contract: U across 12 metres, V along 16 metres.
Deep has four tighter, squared crests across U with a gentle periodic bend.
Afterglow has two broader swells; its warmth comes from the host palette.
Both band masks repeat in U and V, with exact matching source-edge samples.
The rim is zero inward of the last 0.6 m at each road edge, rising smoothly to
one at U=0 and U=1. Clamp U for the finite road width and repeat V.
For road widths other than 12 m, remap the edge distance in the host to keep the
physical 0.6 m extent. White means full highlight or band strength.

No colour, temporal motion, flash or strobe is baked into these masks. The host
owns palette, band scrolling, brightness and Gentle Visuals. Keep its contrast
limiter active; these masks supply shape only. The new crests remain broad.

Original procedural fields for CCP. No third-party images or copied patterns.
Private scripts, masters and static coloured preview stay in glassroad-art/w3/road.
All final KTX2 files pass strict validation and were decoded for seam and visual
review. Pattern frequencies and 512px band resolution are art choices; they leave
additional room under the brief's approximate texture allowance.
## Wave 4 effect flipbooks

Twelve original grayscale effects, each with 64 frames in an 8 x 8 atlas. Every
plain ID is tier desktop at 1024 square with 128px cells; its -phone variant is
tier phone at 256 square with 32px cells. Select one tier per effect. Both tiers
use ETC1S, linear neutral-white RGB and straight alpha. No colour is baked.

| Effect stem after fx-flip- | FPS | Playback |
|---|---:|---|
| impact-burst | 30 | one-shot |
| shockwave-ring | 30 | one-shot |
| spiral-swirl | 30 | loop |
| melt-drip-run | 30 | loop |
| frost-burst | 30 | one-shot |
| heart-pop | 30 | one-shot |
| card-confetti | 30 | one-shot |
| dust-puff | 24 | one-shot |
| dizzy-stars-orbit | 30 | loop |
| sweat-drop-pair | 30 | one-shot |
| boost-flame-sparkle | 30 | loop |
| smoke-curl | 24 | one-shot |

The 30 fps clips last 64/30 seconds; dust and smoke last 64/24 seconds. Eight
one-shots start and finish fully transparent. Four loops have periodic motion,
with frame 63 blending back to frame 0. First and last loop frames intentionally
show consecutive times instead of duplicating the same endpoint.

Binding:
- Frame index is row-major: column = index modulo 8, row = floor(index/8), top-left
  origin. Retain the full cell including its gutter. Apply the half-texel inset.
- Blend adjacent frame samples linearly for smooth motion. For one-shots, clamp
  the next frame at 63 and stop after the lifetime; only the four loops wrap.
- Desktop has a 12px transparent gutter, 11 supplied mips and maxSafeLod 1.
  Phone has a 3px gutter, 9 supplied mips and maxSafeLod 0. Clamp both samples'
  mip selection, including any trilinear upper mip, to the stated safe level.
- RGB remains neutral white even in transparent texels. Use alpha as coverage;
  tint in the host and premultiply once if using premultiplied blending.
- The host controls size, placement, opacity, speed and Gentle Visuals. These
  are small local effects, with soft entrances/exits and no screen-filling flash.
- The phone presentation widens orbit stars, falling drops, sweat drops and cards
  before downsampling. Card symbols simplify at 32px; their tumbling rectangles
  carry the identity. Do not load desktop and phone variants together.
- These temporal effects are explicitly requested by the Wave 4 brief. Road and
  environment patterns retain their separate shader-motion contracts.

| Tier | Assets | Conservative RGBA8 bytes, all mips | 16-byte block bytes | MiB RGBA8 |
|---|---:|---:|---:|---:|
| desktop | 12 | 67,108,848 | 16,777,536 | 64.000 |
| phone | 12 | 4,194,288 | 1,048,896 | 4.000 |

Each desktop atlas accounts for 5,592,404 RGBA8 bytes; each phone atlas accounts
for 349,524. Download bytes are separate from GPU residency. All 24 rows have
zero triangles and boundRadiusM 0. The phone family brings the 109.333317 MiB
Wave 3 persistent baseline, including 17 engine icons, to 113.333302 MiB before
other Wave 4 families. A concurrent phone menu backdrop adds 7.910156 MiB, for 121.243458 MiB.

Validation:
- All 24 final KTX2 files pass strict validation and were decoded for review.
- Every decoded cell has zero alpha throughout its declared base gutter.
  Desktop mip 1 also has zero gutter alpha; phone sampling stays at mip 0.
- All eight one-shots retain zero-alpha first and last frames in both tiers.
- Each loop has an exact periodic source endpoint; decoded wrap-frame changes
  are below the largest ordinary adjacent-frame change for that effect.
- White RGB stays exactly neutral after decoding. Private checks record all
  hashes, alpha packing error, gutter maxima and temporal differences.
- The contact includes representative frames of every effect. A native 32px
  phone proof was opened and reviewed. The ten-second timing video uses final
  decoded cells, linear frame blending and repeated one-shots for review only.

Source: original deterministic analytic fields and vector paths authored for
this project, without external images, fonts or media in runtime textures.
Editable Python sources, full masters, decoded evidence and the timing video
stay in the private art archive. No Blender or image-generation service is used.
Assumptions: restrained 30 fps loops, slower 24 fps dust/smoke and host-controlled
particle quantities. Device integration and normal-speed human playback remain
part of engine review.
## Wave 4 trails

Five original static scalar fields: sparkle ribbon, dotted arc, boost streak,
slipstream wisp and faint drift scorch. No colour or animation is baked.
Sample .r as linear data. The host supplies tint, opacity, placement, lifetime
and Gentle Visuals. Scorch is deliberately low-amplitude.

Desktop IDs are plain; matching phone IDs end in -phone and declare tier phone.
Desktop files are 512 x 128, phone files 256 x 64. Load only the selected tier.
Conservative RGBA8 residency including mips: desktop 1.667 MiB, phone 0.417 MiB.
No triangle allocation. ETC1S KTX2 files pass strict validation.

Repeat U along the trail, clamp V across it. U edge samples match in the sources;
codec edge deltas are recorded in private checks and inspected in packed output.
The side borders are black so opacity or emission falls away before the mesh edge.
Use the same ribbon coordinate mapping for both tiers.

All artwork is deterministic, original mathematical fields and glint motifs.
No downloaded imagery, external fonts in runtime or third-party assets.
Sources, exact packing commands, decoded contacts and checks stay private.
## Wave 4 materials and immutable model shading

Eight original tiling detail maps ship at desktop 512 and phone 256. Use one tier at a time.
Normals are linear UASTC RGB XYZ, OpenGL +Y. Scalars are linear ETC1S and read R only.
Repeat both axes. All mips are supplied; do not interpret any detail texture as sRGB.
Both variants share the same fields. Plain IDs use `tier: desktop`; suffixed IDs use `tier: phone`.
Host shaders choose palette, intensity, glass treatment and motion. No animation, words or media are baked.

| Texture ID suffix after `detail-` | Meaning | Suggested tile size m |
|---|---|---|
| porcelain-roughness | Absolute subtle glaze roughness | 0.35 x 0.35 |
| porcelain-crazing-normal | Fine ceramic crazing, suggested normal strength 0.2 | 0.35 x 0.35 |
| gold-wear | White means more wear; mix into roughness | 0.22 x 0.22 |
| road-ripple-normal | Fine glass ripples, suggested normal strength 0.25 | 4 x 4 |
| road-sparkle-mask | Sparse static sparkle coverage; host controls tint and behavior | 3 x 3 |
| road-edge-frost | Frost coverage; multiply by host edge falloff | 1.5 x 1.5 |
| rail-normal | Cast glass waviness, suggested normal strength 0.2 | 0.5 x 2 |
| rail-thickness | Relative glass thickness, not metres | 0.5 x 2 |

Every desktop map: 1,398,100 texBytes. Every phone map: 349,524 texBytes.
Desktop total: 11,184,800 bytes (10.667 MiB). Phone total: 2,796,192 bytes (2.667 MiB).
These are conservative RGBA8 residency including all mips. `texBytesBlock16` is the smaller block-format bound.
Review images are excluded from texture residency. Do not load desktop and phone variants together.

### Vertex sidecar contract

Every approved GLB stays byte-for-byte unchanged. This family ships no GLBs.
Each `models/<model-id>.vtx.bin` contains exactly two interleaved unsigned bytes per decoded vertex:
AO first, curvature second. No header, padding, index stream, UVs or material changes.
AO 255 means unoccluded and 0 means fully occluded. Curvature 128 is neutral, lower is concave, higher is convex.
From the normalized attribute value, signed curvature is `clamp((value * 255 - 128) / 127, -1, 1)`.
Each manifest row contains the exact compressed source file SHA256, mesh/primitive indices, vertex counts and byte offsets.

Loader sequence:
1. Read the complete source GLB and require its SHA256 to equal `sourceSha256`.
2. Decode its original primitives. Match `meshIndex` and `primitiveIndex`, not object name or traversal order.
3. Require every decoded POSITION accessor count to match the listed `vertexCount`.
4. Read each primitive's `byteLength` bytes at `byteOffset`, retaining its decoded accessor vertex order.
5. Add a separate normalized two-component Uint8 attribute. Preserve existing attributes, indices and materials.
6. If the source hash, primitive mapping, counts or sidecar byte length disagree, skip the sidecar entirely.

The source order was read through glTF-Transform with MeshoptDecoder; it is not Blender's imported vertex order.
Do not weld, optimize, decimate, reorder or merge primitives between decoding and attribute attachment.
Each sidecar is independently tied to its existing LOD. No separate phone sidecar is needed.
`perVertexBytes` and `vertexBytes` both record file bytes; `gpuAttributeBytes` records logical GPU attribute bytes.
They are separate accounting fields for the same data, not values to add together. All sidecar `texBytes` and added `tris` are zero.

| Source model | Decoded vertices | Sidecar / GPU attribute bytes |
|---|---:|---:|
| chess-bishop-lod0 | 4653 | 9306 |
| chess-bishop-lod1 | 1545 | 3090 |
| chess-king-lod0 | 3020 | 6040 |
| chess-king-lod1 | 782 | 1564 |
| chess-knight-lod0 | 2903 | 5806 |
| chess-knight-lod1 | 728 | 1456 |
| chess-pawn-lod0 | 2546 | 5092 |
| chess-pawn-lod1 | 747 | 1494 |
| chess-queen-lod0 | 2971 | 5942 |
| chess-queen-lod1 | 759 | 1518 |
| chess-rook-lod0 | 3085 | 6170 |
| chess-rook-lod1 | 799 | 1598 |
| pocket-watch | 752 | 1504 |
| slot-cabinet-lod0 | 4369 | 8738 |
| slot-cabinet-lod1 | 1542 | 3084 |
| spiral-disc | 376 | 752 |

Sidecar total: 63,154 file bytes, 63,154 logical GPU attribute bytes, zero texture bytes.

AO uses 64 cosine hemisphere rays, a 9.5%-of-bounds-diagonal reach and distance falloff.
Curvature uses a signed local graph measure with mild smoothing. Coincident positions are grouped only for calculation.
The approved vertices, topology, normals, UVs, colours and material assignments are never rewritten.
Review shading uses AO strength 0.5 and curvature strength 0.1; host integration controls final strength.

### Density and UV audit

All six chess LOD pairs, the pocket watch and four cards have no UV0.
The cabinet repeats UV islands and contains degenerate UV triangles; a unique atlas cannot be baked without changing it.
The disc faces overlap intentionally and its rim has degenerate UVs. No UV atlases were generated.
The four cards are excluded: broad faces have boundary-only triangulation, unsuitable for clean interior vertex shading.
The watch enamel, disc shader faces, and screen/credit/emi_screen primitives remain AO255 and curvature128.
Their broad or dynamic surfaces stay neutral while the surrounding model receives the sidecar.

### Validation and evidence

All 16 KTX2 variants pass glTF BasisU validation, with full mip chains and correct linear interpretation.
All source-image tile boundaries are exactly equal. Lossy scalar block encoding introduces small localized edge differences.
Decoded normals preserve RGB XYZ; worst normal error is 2.01 degrees and mean error is at most 0.045 degrees.
Fresh glTF-Transform reads independently verify all 16 source hashes, primitive mappings, vertex counts and contiguous byte ranges.
Private paired renders reproject the sidecars onto the exact decoded positions, normals and colours without remeshing.
All 32 front/three-quarter pairs show changed pixels, while all 16 source hashes remain unchanged.
Material fixture previews use actual KTX2 decoded level 0. Thickness is previewed as roughness variation only.
Contact sheets cover both chess LODs, the other props, all desktop/phone materials and 2x2 tiling.
Native review fixtures, PNG masters, extraction/bake scripts and validation evidence remain private.
## Wave 4 colossal set pieces

Five original sculpted structures, each with LOD0 and LOD1. All dimensions are actual metres, GLB Y up, forward -Z, with a base-centre origin. Shared by desktop and phone. No embedded textures; this family adds 0 MiB to either texture tier.

| Asset | LOD0 tris | LOD1 tris | Span m | Height m | Clearance m | Draws |
|---|---:|---:|---:|---:|---:|---:|
| card-arch-bridge | 4260 | 1236 | 135.751 | 173.923 | 32 | 2 |
| pendulum-gantry | 8064 | 2288 | 154.000 | 144.500 | 34 | 4 |
| spiral-ribbon-arch | 5112 | 1128 | 184.393 | 120.895 | 75 | 2 |
| teacup-tower | 10032 | 2440 | 69.990 | 132.650 | 0 | 2 |
| floating-staircase | 8316 | 2690 | 104.598 | 137.745 | 0 | 2 |

### Placement and materials

- `spanM` is the outer X extent at rest, including plinths and trim.
- `clearanceM` is the conservative free height within a centred 14.4 m-wide road corridor along Z. It is not the overall asset height.
- Card bridge, pendulum gantry and ribbon arch may straddle the road at their exported scale.
- Cup tower and staircase are beside-road pieces; their clearance is 0. Use `boundRadiusM` when placing them away from the driving corridor.
- Scaling requires scaling span, clearance and radius together. The supplied sizes range from about 121 to 174 m high.
- `colossus_surface` uses neutral grayscale vertex colour, clearcoat and soft roughness for palette tinting. Ribbon and stair variants include restrained transmission.
- `colossus_glow` is reserved for rim lines and seams. Its quiet default emission can be driven by the engine. No baked moving pattern, flash, lettering or media.
- Static models use two mesh/material draws. The gantry uses four so the watch and its emissive seams can move independently of the frame.

### Pendulum contract

- Rotate node `pivot` around its local GLB Z axis, limited to +/-25 degrees. The engine supplies timing; the asset contains no animation clip.
- Pivot is at [0,128,0] m in the exported gantry. Its two children contain the entire watch, crown, hands and rigid connecting link.
- The link meets the axle and crown loop. The crown stem overlaps the watch case. Fixed piers and crossbar remain outside the pivot.
- `boundRadiusM` includes the complete allowed swing envelope, calculated analytically per vertex with an outward margin. Do not replace it with a rest-pose radius.
- Clearance remains conservative through the allowed swing. A 14.4 m-wide corridor is checked after import.

### Sources and validation

Original project artwork authored procedurally for this family. The approved W1 card and pocket-watch look informed the rounded rim and original orbit motif. No external geometry, images or texture sources were used. Existing project GLBs, including chess assets, were not modified.

LOD0 stays below 12000 triangles and LOD1 below 3000. Native masters and fresh GLB imports were checked for finite coordinates, degenerate triangles, dimensions, material names, hierarchy, bounds and corridor clearance. Six fixed views plus a sky-and-road context were captured privately. Meshopt packing and final decoded checks are recorded in the private delivery evidence.

Masters, construction source, measured checks and all review images remain private. Only runtime GLBs, manifest rows and this documentation belong in the public art tree.
## Wave 4 sky depth

Three original nebula layers sit between the existing galaxy and star layers.
They are soft static equirectangular colour and straight-alpha fields, with
billowing density, translucent dust and broad veils. No star sprites, motion,
flashes or spiral animation are baked. The existing galaxy is not replaced.

Desktop nebulae are 2048 x 1024; phone variants are 1024 x 512 and use -phone
IDs with tier phone. All are sRGB UASTC KTX2 with mips. Decode sRGB before
compositing. Repeat U and clamp V. Source longitudes match and poles are constant.
The host owns sky layer scale, relative rotation, opacity and Gentle Visuals.

Only ONE active palette nebula is resident on ANY tier. Desktop active allocation
is 10.667 MiB RGBA8 including mips; phone active allocation is 2.667 MiB.
All three palette files are supplied, but do not preload all three.
On a palette change, release the old nebula before loading the new one.
Do not hold two palette nebulae concurrently.

shooting-star is a grayscale ETC1S streak, desktop 256 x 64 and phone 128 x 32.
Sample .r, clamp both axes, orient its bright head toward the direction of travel.
Its opacity, lifetime and movement come from the engine. It adds 0.083 MiB on
desktop and 0.021 MiB on phone. No model triangles.

UASTC avoids visible colour banding in translucent nebula gradients. Every KTX2
passes strict validation, is decoded, and is reviewed alone and over the existing
galaxy. Private checks record seams, poles, error and exact file hashes.
Artwork is original deterministic spherical density fields using project palettes.
No third-party imagery or runtime text. Sources, masters and contacts stay private.
## Wave 4 reaction sprites

Ten original wordless marks extend the existing paper, dark ink, pink and gold UI:
anger puff, sweat drop, question curl, heart, sleepy swirl, sparkle, dizzy spiral,
musical note, surprise burst and oops swirl. No letters or baked labels.

Files are SVG paths and simple shapes in a 64-square viewBox, readable at 32px.
Desktop and phone share the same files. There are zero baked texture bytes or tris.
No fonts, images, external references, filters or scripts appear in runtime SVGs.

The host owns placement above the kart, lifetime, scale, entry/exit and motion
settings. Preserve the viewBox aspect ratio. The curl symbols are original marks,
not literal punctuation or text. Paper strokes preserve contrast over the sky.

Validation rasterizes every file at native 32px, checks clear exterior borders,
and reviews the contact on both paper and dark backgrounds. Editable path sources,
raster proofs and hashes remain private. No third-party assets were added.

### Tier residency and running budget

Load only the selected texture tier: plain IDs on desktop, matching -phone IDs on phone.
The tiers share GLBs, SVGs and per-vertex sidecars. Only the active sky palette nebula
is resident on either tier; release the previous palette layer before loading another.

| Tier | This family MiB | W4 cumulative MiB | Running persistent MiB | With one menu backdrop MiB |
|---|---:|---:|---:|---:|
| desktop | 63.999985 | 63.999985 | 173.333302 | 182.333302 |
| phone | 3.999985 | 3.999985 | 113.333302 | 121.243458 |
| desktop | 1.666698 | 65.666683 | 175.000000 | 184.000000 |
| phone | 0.416698 | 4.416683 | 113.750000 | 121.660156 |
| desktop | 10.666656 | 76.333340 | 185.666656 | 194.666656 |
| phone | 2.666656 | 7.083340 | 116.416656 | 124.326813 |
| desktop | 0.000000 | 76.333340 | 185.666656 | 194.666656 |
| phone | 0.000000 | 7.083340 | 116.416656 | 124.326813 |
| desktop | 10.750008 | 87.083347 | 196.416664 | 205.416664 |
| phone | 2.687508 | 9.770847 | 119.104164 | 127.014320 |
| desktop | 0.000000 | 87.083347 | 196.416664 | 205.416664 |
| phone | 0.000000 | 9.770847 | 119.104164 | 127.014320 |

Running totals include the delivered Wave 3 catalog and 17 existing engine icons.
Wave 3 persistent baseline is 109.333317 MiB. Families accumulate in brief order.
These are conservative decoded RGBA8 allocations including supplied mip chains, not
compressed download sizes. The one-at-a-time Wave 3 backdrops add 9 MiB on desktop
and 7.910156 MiB on phone. SVGs have zero baked texture allocation. Per-vertex
sidecars are separate attribute bytes, never counted as texture memory.
The combined phone persistent addition stays below 14 MiB; the phone ceiling is 128 MiB.

## Grounded EMI and independent cup components

EMI now stands on the cup's real inner floor, with the approved source shoes restored. Her lower hand pads touch the continuous gold brim while riding. The opaque tea cap and inner grip bars are removed. The narrower, lower cup keeps the whole screen above the lip. Cup, saucer and rider have independent controls for staggered separation and reseating.

| Runtime model | Triangles | Draws | Joints | Texture allocation |
|---|---:|---:|---:|---:|
| emi-teacup-kart | 4,892 | 2 | 15 | 2,796,200 bytes, 2.667 MiB |

The two replacement 512 square UASTC atlases use the same allocation as before, including all mip levels under conservative RGBA8 accounting. No texture residency is added. `kart_surface` and blank `emi_screen` remain the two materials. The screen retains its original geometry and full 0..1 UVs. Base-color alpha protects ornament from tinting while the material stays OPAQUE. `_CUP_TINT` and `_SAUCER_TINT` remain independent and do not affect EMI or the shoes. Each vertex has one rigid weight.

Original EMI shapes are retained. All 864 source shoe triangles were restored at the same 0.63 scale. After float32 export, 62 microscopic triangles had exactly zero area; only those index triplets were removed. Every position, normal, UV, weight and tint attribute remained byte-exact across that cleanup. The 802 visible runtime shoe triangles retain the approved shape. Source triangle edge differences before cleanup are below 0.000000056 m; existing rider geometry differs by less than 0.00000021 m in its own joint space after the intentional rest-pose adjustment.

The original 13 joint names remain. `cup_body` and `saucer_body` are new children of `kart_root`, alongside `rider_mount`. Cup walls, handle, floor and brim use `cup_body`; the saucer uses `saucer_body`; EMI and restored footwear follow `rider_mount` through the existing hierarchy. Resolve joints by name, since exported skin indices are reordered. `head_attach` retains its relationship to `EMI_root` within float rounding of 0.00000012 in its local matrix. Old rest-pose animation data must be rebuilt for this seated rig.

Rest dimensions in meters are X 1.469185, Y 1.099799, Z 1.440000. Origin is base center, Y up and forward -Z. Saucer radius remains 0.72 m; rest bound radius is 1.110877 m. Motion supplies the padded animated bound.

Geometry contacts in model meters:

- Both sole minima are Y 0.320 on the actual flat cup floor, with a conservative usable radius of 0.362 m.
- The continuous brim top is Y 0.510, spanning radii 0.374 to 0.400 m with 32 segments.
- Left lower hand pad: `(0.3683814, 0.5100257, -0.1249986)`.
- Right lower hand pad: `(-0.3664382, 0.5099980, -0.1249985)`.
- Actual triangle ray hits place those pads within 0.000026 m of the brim.
- Cup foot and saucer seat meet at Y 0.105; cup foot contact radius is 0.220 m.

The private contact contract records the sole patches, palm patches, actual brim triangle hits, cup-foot patch and seating planes. Floor-to-brim depth is 0.19 m, so full rider emergence needs more than that relative lift. Planted states keep the soles seated; free pitch and roll begin only after sufficient foot clearance. The motion contract defines phase timing, release, regrip and antenna follow-through. Whole-world travel remains engine-owned.

Private source and native masters are reproducible. Build seating, export the raw GLB, run the zero-area index cleanup, then validate the export. A review-only cutaway demonstrates shoe/floor contact and is never exported. No live engine integration is claimed by this model pass.
## EMI seated kart motion and opposing wobble

The kart has 26 clips timed at 30 fps, 15 joints and two draws. Ten assembly actions
use 120 fps base keys with exact support-switch timestamps. The other 16 use 30 fps.
The extra keys represent the cusp where the lowest cup-foot support changes,
keeping gaps stable without oversized uniform sampling. Use GLB timestamps, not
a fixed frame stride. Its original 13
joint names remain, with cup_body and saucer_body added beside rider_mount.
The corrected seated model supplies the actual floor, brim and original feet.
Motion appends samples to the final packed binary without changing its existing
geometry, skin/rest matrices, materials, embedded textures, tint masks or anchors.

| Clip | Seconds | Playback |
|---|---:|---|
| `idle_bob` | 2.40 | loop |
| `lean_left` | 0.50 | hold |
| `lean_right` | 0.50 | hold |
| `hop_squash` | 1.20 | one-shot |
| `landing_squash` | 0.60 | one-shot |
| `hit_wobble` | 1.00 | one-shot |
| `spin_out` | 1.20 | one-shot |
| `wave` | 1.50 | one-shot |
| `cheer` | 1.60 | loop |
| `sulk` | 1.20 | hold |
| `bump_left` | 0.50 | one-shot |
| `bump_right` | 0.50 | one-shot |
| `hit_recoil` | 0.80 | one-shot |
| `rescue_pull` | 0.50 | hold |
| `rescue_hover` | 0.60 | loop |
| `rescue_release` | 0.50 | one-shot |
| `jump_takeoff` | 0.30 | hold |
| `ramp_takeoff` | 0.40 | hold |
| `air_glide` | 1.20 | loop |
| `landing_heavy` | 0.80 | one-shot |
| `max_speed` | 1.20 | loop |
| `boost_recoil` | 0.60 | one-shot |
| `item_ready` | 1.60 | loop |
| `item_throw` | 0.50 | one-shot |
| `item_lob` | 0.70 | one-shot |
| `countdown_ready` | 0.70 | hold |

### Contact and assembly

Model forward is -Z and up +Y. Use this model's seated rest and absolute tracks;
bone-local axes differ from model axes. Resolve controls by name, not skin index.
Clone the complete common scene root with SkeletonUtils, keeping skin and bones
together. Drive the common host root for physics, road alignment and rescue travel.

Soles meet the actual cup floor at Y 0.320. Supported hand pads meet the actual
brim annulus at Y 0.510, radii 0.374-0.400 m. Ordinary driving keeps the soles planted.
The floor/brim surface patches are embedded in the binding file for integration.
At 120 fps, with dense assembly checked at 240 fps plus actual-key midpoints, supported palms deviate at most 0.084 mm and planted sole patches at most
0.130 mm. Intentional foot/hand release windows are explicit per action.

The controls are siblings, with independently opposing model-space slopes about
the actual seat center. Full-hop peak is cup+6 deg and saucer-4.5 deg at frame 13,
followed by opposite rebounds at 20/26 and a damped return to rest. Takeoff and
impacts use smaller opposition; ramp/hit pitch complements the roll. Relative to
R 3, angular envelopes are 1.5 x and impact separation 1.3 x. Full-air cup/rider
gaps are 120/300 mm, with the same 3 mm breathing and unchanged phase timing.

Evaluate the tilted saucer seat plane, then translate cup/rider along its normal
until the actual lowest cup-foot patch has the desired signed clearance. Compose
the rider through the cup and solve soles along the cup floor normal. Finally
ground the common rig above the road support plane. World-Y-only cup offsets
or a common-root lift do not maintain these relative contacts.

The full hop lasts 1.20 s: hands release by frame 5, external takeoff at 6, cup separates
at 9, rider separates at 12, saucer touchdown at 21, cup seats at 25, soles at 29,
hands at 32 and antenna settles at 36. The reference peak uses 120 mm cup clearance
and 300 mm sole-to-floor clearance. Air breathing adds at most 3 mm. Body pitch/roll
is gated above 230 mm sole clearance, because the brim is 190 mm above the floor.
Bump/hit reactions use smaller separate responses. Antenna base remains attached.
The complete hop is a legacy/review alternative to the split jump, never layered
with jump_takeoff/ramp_takeoff, air_glide and landing. No world flight arc is baked.

### Physical landing adaptation is required

The supplied landing clips are canonical full-air templates. At the real land
signal, capture the evaluated saucer offset, cup/sole gaps, saucer/cup/rider quaternions,
arm pose and antenna pose. Apply the binding's exact landing envelopes from that
entry. Zero gap stays closed within 0.2 mm interpolation tolerance, and partial
separation only closes. Post-sampling contact solve removes the residual. Do not crossfade a
partial takeoff into the canonical 120/300 mm first sample or wait for takeoff to end.

Landing decays captured saucer/cup orientation and adds a small opposing, damped
rebound. Soft landing closes the cup by 35% and soles by 60% of its 0.60 s duration. Heavy uses
45% and 72% of 0.80 s. Already planted soles stay planted. Hands regrip over 63-83%,
then antenna finishes. Blend free arm/antenna entry over the first 18% while solving
contacts. Every final local transform returns to rest. Zero-gap, early, normal
and long-air probes replay the same captured-entry recipe and pass independent
240 fps plus every actual-key/midpoint check. Worst zero-gap residual is 0.000 mm; no plane/triangle
penetration or uncovered seat contact exceeds the fixed numerical tolerances.

Use one primary normal-blend rig action. Disable conflicting procedural bone
writes and duplicate spin/body reactions. Preserve physics, road-normal alignment,
tint, ghost wash, faces and marks. Solve contacts after any crossfade. The sampled
bound 1.590 m includes 3 mm margin; it does not certify arbitrary layered combinations.
Actual saucer-seat, floor and brim triangle coverage/ray checks pass. These contact
checks do not constitute a full triangle self-intersection test of every mesh part.

### Events and accessibility

models/emi-kart-event-bindings.json is an art contract for Claude, not implemented
engine code. It carries actual bus/state paths, priorities, ownership, fades,
interruptions, face/mark timelines, contact geometry and assembly envelopes.
The current procedural engine must load/use GLB animations and stop overwriting
owned joints before these actions play. All original 26 names remain available.

Wall side is track-lateral: convert through the track frame and inverse kart
rotation. Wall/bump speed change at or above 7.5 m/s selects spin_out. Countdown
exits on go or phase change. Rescue phase changes override clip completion and
cooldown; actual touchdown starts landing exactly once. Damage requires verdict
hit, so mercy, shield and decoy results do not flinch. Read view.items.held for
held items. Max-speed thresholds 0.98 enter/0.90 exit are proposed art defaults.
There is no slope-entry signal; airborne cause lip selects ramp_takeoff.
An overtake wave is optional, low priority and never queued. Item-fire already
created the projectile, so the cast marker must not delay gameplay. Boost kinds include
drift with tier 0..3 and trick; suppress the latest procedural hitMotion yaw/roll/lift
and fireMotion pitch while the corresponding animation channels are owned.

Gentle halves semantic envelopes, then solves cumulative contacts again. Never
halve independently sampled bone transforms. If that solver is unavailable,
use a coherent assembled-rest fallback with face/static-mark cues and 0.3 s fades.
Physical landing/rescue clocks remain authoritative. Spin retains its continuous
full-turn root rotation at half speed; shortest-arc rest slerp cannot halve a turn.
Motion-off holds a coherent rest pose while physical travel continues. No screen,
flash, strobe, emissive animation, sound, text or media is baked into these clips.
Wave/lob are outward raised casts with original short arms, below the case roof.

This model has 4,892 tris and two 512 px UASTC atlases using 2,796,200 B conservative
RGBA 8 residency including all mips. Motion adds zero texture memory. Validation
checks actual exported skin at 120/240 fps plus actual-key midpoints, exact loops/rest exits/named joins, source
payload preservation, full 0..1 blank emi_screen UVs, tints and head_attach.
Final packed visual review/video and engine integration are separate evidence.


### Final runtime verification and budgets

The final 569,428 byte GLB has SHA256
7362bfc8d07d36b079e0d086fda15378ca3d5d269bcd3b4fad64df8fb65776f1.
The event-binding sidecar pins that hash. Its animated bound is 1.59 m;
the padded rest bound is 1.113 m. Dimensions are 1.469185 x 1.099799 x 1.44 m.

Both replacement UASTC atlases pass strict Khronos KTX2 validation. The packed
asset loads in the game's three.js r186 renderer. Review media shows the actual
runtime GLB, with the existing face atlas and a private lit stage. The full-hop
review supplies one common flight arc outside the GLB to show the physical
trajectory alongside the authored relative separation. The cutaway seating
proof is review-only. Contact sheets, movies and sources never enter the game tree.

This is an art delivery. Engine event wiring, pose adaptation for interrupted
landings, per-racer animation ownership and device play review remain required.
The public tree contains no engine changes. All ten W3 clip names are retained;
this model supersedes the earlier kart payload in PR #2589.

The two 512 atlases retain 2,796,200 bytes of conservative RGBA8 residency with
mips. Motion adds no textures. Totals below are the running delivery projection
including W3/W4 art families, not measured live-engine memory. Only the
active sky palette's nebula is resident on either tier.

| Residency | Desktop MiB | Phone MiB |
|---|---:|---:|
| This kart, replacing existing allocation | 2.666664 | 2.666664 |
| This PR added texture allocation | 0 | 0 |
| W4 additions, unchanged | 87.083347 | 9.770847 |
| Persistent running total | 196.416664 | 119.104164 |
| With selected menu backdrop | 205.416664 | 127.014320 |

These totals exclude render targets, decoding buffers and 63,154 bytes of
optional vertex sidecars. The 17-icon allowance is counted once.
