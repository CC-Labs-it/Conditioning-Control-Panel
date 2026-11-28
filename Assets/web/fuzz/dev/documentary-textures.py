"""Download the museum's CC0 material maps and make bounded web textures."""
import argparse
import hashlib
import json
from io import BytesIO
from pathlib import Path

import requests
from PIL import Image, ImageOps

MATERIALS = {
    'stone': 'sandstone_cracks',
    'plaster': 'painted_plaster_wall',
    'wood': 'wood_table_worn',
    'brick': 'medieval_red_brick',
    'floor': 'marble_mosaic_tiles',
    'terrazzo': 'terrazzo_tiles',
    'velour': 'velour_velvet',
    'leather': 'fabric_leather_02',
    'rubber': 'rubber_tiles',
}
MAPS = {'color': ['Diffuse', 'diff'], 'normal': ['nor_gl'], 'roughness': ['Rough', 'rough']}

parser = argparse.ArgumentParser()
parser.add_argument('--out', type=Path, default=Path(__file__).resolve().parents[1] / 'assets/documentary/textures')
args = parser.parse_args()
args.out.mkdir(parents=True, exist_ok=True)
session = requests.Session()
records = []
for name, asset in MATERIALS.items():
    response = session.get(f'https://api.polyhaven.com/files/{asset}', timeout=30)
    response.raise_for_status()
    files = response.json()
    maps = {}
    for output, choices in MAPS.items():
        key = next(key for key in choices if key in files)
        resolution = '2k' if name in ('terrazzo', 'velour', 'leather', 'rubber') else '1k'
        spec = files[key][resolution]['jpg']
        target = args.out / f'{name}-{output}.webp'
        if not target.exists():
            data = session.get(spec['url'], timeout=60)
            data.raise_for_status()
            assert hashlib.md5(data.content).hexdigest() == spec['md5']
            picture = Image.open(BytesIO(data.content)).convert('RGB')
            if name == 'stone' and output == 'color':
                # Keep the scanned grain while matching the reference's pale limestone.
                picture = ImageOps.colorize(ImageOps.grayscale(picture), '#a89e8a', '#efe6d3')
            if name == 'terrazzo' and output == 'color':
                picture = ImageOps.colorize(ImageOps.grayscale(picture), '#58515c', '#d0c5ca')
            if name == 'velour' and output == 'color':
                picture = ImageOps.colorize(ImageOps.grayscale(picture), '#65616b', '#f2e8ed')
            picture.save(target, quality=90, method=6)
        maps[output] = {'file': target.name, 'source': spec['url'], 'source_md5': spec['md5']}
        if name in ('terrazzo', 'velour') and output == 'color':
            maps[output]['derivation'] = {
                'operation': 'Grayscale and colourise for a tintable museum material',
                'endpoints': ['#58515c', '#d0c5ca'] if name == 'terrazzo' else ['#65616b', '#f2e8ed'],
                'output': 'RGB WebP, quality 90, method 6',
            }
        if name == 'stone' and output == 'color':
            maps[output]['derivation'] = {
                'operation': 'Pillow ImageOps.grayscale then ImageOps.colorize',
                'black': '#a89e8a', 'white': '#efe6d3',
                'output': 'RGB WebP, quality 90, method 6',
            }
        print(name, output, target.stat().st_size, flush=True)
    records.append({'material': name, 'asset': asset, 'source': f'https://polyhaven.com/a/{asset}',
                    'license': 'CC0-1.0', 'license_url': 'https://polyhaven.com/license', 'maps': maps})
environment_file = args.out / 'studio-environment.webp'
response = session.get('https://api.polyhaven.com/files/brown_photostudio_02', timeout=30)
response.raise_for_status()
spec = response.json()['tonemapped']
if not environment_file.exists():
    data = session.get(spec['url'], timeout=90)
    data.raise_for_status()
    assert hashlib.md5(data.content).hexdigest() == spec['md5']
    picture = Image.open(BytesIO(data.content)).convert('RGB')
    picture.resize((1024, 512), Image.Resampling.LANCZOS).save(environment_file, quality=88)
    print('environment', environment_file.stat().st_size, flush=True)
records.append({
    'material': 'environment', 'asset': 'brown_photostudio_02',
    'source': 'https://polyhaven.com/a/brown_photostudio_02',
    'license': 'CC0-1.0', 'license_url': 'https://polyhaven.com/license',
    'maps': {'color': {
        'file': environment_file.name, 'source': spec['url'], 'source_md5': spec['md5'],
        'output_sha256': hashlib.sha256(environment_file.read_bytes()).hexdigest(),
        'derivation': {'source_variant': 'tonemapped', 'operation': 'RGB conversion and Lanczos resize',
                       'size': [1024, 512], 'output': 'WebP, quality 88'},
    }},
})
(args.out / 'sources.json').write_text(json.dumps(records, indent=2) + '\n', encoding='utf-8')
