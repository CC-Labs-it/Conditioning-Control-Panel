"""Keep failed Windows suites from hiding the WPF comparison artifacts (stdlib only)."""
from pathlib import Path
import re
import unittest

WORKFLOW = (Path(__file__).resolve().parents[1] / 'workflows/build.yml').read_text(encoding='utf-8')


class WpfRenderWorkflowTests(unittest.TestCase):
    def setUp(self):
        windows = WORKFLOW.split('\n  windows:\n', 1)[1]
        windows = re.split(r'\n  [\w-]+:\n', windows, maxsplit=1)[0]
        self.steps = re.split(r'(?=^      - )', windows, flags=re.M)

    def step(self, name):
        return next(s for s in self.steps if s.startswith('      - name: ' + name + '\n'))

    def test_render_runs_after_failures_only_when_build_succeeded(self):
        build = self.step('Build solution (hard gate)')
        render = self.step('Render WPF snapshot comparison rows (offscreen, sandbox profile)')
        self.assertIn('id: windows_build', build)
        # !cancelled() overrides GitHub's implicit success(), without running after cancellation.
        self.assertIn("if: ${{ !cancelled() && steps.windows_build.outcome == 'success' }}", render)
        self.assertNotIn('continue-on-error:', render)
        self.assertLess(self.steps.index(build), self.steps.index(render))

    def test_render_is_not_in_the_earlier_suite(self):
        suite = self.step('Test app suite')
        render = self.step('Render WPF snapshot comparison rows (offscreen, sandbox profile)')
        self.assertIn('--filter "FullyQualifiedName!~VerifiedRowRenderTests"', suite)
        self.assertIn('--filter FullyQualifiedName~VerifiedRowRenderTests', render)
        self.assertIn('--no-build', render)
        self.assertNotIn('continue-on-error:', suite)
        self.assertLess(self.steps.index(suite), self.steps.index(render))

    def test_surviving_renders_are_uploaded_from_the_same_directory(self):
        render = self.step('Render WPF snapshot comparison rows (offscreen, sandbox profile)')
        upload = next(s for s in self.steps if 'name: wpf-render\n' in s)
        self.assertIn('WPF_RENDER_OUT: ${{ runner.temp }}/wpf-render', render)
        self.assertIn('path: ${{ runner.temp }}/wpf-render/', upload)
        self.assertIn('if: always()', upload)
        self.assertIn('if-no-files-found: error', upload)
        self.assertLess(self.steps.index(render), self.steps.index(upload))


if __name__ == '__main__':
    unittest.main()
