import unittest

import run
import run_frozen


class ScoringTests(unittest.TestCase):
    def test_frozen_transport_keeps_pixel_hashes_and_canonical_line_endings(self):
        import json
        from pathlib import Path
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); rows = run_frozen.prepare_rows(root)
            self.assertEqual(192, len(rows))
            source = json.loads(run_frozen.PIXELS.read_text(encoding='utf-8'))['rows']
            self.assertEqual([r['bgraSha256'] for r in source], [r['bgr']['sha256'] for r in rows])
            run_frozen.canonical_write(root / 'canonical.json', {'text': '架空\n文字'})
            self.assertEqual(b'{"text":"\xe6\x9e\xb6\xe7\xa9\xba\\n\xe6\x96\x87\xe5\xad\x97"}\n', (root / 'canonical.json').read_bytes())

    @staticmethod
    def fixture():
        gold = [{"id": f"n{i:03d}", "cohort": "sans" if i < 96 else "serif", "kind": "room", "literal": "仮室I0"} for i in range(192)]
        rows = [{"id": g["id"], "model": model, "condition": condition, "text": g["literal"], "minimumConfidence": .9,
                 "unsupportedPadding": False, "decodeExact": True, "legacyDecodeMilliseconds": 1,
                 "currentDecodeMilliseconds": .5, "inferenceMilliseconds": 20}
                for g in gold for model in ("v5-mobile", "v6-medium") for condition in ("bgr", "gray")]
        return rows, gold

    def test_literal_case_and_spaces_are_not_normalized(self):
        self.assertEqual(0, run.edit_distance("仮室 I0", "仮室 I0"))
        self.assertEqual(1, run.edit_distance("仮室 I0", "仮室I0"))
        self.assertEqual(2, run.edit_distance("I0", "lO"))
        self.assertEqual(1, run.edit_distance("𠮷", "吉"))

    def test_wrong_high_confidence_and_unsupported_intervals_stay_separate(self):
        rows, gold = self.fixture()
        rows[0]["text"] = "仮室l0"
        rows[4]["text"] = "仮室l0"; rows[4]["unsupportedPadding"] = True
        rows[8]["minimumConfidence"] = .7
        summary = next(r for r in run.score(rows, gold) if r["cohort"] == "sans" and r["model"] == "v5-mobile" and r["condition"] == "bgr")
        self.assertEqual(94, summary["exact"])
        self.assertEqual(1, summary["incorrectAboveFloor"])
        self.assertEqual(1, summary["unsupportedPadding"])
        self.assertEqual(1, summary["belowFloorOrEmpty"])
        self.assertEqual(2, summary["characterEdits"])

    def test_missing_duplicate_foreign_or_changed_decoder_observations_refuse(self):
        rows, gold = self.fixture()
        with self.assertRaises(ValueError): run.score(rows[:-1], gold)
        with self.assertRaises(ValueError): run.score(rows + [rows[0]], gold)
        rows[0]["id"] = "foreign"
        with self.assertRaises(ValueError): run.score(rows, gold)
        rows, gold = self.fixture(); rows[0]["decodeExact"] = False
        with self.assertRaises(ValueError): run.score(rows, gold)

    def test_supplemental_model_set_requires_all_its_own_pairs(self):
        rows, gold = self.fixture()
        rows = [dict(r, model='v6-small') for r in rows if r['model'] == 'v5-mobile']
        self.assertEqual(384, sum(s['observed'] for s in run.score(rows, gold, ['v6-small'])))
        with self.assertRaises(ValueError): run.score(rows[:-1], gold, ['v6-small'])
        with self.assertRaises(ValueError): run.score(rows, gold)
        with self.assertRaises(ValueError): run.score(rows, gold, ['v6-small', 'v6-small'])
        with self.assertRaises(ValueError): run.score(rows, gold, [])

    def test_color_arms_keep_identical_crop_and_opaque_original_pixels(self):
        from PIL import ImageFont
        font = ImageFont.load_default()
        (width, height), bgr, gray = run.pixels("Al_1", font, (12, 25, 76))
        self.assertEqual(width * height * 4, len(bgr))
        self.assertEqual(len(bgr), len(gray))
        self.assertTrue(all(bgr[i] == gray[i] == 255 for i in range(3, len(bgr), 4)))
        self.assertTrue(all(gray[i] == gray[i + 1] == gray[i + 2] for i in range(0, len(gray), 4)))

    def test_outcome_digest_keeps_text_geometry_and_floor_decisions(self):
        row = {"id": "n000", "model": "v5-mobile", "condition": "bgr", "text": "甲",
               "unsupportedPadding": False, "ValidWidth": 72, "InputWidth": 320, "timeSteps": 80,
               "minimumConfidence": .9, "pieces": [{"Text": "甲", "Start": 3, "End": 4, "Confidence": .9}]}
        digest = run.outcome_sha([row])
        row["minimumConfidence"] = .900001; row["pieces"][0]["Confidence"] = .900001
        self.assertEqual(digest, run.outcome_sha([row]))
        row["pieces"][0]["End"] = 5
        self.assertNotEqual(digest, run.outcome_sha([row]))
        row["pieces"][0]["End"] = 4; row["minimumConfidence"] = .79
        self.assertNotEqual(digest, run.outcome_sha([row]))


if __name__ == "__main__":
    unittest.main()
