#!/usr/bin/env python3
"""Oracle for packet P-GH-1 (the repository front page), checked against the real repository.

Run: python3 -m unittest scripts/test_repo_front_page.py

Covers acceptance checks 2 to 5 of docs/backlog/packets/P-GH-1.md: the two moved paths (identical blobs),
the linguist attributes, the pull-request template and the readability check on the finished tree. The root
rule itself (check 1) is tested in scripts/test_readability_check.py.
"""

import os
import re
import subprocess
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
BASE = "e0f257a29"  # the brief's commit: the tree before the moves

OLD_DIR = "Unfinished code"
NEW_DIR = "docs/legacy/saved-work"
OLD_FILE = "DeepSeek_post_rust_development.md"
NEW_FILE = "docs/agent-prompts/7_DeepSeek_Post_Rust_Development.md"
SAVED_WORK_FILES = ["README.md", "SENTENCED_REDESIGN.md", "opt.py", "opt2.py", "optlib.py", "sentenced-workflow.js"]


def git(*args, stdin=None):
    out = subprocess.run(["git", "-C", ROOT, *args], input=stdin, capture_output=True, check=True)
    return out.stdout


def tracked(*pathspecs):
    out = git("ls-files", "-z", "--", *pathspecs)
    return [n.decode("utf-8") for n in out.split(b"\0") if n]


def blob_at_base(path):
    return git("rev-parse", "%s:%s" % (BASE, path)).decode().strip()


def blob_now(path):
    return git("hash-object", "--", os.path.join(ROOT, path)).decode().strip()


