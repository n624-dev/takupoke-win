import unittest
from report_transport import emit_report
from run_render import CASE_ID, decode_reports


class RequiredReportTests(unittest.TestCase):
    def test_missing_all_chunks_cannot_be_complete(self):
        with self.assertRaisesRegex(ValueError, "Required"):
            decode_reports([])

    def test_wrong_case_cannot_be_complete(self):
        rows = []
        emit_report("wrong-case", {"field": "架空"}, rows.append)
        with self.assertRaisesRegex(ValueError, "Required"):
            decode_reports(rows)

    def test_expected_complete_case_preserves_exact_data(self):
        rows = []
        report = {"field": "架空", "pixels": "x" * 30000}
        emit_report(CASE_ID, report, rows.append)
        self.assertEqual({CASE_ID: report}, decode_reports(rows))


if __name__ == "__main__":
    unittest.main()
