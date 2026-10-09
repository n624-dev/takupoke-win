"""Deterministic, source-only test inventory; never executes or reads school data."""
import ast
import hashlib
import json
from pathlib import Path
import re
import subprocess

CODE_SUFFIXES = {".swift", ".py", ".sh", ".cs", ".xaml", ".ps1", ".yml", ".yaml",
                 ".csproj", ".props", ".sln", ".slnx", ".pbxproj", ".plist", ".xcprivacy", ".json",
                 ".m", ".mm", ".h", ".c", ".cpp", ".xcconfig", ".entitlements", ".xcworkspacedata"}


def git(root, *args):
    return subprocess.check_output(["git", *args], cwd=root, stderr=subprocess.PIPE)


def paths(root):
    return sorted(set(filter(None, git(root, "ls-files", "-co", "--exclude-standard", "-z")
                             .decode().split("\0"))))


def cases(path):
    text = path.read_text(encoding="utf-8")
    if path.suffix == ".py":
        return [(node.name, node.lineno) for node in ast.walk(ast.parse(text))
                if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
                and node.name.startswith("test_")]
    if path.suffix == ".swift":
        pattern = r"\bfunc\s+(test\w+)\s*\("
        if path.name in {"MaterialLibraryChecks.swift", "WebPDFChecks.swift"}:
            pattern = r"\bstatic\s+func\s+(main|run)\s*\("
    elif path.suffix == ".cs":
        pattern = (r"\[(?:Fact|Theory)(?:\([^]]*\))?\][\s\S]*?\bpublic\s+"
                   r"(?:async\s+)?(?:void|Task)\s+(\w+)\s*\(")
        if "UITests" in str(path):
            pattern = r"\bprivate\s+static\s+(?:void|int)\s+(Check\w+)\s*\("
    else:
        return []
    return [(match.group(1), text.count("\n", 0, match.start()) + 1)
            for match in re.finditer(pattern, text)]


def fingerprint(path, content):
    """Ignore formatting and comments, retaining strings, identifiers and operators."""
    text = content.decode("utf-8")
    if Path(path).suffix == ".py":
        return ast.dump(ast.parse(text), include_attributes=False)
    tokens = re.findall(r'"""[\s\S]*?"""|@?"(?:\\.|""|[^"\\])*"|'
                        r"'(?:\\.|[^'\\])*'|//[^\n]*|/\*[\s\S]*?\*/|\w+|[^\s]", text)
    return json.dumps([token for token in tokens if not token.startswith(("//", "/*"))])


def load(root, configuration):
    config = json.loads((root / configuration).read_text(encoding="utf-8"))
    found = paths(root)
    groups = config["groups"]
    if len({group["id"] for group in groups}) != len(groups):
        raise ValueError("Duplicate group id")
    owners = {}
    checks = set()
    source_owners = set()
    for group in groups:
        if not re.fullmatch(r"[a-z][a-z0-9-]*", group["id"]):
            raise ValueError("Invalid group id")
        if not group["checks"]:
            raise ValueError("Missing tests: " + group["id"])
        for path in group["sources"]:
            if path in source_owners or path in checks:
                raise ValueError("Duplicate source ownership: " + path)
            source_owners.add(path)
        for path in group["sources"] + group["checks"]:
            if path not in found or not (root / path).is_file():
                raise ValueError("Missing registered file: " + path)
            owners[path] = group
        for path in group["checks"]:
            if path in source_owners:
                raise ValueError("A source cannot also qualify as its own test: " + path)
            if not (path.startswith("tests/") or "/test_" in path) or not cases(root / path):
                raise ValueError("No test declarations: " + path)
            checks.add(path)
    code = {path for path in found if (root / path).is_file() and Path(path).suffix in CODE_SUFFIXES
            and any(path == prefix or path.startswith(prefix + "/") for prefix in config["code_roots"])}
    missing = code - owners.keys()
    if missing:
        raise ValueError("Unregistered code; search and register its tests: " + ", ".join(sorted(missing)))
    detected = {path for path in code if (path.startswith("tests/") or "/test_" in path)
                and cases(root / path)}
    if detected - checks:
        raise ValueError("Unregistered test declarations: " + ", ".join(sorted(detected - checks)))
    return config, owners


def documents(root, config):
    folder = config["document_folder"]
    index = ["# テスト一覧と変更時の必須手順", "", "この一覧はソースの宣言と対応表から生成する。成功件数や品質合格の記録ではない。",
             "Theoryの入力展開・条件付きskip・CI実行件数は別の検証記録で確認する。",
             "対応付けは変更時に確認すべきテストを示し、各ファイルの全動作を検証済みとは保証しない。", "",
             "1. 編集前に対象ファイル名・機能・ケース名で下記のsearchを実行する。",
             "2. 対応するテストの入力・期待値・実操作を変更し、対象の検証を実行する。",
             "3. writeで一覧を更新し、check --baseで実際の変更範囲を検査する。",
             "4. 未登録コード、古い一覧、対応テストを変更していないコード変更はCIを失敗させる。",
             "   コメントや整形だけのテスト編集ではコード変更の条件を満たさない。",
             "   一覧の再生成はテスト実行の代わりにならない。", "",
             "```bash", f"python3 {config['script']} search 曜日",
             f"python3 {config['script']} write",
             f"python3 {config['script']} check --base <編集前のコミット>", "```", "",
             "CIはpushのbefore、PRのbase、手動実行ではHEADの親を比較する。",
             "ファイルの削除や移動でも以前の対応テストを検査する。",
             "取得・解析・保存をまたぐテストは、関連する複数の対象から参照する。", "",
             "| 対象 | 宣言数 | 検証する環境 |", "|---|---:|---|"]
    output = {}
    for group in config["groups"]:
        declarations = {path: cases(root / path) for path in group["checks"]}
        count = sum(len(items) for items in declarations.values())
        index.append(f"| [{group['title']}](test-catalog/{group['id']}.md) | {count} | {group['environment']} |")
        files = sorted(group["sources"] + group["checks"])
        digest = hashlib.sha256()
        for path in files:
            digest.update(path.encode() + b"\0" + (root / path).read_bytes() + b"\0")
        lines = ["# " + group["title"], "", "対応ソース・テストのSHA-256：", "`" + digest.hexdigest() + "`", "",
                 "環境：" + group["environment"], "", "```bash", *group["commands"], "```", "",
                 "## 変更時に確認するソース", ""]
        lines.extend(f"- [{path}](../../{path})" for path in group["sources"])
        for path, items in declarations.items():
            lines += ["", f"## [{path}](../../{path})", ""]
            lines.extend(f"- `{name}`（宣言行 {number}）" for name, number in items)
        output[f"{folder}/{group['id']}.md"] = "\n".join(lines) + "\n"
    output[config["index"]] = "\n".join(index) + "\n"
    return output
