import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]
FOUNDATION = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
PWSH = shutil.which("pwsh")


class StoreReleaseTests(unittest.TestCase):
    def run_script(self, name, *arguments, environment=None):
        self.assertIsNotNone(PWSH, "PowerShell 7 is required to check Store release boundaries.")
        return subprocess.run(
            [PWSH, "-NoProfile", "-File", str(ROOT / "scripts" / name), *arguments],
            cwd=ROOT, env=environment, text=True, encoding="utf-8", capture_output=True,
        )

    def test_stable_versions_keep_the_store_revision_zero(self):
        for version in ("1.0.0", "1.2.345", "65535.65535.65535"):
            with self.subTest(version=version):
                result = self.run_script("store-version.ps1", "-Version", version)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(version + ".0", result.stdout.strip())

    def test_development_ambiguous_and_out_of_range_versions_are_rejected(self):
        for version in ("0.1.0", "1.0.0-dev.10455", "1.0.0-rc.1", "1.0.0+metadata",
                        "v1.0.0", "01.0.0", "1.0.0.1", "65536.0.0", "1.999999999999.0",
                        "1.0.0\n", "1.0.0;exit 0"):
            with self.subTest(version=version):
                result = self.run_script("store-version.ps1", "-Version", version)
                self.assertNotEqual(0, result.returncode)

    def package_fixture(self, root, architecture, machine=None, missing=None, version="1.0.0.0", self_contained=True, mixed_case=False):
        manifest = ET.parse(ROOT / "packaging/store/AppxManifest.xml")
        identity = manifest.getroot().find("{" + FOUNDATION + "}Identity")
        identity.set("ProcessorArchitecture", architecture)
        identity.set("Version", version)
        binary = bytearray(256)
        struct.pack_into("<H", binary, 0, 0x5A4D)
        struct.pack_into("<I", binary, 0x3C, 0x80)
        struct.pack_into("<I", binary, 0x80, 0x4550)
        struct.pack_into("<H", binary, 0x84, machine or (0x8664 if architecture == "x64" else 0xAA64))
        files = {
            "AppxManifest.xml": ET.tostring(manifest.getroot()),
            "takupoke.exe": binary, "coreclr.dll": binary, "Microsoft.UI.Xaml.dll": binary,
            "takupoke.runtimeconfig.json": json.dumps({"runtimeOptions": {"includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.0"}]}}),
        }
        if not self_contained:
            files["takupoke.runtimeconfig.json"] = json.dumps({"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}})
        for name in ("App.xbf", "MainWindow.xbf", "THIRD-PARTY-NOTICES.txt", "Legal/privacy.txt",
                     "Legal/terms.txt", "Assets/StoreLogo.png", "Assets/Square44x44Logo.png", "Assets/Square150x150Logo.png"):
            files[name] = b"synthetic packaging fixture"
        output = root / "store-architectures"
        output.mkdir(exist_ok=True)
        with zipfile.ZipFile(output / f"takupoke-1.0.0-{architecture}.msix", "w") as archive:
            for name, content in files.items():
                if name != missing:
                    archive.writestr(name.lower() if mixed_case else name, content)

    def verify_fixture(self, **options):
        with tempfile.TemporaryDirectory(prefix="takupoke-store-contract-") as temporary:
            root = Path(temporary)
            self.package_fixture(root, "x64", **options)
            self.package_fixture(root, "arm64")
            environment = {**os.environ, "TAKUPOKE_CI_TEMP": str(root)}
            return self.run_script("test-store-package.ps1", "-Version", "1.0.0", environment=environment)

    def test_both_package_architectures_are_inspected(self):
        result = self.verify_fixture()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_windows_runtime_file_names_are_case_insensitive(self):
        result = self.verify_fixture(mixed_case=True)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_wrong_binary_architecture_cannot_pass_as_x64(self):
        result = self.verify_fixture(machine=0xAA64)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Wrong architecture", result.stderr)

    def test_missing_compiled_ui_or_notices_are_rejected(self):
        for name in ("MainWindow.xbf", "THIRD-PARTY-NOTICES.txt", "Legal/privacy.txt"):
            with self.subTest(name=name):
                result = self.verify_fixture(missing=name)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("missing", result.stderr)

    def test_wrong_store_version_is_rejected(self):
        result = self.verify_fixture(version="1.0.1.0")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("identity does not match", result.stderr)

    def test_machine_installed_dotnet_cannot_hide_a_framework_dependent_package(self):
        result = self.verify_fixture(self_contained=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("not .NET self-contained", result.stderr)

    def test_certificate_trust_is_never_changed_on_a_developer_computer(self):
        environment = {**os.environ, "GITHUB_ACTIONS": "false", "RUNNER_ENVIRONMENT": "self-hosted"}
        result = self.run_script("test-store-installation.ps1", "-Version", "1.0.0", environment=environment)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("ephemeral GitHub-hosted runner", result.stderr)


if __name__ == "__main__":
    unittest.main()
