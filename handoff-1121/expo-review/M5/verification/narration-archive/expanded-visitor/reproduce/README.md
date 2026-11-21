# Reproduce the expanded chronological visit

Start the local exhibition preview serving the verified expansion. In PowerShell, open this folder and run the following. The visit requires Node, Playwright, Chrome, Python and Pillow. The review PC already provides them. PW_DIR and CHROME_EXE can point to another local Playwright installation or Chrome executable.

```powershell
$env:FUZZ_BASE = "http://127.0.0.1:8941/fuzz/"
node ./visit_narration.mjs ../repeated-visit ./narration-path-promenade.json ../../three-slide-audit/media-audit.json
python ./summarize_narration.py ../repeated-visit ../../three-slide-audit/media-audit.json
```

On the shared authoring PC the scheduled entry point is `python work/materials/expanded_visitor.py walk` from the authoring workspace. That entry point waits for the five revised doors and uses the shared media job slot.
