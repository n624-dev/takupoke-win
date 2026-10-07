import unittest
from header_diagnostics import classify


class PositionTraceTests(unittest.TestCase):
    def setUp(self):
        self.spans = [dict(page=1, text='3_CN', body=False, box=[0, 0, 50, 20]),
                      dict(page=1, text='架空科目1', body=True, box=[0, 30, 90, 50])]

    def source(self, sid, text, y, score=.6):
        return dict(Id=sid, Page=1, Glyph=dict(Text=text, X=2, Y=y, Width=10, Height=10), Confidence=score)

    def test_same_text_does_not_move_body_into_header(self):
        actual = classify([self.source('p1s0', '3_CN', 2), self.source('p1s1', '3_CN', 32),
                           self.source('p1s2', 'lost', 90)], [], self.spans)
        self.assertEqual(actual['lowConfidenceNativeSources'], dict(header=1, body=1, unclassified=1))
        self.assertEqual(actual['lowConfidenceBodySpanCount'], 1)
        self.assertEqual(actual['bodyLiteralReconstructionExact'], 0)

    def test_native_identity_reaches_semantics_and_cell(self):
        traces = [dict(Stage='semantic-headers', Labels=[dict(Role='class', Value='3_CN', Ids=['p1s0'])]),
                  dict(Stage='built-cells', Cells=[dict(SourceIds=['p1s1'], HeaderIds=['p1s0'], Slots=[{}])])]
        actual = classify([self.source('p1s0', '3_CN', 2), self.source('p1s1', '架空科目1', 32)], traces, self.spans)
        self.assertEqual(actual['semanticValues']['class'], ['3_CN'])
        self.assertEqual(actual['nativeSourceIdsUsedInCells'], 1)
        self.assertEqual(actual['nativeSourceIdsUsedInHeaders'], 1)
        self.assertEqual(actual['bodyLiteralReconstructionExact'], 1)

    def test_duplicate_source_ids_cannot_hide_loss(self):
        source = self.source('p1s0', '3_CN', 2)
        with self.assertRaises(AssertionError):
            classify([source, source], [], self.spans)

    def test_overlapping_position_is_unclassified(self):
        actual = classify([self.source('p1s0', '3_CN', 2)], [], self.spans + [self.spans[0]])
        self.assertEqual(actual['unclassifiedNativeSources'], 1)
        self.assertEqual(actual['lowConfidenceNativeSources']['unclassified'], 1)


if __name__ == '__main__':
    unittest.main()
