"""Exercise the real Inno installer on a disposable Windows VM; see README.md."""
import argparse
import ctypes
import json
import os
import secrets
import socket
from pathlib import Path
import subprocess
import sys
import tempfile
import time

ACK = 'browser-reset.ack.json'
PENDING = 'browser-reset.pending'
COMPARISON = 'Browser reset original user comparison exit: '
FAILURE = 'Browser reset original user comparison failed'


def verify(mode, identity, roots, target, log):
    """Validate files and the result of the real ExecAsOriginalUser command."""
    target = Path(target)
    for root in map(Path, roots):
        ack = root / ACK
        pending = root / PENDING
        expect_ack = root == target and mode == 'interactive' and identity == 'same'
        expect_pending = root == target and mode == 'silent'
        if ack.exists() != expect_ack:
            raise AssertionError(f'unexpected acknowledgment state at {root}')
        if pending.exists() != expect_pending:
            raise AssertionError(f'unexpected pending state at {root}')
        if expect_ack and ack.read_text(encoding='utf-8-sig').strip() != '{}':
            raise AssertionError('invalid acknowledgment contents')
        if expect_pending and pending.read_text(encoding='utf-8-sig').strip() != 'pending':
            raise AssertionError('invalid pending marker contents')
    if FAILURE in log:
        raise AssertionError('ExecAsOriginalUser did not run successfully')
    results = [line.split(COMPARISON, 1)[1].strip() for line in log.splitlines() if COMPARISON in line]
    if mode == 'interactive':
        if results != ['0' if identity == 'same' else '1']:
            raise AssertionError('missing or incorrect original-user comparison result')
    elif results or FAILURE in log:
        raise AssertionError('silent setup unexpectedly ran the interactive identity check')


def verify_observed_identity(original, expected, identity, seen):
    if (expected == original) != (identity == 'same'):
        raise ValueError('setup SID must match the original user for same, and differ for different')
    if (expected, True) not in seen:
        raise AssertionError('no elevated wizard was observed as the expected user; wrong account or token access denied')
    if identity == 'same' and any(sid != original for sid, _ in seen):
        raise AssertionError('the wizard also ran under a different account')


