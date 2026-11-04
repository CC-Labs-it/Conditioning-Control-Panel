from pathlib import Path

workflow = Path('.github/workflows/build.yml').read_text()
step = workflow.split('- name: Prove WebView2 test detects a missing listener', 1)[1].split('- name:', 1)[0]
assert '$LASTEXITCODE = 0' in step.split('finally', 1)[1], 'Expected successful exit after restoring the source'
