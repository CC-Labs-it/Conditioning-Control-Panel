"""Exercise coverage-job retry routing and its real shell gate, without GitHub/network access."""
from pathlib import Path
import re
import subprocess
import tempfile
import textwrap
import unittest

WORKFLOW = Path(__file__).resolve().parents[1] / "workflows/build.yml"


class CoverageJobTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        job = WORKFLOW.read_text().split("  avalonia-coverage:\n", 1)[1].split("\n  # build-content", 1)[0]
        # Only read the job's step mappings, not the rest of the workflow YAML.
        steps = re.split(r"(?m)^      - ", job.split("    steps:\n", 1)[1])[1:]
        cls.downloads = []
        for step in steps:
            fields = dict(re.findall(r"(?m)^(?:        )?([\w-]+): ([^\n]+)$", step))
            if fields.get("uses", "").startswith("actions/download-artifact@"):
                cls.downloads.append(fields)
        gate = next(step for step in steps if step.startswith("name: Shard totals"))
        cls.gate = textwrap.dedent(gate.split("        run: |\n", 1)[1])

    def run_downloads(self, results):
        outcomes, calls, success = {}, 0, True
        for step in self.downloads:
            if not success:
                break  # GitHub's implicit success() condition.
            if condition := step.get("if"):
                match = re.fullmatch(r"steps\.([\w-]+)\.outcome == 'failure'", condition)
                self.assertIsNotNone(match, "unsupported retry condition")
                if outcomes.get(match[1]) != "failure":
                    continue
            result = results[calls]
            calls += 1
            outcomes[step.get("id")] = "success" if result else "failure"
            if not result and step.get("continue-on-error") != "true":
                success = False
        return calls, success

    def test_first_success_does_not_download_again(self):
        self.assertEqual((1, True), self.run_downloads([True]))

    def test_transient_failure_recovers(self):
        self.assertEqual((2, True), self.run_downloads([False, True]))

    def test_two_failures_recover_on_last_attempt(self):
        self.assertEqual((3, True), self.run_downloads([False, False, True]))

    def test_exhausted_downloads_still_fail_the_job(self):
        self.assertEqual((3, False), self.run_downloads([False, False, False]))

    def run_gate(self, totals):
        with tempfile.TemporaryDirectory() as root:
            for i, total in enumerate(totals):
                folder = Path(root) / "shards" / f"avalonia-linux-shard-{i}"
                folder.mkdir(parents=True)
                (folder / "avalonia-expected.txt").write_text("100\n")
                (folder / f"avalonia-shard-{i}.total").write_text(f"{total}\n")
            # The only expression in this bash step is the OS name used in its log.
            return subprocess.run(["bash", "-c", self.gate.replace("${{ matrix.os }}", "linux")],
                                  cwd=root, capture_output=True, text=True)

    def test_all_shards_are_counted(self):
        result = self.run_gate([20] * 5)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("100 of 100 tests (5 shard files)", result.stdout)

    def test_missing_shard_fails(self):
        self.assertNotEqual(0, self.run_gate([20] * 4).returncode)

    def test_mismatched_total_fails(self):
        self.assertNotEqual(0, self.run_gate([20, 20, 19, 20, 20]).returncode)


if __name__ == "__main__":
    unittest.main()
