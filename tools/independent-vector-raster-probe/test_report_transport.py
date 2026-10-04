import json
import unittest
from report_transport import emit_report, read_reports


class TransportTests(unittest.TestCase):
    def setUp(self):
        self.report = {"nativeReport": {"originalPixels": "abcd" * 70000,
                                       "glyphs": [{"Text": "架空：字形"}] * 30000}}
        self.lines = []
        emit_report("invented", self.report, self.lines.append)

    def test_large_unicode_report_survives_bounded_prefixed_console_lines(self):
        self.assertGreater(len(json.dumps(self.report)), 1024 * 1024)
        self.assertTrue(all(len(line.encode("utf-8")) < 8192 for line in self.lines))
        prefixed = ["job\tstep\t2026-10-04T00:00:00Z " + line for line in self.lines]
        self.assertEqual({"invented": self.report}, read_reports(prefixed))

    def test_missing_chunk_rejects_incomplete_result(self):
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            read_reports(self.lines[:-1])

    def test_duplicate_chunk_rejects(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            read_reports(self.lines + self.lines[:1])

    def test_same_length_modified_payload_rejects(self):
        changed = json.loads(self.lines[0]); changed["base64"] = "AAAA" + changed["base64"][4:]
        with self.assertRaisesRegex(ValueError, "checksum"):
            read_reports([json.dumps(changed)] + self.lines[1:])


if __name__ == "__main__":
    unittest.main()
