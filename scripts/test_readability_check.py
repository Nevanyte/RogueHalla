#!/usr/bin/env python3
"""Oracle for scripts/readability_check.py (packet P-READ-0).

Run: python3 -m unittest scripts/test_readability_check.py

Every test builds a small temporary git repository (git init, write files, git add), runs the
checker on it as a subprocess with --root and --allowlist, and asserts on exit code and output.
The standard being enforced is docs/agent/READABILITY.md.
"""

import os
import re
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "readability_check.py")

RS_LIMIT = 1500
CS_LIMIT = 800

RS_BASE = "crates/foo/src/lib.rs"
CS_BASE = "client-unity/UnityProject/Assets/Scripts/Thing.cs"
CS_NET = "client-unity/RoguehallaNet/Codec.cs"
SYNCED_CS = "client-unity/UnityProject/Assets/RoguehallaNet/Codec.cs"

SUMMARY_OK = re.compile(r"^readability: OK \(.*\)$")


def rs_file(lines, header="//! A module.\n"):
    """A Rust file of exactly `lines` lines (header included)."""
    head = header.count("\n")
    return header + "let x = 1;\n" * (lines - head)


def cs_file(lines):
    """A C# file of exactly `lines` lines, with a summary before its type."""
    head = "/// <summary>\n/// A thing.\n/// </summary>\npublic class Thing {}\n"
    return head + "// pad\n" * (lines - head.count("\n"))


class Result:
    def __init__(self, proc):
        self.code = proc.returncode
        self.out = proc.stdout
        self.err = proc.stderr
        self.text = proc.stdout + proc.stderr
        nonblank = [l for l in proc.stdout.splitlines() if l.strip()]
        self.last = nonblank[-1] if nonblank else ""


class Case(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.base = self._tmp.name
        self.root = os.path.join(self.base, "repo")
        self.allow_path = os.path.join(self.base, "allow.txt")
        os.makedirs(self.root)
        subprocess.run(["git", "init", "-q", self.root], check=True)
        self.allow("")

    # ----- building the repository -----
    def write(self, rel, content, track=True):
        path = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", newline="") as f:
            f.write(content)
        if track:
            subprocess.run(["git", "-C", self.root, "add", "-f", rel], check=True)

    def allow(self, text):
        with open(self.allow_path, "w") as f:
            f.write(text)

    def clean_crate(self):
        """crates/foo with a README and a headed lib.rs: no violations."""
        self.write("crates/foo/README.md", "# foo\n")
        self.write(RS_BASE, rs_file(3))

    # ----- running the checker -----
    def run_check(self, *extra, allowlist=True):
        cmd = [sys.executable, SCRIPT, "--root", self.root]
        if allowlist:
            cmd += ["--allowlist", self.allow_path]
        cmd += list(extra)
        return Result(subprocess.run(cmd, capture_output=True, text=True, timeout=60))

    # ----- assertions -----
    def assertOk(self, res, msg=None):
        self.assertEqual(res.code, 0, msg or "expected exit 0, output:\n" + res.text)
        self.assertRegex(res.last, SUMMARY_OK, msg)

    def assertFails(self, res, problems=None, line=None):
        self.assertEqual(res.code, 1, "expected exit 1, output:\n" + res.text)
        self.assertRegex(res.last, r"^readability: FAIL \(\d+ problems\)$")
        if problems is not None:
            self.assertEqual(res.last, "readability: FAIL (%d problems)" % problems, res.text)
        if line is not None:
            self.assertRegex(res.out, r"(?m)^" + line, "no problem line matching %r in:\n%s" % (line, res.out))


# ======================================================================
# The four rules and their path scopes
# ======================================================================
class RustSizeRule(Case):
    def test_size_rust_at_limit_passes(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT))
        self.assertOk(self.run_check())

    def test_size_rust_over_limit_fails_unlisted(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 1))
        self.assertFails(self.run_check(), 1, r"size crates/foo/src/big\.rs: ")

    def test_size_counts_final_line_without_newline(self):
        self.clean_crate()
        content = rs_file(RS_LIMIT + 1).rstrip("\n")
        self.assertFalse(content.endswith("\n"))
        self.write("crates/foo/src/big.rs", content)
        self.assertFails(self.run_check(), 1, r"size crates/foo/src/big\.rs: ")

    def test_size_final_line_without_newline_at_limit_passes(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT).rstrip("\n"))
        self.assertOk(self.run_check())

    def test_size_counts_whole_file_including_inline_tests(self):
        self.clean_crate()
        body = rs_file(10) + "#[cfg(test)]\nmod tests {\n" + "    // t\n" * RS_LIMIT + "}\n"
        self.write("crates/foo/src/inline.rs", body)
        self.assertFails(self.run_check(), 1, r"size crates/foo/src/inline\.rs: ")

    def test_size_rule_covers_nested_src_modules(self):
        self.clean_crate()
        self.write("crates/foo/src/a/b/c.rs", rs_file(RS_LIMIT + 1))
        self.assertFails(self.run_check(), 1, r"size crates/foo/src/a/b/c\.rs: ")

    def test_size_rule_ignores_rust_outside_crate_src(self):
        self.clean_crate()
        self.write("crates/foo/build.rs", rs_file(RS_LIMIT + 5, header=""))
        self.write("crates/foo/benches/b.rs", rs_file(RS_LIMIT + 5, header=""))
        self.write("tools/x/helper.rs", rs_file(RS_LIMIT + 5, header=""))
        self.write("tools/README.md", "# t\n")
        self.assertOk(self.run_check())


