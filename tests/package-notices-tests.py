import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


spec = importlib.util.spec_from_file_location("notices", Path(__file__).parents[1] / "scripts/collect-package-notices.py")
notices = importlib.util.module_from_spec(spec)
spec.loader.exec_module(notices)


class NoticesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="takupoke-notices-tests-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.package = self.root / "packages/fake/1.0"
        self.package.mkdir(parents=True)
        self.templates = self.root / "templates"
        self.templates.mkdir()
        (self.templates / "MIT.txt").write_text("Fake MIT template", encoding="utf-8")

    def metadata(self, license_xml):
        (self.package / "fake.nuspec").write_text(
            '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>'
            '<id>Fake.Package</id><version>1.0</version><copyright>Fake author</copyright>'
            + license_xml + '</metadata></package>', encoding="utf-8")

    def test_preserves_package_copyright_and_original_notice_with_expression(self):
        self.metadata('<license type="expression">MIT</license>')
        (self.package / "LICENSE.txt").write_text("Copyright Fake original author", encoding="utf-8")
        (self.package / "NOTICE.txt").write_text("Fake attribution", encoding="utf-8")
        notices.collect(self.root / "packages", self.root / "out", self.templates)
        text = (self.root / "out/THIRD-PARTY-NOTICES.txt").read_text(encoding="utf-8")
        for expected in ["Fake.Package 1.0", "Fake author", "Fake MIT template", "Copyright Fake original author", "Fake attribution"]:
            self.assertIn(expected, text)

    def test_refuses_license_path_outside_package(self):
        self.metadata('<license type="file">../../private.txt</license>')
        with self.assertRaisesRegex(ValueError, "Invalid license path"):
            notices.collect(self.root / "packages", self.root / "out", self.templates)

    def test_unknown_license_requires_explicit_text(self):
        self.metadata('<license type="expression">Unknown-License</license>')
        with self.assertRaisesRegex(ValueError, "Missing license text"):
            notices.collect(self.root / "packages", self.root / "out", self.templates)

    def test_file_license_is_included_verbatim(self):
        self.metadata('<license type="file">licenses/license.txt</license>')
        folder = self.package / "licenses"
        folder.mkdir()
        text = "Fake package license\nExact original terms.\n"
        (folder / "license.txt").write_text(text, encoding="utf-8")
        notices.collect(self.root / "packages", self.root / "out", self.templates)
        self.assertIn(text, (self.root / "out/THIRD-PARTY-NOTICES.txt").read_text(encoding="utf-8"))

    def build_tools(self, assets=None):
        folder = self.root / "packages/sdktools/1.0"
        folder.mkdir(parents=True)
        (folder / "tools.nuspec").write_text('<package><metadata><id>Microsoft.Windows.SDK.BuildTools</id><version>1.0</version></metadata></package>', encoding="utf-8")
        output = self.root / "out"
        output.mkdir()
        (output / "app.deps.json").write_text(json.dumps({"targets": {"win": {"Microsoft.Windows.SDK.BuildTools/1.0": assets or {}}}}), encoding="utf-8")
        self.metadata('<license type="expression">MIT</license>')
        return folder, output

    def test_excludes_verified_build_only_tools(self):
        self.build_tools()
        notices.collect(self.root / "packages", self.root / "out", self.templates)
        self.assertNotIn("Microsoft.Windows.SDK.BuildTools", (self.root / "out/THIRD-PARTY-NOTICES.txt").read_text())

    def test_refuses_build_tools_with_runtime_assets(self):
        self.build_tools({"native": {"tools.dll": {}}})
        with self.assertRaisesRegex(ValueError, "deployed runtime assets"):
            notices.collect(self.root / "packages", self.root / "out", self.templates)

    def test_refuses_build_tool_copied_outside_manifest(self):
        folder, output = self.build_tools()
        (folder / "tool.dll").write_bytes(b"fake binary")
        (output / "tool.dll").write_bytes(b"fake binary")
        with self.assertRaisesRegex(ValueError, "included in the published app"):
            notices.collect(self.root / "packages", output, self.templates)

    def test_build_only_exclusion_requires_manifest(self):
        self.build_tools()
        (self.root / "out/app.deps.json").unlink()
        with self.assertRaisesRegex(ValueError, "manifest is required"):
            notices.collect(self.root / "packages", self.root / "out", self.templates)


if __name__ == "__main__":
    unittest.main()
