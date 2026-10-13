import unittest
import run


class ClosedProtocolScoreTests(unittest.TestCase):
    def setUp(self):
        self.oracle = [{'id': f'n{i:03d}', 'kind': 'blank' if i == 0 else 'room',
            'cohort': 'invented', 'literal': '' if i == 0 else '独立室'} for i in range(192)]
        self.records = [{'model': model['id'], 'id': o['id'], 'text': o['literal'],
            'eosObserved': True, 'minimumTokenProbabilityIncludingEos': .99}
            for model in run.MODELS for o in self.oracle]

    def test_missing_duplicate_or_foreign_native_call_is_unassessed(self):
        for records in (self.records[:-1], self.records + [self.records[0]],
                        self.records[:-1] + [{**self.records[-1], 'id': 'not-a-planned-call'}]):
            with self.assertRaises(ValueError): run.score(records, self.oracle)

    def test_blank_hallucination_and_high_confidence_wrong_room_remain_errors(self):
        self.records[0].update(text='0.00')
        self.records[1].update(text='独立教員')
        scored = run.score(self.records, self.oracle)
        blank = next(r for r in scored if r['model'] == 'ndl-30' and r['kind'] == 'blank')
        room = next(r for r in scored if r['model'] == 'ndl-30' and r['kind'] == 'room')
        self.assertEqual((0, 1, 1), (blank['exact'], blank['wrong'], blank['wrongWithMinimumTokenProbabilityAtLeast08']))
        self.assertEqual(1, room['wrongWithMinimumTokenProbabilityAtLeast08'])
        self.assertGreater(room['editDistance'], 0)


if __name__ == '__main__': unittest.main()