class CSharpSizeRule(Case):
    def test_size_csharp_at_limit_passes(self):
        self.write(CS_BASE, cs_file(CS_LIMIT))
        self.write("client-unity/UnityProject/README.md", "# u\n")
        self.assertOk(self.run_check())

    def test_size_csharp_over_limit_fails_unlisted(self):
        self.write(CS_BASE, cs_file(CS_LIMIT + 1))
        self.write("client-unity/UnityProject/README.md", "# u\n")
        self.assertFails(self.run_check(), 1, r"size " + re.escape(CS_BASE) + ": ")

    def test_size_csharp_limit_is_not_the_rust_limit(self):
        self.write(CS_NET, cs_file(CS_LIMIT + 1))
        self.write("client-unity/RoguehallaNet/README.md", "# n\n")
        self.assertFails(self.run_check(), 1, r"size " + re.escape(CS_NET) + ": ")

    def test_size_rust_limit_is_not_the_csharp_limit(self):
        self.clean_crate()
        self.write("crates/foo/src/mid.rs", rs_file(CS_LIMIT + 100))
        self.assertOk(self.run_check())


class TestFileExclusion(Case):
    def over_and_headerless(self):
        return "let x = 1;\n" * (RS_LIMIT + 5)

    def test_tests_dir_excluded_from_rules(self):
        self.clean_crate()
        self.write("crates/foo/tests/it.rs", self.over_and_headerless())
        self.assertOk(self.run_check())

    def test_tests_dir_inside_src_excluded(self):
        self.clean_crate()
        self.write("crates/foo/src/tests/case.rs", self.over_and_headerless())
        self.assertOk(self.run_check())

    def test_tests_rs_excluded(self):
        self.clean_crate()
        self.write("crates/foo/src/room/tests.rs", self.over_and_headerless())
        self.assertOk(self.run_check())

    def test_suffix_tests_rs_excluded(self):
        self.clean_crate()
        self.write("crates/foo/src/room_tests.rs", self.over_and_headerless())
        self.assertOk(self.run_check())

    def test_similar_name_not_a_test_file(self):
        self.clean_crate()
        self.write("crates/foo/src/attests.rs", "let x = 1;\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/attests\.rs: ")

    def test_csharp_Tests_dir_excluded(self):
        self.write("client-unity/UnityProject/README.md", "# u\n")
        self.write("client-unity/UnityProject/Assets/EditModeTests/A.cs", "class A {}\n" + "// pad\n" * 900)
        self.assertOk(self.run_check())

    def test_csharp_dot_Tests_dir_excluded(self):
        self.write("client-unity/RoguehallaNet/README.md", "# n\n")
        self.write("client-unity/RoguehallaNet/Net.Tests/A.cs", "class A {}\n" + "// pad\n" * 900)
        self.assertOk(self.run_check())

    def test_synced_unity_copy_excluded(self):
        self.write("client-unity/UnityProject/README.md", "# u\n")
        self.write(SYNCED_CS, "class Codec {}\n" + "// pad\n" * 900)
        self.assertOk(self.run_check())

    def test_source_copy_of_synced_file_not_excluded(self):
        self.write("client-unity/RoguehallaNet/README.md", "# n\n")
        self.write(CS_NET, "class Codec {}\n")
        self.assertFails(self.run_check(), 1, r"header " + re.escape(CS_NET) + ": ")


class RustHeaderRule(Case):
    def test_header_present_passes(self):
        self.clean_crate()
        self.assertOk(self.run_check())

    def test_header_missing_fails_unlisted(self):
        self.clean_crate()
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/bare\.rs: ")

    def test_header_after_leading_blank_lines_passes(self):
        self.clean_crate()
        self.write("crates/foo/src/lead.rs", "\n\n  \n//! After blanks.\npub fn f() {}\n")
        self.assertOk(self.run_check())

    def test_header_plain_comment_does_not_count(self):
        self.clean_crate()
        self.write("crates/foo/src/plain.rs", "// not a module doc\npub fn f() {}\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/plain\.rs: ")

    def test_header_outer_doc_comment_does_not_count(self):
        self.clean_crate()
        self.write("crates/foo/src/outer.rs", "/// item doc\npub fn f() {}\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/outer\.rs: ")

    def test_header_after_code_does_not_count(self):
        self.clean_crate()
        self.write("crates/foo/src/late.rs", "use std::fmt;\n//! too late\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/late\.rs: ")

    def test_included_fragment_takes_a_plain_comment_header(self):
        self.clean_crate()
        self.write("crates/foo/src/host.rs", '//! Host.\ninclude!("host/part.rs");\n')
        self.write("crates/foo/src/host/part.rs", "// Fragment included by host.rs.\nfn g() {}\n")
        self.assertOk(self.run_check())

    def test_included_fragment_without_comment_fails(self):
        self.clean_crate()
        self.write("crates/foo/src/host.rs", '//! Host.\ninclude!("host/part.rs");\n')
        self.write("crates/foo/src/host/part.rs", "fn g() {}\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/host/part\.rs: ")

    def test_header_empty_file_fails(self):
        self.clean_crate()
        self.write("crates/foo/src/empty.rs", "")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/empty\.rs: ")


