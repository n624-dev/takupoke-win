"""Reject reserved local files and non-noreply commit identities before CI builds."""

import re
import subprocess
from pathlib import PurePosixPath


def git(*arguments):
    return subprocess.check_output(["git", *arguments])


reserved = {"docs", "internal", "private", "local-data", "secrets"}
extensions = {
    ".pdf", ".xlsx", ".xlsm", ".xls", ".csv", ".db", ".sqlite", ".sqlite3",
    ".log", ".dmp", ".pfx", ".p12", ".pem", ".key", ".cer", ".zip",
    ".msix", ".msixbundle", ".appx", ".appxbundle",
}
files = git("ls-files", "-z").decode().split("\0")
for name in filter(None, files):
    path = PurePosixPath(name)
    parts = {part.lower() for part in path.parts}
    if (
        parts & reserved
        or path.name.lower() == "agents.md"
        or path.name.lower().startswith(".env")
        or path.suffix.lower() in extensions
        or any(part.lower() in {"bin", "obj", "testresults"} for part in path.parts)
        or ".local." in path.name.lower()
        or re.search(r"\.(?:db|sqlite|sqlite3)-", path.name.lower())
    ):
        raise SystemExit("Reserved local data or generated output is tracked. Review the commit locally.")

addresses = git("log", "--all", "--format=%ae%n%ce").decode().splitlines()
for address in addresses:
    if not (
        re.fullmatch(r"[^@\s]+@users\.noreply\.github\.com", address)
        or address == "noreply@github.com"
    ):
        raise SystemExit("A commit identity is not a GitHub noreply address. Review the history locally.")

print("Public tree and noreply commit identities checked.")
