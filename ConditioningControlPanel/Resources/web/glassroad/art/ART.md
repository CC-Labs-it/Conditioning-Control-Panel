# Glassroad art: Wave 1 countdown and finish

Three static meshopt GLBs: an instanced countdown ring and two LODs of an open finish arch.
No textures are embedded. The spiral faces use the shared sky masks.

## Integration

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

## Sources and licence

Original procedural glass bands, inset fillets, trim and annular shader faces created for CCP.
The supplied start/finish concepts guided mood only, with no tracing or copied pixels.
No third-party meshes, textures, photography or media are included.
Rights follow the project's existing asset terms; no CC0 claim is made.
The private master archive retains the generator, .blend masters and independent import evidence.

## Assumptions and review

One instanced countdown ring supplies all countdown lights; the engine chooses placement and count.
Portal opening was chosen for the plan's 12 m road plus lateral clearance.
The LOD1 removes decorative fillets but retains the double-arch silhouette and clearance.
Review images include front, three-quarter and a sky-coloured material preview, plus six fixed views.
The context view temporarily binds the shared neutral mask; no coloured or moving pattern is baked.

## Triangles and texture memory

| Asset | Tris | Texture bytes | Bound radius (m) | Draws |
|---|---:|---:|---:|---:|
| countdown-ring | 1,224 | 0 | 2.912 | 3 |
| finish-portal-lod0 | 2,876 | 0 | 10.923 | 3 |
| finish-portal-lod1 | 780 | 0 | 10.860 | 3 |

Texture memory excludes already-shared sky masks; no new per-model texture residency is required.
Review PNGs are evidence only, not runtime inputs.

Each family branch starts directly from the same main snapshot. When integrating families,
append their distinct manifest rows and retain all family ART.md sections. The sampled palettes are identical.