class CSharpSummaryRule(Case):
    def setUp(self):
        super().setUp()
        self.write("client-unity/UnityProject/README.md", "# u\n")

    def cs(self, content):
        self.write(CS_BASE, content)
        return self.run_check()

    def test_summary_before_class_passes(self):
        self.assertOk(self.cs("/// <summary>\n/// x\n/// </summary>\npublic class A {}\n"))

    def test_summary_missing_fails_unlisted(self):
        self.assertFails(self.cs("public class A {}\n"), 1, r"header " + re.escape(CS_BASE) + ": ")

    def test_summary_after_type_does_not_count(self):
        res = self.cs("public class A {}\n/// <summary>\n/// x\n/// </summary>\npublic int F;\n")
        self.assertFails(res, 1, r"header " + re.escape(CS_BASE) + ": ")

    def test_summary_after_using_lines_passes(self):
        res = self.cs("using System;\nusing UnityEngine;\n\nnamespace N\n{\n    /// <summary>\n    /// x\n"
                      "    /// </summary>\n    public sealed class A {}\n}\n")
        self.assertOk(res)

    def test_summary_before_attribute_and_static_class_passes(self):
        self.assertOk(self.cs("/// <summary>\n/// x\n/// </summary>\n[Serializable]\npublic static class A {}\n"))

    def test_summary_before_enum_passes(self):
        self.assertOk(self.cs("/// <summary>x</summary>\npublic enum E { A }\n"))

    def test_summary_before_struct_passes(self):
        self.assertOk(self.cs("/// <summary>x</summary>\ninternal struct S {}\n"))

    def test_summary_before_interface_passes(self):
        self.assertOk(self.cs("/// <summary>x</summary>\npublic interface I {}\n"))

    def test_summary_before_record_passes(self):
        self.assertOk(self.cs("/// <summary>x</summary>\npublic record R(int A);\n"))

    def test_summary_after_first_of_two_types_does_not_count(self):
        res = self.cs("public class A {}\n/// <summary>x</summary>\npublic class B {}\n")
        self.assertFails(res, 1, r"header " + re.escape(CS_BASE) + ": ")

    def test_summary_before_partial_class_passes(self):
        self.assertOk(self.cs("/// <summary>x</summary>\npublic partial class A {}\n"))

    def test_summary_rule_applies_to_roguehalla_net_source(self):
        self.write("client-unity/RoguehallaNet/README.md", "# n\n")
        self.write(CS_NET, "public class Codec {}\n")
        self.write(CS_BASE, "/// <summary>x</summary>\npublic class A {}\n")
        self.assertFails(self.run_check(), 1, r"header " + re.escape(CS_NET) + ": ")


class ReadmeRule(Case):
    FIXED = ["client-unity/RoguehallaNet", "client-unity/UnityProject", "hosting", "tools", "scripts",
             "parity", "protocol", "docs"]

    def test_readme_missing_in_crate_fails_unlisted(self):
        self.write(RS_BASE, rs_file(3))
        self.assertFails(self.run_check(), 1, r"readme crates/foo/?: ")

    def test_readme_present_in_crate_passes(self):
        self.clean_crate()
        self.assertOk(self.run_check())

    def test_readme_required_in_each_listed_directory(self):
        for d in self.FIXED:
            with self.subTest(dir=d):
                self.tearDown_repo()
                self.write(d + "/notes.txt", "x\n")
                self.assertFails(self.run_check(), 1, r"readme " + re.escape(d) + r"/?: ")

    def test_readme_present_in_each_listed_directory_passes(self):
        for d in self.FIXED:
            with self.subTest(dir=d):
                self.tearDown_repo()
                self.write(d + "/notes.txt", "x\n")
                self.write(d + "/README.md", "# r\n")
                self.assertOk(self.run_check())

    def test_readme_not_required_in_other_directories(self):
        self.write("misc/notes.txt", "x\n")
        self.write("crates/stray.txt", "x\n")
        self.write("client-unity/loose.txt", "x\n")
        self.write("README.md", "# root\n")
        self.assertOk(self.run_check())

    def test_readme_must_be_tracked(self):
        self.write(RS_BASE, rs_file(3))
        self.write("crates/foo/README.md", "# foo\n", track=False)
        self.assertFails(self.run_check(), 1, r"readme crates/foo/?: ")

    def test_readme_in_subdirectory_does_not_satisfy_parent(self):
        self.write(RS_BASE, rs_file(3))
        self.write("crates/foo/src/README.md", "# s\n")
        self.assertFails(self.run_check(), 1, r"readme crates/foo/?: ")

    def tearDown_repo(self):
        """Fresh empty repository for the next sub-case."""
        subprocess.run(["git", "-C", self.root, "rm", "-rq", "--cached", "--ignore-unmatch", "."], check=True)
        for dirpath, dirnames, filenames in os.walk(self.root, topdown=False):
            if ".git" in dirpath.split(os.sep):
                continue
            for n in filenames:
                os.remove(os.path.join(dirpath, n))
            for n in dirnames:
                if n != ".git":
                    p = os.path.join(dirpath, n)
                    if os.path.isdir(p) and not os.listdir(p):
                        os.rmdir(p)


