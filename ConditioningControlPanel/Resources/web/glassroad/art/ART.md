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
