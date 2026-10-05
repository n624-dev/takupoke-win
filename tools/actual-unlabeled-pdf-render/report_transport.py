"""Bounded console transport; no Actions artifact or unbounded log line."""
import base64
import hashlib
import json

LIMIT = 64 * 1024 * 1024
CHUNK_BYTES = 4096
MARKER = "fictional-actual-pdf-render-v1"


def emit_report(report_id, report, write=print):
    payload = json.dumps(report, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    if len(payload) > LIMIT:
        raise ValueError("Report exceeds console transport bound")
    digest = hashlib.sha256(payload).hexdigest()
    count = (len(payload) + CHUNK_BYTES - 1) // CHUNK_BYTES
    for index in range(count):
        chunk = payload[index * CHUNK_BYTES:(index + 1) * CHUNK_BYTES]
        write(json.dumps({"transport": MARKER, "id": report_id, "index": index,
                          "count": count, "bytes": len(payload), "sha256": digest,
                          "base64": base64.b64encode(chunk).decode("ascii")}, separators=(",", ":")))


def read_reports(lines):
    """Reject incomplete, duplicated or modified chunks; supports Actions prefixes."""
    records = {}
    for line in lines:
        offset = line.find('{"transport":')
        if offset < 0:
            continue
        record = json.loads(line[offset:])
        if record["transport"] != MARKER:
            continue
        count, index, size = record["count"], record["index"], record["bytes"]
        if (type(count) is not int or type(index) is not int or type(size) is not int
                or not 0 < size <= LIMIT or count != (size + CHUNK_BYTES - 1) // CHUNK_BYTES
                or not 0 <= index < count):
            raise ValueError("Invalid report envelope")
        header = (count, size, record["sha256"])
        existing_header, chunks = records.setdefault(record["id"], (header, {}))
        if existing_header != header or index in chunks:
            raise ValueError("Duplicate or conflicting report chunk")
        chunk = base64.b64decode(record["base64"], validate=True)
        if len(chunk) != min(CHUNK_BYTES, size - index * CHUNK_BYTES):
            raise ValueError("Invalid report chunk length")
        chunks[index] = chunk
    reports = {}
    for report_id, ((count, size, digest), chunks) in records.items():
        if len(chunks) != count:
            raise ValueError("Incomplete report")
        payload = b"".join(chunks[index] for index in range(count))
        if len(payload) != size or hashlib.sha256(payload).hexdigest() != digest:
            raise ValueError("Report checksum mismatch")
        reports[report_id] = json.loads(payload.decode("utf-8", errors="strict"))
    return reports
