"""Recover only a complete byte/hash-verified bounded report from CI console."""
import base64
import hashlib
import json
import re
import sys
from pathlib import Path

IDS = {"subject", "teacher", "room"}


def restore(log):
    metadata, chunks = [], {}
    for line in log.splitlines():
        if "COMPACT_HEAD_REPORT_META " in line:
            metadata.append(json.loads(line.split("COMPACT_HEAD_REPORT_META ", 1)[1]))
        match = re.search(r"COMPACT_HEAD_REPORT_CHUNK (\d+) ([A-Za-z0-9+/=]+)$", line)
        if match:
            index = int(match[1])
            if index in chunks:
                raise ValueError("Duplicate chunk")
            chunks[index] = match[2]
    if len(metadata) != 1:
        raise ValueError("One metadata record required")
    meta = metadata[0]
    if not 0 < meta["bytes"] <= 256 * 1024 or not 0 < meta["chunks"] <= 60:
        raise ValueError("Report exceeds bounds")
    if set(chunks) != set(range(meta["chunks"])) or any(len(c) > 6000 for c in chunks.values()):
        raise ValueError("Incomplete chunks")
    raw = base64.b64decode("".join(chunks[i] for i in range(meta["chunks"])), validate=True)
    if len(raw) != meta["bytes"] or hashlib.sha256(raw).hexdigest() != meta["sha256"]:
        raise ValueError("Report integrity mismatch")
    report = json.loads(raw)
    rows = report["observations"]
    if len(rows) != 3 or {r["Id"] for r in rows} != IDS:
        raise ValueError("Complete planned case inventory required")
    s = report["summary"]
    if s["planned"] != 3 or s["recorded"] != len(rows) or s["assessed"] != sum(r["Assessed"] for r in rows):
        raise ValueError("Denominator mismatch")
    if s["operationallyUnassessed"] + s["assessed"] != 3:
        raise ValueError("Assessment conservation failed")
    # Transport can complete while native assessment fails. Keep both statuses.
    return raw


if __name__ == "__main__":
    Path(sys.argv[2]).write_bytes(restore(Path(sys.argv[1]).read_text(encoding="utf-8-sig")))
