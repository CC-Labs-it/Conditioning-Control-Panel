"""Exercise the real installer's "Open with CCP" task on a disposable Windows VM; see README.md."""
import argparse
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

# WPF installer.iss:216-265: two ProgIDs and an Open With value per extension, all per-user.
EXTENSIONS = ('.mp4', '.webm', '.mkv', '.mov', '.avi', '.m4v', '.mp3', '.wav', '.m4a', '.aac', '.flac', '.ogg')
PROGIDS = {'CCPanel.Player.1': ('Player', '--play'), 'CCPanel.Editor.1': ('Editor', '--edit')}
FOREIGN = 'CCPTest.Foreign'  # another application's Open With entry that must survive
REPO = Path(__file__).resolve().parents[2]


def expected(exe):
    """(HKCU subkey, value name) -> data the fileassoc task writes for exe."""
    rows = {}
    for progid, (role, flag) in PROGIDS.items():
        root = 'Software\\Classes\\' + progid
        rows[(root, '')] = f'Conditioning Control Panel - {role}'
        rows[(root, 'FriendlyTypeName')] = f'CCP {role}'
        rows[(root + '\\DefaultIcon', '')] = f'{exe},0'
        rows[(root + '\\shell\\open', 'FriendlyAppName')] = f'CCP {role}'
        rows[(root + '\\shell\\open\\command', '')] = f'"{exe}" {flag} "%1"'
        for ext in EXTENSIONS:
            rows[(f'Software\\Classes\\{ext}\\OpenWithProgids', progid)] = ''
    return rows


def script_rows(source):
    """The fileassoc registry rows of an .iss, in the same shape as expected()."""
    rows = {}
    for line in source.splitlines():
        if line.startswith('Root: HKCU;') and 'Tasks: fileassoc' in line:
            fields = dict(part.split(': ', 1) for part in line.split('; '))
            value = lambda name: fields[name][1:-1].replace('""', '"')
            rows[(value('Subkey'), value('ValueName'))] = value('ValueData')
    return rows


def verify(read, exe, present, defaults):
    """read(subkey, name) -> HKCU string or None. defaults: each extension's default before setup."""
    for (key, name), data in expected(exe).items():
        actual = read(key, name)
        if present and actual != data:
            raise AssertionError(f'{key} [{name}] is {actual!r}, expected {data!r}')
        if not present and actual is not None:
            raise AssertionError(f'{key} [{name}] survived: {actual!r}')
    for ext in EXTENSIONS:
        if read(f'Software\\Classes\\{ext}\\OpenWithProgids', FOREIGN) != '':
            raise AssertionError(f"another application's {ext} Open With entry was removed")
        if read(f'Software\\Classes\\{ext}', '') != defaults[ext]:
            raise AssertionError(f'the default application for {ext} changed')


def verify_launch(command_line, exe, flag, media):
    if command_line.casefold() != f'"{exe}" {flag} "{media}"'.casefold():
        raise AssertionError(f'Explorer launched {command_line!r}')


def hkcu_read(key, name):
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key) as handle:
            value, kind = winreg.QueryValueEx(handle, name)
    except FileNotFoundError:
        return None
    return value if kind == winreg.REG_SZ else repr(value)


def set_foreign(present):
    import winreg
    for ext in EXTENSIONS:
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, f'Software\\Classes\\{ext}\\OpenWithProgids') as handle:
            if present:
                winreg.SetValueEx(handle, FOREIGN, 0, winreg.REG_SZ, '')
            else:
                try:
                    winreg.DeleteValue(handle, FOREIGN)
                except FileNotFoundError:
                    pass


