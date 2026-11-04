"""Portable checks for the Windows integration test's result validator."""
import tempfile
import unittest
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


if __name__ == '__main__':
    unittest.main()
