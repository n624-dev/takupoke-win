"""Post-worker literal whole-document checks; never imported by the native reader."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

from run import PACKET_SHA, write


def slot(value, native=True):
    return (value['ClassName' if native else 'className'],
            str(value['Day' if native else 'day']), value['Period' if native else 'period'])


def lesson(value, native=True):
    names = value['Names' if native else 'names']
    return (value['ClassName' if native else 'className'],
            value['Weekday' if native else 'weekday'], value['Period' if native else 'period'],
            *(names[k] for k in (('Subject', 'Teacher', 'Room') if native else ('subject', 'teacher', 'room'))))


def assess(observation, oracle):
    # A refusal on an intended positive is a positive failure, not a successful
    # negative. Execution/missing-data failures carry no accuracy credit.
    formal = observation.get('formal')
    result = {'id': observation['id'], 'condition': observation['condition'],
              'outcome': observation['outcome'], 'failure': observation.get('failure'),
              'exactDocument': False, 'positiveRefused': formal is None,
              'manualCorrections': observation['manualCorrections']}
    if observation['outcome'] == 'execution-error-unassessed':
        result['quality'] = 'unassessed'
        return result
    if formal is None:
        result['quality'] = 'positive-failed'
        return result
    document = observation['document']
    expected = Counter(slot(s, False) for s in oracle['requiredSlotSet'])
    actual = Counter(slot(s) for s in document['RequiredSlots'])
    cell_slots = Counter(slot(s) for c in document['Cells'] for s in c['Slots'])
    nonempty = {(v['className'], str(v['weekday']), v['period']) for v in oracle['timetable']['lessons']}
    blank_ok = oracle['allOtherSlotsBlank'] and all(
        bool(c['ConfirmedEmpty']) == (slot(s) not in nonempty)
        for c in document['Cells'] for s in c['Slots'])
    lessons_ok = Counter(lesson(v) for v in formal['Lessons']) == Counter(
        lesson(v, False) for v in oracle['timetable']['lessons'])
    checks = {
        'year': formal['SchoolYear'] == oracle['schoolYear'],
        'term': formal['Term'] == oracle['timetable']['term'],
        'requiredSlotSet': expected == actual and all(n == 1 for n in actual.values()),
        'cellSlotCoverage': expected == cell_slots and all(n == 1 for n in cell_slots.values()),
        'allOtherSlotsBlank': blank_ok,
        'lessonsAndLiteralNames': lessons_ok,
        'zeroManualCorrections': observation['manualCorrections'] == 0,
        'completeSourceCapture': document['Complete'] and bool(document['Capture'])
            and document['Capture']['PageCount'] == len(document['Capture']['Pages'])
            and all(p['InkCoverageVerified'] for p in document['Capture']['Pages']),
        'formalReturned': observation['outcome'] == 'formal-returned-unscored',
    }
    result.update(checks=checks, exactDocument=all(checks.values()),
                  quality='positive-exact' if all(checks.values()) else 'positive-wrong',
                  positiveRefused=False)
    return result


def evaluate(raw_path, packet_path):
    packet_bytes = packet_path.read_bytes()
    if hashlib.sha256(packet_bytes).hexdigest() != PACKET_SHA:
        raise ValueError('Consumed oracle packet changed')
    cases = json.loads(packet_bytes)['cases']
    raw = raw_path.read_bytes()
    observations = [json.loads(line) for line in raw.decode('utf-8-sig').splitlines()]
    keys = Counter((o['id'], o['condition']) for o in observations)
    expected = {(f'd{i}', condition) for i in range(len(cases))
                for condition in ('production', 'physical-input-frame')}
    if set(keys) != expected or any(n != 1 for n in keys.values()):
        raise ValueError('Missing or duplicate native document/condition; unassessed')
    return {'recipe': 'post-worker-consumed-full-document-exact-v1',
            'rawSHA256': hashlib.sha256(raw).hexdigest(), 'oraclePacketSHA256': PACKET_SHA,
            'scope': 'Consumed original drawings only; no new heldout, production success rate or device qualification',
            'observations': [assess(o, cases[int(o['id'][1:])]['oracle']) for o in observations]}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--raw', type=Path, required=True)
    parser.add_argument('--packet', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = evaluate(args.raw, args.packet)
    write(args.output, report)
    print(json.dumps(report, ensure_ascii=False))