class UntrackedFiles(Case):
    def test_untracked_rust_violations_ignored(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", "let x = 1;\n" * (RS_LIMIT + 5), track=False)
        self.assertOk(self.run_check())

    def test_untracked_directory_needs_no_readme(self):
        self.clean_crate()
        self.write("crates/bar/src/lib.rs", "pub fn f() {}\n", track=False)
        self.assertOk(self.run_check())

    def test_untracked_file_named_by_allowlist_counts_as_missing(self):
        self.clean_crate()
        self.write("crates/foo/src/ghost.rs", "x\n", track=False)
        self.allow("frozen crates/foo/src/ghost.rs named by a tool\n")
        self.assertFails(self.run_check(), 1)


# ======================================================================
# The allowlist: each kind, and the five failure classes
# ======================================================================
class AllowlistKinds(Case):
    def test_size_entry_covers_violation(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 10))
        self.allow("size crates/foo/src/big.rs %d\n" % (RS_LIMIT + 10))
        self.assertOk(self.run_check())

    def test_size_entry_with_larger_max_still_passes_when_file_shrank(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 10))
        self.allow("size crates/foo/src/big.rs 2000\n")
        self.assertOk(self.run_check())

    def test_header_entry_covers_violation(self):
        self.clean_crate()
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.allow("header crates/foo/src/bare.rs\n")
        self.assertOk(self.run_check())

    def test_header_entry_covers_csharp_violation(self):
        self.write("client-unity/UnityProject/README.md", "# u\n")
        self.write(CS_BASE, "public class A {}\n")
        self.allow("header %s\n" % CS_BASE)
        self.assertOk(self.run_check())

    def test_readme_entry_covers_violation(self):
        self.write(RS_BASE, rs_file(3))
        self.allow("readme crates/foo\n")
        self.assertOk(self.run_check())

    def test_frozen_entry_for_existing_file_passes(self):
        self.clean_crate()
        self.allow("frozen crates/foo/src/lib.rs named by tools/x/run-mutants.sh\n")
        self.assertOk(self.run_check())

    def test_exempt_entry_for_existing_match_passes(self):
        self.clean_crate()
        self.write("crates/foo/src/table.rs", rs_file(RS_LIMIT + 10))
        self.allow("exempt crates/foo/src/table.rs one long list by design\n")
        self.assertOk(self.run_check())

    def test_exempt_double_star_matches_across_directories(self):
        self.clean_crate()
        self.write("crates/foo/src/dormant/a.rs", rs_file(RS_LIMIT + 10))
        self.write("crates/foo/src/dormant/deep/er/b.rs", rs_file(RS_LIMIT + 10))
        self.allow("exempt crates/foo/src/dormant/** dormant code\n")
        self.assertOk(self.run_check())

    def test_exempt_affects_size_only_header_still_applies(self):
        self.clean_crate()
        self.write("crates/foo/src/table.rs", "let x = 1;\n" * (RS_LIMIT + 10))
        self.allow("exempt crates/foo/src/table.rs one long list by design\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/table\.rs: ")

    def test_exempt_does_not_waive_readme(self):
        self.write(RS_BASE, rs_file(3))
        self.allow("exempt crates/foo/src/lib.rs not a size problem\n")
        self.assertFails(self.run_check(), 1, r"readme crates/foo/?: ")

    def test_frozen_does_not_waive_violations(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 10))
        self.allow("frozen crates/foo/src/big.rs named by a tool\n")
        self.assertFails(self.run_check(), 1, r"size crates/foo/src/big\.rs: ")

    def test_exempt_file_without_size_problem_is_not_stale(self):
        self.clean_crate()
        self.allow("exempt crates/foo/src/lib.rs generated\n")
        self.assertOk(self.run_check())

    def test_allowlist_comments_blank_lines_and_whitespace_ignored(self):
        self.clean_crate()
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.allow("# a comment\n\n   \n  # indented comment\nheader\tcrates/foo/src/bare.rs\n")
        self.assertOk(self.run_check())

    def test_allowlist_fields_split_on_runs_of_whitespace(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 10))
        self.allow("size     crates/foo/src/big.rs    %d\n" % (RS_LIMIT + 10))
        self.assertOk(self.run_check())

    def test_default_allowlist_location_is_scripts_readability_allowlist(self):
        self.clean_crate()
        self.write("scripts/README.md", "# s\n")
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.write("scripts/readability-allowlist.txt", "header crates/foo/src/bare.rs\n")
        self.assertOk(self.run_check(allowlist=False))

    def test_default_allowlist_absent_entry_means_violation_fails(self):
        self.clean_crate()
        self.write("scripts/README.md", "# s\n")
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.write("scripts/readability-allowlist.txt", "# nothing listed\n")
        self.assertFails(self.run_check(allowlist=False), 1, r"header crates/foo/src/bare\.rs: ")


class FailureUnlisted(Case):
    def test_unlisted_violations_each_counted(self):
        self.clean_crate()
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 1))
        self.write("crates/bar/src/lib.rs", rs_file(3))
        res = self.run_check()
        self.assertFails(res, 3)
        self.assertRegex(res.out, r"(?m)^header crates/foo/src/bare\.rs: ")
        self.assertRegex(res.out, r"(?m)^size crates/foo/src/big\.rs: ")
        self.assertRegex(res.out, r"(?m)^readme crates/bar/?: ")

    def test_allowlist_entry_for_other_file_does_not_cover(self):
        self.clean_crate()
        self.write("crates/foo/src/a.rs", "pub fn f() {}\n")
        self.write("crates/foo/src/b.rs", "pub fn g() {}\n")
        self.allow("header crates/foo/src/a.rs\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/b\.rs: ")

    def test_size_entry_does_not_cover_header_violation(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", "let x = 1;\n" * (RS_LIMIT + 10))
        self.allow("size crates/foo/src/big.rs %d\n" % (RS_LIMIT + 10))
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/big\.rs: ")


class FailureGrew(Case):
    def test_size_grew_past_recorded_max_fails(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 11))
        self.allow("size crates/foo/src/big.rs %d\n" % (RS_LIMIT + 10))
        self.assertFails(self.run_check(), 1, r"size crates/foo/src/big\.rs: ")

    def test_size_grew_by_one_line_fails(self):
        self.clean_crate()
        self.write(CS_BASE, cs_file(CS_LIMIT + 6))
        self.write("client-unity/UnityProject/README.md", "# u\n")
        self.allow("size %s %d\n" % (CS_BASE, CS_LIMIT + 5))
        self.assertFails(self.run_check(), 1, r"size " + re.escape(CS_BASE) + ": ")


