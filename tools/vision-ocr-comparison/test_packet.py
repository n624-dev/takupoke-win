import base64
import hashlib
import io
import json
from pathlib import Path
import unittest

from PIL import Image
import run


class FrozenPacketTests(unittest.TestCase):
    def test_all_original_pixels_are_retained_without_expected_text(self):
        path = Path(__file__).parent / 'fictional-pixels.json'
        self.assertEqual(run.PIXEL_SHA, hashlib.sha256(path.read_bytes()).hexdigest())
        rows = json.loads(path.read_text(encoding='utf-8'))['rows']
        self.assertEqual([f'n{i:03d}' for i in range(192)], [r['id'] for r in rows])
        for row in rows:
            self.assertEqual({'id', 'width', 'height', 'bgraSha256', 'pngSha256', 'pngBase64'}, set(row))
            data = base64.b64decode(row['pngBase64'], validate=True)
            self.assertEqual(row['pngSha256'], hashlib.sha256(data).hexdigest())
            image = Image.open(io.BytesIO(data)); self.assertEqual('RGB', image.mode)
            self.assertEqual((row['width'], row['height']), image.size)
            self.assertEqual(row['bgraSha256'], hashlib.sha256(image.convert('RGBA').tobytes('raw', 'BGRA')).hexdigest())

    def test_oracle_owns_exactly_the_same_ids_and_remains_separate(self):
        rows = json.loads((Path(__file__).parent / 'oracle.json').read_text(encoding='utf-8'))
        self.assertEqual([f'n{i:03d}' for i in range(192)], [r['id'] for r in rows])
        self.assertEqual({'sans', 'serif'}, {r['cohort'] for r in rows})
        for cohort in ('sans', 'serif'):
            self.assertEqual(72, sum(r['cohort'] == cohort and r['kind'] in ('subject', 'teacher', 'room') for r in rows))
            self.assertEqual(21, sum(r['cohort'] == cohort and r['kind'] == 'header' for r in rows))
            self.assertEqual(3, sum(r['cohort'] == cohort and r['kind'] == 'blank' and r['literal'] == '' for r in rows))


if __name__ == '__main__': unittest.main()