def read(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as f:
        return f.read()


def attr(name, paths):
    """{path: value} of one attribute, for any paths (tracked or not)."""
    stdin = b"".join(p.encode("utf-8") + b"\0" for p in paths)
    parts = git("check-attr", "-z", "--stdin", name, stdin=stdin).split(b"\0")
    return {parts[i].decode("utf-8"): parts[i + 2].decode() for i in range(0, len(parts) - 2, 3)}


class MovedPaths(unittest.TestCase):
    def test_unfinished_code_files_moved_with_identical_blobs(self):
        for name in SAVED_WORK_FILES:
            old, new = OLD_DIR + "/" + name, NEW_DIR + "/" + name
            with self.subTest(file=name):
                self.assertEqual(tracked(new), [new], "not tracked at the new path")
                self.assertEqual(blob_now(new), blob_at_base(old))

    def test_unfinished_code_no_longer_tracked_and_nothing_extra_at_the_new_home(self):
        self.assertEqual(tracked(OLD_DIR), [])
        self.assertEqual(sorted(tracked(NEW_DIR)), sorted(NEW_DIR + "/" + n for n in SAVED_WORK_FILES))

    def test_deepseek_prompt_moved_with_identical_blob(self):
        self.assertEqual(tracked(OLD_FILE), [])
        self.assertEqual(tracked(NEW_FILE), [NEW_FILE])
        self.assertEqual(blob_now(NEW_FILE), blob_at_base(OLD_FILE))

    def test_references_follow_the_moves(self):
        self.assertNotIn(OLD_DIR + "/", read("docs/legacy/INVENTORY.md"))
        self.assertNotIn(OLD_DIR + "/", read("testing/TUNING_SENTENCED.md"))
        self.assertNotIn(OLD_FILE, read("docs/agent/REVIEW-2026-11-10.md"))
        self.assertIn("7_DeepSeek_Post_Rust_Development.md", read("docs/agent-prompts/README.md"))

    def test_new_folder_readmes_exist(self):
        for rel in ("crates/README.md", "deployment/README.md", "testing/README.md"):
            with self.subTest(file=rel):
                self.assertEqual(tracked(rel), [rel])


class LanguageAttributes(unittest.TestCase):
    SAMPLES = {
        "linguist-vendored": [
            "packages/server/src/index.ts",
            "client-unity/UnityProject/Assets/Art/ThirdParty/anything/File.png",
            "client-unity/UnityProject/Assets/Art/ThirdParty/x.cs",
        ],
        "linguist-generated": [
            "parity/card-runtime/source.json",
            "parity/some/deeper/capture.json",
            "protocol/fixtures/v1.json",
            "protocol/fixtures/nested/file.bin",
            "crates/rh-sim/src/mapgen/fixtures/map-repair.json",
            "client-unity/UnityProject/Assets/Scripts/BakedContent.cs",
        ],
        "linguist-documentation": [
            "docs/evidence/anything/report.html",
            "docs/evidence/a/b/c.json",
        ],
    }
    NOT_FLAGGED = [
        "crates/rh-sim/src/lib.rs",
        "client-unity/RoguehallaNet/README.md",
        "scripts/gate.sh",
    ]

    def test_attributes_are_set_on_the_sample_paths(self):
        for name, paths in self.SAMPLES.items():
            for path, value in attr(name, paths).items():
                with self.subTest(attribute=name, path=path):
                    self.assertEqual(value, "set")

    def test_attributes_cover_every_tracked_file_of_each_group(self):
        groups = [
            ("linguist-generated", [p for p in tracked("parity") if p.endswith(".json")]),
            ("linguist-generated", [p for p in tracked("protocol/fixtures") if not p.endswith(".md")]),
            ("linguist-generated", [p for p in tracked("crates/rh-sim/src/mapgen/fixtures") if p.endswith(".json")]),
            ("linguist-documentation", tracked("docs/evidence")),
        ]
        for name, paths in groups:
            self.assertTrue(paths, name)
            unset = [p for p, v in attr(name, paths).items() if v != "set"]
            self.assertEqual(unset, [], "%s not set on %d files, e.g. %s" % (name, len(unset), unset[:3]))

    def test_source_and_documents_stay_unflagged(self):
        for name in self.SAMPLES:
            for path, value in attr(name, self.NOT_FLAGGED).items():
                with self.subTest(attribute=name, path=path):
                    self.assertNotEqual(value, "set")

    def test_existing_whitespace_attributes_are_kept(self):
        path = "docs/evidence/unity-functional-035b22c8/editor-first/source-overlays.patch"
        self.assertEqual(attr("whitespace", [path])[path], "-blank-at-eol")


class PullRequestTemplate(unittest.TestCase):
    TEMPLATE = ".github/pull_request_template.md"

    @staticmethod
    def report_labels():
        """The field labels of the hand-off report block in AGENTS.md (the fenced block starting `PACKET:`)."""
        for block in re.findall(r"```\n(.*?)```", read("AGENTS.md"), re.S):
            if block.startswith("PACKET:"):
                return re.findall(r"^([A-Z][A-Z -]*:)", block, re.M)
        raise AssertionError("no hand-off report block in AGENTS.md")

    def test_the_agents_md_block_has_the_expected_shape(self):
        labels = self.report_labels()
        self.assertEqual(labels[0], "PACKET:")
        self.assertEqual(labels[-1], "RISKS:")
        self.assertGreaterEqual(len(labels), 8)

    def test_template_carries_every_field_in_order(self):
        self.assertTrue(os.path.isfile(os.path.join(ROOT, self.TEMPLATE)), self.TEMPLATE + " is missing")
        text = read(self.TEMPLATE)
        pos = 0
        for label in self.report_labels():
            m = re.compile(r"^" + re.escape(label), re.M).search(text, pos)
            self.assertIsNotNone(m, "%s missing or out of order after offset %d" % (label, pos))
            pos = m.end()

    def test_template_opens_with_a_line_asking_for_the_packet_id(self):
        self.assertTrue(os.path.isfile(os.path.join(ROOT, self.TEMPLATE)), self.TEMPLATE + " is missing")
        text = read(self.TEMPLATE)
        before = text[:text.index("PACKET:")]
        self.assertTrue(before.strip(), "no line before the report block")
        self.assertIn("packet", before.lower())


class ReadabilityOnTheFinishedTree(unittest.TestCase):
    def test_readability_check_passes(self):
        res = subprocess.run([sys.executable, os.path.join(HERE, "readability_check.py"), "--root", ROOT],
                             capture_output=True, text=True, timeout=120)
        self.assertEqual(res.returncode, 0, res.stdout + res.stderr)
        self.assertRegex(res.stdout.strip().splitlines()[-1], r"^readability: OK \(.*\)$")


if __name__ == "__main__":
    unittest.main()