class FailureStale(Case):
    def test_stale_size_entry_when_file_within_limit_fails(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT))
        self.allow("size crates/foo/src/big.rs 1600\n")
        self.assertFails(self.run_check(), 1, r"size crates/foo/src/big\.rs: ")

    def test_stale_header_entry_when_file_has_header_fails(self):
        self.clean_crate()
        self.allow("header crates/foo/src/lib.rs\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/lib\.rs: ")

    def test_stale_header_entry_for_csharp_with_summary_fails(self):
        self.write("client-unity/UnityProject/README.md", "# u\n")
        self.write(CS_BASE, cs_file(10))
        self.allow("header %s\n" % CS_BASE)
        self.assertFails(self.run_check(), 1, r"header " + re.escape(CS_BASE) + ": ")

    def test_stale_readme_entry_when_directory_has_readme_fails(self):
        self.clean_crate()
        self.allow("readme crates/foo\n")
        self.assertFails(self.run_check(), 1, r"readme crates/foo/?: ")

    def test_stale_entries_each_counted(self):
        self.clean_crate()
        self.write("crates/foo/src/ok.rs", rs_file(5))
        self.allow("header crates/foo/src/ok.rs\nreadme crates/foo\nsize crates/foo/src/ok.rs 1600\n")
        self.assertFails(self.run_check(), 3)


class FailureMissingPath(Case):
    def test_size_entry_for_missing_file_fails(self):
        self.clean_crate()
        self.allow("size crates/foo/src/gone.rs 1600\n")
        self.assertFails(self.run_check(), 1, r"size crates/foo/src/gone\.rs: ")

    def test_header_entry_for_missing_file_fails(self):
        self.clean_crate()
        self.allow("header crates/foo/src/gone.rs\n")
        self.assertFails(self.run_check(), 1, r"header crates/foo/src/gone\.rs: ")

    def test_readme_entry_for_missing_directory_fails(self):
        self.clean_crate()
        self.allow("readme crates/gone\n")
        self.assertFails(self.run_check(), 1, r"readme crates/gone/?: ")

    def test_frozen_entry_for_missing_file_fails(self):
        self.clean_crate()
        self.allow("frozen crates/foo/src/moved.rs named by tools/x/run-mutants.sh\n")
        self.assertFails(self.run_check(), 1, r"frozen crates/foo/src/moved\.rs: ")

    def test_frozen_file_renamed_is_caught(self):
        self.clean_crate()
        self.write("crates/foo/src/new_name.rs", rs_file(3))
        self.allow("frozen crates/foo/src/old_name.rs named by a patch\n")
        self.assertFails(self.run_check(), 1, r"frozen crates/foo/src/old_name\.rs: ")

    def test_exempt_glob_matching_nothing_fails(self):
        self.clean_crate()
        self.allow("exempt crates/foo/src/nothing/** generated\n")
        self.assertFails(self.run_check(), 1, r"exempt crates/foo/src/nothing/\*\*: ")

    def test_exempt_literal_path_matching_nothing_fails(self):
        self.clean_crate()
        self.allow("exempt crates/foo/src/gone.rs generated\n")
        self.assertFails(self.run_check(), 1, r"exempt crates/foo/src/gone\.rs: ")


class FailureMalformed(Case):
    def check_one_problem(self, allowlist_text):
        self.clean_crate()
        self.allow(allowlist_text)
        self.assertFails(self.run_check(), 1)

    def test_unknown_kind_fails(self):
        self.check_one_problem("bogus crates/foo/src/lib.rs\n")

    def test_duplicate_header_entry_fails(self):
        self.clean_crate()
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.allow("header crates/foo/src/bare.rs\nheader crates/foo/src/bare.rs\n")
        self.assertFails(self.run_check(), 1)

    def test_duplicate_size_entry_with_different_max_fails(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 10))
        self.allow("size crates/foo/src/big.rs 1510\nsize crates/foo/src/big.rs 1600\n")
        self.assertFails(self.run_check(), 1)

    def test_duplicate_frozen_entry_fails(self):
        self.check_one_problem("frozen crates/foo/src/lib.rs why\nfrozen crates/foo/src/lib.rs why\n")

    def test_duplicate_exempt_entry_fails(self):
        self.check_one_problem("exempt crates/foo/src/lib.rs why\nexempt crates/foo/src/lib.rs why\n")

    def test_frozen_without_reason_fails(self):
        self.check_one_problem("frozen crates/foo/src/lib.rs\n")

    def test_exempt_without_reason_fails(self):
        self.check_one_problem("exempt crates/foo/src/lib.rs\n")

    def test_size_without_max_fails(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 10))
        self.allow("size crates/foo/src/big.rs\n")
        self.assertFails(self.run_check())

    def test_size_with_non_numeric_max_fails(self):
        self.clean_crate()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 10))
        self.allow("size crates/foo/src/big.rs lots\n")
        self.assertFails(self.run_check())

    def test_header_without_path_fails(self):
        self.check_one_problem("header\n")

    def test_readme_without_path_fails(self):
        self.check_one_problem("readme\n")

    def test_malformed_line_does_not_hide_a_real_violation(self):
        self.clean_crate()
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.allow("bogus whatever\n")
        res = self.run_check()
        self.assertFails(res, 2)
        self.assertRegex(res.out, r"(?m)^header crates/foo/src/bare\.rs: ")


