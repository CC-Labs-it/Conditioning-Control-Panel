"""The tarball launcher's OpenSSL 3 probe (conditioning-control-panel.sh). No .NET, display or real libssl needed:
ldconfig, os-release and the library dirs are stand-ins in a temp dir, CCP.Avalonia is a stub that records argv."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

LAUNCHER = Path(__file__).with_name('conditioning-control-panel.sh').resolve()


class LauncherOpenSslTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.app = root / 'app'
        self.app.mkdir()
        shutil.copy(LAUNCHER, self.app / LAUNCHER.name)
        self.ran = root / 'ran'
        self.script(self.app / 'CCP.Avalonia', f'printf "%s\\n" "$@" > "{self.ran}"')
        self.bin = root / 'bin'
        self.bin.mkdir()
        self.libs = root / 'libs'
        self.libs.mkdir()
        self.os_release = root / 'os-release'
        self.ldconfig('')
        self.distro('fedora', '')
        self.env = {'PATH': f'{self.bin}:/usr/bin:/bin', 'CCP_OPENSSL_PROBE_DIRS': str(self.libs),
                    'CCP_OS_RELEASE': str(self.os_release)}

    def script(self, path, body):
        path.write_text(f'#!/bin/sh\n{body}\n')
        path.chmod(0o755)

    def ldconfig(self, listing):
        self.script(self.bin / 'ldconfig', f"printf '%s' '{listing}'")

    def distro(self, id_, like):
        self.os_release.write_text(f'NAME=x\nID={id_}\nID_LIKE="{like}"\n')

    def run_launcher(self, **extra):
        return subprocess.run([str(self.app / LAUNCHER.name), '--smoke', 'a b'], env={**self.env, **extra},
                              text=True, capture_output=True, timeout=10)

    def assert_hint(self, result, command):
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertFalse(self.ran.exists(), 'CCP.Avalonia must not start without libssl.so.3')
        self.assertIn('libssl.so.3', result.stderr)
        self.assertIn(command, result.stderr)

    def test_fedora_without_libssl3_names_openssl3_libs(self):
        self.ldconfig('\tlibssl.so.4 (libc6,x86-64) => /usr/lib64/libssl.so.4\n')
        self.assert_hint(self.run_launcher(), 'sudo dnf install openssl3-libs')

    def test_fedora_derivative_by_id_like(self):
        self.distro('nobara', 'rhel centos fedora')
        self.assert_hint(self.run_launcher(), 'sudo dnf install openssl3-libs')

    def test_other_families_name_their_package(self):
        for id_, like, command in (('arch', '', 'sudo pacman -S openssl'),
                                   ('ubuntu', 'debian', 'sudo apt install libssl3t64'),
                                   ('opensuse-tumbleweed', 'opensuse suse', 'sudo zypper install libopenssl3')):
            with self.subTest(id_):
                self.distro(id_, like)
                self.assert_hint(self.run_launcher(), command)

    def test_unknown_distro_gets_generic_hint(self):
        self.distro('gentoo', '')
        result = self.run_launcher()
        self.assert_hint(result, 'OpenSSL 3 library package')
        self.assertNotIn('sudo', result.stderr)
        self.os_release.unlink()
        self.assert_hint(self.run_launcher(), 'OpenSSL 3 library package')

    def assert_started(self, result):
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stderr, '')
        self.assertEqual(self.ran.read_text(), '--smoke\na b\n')

    def test_ldconfig_listing_libssl3_starts_the_app_with_its_args(self):
        self.ldconfig('\tlibssl.so.3 (libc6,x86-64) => /usr/lib64/libssl.so.3\n')
        self.assert_started(self.run_launcher())

    def test_libssl11_also_starts(self):
        self.ldconfig('\tlibssl.so.1.1 (libc6,x86-64) => /usr/lib/libssl.so.1.1\n')
        self.assert_started(self.run_launcher())

    def test_library_dir_or_ld_library_path_starts(self):
        (self.libs / 'libssl.so.3').touch()
        self.assert_started(self.run_launcher())
        (self.libs / 'libssl.so.3').unlink()
        self.ran.unlink()
        extra = Path(self.temp.name) / 'extra'
        extra.mkdir()
        (extra / 'libssl.so.3').touch()
        self.assert_started(self.run_launcher(LD_LIBRARY_PATH=f'/nonexistent:{extra}'))

    def test_overrides_skip_the_probe(self):
        for key, value in (('CCP_SKIP_OPENSSL_CHECK', '1'), ('CLR_OPENSSL_VERSION_OVERRIDE', '4')):
            with self.subTest(key):
                self.assert_started(self.run_launcher(**{key: value}))
                self.ran.unlink()

    def test_symlinked_launcher_execs_beside_its_target(self):
        self.ldconfig('\tlibssl.so.3 (libc6,x86-64) => /usr/lib64/libssl.so.3\n')
        link = Path(self.temp.name) / 'ccp'
        link.symlink_to(self.app / LAUNCHER.name)
        result = subprocess.run([str(link), '--smoke', 'a b'], env=self.env, text=True, capture_output=True, timeout=10)
        self.assert_started(result)


class LauncherWebKitDmabufTests(unittest.TestCase):
    """live-safety S18: on NVIDIA the launcher sets WEBKIT_DISABLE_DMABUF_RENDERER=1 unless the user set it (CCP.Core LinuxWebKitEnv)."""
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        app = self.root / 'app'
        app.mkdir()
        self.launcher = app / LAUNCHER.name
        shutil.copy(LAUNCHER, self.launcher)
        self.seen = self.root / 'seen'
        stub = app / 'CCP.Avalonia'
        stub.write_text(f'#!/bin/sh\nprintf "%s" "${{WEBKIT_DISABLE_DMABUF_RENDERER-unset}}" > "{self.seen}"\n')
        stub.chmod(0o755)
        self.nvidia = self.root / 'nvidia-version'

    def value(self, **extra):
        env = {'PATH': '/usr/bin:/bin', 'CCP_SKIP_OPENSSL_CHECK': '1', 'CCP_NVIDIA_PROBE': f'/nonexistent {self.nvidia}', **extra}
        result = subprocess.run([str(self.launcher)], env=env, text=True, capture_output=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        return self.seen.read_text()

    def test_nvidia_disables_the_dmabuf_renderer(self):
        self.nvidia.touch()
        self.assertEqual(self.value(), '1')

    def test_without_nvidia_it_stays_unset(self):
        self.assertEqual(self.value(), 'unset')

    def test_a_value_the_user_set_wins(self):
        self.nvidia.touch()
        for v in ('0', ''):
            with self.subTest(v):
                self.assertEqual(self.value(WEBKIT_DISABLE_DMABUF_RENDERER=v), v)


if __name__ == '__main__':
    unittest.main()
