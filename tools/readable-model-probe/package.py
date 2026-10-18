"""Build the explicitly fictional development benchmark; never package models."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def collect_notices(repository, project_dir, package, output):
    assets = json.loads((project_dir / "obj/project.assets.json").read_text(encoding="utf-8-sig"))
    roots = [Path(root) for root in assets["packageFolders"]]
    identities = {key.lower() for key, value in assets["libraries"].items() if value["type"] == "package"}
    deployed = json.loads((package / "WindowsReadableNativeProbe.deps.json").read_text(encoding="utf-8-sig"))
    identities.update(key.removeprefix("runtimepack.").lower() for key, value in deployed["libraries"].items()
                      if value["type"] == "runtimepack")
    for framework in assets["project"]["frameworks"].values():
        for dependency in framework.get("downloadDependencies", []):
            if dependency["name"].lower() == "microsoft.netcore.app.host.win-x64":
                version = dependency["version"].strip("[]").split(",")[0].strip()
                identities.add(dependency["name"].lower() + "/" + version.lower())
    isolated = output / "notice-inputs"
    provenance = []
    for identity in sorted(identities):
        folder = next((root / identity for root in roots if (root / identity).is_dir()), None)
        if folder is None:
            raise RuntimeError("Resolved package notice source missing: " + identity)
        specs = list(folder.glob("*.nuspec"))
        if len(specs) != 1:
            raise RuntimeError("Exactly one resolved NuGet metadata source is required: " + identity)
        original_files = {specs[0]}
        document = ET.fromstring(specs[0].read_bytes())
        for node in document.iter():
            if node.tag.split("}")[-1] == "license" and node.attrib.get("type") == "file":
                original_files.add(folder / (node.text or "").replace("\\", "/"))
        original_files.update(path for path in folder.iterdir() if path.is_file() and
                              re.match(r"(?i)^(?:licen[cs]e|notice|third.?party.?notices)(?:[._-]|$)", path.name))
        for source in sorted(original_files):
            if not source.resolve().is_relative_to(folder.resolve()) or not source.is_file():
                raise RuntimeError("Invalid original notice path: " + identity)
            relative = source.relative_to(folder)
            target = isolated / identity / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            destination = package / "Licenses/NuGet" / identity / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
            provenance.append({"package": identity, "originalPath": relative.as_posix(), "sha256": sha256(source)})
    specification = importlib.util.spec_from_file_location("package_notices", repository / "scripts/collect-package-notices.py")
    collector = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(collector)
    collector.collect(isolated, package, repository / "licenses/spdx")
    (package / "Licenses/notice-sources.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    runtime = next(identity for identity in identities if identity.startswith("microsoft.netcore.app.runtime.win-x64/"))
    runtime_originals = {p["originalPath"].upper() for p in provenance if p["package"] == runtime}
    if not {"LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"}.issubset(runtime_originals):
        raise RuntimeError("Exact Windows .NET runtime original license and notices are required.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() and any(output.iterdir()):
        raise RuntimeError("Package output must be a new or empty dedicated directory.")
    output.mkdir(parents=True, exist_ok=True)
    project_dir = Path(__file__).resolve().parent
    repository = project_dir.parent.parent
    package = output / "takupoke-windows-fictional-benchmark-x64"
    subprocess.run([args.dotnet, "publish", str(project_dir / "WindowsReadableNativeProbe.csproj"),
                    "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true",
                    "--output", str(package)], check=True)
    # Native import libraries and debug symbols are build inputs, not runtime inputs.
    for path in package.rglob("*"):
        if path.is_file() and path.suffix.lower() in {".pdb", ".lib"}:
            path.unlink()
    if any(path.is_file() and path.suffix.lower() in {".onnx", ".gguf", ".safetensors"}
           for path in package.rglob("*")):
        raise RuntimeError("Model weights must never be embedded in the QA download.")
    if os.name == "nt":
        executable = [str(package / "WindowsReadableNativeProbe.exe")]
        preflight_host = "Windows self-contained executable"
    else:
        # Cross-publishing cannot run Windows native libraries on Linux. Only the
        # managed, model-free preflight is run with the installed .NET host.
        runtime_config = output / "managed-preflight.runtimeconfig.json"
        runtime_config.write_text(json.dumps({"runtimeOptions": {"tfm": "net10.0", "framework": {
            "name": "Microsoft.NETCore.App", "version": "10.0.0"}}}), encoding="utf-8")
        executable = [args.dotnet, "exec", "--runtimeconfig", str(runtime_config),
                      str(package / "WindowsReadableNativeProbe.dll")]
        preflight_host = "Non-Windows managed preflight; Windows native execution remains unverified"
    preflight = subprocess.run(executable + ["--preflight"], check=True,
                               capture_output=True, text=True, encoding="utf-8")
    controls = json.loads(preflight.stdout)["productionRuleControls"]
    expected = {"Timetable": 40, "Exam": 510, "ExamReturn": 680}
    if len(controls) != 3 or {c["kind"]: c["requiredSlots"] for c in controls} != expected:
        raise RuntimeError("Production Rules control coverage mismatch.")
    if any(not c["exactFormalValues"] or c["providerCalls"] != 0 or c["providerAvailabilityCalls"] != 0
           for c in controls):
        raise RuntimeError("Production Rules controls attempted a provider call or changed values.")
    (package / "packaging-preflight.json").write_text(json.dumps({"executionHost": preflight_host,
        "productionRuleControls": controls}, indent=2) + "\n", encoding="utf-8")
    print(preflight.stdout, end="")
    shutil.copyfile(project_dir / "README.txt", package / "README.txt")
    collect_notices(repository, project_dir, package, output)
    for name, argument, log_name in [("RunPreflight", "--preflight", "preflight.log"),
                                     ("RunQwen25", "qwen2.5-1.5b-instruct-generic-cpu:4", "benchmark-qwen25.log"),
                                     ("RunQwen35", "qwen3.5-2b-text-generic-cpu:1", "benchmark-qwen35.log")]:
        command = ("@echo off\r\nchcp 65001 >nul\r\n"
                   f'"%~dp0WindowsReadableNativeProbe.exe" {argument} > "%~dp0{log_name}" 2>&1\r\n'
                   "set benchmark_exit=%ERRORLEVEL%\r\n"
                   f"echo Exit code: %benchmark_exit%\r\necho Log: %~dp0{log_name}\r\n"
                   "pause\r\nexit /b %benchmark_exit%\r\n")
        (package / (name + ".cmd")).write_bytes(command.encode("utf-8"))
    sources = sorted(set(repository.glob("src/**/*.cs")) | set(repository.glob("src/**/*.csproj"))
                     | set(project_dir.glob("*.cs")) | set(project_dir.glob("*.csproj"))
                     | {project_dir / "package.py", project_dir / "README.txt",
                        repository / "tools/model-evaluation/QualificationCorpus.cs",
                        repository / "Directory.Build.props", repository / "global.json",
                        repository / "scripts/collect-package-notices.py", repository / "scripts/check-public-tree.py",
                        repository / ".github/workflows/readable-native-probe.yml",
                        repository / ".github/workflows/windows-fictional-benchmark.yml"}
                     | set(repository.glob("licenses/**/*.txt"))
                     | set(repository.glob("tests/Takupoke.Core.Tests/fixtures/recovery-*.json")))
    sources = [p for p in sources if "obj" not in p.parts and "bin" not in p.parts]
    commit = subprocess.run(["git", "rev-parse", "HEAD"], cwd=repository, check=True,
                            capture_output=True, text=True).stdout.strip()
    snapshot = {"stage": "isolated development benchmark; no qualification or activation",
                "sourceCommit": commit, "runtime": "Foundry.Local.WinML:1.2.4",
                "backend": "CPU", "preflightExecutionHost": preflight_host,
                "sources": [{"name": p.relative_to(repository).as_posix(),
                                               "sha256": sha256(p)} for p in sources]}
    (package / "source-snapshot.json").write_text(json.dumps(snapshot, indent=2) + "\n", encoding="utf-8")
    inventory = [{"path": p.relative_to(package).as_posix(), "bytes": p.stat().st_size, "sha256": sha256(p)}
                 for p in sorted(package.rglob("*")) if p.is_file()]
    (package / "package-inventory.json").write_text(json.dumps(inventory, indent=2) + "\n", encoding="utf-8")
    archive = output / "takupoke-windows-fictional-benchmark-x64.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
        for path in sorted(package.rglob("*")):
            if path.is_file():
                zipped.write(path, path.relative_to(package).as_posix())
    checksum = sha256(archive)
    (output / (archive.name + ".sha256.txt")).write_text(f"{checksum}  {archive.name}\n", encoding="ascii")
    print(json.dumps({"archive": str(archive), "bytes": archive.stat().st_size, "sha256": checksum,
                      "scope": "Windows x64 fictional QA prerelease; physical execution still required"}))


if __name__ == "__main__":
    main()