# ======================================================================
# Output and exit codes
# ======================================================================
class SummaryLine(Case):
    def test_ok_summary_on_clean_tree_with_zero_counts(self):
        self.clean_crate()
        res = self.run_check()
        self.assertEqual(res.code, 0, res.text)
        self.assertEqual(
            res.last,
            "readability: OK (allowlisted: 0 over size, 0 without header, 0 without README; 0 frozen, 0 exempt)",
        )

    def test_ok_summary_counts_each_allowlist_kind(self):
        self.write("crates/foo/README.md", "# foo\n")
        self.write(RS_BASE, rs_file(3))
        self.write("crates/foo/src/big1.rs", rs_file(RS_LIMIT + 1))
        self.write("crates/foo/src/big2.rs", rs_file(RS_LIMIT + 2))
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.write("crates/bar/src/lib.rs", rs_file(3))
        self.write("crates/foo/src/table.rs", rs_file(RS_LIMIT + 9))
        self.allow(
            "size crates/foo/src/big1.rs %d\n"
            "size crates/foo/src/big2.rs %d\n"
            "header crates/foo/src/bare.rs\n"
            "readme crates/bar\n"
            "frozen crates/foo/src/lib.rs named by a tool\n"
            "exempt crates/foo/src/table.rs one long list\n" % (RS_LIMIT + 1, RS_LIMIT + 2)
        )
        res = self.run_check()
        self.assertEqual(res.code, 0, res.text)
        self.assertEqual(
            res.last,
            "readability: OK (allowlisted: 2 over size, 1 without header, 1 without README; 1 frozen, 1 exempt)",
        )

    def test_fail_summary_reports_problem_count(self):
        self.clean_crate()
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        res = self.run_check()
        self.assertEqual(res.last, "readability: FAIL (1 problems)")

    def test_fail_summary_is_last_line_after_problem_lines(self):
        self.clean_crate()
        self.write("crates/foo/src/bare.rs", "pub fn f() {}\n")
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 1))
        res = self.run_check()
        lines = [l for l in res.out.splitlines() if l.strip()]
        self.assertTrue(lines, "no output")
        self.assertEqual(lines[-1], "readability: FAIL (2 problems)")
        self.assertEqual(len([l for l in lines[:-1] if re.match(r"^(size|header|readme) \S+: ", l)]), 2)

    def test_ok_run_prints_no_problem_lines(self):
        self.clean_crate()
        res = self.run_check()
        self.assertEqual(res.code, 0, res.text)
        self.assertNotRegex(res.out, r"(?m)^(size|header|readme|frozen|exempt) \S+: ")


