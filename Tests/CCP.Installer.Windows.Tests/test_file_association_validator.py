"""Portable checks for the Open with CCP installer test's expectations and validator."""
import unittest

import file_association_installer_test as assoc

EXE = r'C:\vm\app\ConditioningControlPanel.exe'
DEFAULTS = {ext: 'VLC.mp4' if ext == '.mp4' else None for ext in assoc.EXTENSIONS}


def registry(installed):
    rows = dict(assoc.expected(EXE)) if installed else {}
    rows.update({(f'Software\\Classes\\{ext}\\OpenWithProgids', assoc.FOREIGN): '' for ext in assoc.EXTENSIONS})
    rows.update({(f'Software\\Classes\\{ext}', ''): value for ext, value in DEFAULTS.items() if value})
    return rows


class FileAssociationTests(unittest.TestCase):
    def test_expectations_match_both_installer_scripts(self):
        for script in ('installer.iss', 'installer-avalonia.iss'):
            with self.subTest(script=script):
                rows = assoc.script_rows((assoc.REPO / script).read_text(encoding='utf-8'))
                self.assertEqual(assoc.expected(r'{app}\{#MyAppExeName}'), rows)
                self.assertEqual(34, len(rows))

    def test_installed_and_removed_states_pass(self):
        for installed in (True, False):
            rows = registry(installed)
            assoc.verify(lambda key, name: rows.get((key, name)), EXE, installed, DEFAULTS)

    def test_rejects_missing_wrong_leftover_foreign_and_default_changes(self):
        command = ('Software\\Classes\\CCPanel.Editor.1\\shell\\open\\command', '')
        cases = [
            (True, lambda rows: rows.pop(('Software\\Classes\\.ogg\\OpenWithProgids', 'CCPanel.Player.1'))),
            (True, lambda rows: rows.__setitem__(command, f'"{EXE}" --play "%1"')),
            (False, lambda rows: rows.__setitem__(command, f'"{EXE}" --edit "%1"')),
            (False, lambda rows: rows.pop(('Software\\Classes\\.mkv\\OpenWithProgids', assoc.FOREIGN))),
            (True, lambda rows: rows.__setitem__(('Software\\Classes\\.mp4', ''), 'CCPanel.Player.1')),
        ]
        for installed, damage in cases:
            rows = registry(installed)
            damage(rows)
            with self.subTest(installed=installed), self.assertRaises(AssertionError):
                assoc.verify(lambda key, name: rows.get((key, name)), EXE, installed, DEFAULTS)

    def test_launch_must_carry_the_role_flag_and_file(self):
        media = r'C:\vm\sample.mp4'
        assoc.verify_launch(f'"{EXE.lower()}" --play "{media}"', EXE, '--play', media)
        for wrong in (f'"{EXE}" --edit "{media}"', f'"{EXE}"', f'"{EXE}" --play {media}'):
            with self.subTest(wrong=wrong), self.assertRaises(AssertionError):
                assoc.verify_launch(wrong, EXE, '--play', media)


if __name__ == '__main__':
    unittest.main()