def shell_open(progid, media):
    """Open media through the ProgID's registered verb, as Explorer's Open With does; returns the command line."""
    import ctypes
    from ctypes import wintypes

    class Info(ctypes.Structure):
        _fields_ = [('cbSize', wintypes.DWORD), ('fMask', wintypes.ULONG), ('hwnd', wintypes.HWND),
                    ('lpVerb', wintypes.LPCWSTR), ('lpFile', wintypes.LPCWSTR), ('lpParameters', wintypes.LPCWSTR),
                    ('lpDirectory', wintypes.LPCWSTR), ('nShow', ctypes.c_int), ('hInstApp', wintypes.HINSTANCE),
                    ('lpIDList', ctypes.c_void_p), ('lpClass', wintypes.LPCWSTR), ('hkeyClass', wintypes.HKEY),
                    ('dwHotKey', wintypes.DWORD), ('hIcon', wintypes.HANDLE), ('hProcess', wintypes.HANDLE)]

    info = Info(cbSize=ctypes.sizeof(Info), fMask=0x1 | 0x40 | 0x400, lpVerb='open', lpFile=str(media),
                lpClass=progid, nShow=7)  # CLASSNAME | NOCLOSEPROCESS | FLAG_NO_UI, minimized
    shell = ctypes.WinDLL('shell32', use_last_error=True)
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    shell.ShellExecuteExW.argtypes = [ctypes.POINTER(Info)]
    kernel.GetProcessId.argtypes = [wintypes.HANDLE]
    kernel.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    if not shell.ShellExecuteExW(ctypes.byref(info)) or not info.hProcess:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        pid = kernel.GetProcessId(info.hProcess)
        return subprocess.run(['powershell', '-NoProfile', '-Command',
                               f'(Get-CimInstance Win32_Process -Filter "ProcessId={pid}").CommandLine'],
                              capture_output=True, text=True, check=True).stdout.strip()
    finally:
        kernel.TerminateProcess(info.hProcess, 1)
        kernel.CloseHandle(info.hProcess)


def run_setup(setup, run_dir, step, tasks, timeout):
    log = run_dir / f'{step}.log'
    cmd = [setup, '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/LANG=english',
           '/DIR=' + str(run_dir / 'app'), '/TASKS=' + tasks, '/LOG=' + str(log)]
    code = subprocess.run(cmd, timeout=timeout).returncode
    if code != 0:
        raise AssertionError(f'{step}: setup exited {code}; log: {log}')


def run(args):
    if os.name != 'nt':
        raise RuntimeError('The installer integration test requires Windows')
    if not args.disposable_vm:
        raise RuntimeError('Use only on a disposable VM: setup modifies install and registry state')
    if any(hkcu_read('Software\\Classes\\' + progid, '') is not None for progid in PROGIDS):
        raise RuntimeError('CCP associations already exist; use a VM without a CCP installation')
    setup = str(Path(args.setup).resolve(strict=True))
    run_dir = Path(tempfile.mkdtemp(prefix='ccp-fileassoc-test-', dir=Path(args.workspace).resolve(strict=True)))
    name = re.search(r'#define MyAppExeName "([^"]+)"', (REPO / 'installer-avalonia.iss').read_text(encoding='utf-8')).group(1)
    exe = str(run_dir / 'app' / name)
    media = run_dir / 'sample.mp4'
    media.write_bytes(b'\0' * 64)
    defaults = {ext: hkcu_read(f'Software\\Classes\\{ext}', '') for ext in EXTENSIONS}
    set_foreign(True)
    try:
        run_setup(setup, run_dir, 'opt-in', 'fileassoc', args.timeout)
        verify(hkcu_read, exe, True, defaults)
        for progid, (_, flag) in PROGIDS.items():
            verify_launch(shell_open(progid, media), exe, flag, media)
        run_setup(setup, run_dir, 'opt-out', 'startmenuicon', args.timeout)
        verify(hkcu_read, exe, False, defaults)
        run_setup(setup, run_dir, 'opt-in-again', 'fileassoc', args.timeout)
        verify(hkcu_read, exe, True, defaults)
        uninstaller = run_dir / 'app' / 'unins000.exe'
        subprocess.run([str(uninstaller), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
                        '/LOG=' + str(run_dir / 'uninstall.log')], timeout=args.timeout)
        deadline = time.monotonic() + args.timeout
        while uninstaller.exists():  # the uninstaller finishes from a temporary copy
            if time.monotonic() > deadline:
                raise TimeoutError('uninstall did not finish')
            time.sleep(1)
        verify(hkcu_read, exe, False, defaults)
        print('PASS: opt-in, Explorer player and editor launch, opt-out, opt-in again, uninstall')
        print(f'Logs: {run_dir}')
    finally:
        set_foreign(False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--setup', required=True, help='compiled installer from this checkout')
    parser.add_argument('--workspace', required=True, help='empty VM test directory')
    parser.add_argument('--timeout', type=int, default=600)
    parser.add_argument('--disposable-vm', action='store_true')
    run(parser.parse_args())


if __name__ == '__main__':
    sys.exit(main())
