"""Search the test map, validate its declarations and report checks affected by a Git base."""
import argparse
from pathlib import Path
import os
import sys

from test_catalog_inventory import cases, documents, git, load

ROOT = Path(__file__).resolve().parents[1]
CONFIG = "tests/test-catalog.json"


def changed(root, base):
    git(root, "rev-parse", "--verify", base + "^{commit}")
    result = git(root, "diff", "--name-only", "--no-renames", "-z", base)
    result += git(root, "ls-files", "--others", "--exclude-standard", "-z")
    return set(filter(None, result.decode().split("\0")))


def enforce(root, config, owners, base):
    modified = changed(root, base)
    try:
        import json
        previous = json.loads(git(root, "show", f"{base}:{CONFIG}"))
    except Exception:
        # Initial adoption is still checked against the complete current mapping.
        previous = config
    affected = {}
    for path in modified:
        if path == CONFIG:
            continue  # Mapping edits are validated by load and exact document generation.
        for group in config["groups"] + previous["groups"]:
            if path in group["sources"] + group["checks"]:
                name = group["id"]
                if name in affected:
                    group = {**group, "checks": sorted(set(group["checks"] + affected[name]["checks"]))}
                affected[name] = group
    for name, group in sorted(affected.items()):
        print("対象検索:", group["title"], "=>", ", ".join(group["checks"]))
        print("対象検証:", " / ".join(group["commands"]))
    if affected:
        print("既存ケースの十分性と実行結果を検証記録へ残す。必要な場合だけテストを更新する。")


def run(root, mode, query=None, base=None):
    config, owners = load(root, CONFIG)
    generated = documents(root, config)
    if mode == "search":
        for group in config["groups"]:
            names = [name for path in group["checks"] for name, _ in cases(root / path)]
            if query.casefold() in "\n".join([group["title"], *group["sources"],
                                             *group["checks"], *names]).casefold():
                print(group["title"])
                print("ソース:", ", ".join(group["sources"]))
                print("テスト:", ", ".join(group["checks"]))
                print("実行:", " / ".join(group["commands"]))
        return
    for path, expected in generated.items():
        target = root / path
        if mode == "write":
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(expected, encoding="utf-8")
        elif not target.exists() or target.read_text(encoding="utf-8") != expected:
            raise ValueError("Stale test catalogue; run write: " + path)
    extras = {str(path.relative_to(root)) for path in (root / config["document_folder"]).glob("*.md")} - generated.keys()
    if extras:
        raise ValueError("Remove obsolete generated catalogue files: " + ", ".join(sorted(extras)))
    if mode == "check" and base:
        enforce(root, config, owners, base)
    print("Test catalogue:", len(owners), "registered files; source declarations checked.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["search", "write", "check"])
    parser.add_argument("query", nargs="?")
    parser.add_argument("--base", default=os.environ.get("TKPK_TEST_BASE"))
    args = parser.parse_args()
    if args.mode == "search" and not args.query:
        parser.error("search requires a path, feature or case name")
    try:
        base = args.base
        if args.mode == "check" and os.environ.get("GITHUB_ACTIONS") == "true" and not base:
            base = git(ROOT, "rev-parse", "HEAD^").decode().strip()
        run(ROOT, args.mode, args.query, base)
    except (ValueError, KeyError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
