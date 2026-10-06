#!/usr/bin/env python3
"""Check release selection without accessing GitHub or publishing a release."""

import copy
import importlib.util
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("release", Path(__file__).with_name("validate-release.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
COMMIT = "a" * 40
REPOSITORY = "example/FrogSmashersRebuilt"


class ReleaseChecks(unittest.TestCase):
    def setUp(self):
        self.run = {
            "path": ".github/workflows/build.yml",
            "status": "completed",
            "conclusion": "success",
            "event": "push",
            "head_repository": {"full_name": REPOSITORY},
            "head_sha": COMMIT,
        }

    def validate(self, run=None, refs=None, tag_object=None, run_id="123", tag="v1.0.0"):
        responses = [run if run is not None else self.run, refs or []]
        if tag_object is not None:
            responses.append(tag_object)
        with patch.object(release, "github_json", side_effect=responses):
            return release.validate_release(REPOSITORY, run_id, tag)

    def test_new_tag_uses_selected_commit(self):
        for event in ("push", "workflow_dispatch"):
            with self.subTest(event=event):
                self.run["event"] = event
                self.assertEqual(COMMIT, self.validate())

    def test_rejects_invalid_builds(self):
        for field, value in (
            ("path", ".github/workflows/release.yml"),
            ("status", "in_progress"),
            ("conclusion", "failure"),
            ("conclusion", "cancelled"),
            ("event", "pull_request"),
            ("head_repository", {"full_name": "someone/fork"}),
            ("head_sha", "main"),
        ):
            with self.subTest(field=field, value=value):
                run = copy.deepcopy(self.run)
                run[field] = value
                with self.assertRaises(ValueError):
                    self.validate(run=run)

    def test_checks_existing_lightweight_and_annotated_tags(self):
        for commit in (COMMIT, "b" * 40):
            for annotated in (False, True):
                with self.subTest(commit=commit, annotated=annotated):
                    target = {"type": "commit", "sha": commit}
                    ref = {"ref": "refs/tags/v1.0.0", "object": target}
                    if annotated:
                        ref["object"] = {"type": "tag", "sha": "c" * 40}
                    if commit == COMMIT:
                        self.assertEqual(COMMIT, self.validate(refs=[ref], tag_object={"object": target}))
                    else:
                        with self.assertRaises(ValueError):
                            self.validate(refs=[ref], tag_object={"object": target})

    def test_does_not_confuse_tag_prefixes(self):
        ref = {"ref": "refs/tags/v1.0.0-rc1", "object": {"type": "commit", "sha": "b" * 40}}
        self.assertEqual(COMMIT, self.validate(refs=[ref]))

    def test_rejects_bad_inputs_before_accessing_github(self):
        for run_id, tag in (("abc", "v1"), ("123\n", "v1"), ("123", ""), ("123", "-v1"),
                            ("123", "bad tag"), ("123", "v1\nother"), ("123", "../v1")):
            with self.subTest(run_id=run_id, tag=tag), patch.object(release, "github_json") as api:
                with self.assertRaises(ValueError):
                    release.validate_release(REPOSITORY, run_id, tag)
                api.assert_not_called()


if __name__ == "__main__":
    unittest.main()
