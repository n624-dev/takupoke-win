import unittest
from shadow_metrics import summarize


class ShadowMetricsTests(unittest.TestCase):
    def test_occurrences_keep_multiplicity_and_do_not_count_unsupported_space_proof(self):
        def row(text, confidence, supported=True):
            return {"pieces": [{"Text": text, "Confidence": confidence},
                               {"Text": " ", "Confidence": .4}], "paddingSupported": supported}
        gold = {"slots": [{"lessons": [{"subject": "架空 ", "teacher": "架空 ", "room": "室 "}]}]}
        shadow = {"pages": [{"rows": [row("架空", 1), row("架空", 1), row("架空", 1),
                                     row("室", .79), row("室", 1, False),
                                     {"pieces": [], "paddingSupported": True}]}], "error": None}
        result = summarize(shadow, gold)
        self.assertEqual(result["exactBodyLiteralOccurrences"], 3)
        self.assertEqual(result["exactBodyLiteralsWithOnlyUncertainWhitespace"], 2)
        self.assertEqual(result["lowWhitespaceRows"], 5)
        self.assertEqual(result["lowNonWhitespaceRows"], 1)
        self.assertEqual(result["unsupportedPaddingRows"], 1)
        self.assertEqual(result["zeroPieceRows"], 1)
        self.assertTrue(result["nonAdoptable"])


if __name__ == "__main__":
    unittest.main()