def windows_api():
    from ctypes import wintypes
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    advapi = ctypes.WinDLL('advapi32', use_last_error=True)
    user = ctypes.WinDLL('user32', use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.LocalFree.argtypes = [wintypes.HLOCAL]
    advapi.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
    advapi.GetTokenInformation.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
    advapi.ConvertSidToStringSidW.argtypes = [ctypes.c_void_p, ctypes.POINTER(wintypes.LPWSTR)]
    user.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    return kernel, advapi, user, wintypes


def process_identity(api, handle):
    kernel, advapi, _, wintypes = api
    token = wintypes.HANDLE()
    if not advapi.OpenProcessToken(handle, 0x0008, ctypes.byref(token)):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        size = wintypes.DWORD()
        advapi.GetTokenInformation(token, 1, None, 0, ctypes.byref(size))
        if not size.value:
            raise ctypes.WinError(ctypes.get_last_error())
        buf = ctypes.create_string_buffer(size.value)
        if not advapi.GetTokenInformation(token, 1, buf, size, ctypes.byref(size)):
            raise ctypes.WinError(ctypes.get_last_error())
        sid = ctypes.cast(buf, ctypes.POINTER(ctypes.c_void_p))[0]
        result = wintypes.LPWSTR()
        if not advapi.ConvertSidToStringSidW(sid, ctypes.byref(result)):
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            elevation = wintypes.DWORD()
            if not advapi.GetTokenInformation(token, 20, ctypes.byref(elevation), ctypes.sizeof(elevation), ctypes.byref(size)):
                raise ctypes.WinError(ctypes.get_last_error())
            return result.value, bool(elevation.value)
        finally:
            kernel.LocalFree(result)
    finally:
        kernel.CloseHandle(token)


def wizard_identities(api):
    kernel, _, user, wintypes = api
    found = set()
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    @callback_type
    def visit(hwnd, _):
        cls, title = ctypes.create_unicode_buffer(256), ctypes.create_unicode_buffer(512)
        user.GetClassNameW(hwnd, cls, len(cls))
        user.GetWindowTextW(hwnd, title, len(title))
        if cls.value == 'TWizardForm' and 'Conditioning Control Panel' in title.value:
            pid = wintypes.DWORD()
            user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            process = kernel.OpenProcess(0x1000, False, pid.value)  # QUERY_LIMITED_INFORMATION
            if process:
                try:
                    found.add(process_identity(api, process))
                except OSError:
                    pass  # fail closed below if the elevated token cannot be inspected
                finally:
                    kernel.CloseHandle(process)
        return True

    user.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user.EnumWindows(visit, 0)
    return found


def send_message(stream, value):
    stream.write((json.dumps(value, separators=(',', ':')) + '\n').encode('utf-8'))
    stream.flush()


def read_message(stream):
    line = stream.readline(65537)
    if not line or len(line) > 65536 or not line.endswith(b'\n'):
        raise RuntimeError('observer connection closed or sent an invalid message')
    value = json.loads(line)
    if not isinstance(value, dict):
        raise RuntimeError('invalid observer message')
    return value


def authenticate_observer(stream, nonce, expected_sid):
    hello = read_message(stream)
    if (hello.get('kind') != 'hello' or not isinstance(hello.get('nonce'), str)
            or not secrets.compare_digest(hello['nonce'], nonce)
            or hello.get('sid') != expected_sid or hello.get('elevated') is not True):
        raise RuntimeError('observer is not the expected elevated setup account')


def poll_observer(stream):
    send_message(stream, {'kind': 'poll'})
    message = read_message(stream)
    values = message.get('identities')
    if message.get('kind') != 'identities' or not isinstance(values, list):
        raise RuntimeError('invalid observer identity response')
    if any(not isinstance(item, list) or len(item) != 2 or not isinstance(item[0], str)
           or type(item[1]) is not bool for item in values):
        raise RuntimeError('invalid observer identity response')
    return {(sid, elevated) for sid, elevated in values}


def serve_observer(stream, nonce, identity, inspect):
    sid, elevated = identity
    if not elevated:
        raise RuntimeError('observer must run elevated')
    send_message(stream, {'kind': 'hello', 'nonce': nonce, 'sid': sid, 'elevated': elevated})
    while True:
        request = read_message(stream)
        if request.get('kind') == 'stop':
            return
        if request.get('kind') != 'poll':
            raise RuntimeError('unknown observer request')
        send_message(stream, {'kind': 'identities', 'identities': sorted(inspect())})


def observer_main(port, nonce):
    api = windows_api()
    identity = process_identity(api, api[0].GetCurrentProcess())
    with socket.create_connection(('127.0.0.1', port), timeout=30) as connection:
        connection.settimeout(30)
        with connection.makefile('rwb') as stream:
            serve_observer(stream, nonce, identity, lambda: wizard_identities(api))


def start_elevated_observer(expected_sid, timeout):
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        listener.bind(('127.0.0.1', 0))
        listener.listen(1)
        listener.settimeout(min(timeout, 120))
        nonce = secrets.token_urlsafe(32)
        params = subprocess.list2cmdline([str(Path(__file__).resolve()), '--elevated-observer',
                                         str(listener.getsockname()[1]), nonce])
        shell = ctypes.WinDLL('shell32', use_last_error=True)
        shell.ShellExecuteW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_wchar_p,
                                       ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_int]
        shell.ShellExecuteW.restype = ctypes.c_void_p
        print('Approve the observer UAC prompt as the expected setup administrator.')
        result = shell.ShellExecuteW(None, 'runas', sys.executable, params, None, 1)
        if not result or result <= 32:
            raise RuntimeError('could not start the elevated observer')
        connection, _ = listener.accept()
        try:
            connection.settimeout(30)
            stream = connection.makefile('rwb')
            try:
                authenticate_observer(stream, nonce, expected_sid)
                return connection, stream
            except BaseException:
                stream.close()
                raise
        except BaseException:
            connection.close()
            raise
    finally:
        listener.close()


def stop_observer(observer):
    connection, stream = observer
    try:
        send_message(stream, {'kind': 'stop'})
    except (OSError, ValueError):
        pass
    finally:
        stream.close()
        connection.close()


def observe_setup(proc, stream, timeout):
    seen = set()
    deadline = time.monotonic() + timeout
    while proc.poll() is None and time.monotonic() < deadline:
        seen.update(poll_observer(stream))
        time.sleep(0.1)
    return seen


def read_log(path):
    data = Path(path).read_bytes()
    if data.startswith((b'\xff\xfe', b'\xfe\xff')):
        return data.decode('utf-16')
    return data.decode('utf-8-sig')


