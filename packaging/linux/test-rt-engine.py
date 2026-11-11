"""Focused packaging tests. No engine download, display or namespace access needed."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

CHECK = Path(os.environ.get('RT_ENGINE_CHECK', Path(__file__).with_name('rt-engine-check.sh'))).resolve()


class RaceEnginePackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.engine = self.root / 'engine'
        self.engine.mkdir()
        for name in ('LICENSE', 'LICENSES.chromium.html'):
            (self.engine / name).write_text('upstream licence\n')
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        self.trace = self.root / 'probe'
        self.env = dict(os.environ, PATH=f'{self.bin}:/usr/bin:/bin', FLATPAK_ID='')
        self.tool('timeout', 'exec "$@"', prefix='shift 2')
        self.tool('unshare', f'printf "%s\\n" "$@" > "{self.trace}"\nexit 0')

    def tool(self, name, body, prefix=''):
        path = self.bin / name
        path.write_text(f'#!/bin/bash\n{prefix}\n{body}\n')
        path.chmod(0o755)

    def run_check(self):
        return subprocess.run(['/bin/bash', str(CHECK), str(self.engine)],
                              env=self.env, text=True, capture_output=True, timeout=6)

    def test_about_names_upstream_licences(self):
        about = Path(__file__).resolve().parents[2] / 'CCP.Avalonia/Views/Features/AppInfoFeatureControl.axaml'
        blocks = ET.parse(about).iter('{https://github.com/avaloniaui}TextBlock')
        self.assertTrue(any(b.get('Text') == 'Electron (MIT) / Chromium (BSD)' for b in blocks))

    def test_available_namespaces_keep_sandbox_and_log(self):
        result = self.run_check()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('', result.stdout)
        self.assertIn('sandbox=enabled', result.stderr)
        self.assertEqual(['--user', '--map-root-user', '--pid', '--fork', '--net', '--ipc', 'true'],
                         self.trace.read_text().splitlines())

    def test_denied_or_timed_out_probe_uses_one_flag_and_logs(self):
        for status in (1, 124, 137):
            with self.subTest(status=status):
                self.tool('unshare', f'exit {status}')
                result = self.run_check()
                self.assertEqual(0, result.returncode)
                self.assertEqual('--no-sandbox\n', result.stdout)
                self.assertIn('sandbox=disabled reason=user-namespaces-unavailable', result.stderr)

    def test_flatpak_skips_probe(self):
        self.env['FLATPAK_ID'] = 'io.github.CodeBambi.ConditioningControlPanel'
        result = self.run_check()
        self.assertEqual(0, result.returncode)
        self.assertEqual('--no-sandbox\n', result.stdout)
        self.assertIn('reason=flatpak', result.stderr)
        self.assertFalse(self.trace.exists())

    def test_missing_empty_or_symlink_notice_refuses_before_probe(self):
        for name in ('LICENSE', 'LICENSES.chromium.html'):
            path = self.engine / name
            for state in ('missing', 'empty', 'symlink'):
                with self.subTest(name=name, state=state):
                    path.unlink()
                    if state == 'empty':
                        path.touch()
                    elif state == 'symlink':
                        path.symlink_to(CHECK)
                    result = self.run_check()
                    self.assertNotEqual(0, result.returncode)
                    self.assertEqual('', result.stdout)
                    self.assertIn(f'missing licence {name}', result.stderr)
                    self.assertFalse(self.trace.exists())
                    if path.exists():
                        path.unlink()
                    path.write_text('upstream licence\n')

    def test_real_timeout_bounds_hung_probe(self):
        (self.bin / 'timeout').unlink()
        self.tool('unshare', 'exec sleep 30')
        result = self.run_check()
        self.assertEqual(0, result.returncode)
        self.assertEqual('--no-sandbox\n', result.stdout)
        self.assertIn('sandbox=disabled', result.stderr)


if __name__ == '__main__':
    unittest.main()
