# Expo collection workshop

The packed Blender scenes open with the parts arranged on trays. All game templates export with a zero translation, in metres. The front of a sign faces local +Z in Three.js. The First Press glass, shelves and control panels use a base-centre origin. The forum gate uses the hinge as its origin.

| Scene | Contents |
| --- | --- |
| `renewal-collection.blend` | The First Press hardware, both Hoof Sciences test panels, stock fixtures, thirteen keepsakes, settlement printer and Renewal door |
| `visitor-services.blend` | The heater and plug, staff supplies, break-room tray, archive joinery and scarf, statue accessories, corrections desk, photographs, museum visitor with lanyard, forum gate, range equipment, keeper entrance and staff exit |

Both builds use Blender 5.2.1 LTS. The print preparers use Pillow, the installed Windows Georgia and Arial fonts, and the approved art and materials in `C:/Projects/blender-scripting/expo`. They rasterize the type and pack the original approved llama artwork into the models. The fonts themselves are not redistributed. The workshop surface credits are in the shipped Expo's `assets/expo/ATTRIBUTION.md`.

Run these commands in this directory, setting the destinations to the work folder and the v4 Expo asset directory.

```powershell
$source = 'C:/Projects/blender-scripting/expo'
$blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
$art = Resolve-Path '../../../ConditioningControlPanel/Resources/web/fuzz/assets/expo'
$scratch = Join-Path $env:TEMP 'expo-collection-workshop'

python print_collection.py $source "$scratch/renewal"
& $blender -b --factory-startup --python-exit-code 2 --python build_collection.py -- "$scratch/renewal" "$PWD/renewal-collection.blend" "$art/renewal-collection.glb"
python print_services.py $source "$scratch/services"
& $blender -b --factory-startup --python-exit-code 2 --python build_services.py -- "$scratch/services" "$PWD/visitor-services.blend" "$art/visitor-services.glb"
```

`inspect_collection.py` imports either shipped GLB in a fresh Blender process. It checks the template origins, required moving parts and packed textures, measures bounds and the triangle count for each model, and photographs each family.

```powershell
& $blender -b --factory-startup -t 6 --python-exit-code 2 --python inspect_collection.py -- "$art/renewal-collection.glb" "$scratch/review"
& $blender -b --factory-startup -t 6 --python-exit-code 2 --python inspect_collection.py -- "$art/visitor-services.glb" "$scratch/review"
```