def run(args):
    if os.name != 'nt':
        raise RuntimeError('The installer integration test requires Windows')
    if not args.disposable_vm:
        raise RuntimeError('Use only on a disposable VM: setup modifies install and registry state')
    api = windows_api()
    original_sid, original_elevated = process_identity(api, api[0].GetCurrentProcess())
    if original_elevated:
        raise RuntimeError('launch the harness from the original user without elevation')
    expected_sid = args.setup_sid or original_sid
    if (expected_sid == original_sid) != (args.identity == 'same'):
        raise ValueError('setup SID must match the original user for same, and differ for different')
    original = Path(os.environ['LOCALAPPDATA']) / 'ConditioningControlPanel'
    if args.identity == 'different' and not args.setup_localappdata:
        raise ValueError('different account needs --setup-localappdata for its real profile')
    setup_root = Path(args.setup_localappdata or os.environ['LOCALAPPDATA']) / 'ConditioningControlPanel'
    if (os.path.normcase(str(setup_root)) == os.path.normcase(str(original))) != (args.identity == 'same'):
        raise ValueError('setup and original profile paths do not match the requested identity')
    workspace = Path(args.workspace).resolve(strict=True)
    run_dir = Path(tempfile.mkdtemp(prefix='ccp-installer-test-', dir=workspace))
    override = run_dir / 'override'
    roots = list(dict.fromkeys([original, setup_root, override]))
    made_browser = []
    cleanup_safe = True
    # Never overwrite a real profile, its notices, or an earlier test's data.
    for root in roots:
        if any((root / name).exists() for name in ('browser_data', 'browser_data_avalonia', ACK, PENDING)):
            raise RuntimeError(f'profile is not empty: {root}')
    try:
        for root in roots:
            (root / 'browser_data').mkdir(parents=True)
            made_browser.append(root / 'browser_data')
        env = os.environ.copy()
        env.pop('CCP_USERDATA_DIR', None)
        target = override if args.data == 'override' else setup_root
        if args.data == 'override':
            env['CCP_USERDATA_DIR'] = str(override)
        log = run_dir / 'setup.log'
        cmd = [str(Path(args.setup).resolve(strict=True)), '/SP-', '/SUPPRESSMSGBOXES', '/NORESTART',
               '/LANG=english', '/DIR=' + str(run_dir / 'app'), '/LOG=' + str(log)]
        if args.mode == 'silent':
            cmd.append('/SILENT')
        observer = start_elevated_observer(expected_sid, args.timeout)
        try:
            print('Starting setup. Complete any Windows elevation prompt and installer wizard; do not launch CCP.')
            proc = subprocess.Popen(cmd, env=env)
            try:
                seen = observe_setup(proc, observer[1], args.timeout)
            except BaseException:
                cleanup_safe = proc.poll() is not None  # preserve fixtures while setup may be running
                raise
        finally:
            stop_observer(observer)
        if proc.poll() is None:
            cleanup_safe = False  # an elevated child can outlive the launching process
            proc.terminate()
            raise TimeoutError(f'setup timed out; stop it before removing test fixtures in {run_dir}')
        if proc.returncode != 0:
            raise AssertionError(f'setup exited {proc.returncode}; log: {log}')
        verify_observed_identity(original_sid, expected_sid, args.identity, seen)
        verify(args.mode, args.identity, roots, target, read_log(log))
        print(f'PASS: {args.mode}, {args.identity} account, {args.data} profile')
        print(f'Installer log: {log}')
    finally:
        if cleanup_safe:
            for root in roots:
                for name in (ACK, PENDING):
                    (root / name).unlink(missing_ok=True)
            for browser in made_browser:
                browser.rmdir()  # refuse recursive deletion if anything else appeared


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--setup', required=True, help='compiled installer from this checkout')
    parser.add_argument('--workspace', required=True, help='empty VM test directory writable by both accounts')
    parser.add_argument('--mode', required=True, choices=('interactive', 'silent'))
    parser.add_argument('--identity', required=True, choices=('same', 'different'))
    parser.add_argument('--data', required=True, choices=('default', 'override'))
    parser.add_argument('--setup-sid', help='SID of the elevated installer account (required if different)')
    parser.add_argument('--setup-localappdata', help='LOCALAPPDATA of the elevated installer account')
    parser.add_argument('--timeout', type=int, default=600)
    parser.add_argument('--disposable-vm', action='store_true')
    args = parser.parse_args()
    if args.identity == 'different' and not args.setup_sid:
        parser.error('different account requires --setup-sid')
    run(args)


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--elevated-observer':
        observer_main(int(sys.argv[2]), sys.argv[3])
    else:
        main()
