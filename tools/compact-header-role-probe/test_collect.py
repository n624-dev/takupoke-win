import base64
import hashlib
import json
import unittest
from collect import IDS, restore


class TransportTests(unittest.TestCase):
    def setUp(self):
        self.report = {"observations": [{"Id": i, "Assessed": i != "teacher"} for i in sorted(IDS)],
                       "summary": {"planned": 3, "recorded": 3, "assessed": 2, "operationallyUnassessed": 1}}

    def encode(self, report):
        raw = json.dumps(report).encode()
        encoded = base64.b64encode(raw).decode()
        chunks = [encoded[i:i+120] for i in range(0, len(encoded), 120)]
        meta = {"bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest(), "chunks": len(chunks)}
        log = "timestamp COMPACT_HEAD_REPORT_META " + json.dumps(meta) + "\n"
        return raw, log + "\n".join(f"timestamp COMPACT_HEAD_REPORT_CHUNK {i} {c}" for i, c in enumerate(chunks))

    def test_exact_bytes_with_operational_failure_preserved(self):
        raw, log = self.encode(self.report)
        self.assertEqual(raw, restore(log))

    def test_missing_duplicate_changed_chunks_rejected(self):
        _, log = self.encode(self.report)
        with self.assertRaises(ValueError): restore(log.rsplit("\n", 1)[0])
        with self.assertRaises(ValueError): restore(log + "\n" + log.splitlines()[1])
        with self.assertRaises(ValueError): restore(log[:-1] + ("A" if log[-1] != "A" else "B"))

    def test_empty_log_and_missing_case_rejected(self):
        with self.assertRaises(ValueError): restore("")
        self.report["observations"].pop()
        with self.assertRaises(ValueError): restore(self.encode(self.report)[1])

    def test_wrong_denominator_rejected(self):
        self.report["summary"]["assessed"] = 3
        with self.assertRaises(ValueError): restore(self.encode(self.report)[1])


if __name__ == "__main__":
    unittest.main()