# ======================================================================
# --write-allowlist
# ======================================================================
class WriteAllowlist(Case):
    def violations_tree(self):
        self.write("crates/foo/README.md", "# foo\n")
        self.write(RS_BASE, rs_file(3))
        self.write("crates/foo/src/zbig.rs", rs_file(RS_LIMIT + 7))
        self.write("crates/foo/src/abig.rs", rs_file(RS_LIMIT + 3))
        self.write("crates/foo/src/zbare.rs", "pub fn f() {}\n")
        self.write("crates/foo/src/abare.rs", "pub fn g() {}\n")
        self.write("crates/bar/src/lib.rs", rs_file(3))
        self.write("crates/baz/src/lib.rs", rs_file(3))

    SENTINEL = ["header", "crates/foo/src/sentinel.rs"]

    def sentinel(self):
        """A known violation, so an empty output cannot make an absence check pass."""
        self.write("crates/foo/src/sentinel.rs", "pub fn s() {}\n")

    def entries(self, res):
        self.assertEqual(res.code, 0, res.text)
        out = []
        for line in res.out.splitlines():
            s = line.strip()
            if s and not s.startswith("#"):
                out.append(s.split(None, 2))
        return out

    def test_write_allowlist_exits_zero_with_violations_present(self):
        self.violations_tree()
        res = self.run_check("--write-allowlist")
        self.assertEqual(res.code, 0, res.text)

    def test_write_allowlist_lists_each_violation_with_its_line_count(self):
        self.violations_tree()
        e = self.entries(self.run_check("--write-allowlist"))
        self.assertIn(["size", "crates/foo/src/zbig.rs", str(RS_LIMIT + 7)], e)
        self.assertIn(["size", "crates/foo/src/abig.rs", str(RS_LIMIT + 3)], e)
        self.assertIn(["header", "crates/foo/src/zbare.rs"], e)
        self.assertIn(["header", "crates/foo/src/abare.rs"], e)
        readme_dirs = [x[1].rstrip("/") for x in e if x[0] == "readme"]
        self.assertEqual(sorted(readme_dirs), ["crates/bar", "crates/baz"])

    def test_write_allowlist_omits_files_that_comply(self):
        self.violations_tree()
        res = self.run_check("--write-allowlist")
        self.assertIn(["size", "crates/foo/src/zbig.rs", str(RS_LIMIT + 7)], self.entries(res))
        text = res.out
        self.assertNotIn("crates/foo/src/lib.rs", text)
        self.assertNotIn("crates/foo/README.md", text)

    def test_write_allowlist_sorted_by_kind_then_path(self):
        self.violations_tree()
        e = self.entries(self.run_check("--write-allowlist"))
        keys = [(a[0], a[1].rstrip("/")) for a in e if a[0] in ("size", "header", "readme")]
        self.assertEqual(keys, sorted(keys), "entries not sorted by kind then path: %r" % keys)

    def test_write_allowlist_groups_each_kind_together(self):
        self.violations_tree()
        kinds = [a[0] for a in self.entries(self.run_check("--write-allowlist"))]
        collapsed = [k for i, k in enumerate(kinds) if i == 0 or kinds[i - 1] != k]
        self.assertEqual(len(collapsed), len(set(collapsed)), "kinds interleaved: %r" % kinds)

    def test_write_allowlist_keeps_frozen_and_exempt_with_reasons(self):
        self.violations_tree()
        self.write("crates/foo/src/table.rs", rs_file(RS_LIMIT + 50))
        self.allow(
            "# existing\n"
            "frozen crates/foo/src/lib.rs named by tools/x/run-mutants.sh\n"
            "exempt crates/foo/src/table.rs golden table, one long list\n"
        )
        e = self.entries(self.run_check("--write-allowlist"))
        self.assertIn(["frozen", "crates/foo/src/lib.rs", "named by tools/x/run-mutants.sh"], e)
        self.assertIn(["exempt", "crates/foo/src/table.rs", "golden table, one long list"], e)

    def test_write_allowlist_does_not_list_exempt_file_as_size_violation(self):
        self.violations_tree()
        self.write("crates/foo/src/table.rs", rs_file(RS_LIMIT + 50))
        self.allow("exempt crates/foo/src/table.rs one long list\n")
        e = self.entries(self.run_check("--write-allowlist"))
        self.assertIn(["size", "crates/foo/src/zbig.rs", str(RS_LIMIT + 7)], e)
        self.assertEqual([a for a in e if a[0] == "size" and a[1] == "crates/foo/src/table.rs"], [])

    def test_write_allowlist_drops_old_size_header_readme_entries(self):
        self.clean_crate()
        self.sentinel()
        self.allow("size crates/foo/src/lib.rs 1600\nheader crates/foo/src/lib.rs\nreadme crates/foo\n")
        e = self.entries(self.run_check("--write-allowlist"))
        self.assertEqual(e, [self.SENTINEL])

    def test_write_allowlist_checks_nothing_and_ignores_bad_entries(self):
        self.clean_crate()
        self.allow("bogus line\nfrozen crates/foo/src/missing.rs gone\n")
        res = self.run_check("--write-allowlist")
        self.assertEqual(res.code, 0, res.text)
        self.assertNotIn("readability: FAIL", res.text)

    def test_write_allowlist_output_makes_the_check_pass(self):
        self.violations_tree()
        self.write("crates/foo/src/table.rs", rs_file(RS_LIMIT + 50))
        self.allow(
            "frozen crates/foo/src/lib.rs named by a tool\n"
            "exempt crates/foo/src/table.rs one long list\n"
        )
        generated = self.run_check("--write-allowlist").out
        self.allow(generated)
        res = self.run_check()
        self.assertOk(res)
        self.assertEqual(
            res.last,
            "readability: OK (allowlisted: 2 over size, 2 without header, 2 without README; 1 frozen, 1 exempt)",
        )

    def test_write_allowlist_does_not_modify_the_allowlist_file(self):
        self.violations_tree()
        self.allow("frozen crates/foo/src/lib.rs named by a tool\n")
        self.assertEqual(self.run_check("--write-allowlist").code, 0)
        with open(self.allow_path) as f:
            self.assertEqual(f.read(), "frozen crates/foo/src/lib.rs named by a tool\n")

    def test_write_allowlist_ignores_untracked_files(self):
        self.clean_crate()
        self.sentinel()
        self.write("crates/foo/src/big.rs", rs_file(RS_LIMIT + 9), track=False)
        res = self.run_check("--write-allowlist")
        self.assertEqual(self.entries(res), [self.SENTINEL])
        self.assertNotIn("big.rs", res.out)


