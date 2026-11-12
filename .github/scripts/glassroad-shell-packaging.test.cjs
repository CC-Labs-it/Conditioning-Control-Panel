'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const repo = path.resolve(__dirname, '../..');
const shellFiles = ['main.js', 'preload.js', 'README.md', 'tests/shell.test.cjs'];

// Use the actual projects and MSBuild targets with a tiny disposable asset tree.
function fixture(t) {
  const root = canonical(fs.mkdtempSync(path.join(os.tmpdir(), 'ccp-shell-packaging-')));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  for (const file of ['Version.props', 'ContentPackSounds.props',
    'ConditioningControlPanel/ConditioningControlPanel.csproj', 'CCP.Avalonia/CCP.Avalonia.csproj']) {
    const dest = path.join(root, file); fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.copyFileSync(path.join(repo, file), dest);
  }
  for (const file of [...shellFiles.map(x => 'glassroad-shell/' + x), 'glassroad/index.html']) {
    const dest = path.join(root, 'Assets/web', file); fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.writeFileSync(dest, 'fixture');
  }
  return root;
}

// os.tmpdir() on windows-latest is the 8.3 short path C:\Users\RUNNER~1\..., and MSBuild may hand back
// FullPath in a different spelling of the same directory (long name, other case, symlink). path.relative
// then walks out with '..' and nothing matches. Compare canonical on-disk paths on both sides.
function canonical(p) {
  try { return fs.realpathSync.native(p); } catch { return path.resolve(p); }
}

// `at` is the directory MSBuild is pointed at; it defaults to root but may be another spelling of it.
function items(root, project, name, extra = [], at = root) {
  const r = spawnSync('dotnet', ['msbuild', path.join(at, project), '-nologo',
    '-p:EnableWindowsTargeting=true', ...extra, '-getItem:' + name], { encoding: 'utf8', maxBuffer: 4 * 1024 * 1024 });
  assert.equal(r.status, 0, r.stderr || r.stdout || String(r.error));
  const base = canonical(root);
  return JSON.parse(r.stdout).Items[name].map(x => path.relative(base, canonical(x.FullPath)).replaceAll('\\', '/').toLowerCase());
}

test('WPF build content excludes the entire Electron shell while retaining Glassroad', t => {
  const root = fixture(t), content = items(root, 'ConditioningControlPanel/ConditioningControlPanel.csproj', 'Content');
  assert.ok(content.includes('assets/web/glassroad/index.html'), content.join('\n'));
  assert.ok(!content.some(x => x.startsWith('assets/web/glassroad-shell/')), content.join('\n'));
});

test('items() matches when MSBuild sees another spelling of the fixture root', t => {
  const root = fixture(t), alias = root + '-alias';
  fs.symlinkSync(root, alias, 'junction');
  t.after(() => fs.rmSync(alias, { force: true }));
  const content = items(root, 'ConditioningControlPanel/ConditioningControlPanel.csproj', 'Content', [], alias);
  assert.ok(content.includes('assets/web/glassroad/index.html'), content.join('\n'));
});

test('WPF publish does not copy the shell back into the installer', t => {
  const root = fixture(t), published = path.join(root, 'published');
  const files = items(root, 'ConditioningControlPanel/ConditioningControlPanel.csproj', 'WebFiles',
    ['-target:CopyContentAfterPublish', '-p:PublishDir=' + published + path.sep]);
  assert.ok(files.includes('assets/web/glassroad/index.html'), files.join('\n'));
  assert.ok(!files.some(x => x.startsWith('assets/web/glassroad-shell/')), files.join('\n'));
  assert.ok(fs.existsSync(path.join(published, 'Resources/web/glassroad/index.html')));
  assert.ok(!fs.existsSync(path.join(published, 'Resources/web/glassroad-shell')));
});

test('Avalonia includes the two shell entry points and excludes notes and tests', t => {
  const root = fixture(t), content = items(root, 'CCP.Avalonia/CCP.Avalonia.csproj', 'Content');
  for (const file of ['main.js', 'preload.js']) assert.ok(content.includes('assets/web/glassroad-shell/' + file), content.join('\n'));
  for (const file of ['readme.md', 'tests/shell.test.cjs']) assert.ok(!content.includes('assets/web/glassroad-shell/' + file));
  assert.ok(content.includes('assets/web/glassroad/index.html'), content.join('\n'));
});
