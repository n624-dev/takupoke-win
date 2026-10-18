import copy
import unittest
from score import assess


class WholeDocumentScoreTests(unittest.TestCase):
    def setUp(self):
        self.oracle = {'schoolYear': 2027, 'allOtherSlotsBlank': True,
            'requiredSlotSet': [{'className': '5_ES', 'day': '1', 'period': p} for p in (1, 2)],
            'timetable': {'term': '前期', 'lessons': [{'className': '5_ES', 'weekday': 1,
                'period': 1, 'names': {'subject': '架空化学', 'teacher': '架空担当', 'room': '架空室'}}]}}
        slots = [{'ClassName': '5_ES', 'Day': '1', 'Period': p} for p in (1, 2)]
        self.observation = {'id': 'd0', 'condition': 'physical-input-frame',
            'outcome': 'formal-returned-unscored', 'failure': None, 'manualCorrections': 0,
            'formal': {'SchoolYear': 2027, 'Term': '前期', 'Lessons': [{'ClassName': '5_ES',
                'Weekday': 1, 'Period': 1, 'Names': {'Subject': '架空化学', 'Teacher': '架空担当', 'Room': '架空室'}}]},
            'document': {'Complete': True, 'RequiredSlots': slots,
                'Cells': [{'Slots': [s], 'ConfirmedEmpty': i == 1} for i, s in enumerate(slots)],
                'Capture': {'PageCount': 1, 'Pages': [{'InkCoverageVerified': True}]}}}

    def test_exact_document(self):
        self.assertTrue(assess(self.observation, self.oracle)['exactDocument'])

    def test_correct_names_cannot_hide_bad_slot_or_blank(self):
        for change in ('missing', 'duplicate', 'blank', 'capture', 'manual', 'year', 'literal', 'extraLesson'):
            with self.subTest(change=change):
                o = copy.deepcopy(self.observation)
                if change == 'missing': o['document']['RequiredSlots'].pop()
                if change == 'duplicate': o['document']['Cells'].append(o['document']['Cells'][0])
                if change == 'blank': o['document']['Cells'][1]['ConfirmedEmpty'] = False
                if change == 'capture': o['document']['Capture']['Pages'][0]['InkCoverageVerified'] = False
                if change == 'manual': o['manualCorrections'] = 1
                if change == 'year': o['formal']['SchoolYear'] = 2026
                if change == 'literal': o['formal']['Lessons'][0]['Names']['Room'] = '架空室 '
                if change == 'extraLesson': o['formal']['Lessons'].append(o['formal']['Lessons'][0])
                self.assertFalse(assess(o, self.oracle)['exactDocument'])

    def test_positive_refusal_gets_no_negative_credit(self):
        self.observation.update(formal=None, outcome='engine-refused')
        scored = assess(self.observation, self.oracle)
        self.assertEqual('positive-failed', scored['quality'])
        self.assertFalse(scored['exactDocument'])

    def test_execution_error_is_unassessed(self):
        self.observation.update(formal=None, outcome='execution-error-unassessed')
        self.assertEqual('unassessed', assess(self.observation, self.oracle)['quality'])


if __name__ == '__main__':
    unittest.main()