# ======================================================================
# The root rule (packet P-GH-1): top-level names, naming in the root README, a README per folder
# ======================================================================
class RootRule(Case):
    """Applies only when a root README.md is tracked (the real repository always has one)."""

    def root_repo(self, readme, dirs=("maps",), readmes=True):
        """A root README.md with the given text, and each top-level directory in `dirs` holding a file and
        (when `readmes`) its own README.md."""
        self.write("README.md", readme)
        for d in dirs:
            self.write(d + "/data.txt", "x\n")
            if readmes:
                self.write(d + "/README.md", "# r\n")

    def test_root_directory_named_with_backtick_slash_passes(self):
        self.root_repo("The `maps/` folder holds maps.\n")
        self.assertOk(self.run_check())

    def test_root_directory_named_in_a_link_passes(self):
        self.root_repo("See [maps](maps/README.md).\n")
        self.assertOk(self.run_check())

    def test_root_directory_named_by_a_path_inside_it_passes(self):
        self.root_repo("The first map is `maps/first-playable.json`.\n")
        self.assertOk(self.run_check())

    def test_root_directory_not_named_fails(self):
        self.root_repo("Nothing about folders here.\n")
        self.assertFails(self.run_check(), 1, r"root maps/?: ")

    def test_root_bare_word_or_no_slash_does_not_name_a_directory(self):
        self.root_repo("The maps folder, also `maps` and [maps](maps.md).\n")
        self.assertFails(self.run_check(), 1, r"root maps/?: ")

    def test_root_nested_mention_does_not_name_the_top_level_directory(self):
        self.root_repo("Look in `docs/maps/` and [x](docs/maps/a.md); also `foomaps/`.\n")
        self.assertFails(self.run_check(), 1, r"root maps/?: ")

    def test_root_each_unnamed_directory_is_reported(self):
        self.root_repo("Names `a/` only.\n", dirs=("a", "b", "c"))
        self.assertFails(self.run_check(), 2)
        self.assertFails(self.run_check(), line=r"root b/?: ")
        self.assertFails(self.run_check(), line=r"root c/?: ")

    def test_root_missing_readme_fails_unlisted(self):
        self.root_repo("The `maps/` folder.\n", readmes=False)
        self.assertFails(self.run_check(), 1, r"(root|readme) maps/?: ")

    def test_root_readme_in_subdirectory_does_not_satisfy_the_directory(self):
        self.root_repo("The `maps/` folder.\n", readmes=False)
        self.write("maps/sub/README.md", "# s\n")
        self.assertFails(self.run_check(), 1, r"(root|readme) maps/?: ")

    def test_root_untracked_readme_does_not_count(self):
        self.root_repo("The `maps/` folder.\n", readmes=False)
        self.write("maps/README.md", "# r\n", track=False)
        self.assertFails(self.run_check(), 1, r"(root|readme) maps/?: ")

    def test_root_missing_readme_passes_when_allowlisted(self):
        self.root_repo("The `maps/` folder.\n", readmes=False)
        self.allow("readme maps\n")
        res = self.run_check()
        self.assertOk(res)
        self.assertIn("1 without README", res.last)

    def test_root_allowlist_does_not_excuse_an_unnamed_directory(self):
        self.root_repo("Nothing.\n", readmes=False)
        self.allow("readme maps\n")
        self.assertFails(self.run_check(), 1, r"root maps/?: ")

    def test_root_stale_readme_allowlist_entry_still_fails(self):
        self.root_repo("The `maps/` folder.\n")
        self.allow("readme maps\n")
        self.assertFails(self.run_check(), 1, r"readme maps: directory has a README now")

    def test_root_dot_directories_are_ignored(self):
        self.root_repo("Nothing about dot folders.\n", dirs=())
        self.write(".github/workflows/ci.yml", "x\n")
        self.write(".codex/config.toml", "x\n")
        self.assertOk(self.run_check())

    def test_root_top_level_files_need_no_naming_or_readme(self):
        self.root_repo("Nothing.\n", dirs=())
        self.write("LICENSE", "x\n")
        self.write("Cargo.toml", "x\n")
        self.assertOk(self.run_check())

    def test_root_untracked_directory_is_ignored(self):
        self.root_repo("Nothing.\n", dirs=())
        self.write("scratch/a.txt", "x\n", track=False)
        self.assertOk(self.run_check())

    def test_root_whitespace_in_a_directory_name_fails(self):
        self.root_repo("The `Saved work/` folder.\n", dirs=("Saved work",))
        self.assertFails(self.run_check(), 1, r"root Saved work/?: ")

    def test_root_whitespace_in_a_file_name_fails(self):
        self.root_repo("Nothing.\n", dirs=())
        self.write("my notes.txt", "x\n")
        self.assertFails(self.run_check(), 1, r"root my notes\.txt: ")

    def test_root_whitespace_deeper_down_is_allowed(self):
        self.root_repo("The `maps/` folder.\n")
        self.write("maps/old notes/a b.txt", "x\n")
        self.assertOk(self.run_check())

    def test_root_problem_lines_start_with_root_and_the_path(self):
        self.root_repo("Nothing.\n")
        res = self.run_check()
        lines = [l for l in res.out.splitlines() if l.startswith("root ")]
        self.assertEqual(len(lines), 1, res.text)
        self.assertRegex(lines[0], r"^root maps/?: \S")

    def test_root_fixed_readme_directory_missing_readme_is_reported_once(self):
        self.root_repo("The `tools/` folder.\n", dirs=("tools",), readmes=False)
        res = self.run_check()
        self.assertFails(res, 1, r"readme tools/?: ")
        self.assertNotRegex(res.out, r"(?m)^root tools")

    def test_root_fixed_readme_directory_unnamed_but_with_readme_fails_as_root(self):
        self.root_repo("Nothing.\n", dirs=("tools",))
        self.assertFails(self.run_check(), 1, r"root tools/?: ")

    def test_root_fixed_directory_allowlisted_and_named_passes(self):
        self.root_repo("The `tools/` folder.\n", dirs=("tools",), readmes=False)
        self.allow("readme tools\n")
        self.assertOk(self.run_check())

    def test_root_rule_is_off_without_a_tracked_root_readme(self):
        # Reading pinned in the packet's Oracle notes: with no root README.md the rule has nothing to
        # check names against and is not applied (the older tests build repositories this way).
        self.clean_crate()
        self.write("misc/notes.txt", "x\n")
        self.assertOk(self.run_check())

    def test_root_write_allowlist_does_not_list_root_problems(self):
        self.root_repo("Nothing.\n")
        res = self.run_check("--write-allowlist")
        self.assertEqual(res.code, 0, res.text)
        self.assertNotIn("maps", res.out)


if __name__ == "__main__":
    unittest.main()
