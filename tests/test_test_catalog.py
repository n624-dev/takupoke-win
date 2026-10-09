"""Exercise missing mappings, stale catalogues and real Git change enforcement."""
import contextlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import test_catalog as catalog
from test_catalog_inventory import cases, fingerprint, load


class TestCatalogueTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="takupoke-test-catalog-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.write("src/Feature.swift", "struct Feature { let value = 1 }\n")
        self.write("tests/FeatureTests.swift", "func testRejectsUnknown() { XCTAssertEqual(1, 1) }\n")
        group = {"id": "feature", "title": "架空の機能", "environment": "架空の検証環境",
                 "sources": ["src/Feature.swift"], "checks": ["tests/FeatureTests.swift"],
                 "commands": ["fictional-check"]}
        self.write("tests/test-catalog.json", json.dumps({"code_roots": ["src", "tests"],
                   "groups": [group], "index": "docs/test-catalog.md",
                   "document_folder": "docs/test-catalog", "script": "tools/test_catalog.py"}))
        # The manifest is metadata, not executable code requiring another case.
        group["sources"].append("tests/test-catalog.json")
        self.write("tests/test-catalog.json", json.dumps({"code_roots": ["src", "tests"],
                   "groups": [group], "index": "docs/test-catalog.md",
                   "document_folder": "docs/test-catalog", "script": "tools/test_catalog.py"}))
        self.git("init", "-q")
        self.git("config", "user.name", "n624-dev")
        self.git("config", "user.email", "91827902+n624-dev@users.noreply.github.com")
        self.run_mode("write")
        self.commit()
        self.base = self.git("rev-parse", "HEAD").strip()

    def write(self, path, content):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def git(self, *args):
        return subprocess.check_output(["git", *args], cwd=self.root, stderr=subprocess.PIPE).decode()

    def commit(self):
        self.git("add", ".")
        self.git("commit", "-qm", "Independent catalogue fixture")

    def run_mode(self, mode, **arguments):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            catalog.run(self.root, mode, **arguments)
        return output.getvalue()

    def update_source(self):
        self.write("src/Feature.swift", "struct Feature { let value = 2 }\n")

    def test_changed_runtime_requires_corresponding_test_and_updated_document(self):
        self.update_source()
        with self.assertRaisesRegex(ValueError, "Stale"):
            self.run_mode("check", base=self.base)
        self.run_mode("write")
        with self.assertRaisesRegex(ValueError, "Change a corresponding test"):
            self.run_mode("check", base=self.base)
        self.write("tests/FeatureTests.swift", "func testRejectsUnknown() { XCTAssertEqual(2, 2) }\n")
        self.run_mode("write")
        self.assertIn("対象検索", self.run_mode("check", base=self.base))

    def test_comments_and_formatting_do_not_qualify_as_a_test_change(self):
        self.update_source()
        self.write("tests/FeatureTests.swift",
                   "// reviewed\nfunc testRejectsUnknown() {\n  XCTAssertEqual(1, 1)\n}\n")
        self.run_mode("write")
        with self.assertRaisesRegex(ValueError, "Change a corresponding test"):
            self.run_mode("check", base=self.base)

    def test_new_source_without_mapping_is_rejected(self):
        self.write("src/NewFeature.swift", "struct NewFeature {}")
        with self.assertRaisesRegex(ValueError, "Unregistered code.*NewFeature"):
            self.run_mode("write")

    def test_unregistered_test_and_missing_registered_file_are_rejected(self):
        self.write("tests/NewTests.swift", "func testMustNotDisappear() {}")
        with self.assertRaisesRegex(ValueError, "Unregistered code"):
            self.run_mode("check")
        (self.root / "tests/NewTests.swift").unlink()
        (self.root / "tests/FeatureTests.swift").unlink()
        with self.assertRaisesRegex(ValueError, "Missing registered file"):
            self.run_mode("check")

    def test_deleting_tests_cannot_qualify_runtime_changes(self):
        config, owners = load(self.root, catalog.CONFIG)
        (self.root / "tests/FeatureTests.swift").unlink()
        self.update_source()
        with contextlib.redirect_stdout(io.StringIO()), self.assertRaisesRegex(ValueError, "Change a corresponding"):
            catalog.enforce(self.root, config, owners, self.base)

    def test_document_only_changes_do_not_require_test_edits(self):
        self.write("docs/verification.md", "架空の実測記録\n")
        self.run_mode("check", base=self.base)

    def test_renamed_source_keeps_the_previous_test_requirement(self):
        (self.root / "src/Feature.swift").rename(self.root / "src/Renamed.swift")
        config = json.loads((self.root / catalog.CONFIG).read_text())
        config["groups"][0]["sources"][0] = "src/Renamed.swift"
        self.write(catalog.CONFIG, json.dumps(config))
        self.run_mode("write")
        with self.assertRaisesRegex(ValueError, "Change a corresponding test"):
            self.run_mode("check", base=self.base)

    def test_new_corresponding_case_file_can_qualify_existing_runtime_changes(self):
        self.update_source()
        self.write("tests/NewTests.swift", "func testNewBoundary() { XCTAssertEqual(2, 2) }\n")
        config = json.loads((self.root / catalog.CONFIG).read_text())
        config["groups"][0]["checks"].append("tests/NewTests.swift")
        self.write(catalog.CONFIG, json.dumps(config))
        self.run_mode("write")
        self.run_mode("check", base=self.base)

    def test_stale_or_obsolete_generated_documents_are_rejected(self):
        self.write("docs/test-catalog/feature.md", "古い一覧\n")
        with self.assertRaisesRegex(ValueError, "Stale"):
            self.run_mode("check")
        self.run_mode("write")
        self.write("docs/test-catalog/obsolete.md", "未登録の一覧\n")
        with self.assertRaisesRegex(ValueError, "obsolete"):
            self.run_mode("check")

    def test_integration_case_can_cover_two_sources_without_duplicate_source_ownership(self):
        self.write("src/Storage.swift", "struct Storage {}\n")
        config = json.loads((self.root / catalog.CONFIG).read_text())
        config["groups"].append({**config["groups"][0], "id": "storage", "title": "架空の保存",
                                 "sources": ["src/Storage.swift"]})
        self.write(catalog.CONFIG, json.dumps(config))
        self.run_mode("write")
        config["groups"][1]["sources"].append("src/Feature.swift")
        self.write(catalog.CONFIG, json.dumps(config))
        with self.assertRaisesRegex(ValueError, "Duplicate source"):
            self.run_mode("check")

    def test_search_finds_case_and_source_names(self):
        self.assertIn("FeatureTests.swift", self.run_mode("search", query="Feature.swift"))
        self.assertIn("fictional-check", self.run_mode("search", query="testRejectsUnknown"))

    def test_inventory_distinguishes_declarations_from_theory_expansions(self):
        self.write("example.cs", "[Theory]\n[InlineData(1)]\n[InlineData(2)]\npublic void Reject(int value) {}")
        self.assertEqual([("Reject", 1)], cases(self.root / "example.cs"))
        self.assertEqual(fingerprint("check.py", b"def test_x():\n assert 1 == 1\n"),
                         fingerprint("check.py", b"# comment\ndef test_x():\n    assert 1 == 1\n"))
        self.assertNotEqual(fingerprint("check.cs", b'Assert.Equal("//a", value);'),
                            fingerprint("check.cs", b'Assert.Equal("//b", value);'))

    def test_workflow_enforces_changed_code_against_event_base(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("python3 -B scripts/test_catalog.py check", workflow)
        self.assertIn("fetch-depth: 0", workflow)
        self.assertIn("github.event.before", workflow)
        self.assertIn("github.event.pull_request.base.sha", workflow)


if __name__ == "__main__":
    unittest.main()
