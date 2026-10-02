"""Collect original notices from the job-owned restored NuGet packages, without network calls."""
import argparse
import re
import xml.etree.ElementTree as ET
from pathlib import Path


def collect(packages: Path, output: Path, license_templates: Path):
    packages = packages.resolve()
    output.mkdir(parents=True, exist_ok=True)
    sections = ["Third-party software included or used when building Takupoke Win.\n"
                "Each package retains its own license; this document does not license the app itself.\n"]
    count = 0
    for spec in sorted(packages.glob("*/*/*.nuspec")):
        if not spec.resolve().is_relative_to(packages):
            raise ValueError("Package path is outside the isolated package root.")
        if spec.stat().st_size > 2_000_000:
            raise ValueError("Unexpected package metadata size.")
        document = ET.fromstring(spec.read_bytes())
        metadata = next((node for node in document if node.tag.split("}")[-1] == "metadata"), None)
        if metadata is None:
            raise ValueError("Package metadata is missing.")
        values = {node.tag.split("}")[-1]: node for node in metadata}
        name = values["id"].text or ""
        version = values["version"].text or ""
        license_node = values.get("license")
        text = []
        if license_node is not None and license_node.attrib.get("type") == "file":
            license_path = (spec.parent / (license_node.text or "").replace("\\", "/")).resolve()
            if not license_path.is_relative_to(spec.parent.resolve()) or not license_path.is_file():
                raise ValueError(f"Invalid license path for {name}.")
            text.append(license_path.read_text(encoding="utf-8-sig"))
        elif license_node is not None and license_node.attrib.get("type") == "expression":
            expression = license_node.text or ""
            for identifier in sorted(set(re.findall(r"[A-Za-z0-9][A-Za-z0-9.+-]*", expression)) - {"AND", "OR", "WITH"}):
                template = license_templates / (identifier + ".txt")
                if not template.is_file():
                    raise ValueError(f"Missing license text {identifier} for {name}.")
                text.append(template.read_text(encoding="utf-8"))
            for original in spec.parent.iterdir():
                if original.is_file() and re.match(r"(?i)^licen[cs]e(?:[._-]|$)", original.name) and original.suffix.lower() in {".txt", ".md", ""}:
                    text.append(original.read_text(encoding="utf-8-sig"))
        else:
            # Older packages sometimes ship their notice without a modern nuspec license field.
            candidates = [p for p in spec.parent.iterdir() if p.is_file() and re.match(r"(?i)^licen[cs]e(?:[._-]|$)", p.name)]
            if not candidates:
                raise ValueError(f"No bundled license text for {name} {version}.")
            text.extend(p.read_text(encoding="utf-8-sig") for p in candidates)
        for extra in spec.parent.iterdir():
            if extra.is_file() and re.match(r"(?i)^(?:notice|third.?party.?notices)(?:[._-]|$)", extra.name):
                text.append(extra.read_text(encoding="utf-8-sig"))
        copyright_node = values.get("copyright")
        copyright_text = copyright_node.text if copyright_node is not None else ""
        sections.append(f"\n{'=' * 72}\n{name} {version}\n{copyright_text or ''}\n" + "\n\n".join(dict.fromkeys(text)))
        count += 1
    if count == 0:
        raise ValueError("No restored package notices were found.")
    (output / "THIRD-PARTY-NOTICES.txt").write_text("\n".join(sections), encoding="utf-8")
    print(f"Collected original license notices for {count} restored packages.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("packages", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("license_templates", type=Path)
    args = parser.parse_args()
    collect(args.packages, args.output, args.license_templates)
