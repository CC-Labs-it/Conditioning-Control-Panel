"""Portable checks for the Windows integration test's result validator."""
import io
import json
import socket
import tempfile
import threading
import unittest
from unittest import mock
from pathlib import Path

import browser_reset_installer_test as installer


class ValidatorTests(unittest.TestCase):
    def test_eight_mode_identity_and_profile_combinations(self):
        for mode in ('interactive', 'silent'):
            for identity in ('same', 'different'):
                for data in ('default', 'override'):
                    with self.subTest(mode=mode, identity=identity, data=data), tempfile.TemporaryDirectory() as tmp:
                        roots = [Path(tmp) / name for name in ('original', 'admin', 'override')]
                        for root in roots:
                            root.mkdir()
                        target = roots[2] if data == 'override' else roots[0 if identity == 'same' else 1]
                        log = ''
                        if mode == 'silent':
                            (target / installer.PENDING).write_text('pending')
                        elif identity == 'same':
                            (target / installer.ACK).write_text('{}')
                            log = installer.COMPARISON + '0'
                        else:
                            log = installer.COMPARISON + '1'
                        installer.verify(mode, identity, roots, target, log)

    def test_rejects_missing_wrong_user_and_wrong_contents(self):
        with tempfile.TemporaryDirectory() as tmp:
            roots = [Path(tmp) / 'original', Path(tmp) / 'admin']
            for root in roots:
                root.mkdir()
            with self.assertRaises(AssertionError):
                installer.verify('interactive', 'same', roots, roots[0], installer.COMPARISON + '0')
            (roots[1] / installer.ACK).write_text('{}')
            with self.assertRaises(AssertionError):
                installer.verify('interactive', 'different', roots, roots[1], installer.COMPARISON + '1')
            (roots[1] / installer.ACK).unlink()
            (roots[0] / installer.ACK).write_text('incorrect')
            with self.assertRaises(AssertionError):
                installer.verify('interactive', 'same', roots, roots[0], installer.COMPARISON + '0')

    def test_silent_requires_pending_in_the_correct_profile(self):
        with tempfile.TemporaryDirectory() as tmp:
            roots = [Path(tmp) / 'original', Path(tmp) / 'admin']
            for root in roots:
                root.mkdir()
            with self.assertRaises(AssertionError):
                installer.verify('silent', 'same', roots, roots[0], '')
            (roots[0] / installer.PENDING).write_text('bad')
            with self.assertRaises(AssertionError):
                installer.verify('silent', 'same', roots, roots[0], '')
            (roots[0] / installer.PENDING).write_text('pending')
            (roots[1] / installer.PENDING).write_text('pending')
            with self.assertRaises(AssertionError):
                installer.verify('silent', 'same', roots, roots[0], '')

    def test_rejects_failed_or_wrong_identity_command_and_silent_execution(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / installer.ACK).write_text('{}')
            with self.assertRaises(AssertionError):
                installer.verify('interactive', 'same', [root], root, installer.COMPARISON + '0\n' + installer.FAILURE)
            with self.assertRaises(AssertionError):
                installer.verify('interactive', 'same', [root], root, installer.COMPARISON + '1')
            with self.assertRaises(AssertionError):
                installer.verify('interactive', 'same', [root], root, installer.COMPARISON + '01')
            (root / installer.ACK).unlink()
            (root / installer.PENDING).write_text('pending')
            with self.assertRaises(AssertionError):
                installer.verify('silent', 'same', [root], root, installer.COMPARISON + '0')

    def test_identity_verification_fails_closed(self):
        installer.verify_observed_identity('S-1', 'S-1', 'same', {('S-1', True)})
        installer.verify_observed_identity('S-1', 'S-2', 'different', {('S-1', False), ('S-2', True)})
        with self.assertRaises(AssertionError):
            installer.verify_observed_identity('S-1', 'S-2', 'different', {('S-1', False)})
        with self.assertRaises(AssertionError):
            installer.verify_observed_identity('S-1', 'S-2', 'different', {('S-2', False)})
        with self.assertRaises(AssertionError):
            installer.verify_observed_identity('S-1', 'S-1', 'same', {('S-1', True), ('S-2', True)})
        with self.assertRaises(ValueError):
            installer.verify_observed_identity('S-1', 'S-1', 'different', {('S-1', True)})

    def test_elevated_observer_monitors_when_original_user_cannot_query(self):
        original, elevated = socket.socketpair()
        original.settimeout(2); elevated.settimeout(2)
        errors = []
        calls = []

        def inspect():
            calls.append(True)
            return {('S-2', True)}

        def serve():
            try:
                with elevated.makefile('rwb') as stream:
                    installer.serve_observer(stream, 'nonce', ('S-2', True), inspect)
            except Exception as exc:
                errors.append(exc)

        thread = threading.Thread(target=serve, daemon=True)
        thread.start()
        try:
            with original.makefile('rwb') as stream:
                installer.authenticate_observer(stream, 'nonce', 'S-2')
                proc = mock.Mock()
                proc.poll.side_effect = [None, 0]
                with mock.patch.object(installer, 'wizard_identities', side_effect=PermissionError('access denied')), \
                     mock.patch.object(installer, 'windows_api', return_value=None), mock.patch.object(installer.time, 'sleep'):
                    seen = installer.observe_setup(proc, stream, 2)
                installer.verify_observed_identity('S-1', 'S-2', 'different', seen)
                self.assertEqual([True], calls)
                installer.send_message(stream, {'kind': 'stop'})
            thread.join(2)
            self.assertFalse(thread.is_alive())
            self.assertEqual([], errors)
        finally:
            original.close(); elevated.close()
            thread.join(2)

    def test_observer_authentication_fails_closed(self):
        hello = {'kind': 'hello', 'nonce': 'nonce', 'sid': 'S-2', 'elevated': True}
        def stream(value):
            return io.BytesIO((json.dumps(value) + '\n').encode())
        installer.authenticate_observer(stream(hello), 'nonce', 'S-2')
        for key, value in [('nonce', 'wrong'), ('sid', 'S-1'), ('elevated', False), ('kind', 'other')]:
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                installer.authenticate_observer(stream(dict(hello, **{key: value})), 'nonce', 'S-2')
        with self.assertRaises(RuntimeError):
            installer.serve_observer(io.BytesIO(), 'nonce', ('S-2', False), lambda: set())

    def test_harness_rejects_elevated_original_user(self):
        api = (mock.Mock(), None, None, None)
        class Arguments:
            disposable_vm = True
            @property
            def setup_sid(self): raise RuntimeError('guard bypassed')
        with mock.patch.object(installer.os, 'name', 'nt'), mock.patch.object(installer, 'windows_api', return_value=api), \
             mock.patch.object(installer, 'process_identity', return_value=('S-1', True)), self.assertRaisesRegex(RuntimeError, 'without elevation'):
            installer.run(Arguments())

    def test_observer_failure_does_not_become_an_empty_result(self):
        proc = mock.Mock()
        proc.poll.return_value = None
        with mock.patch.object(installer, 'poll_observer', side_effect=ConnectionError('observer lost')), self.assertRaises(ConnectionError):
            installer.observe_setup(proc, None, 2)

    def test_rejects_disconnected_or_malformed_observer(self):
        with self.assertRaises(RuntimeError):
            installer.read_message(io.BytesIO(b''))
        for reply in ({'kind': 'other', 'identities': []}, {'kind': 'identities', 'identities': [['S-2', 1]]}):
            with self.subTest(reply=reply), mock.patch.object(installer, 'send_message'), \
                 mock.patch.object(installer, 'read_message', return_value=reply), self.assertRaises(RuntimeError):
                installer.poll_observer(None)


if __name__ == '__main__':
    unittest.main()
