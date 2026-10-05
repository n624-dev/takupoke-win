import io
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from PIL import Image
import fitz
import generate_pdf as generator


class LosslessPDFTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.png = generator.generate_png(Path(os.environ.get('PUBLIC_FONT_PATH', '/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc')))
        cls.pdf, cls.rgb = generator.wrap_png(cls.png)

    def test_actual_pdf_embedded_and_rendered_pixels_equal_original(self):
        proof = generator.verify_pdf(self.pdf, self.rgb)
        self.assertTrue(proof['embeddedPixelEquality'])
        self.assertTrue(proof['actual1xRenderPixelEquality'])
        self.assertEqual(generator.sha(self.pdf), '7ceb34d191dc48a5d6bc072e75898172cd20f356f6c9c312f558635d6a454323')
        self.assertEqual(generator.wrap_png(self.png)[0], self.pdf)

    def test_pixel_corruption_is_rejected_after_actual_pdf_decode(self):
        altered = bytearray(self.rgb)
        altered[0] ^= 1
        with self.assertRaisesRegex(ValueError, 'embedded decoded RGB mismatch'):
            generator.verify_pdf(self.pdf, bytes(altered))

    def test_png_hash_and_dimensions_are_checked(self):
        with self.assertRaisesRegex(ValueError, 'PNG pin'):
            generator.wrap_png(self.png + b'corruption')
        data = io.BytesIO()
        Image.new('RGB', (2, 3), 'white').save(data, format='PNG')
        wrong = data.getvalue()
        with patch.object(generator, 'PNG_SHA', generator.sha(wrong)):
            with self.assertRaisesRegex(ValueError, 'pixel contract'):
                generator.wrap_png(wrong)

    def test_font_and_source_pins_are_checked_before_execution(self):
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory) / 'font.ttc'
            p.write_bytes(b'wrong font')
            with self.assertRaisesRegex(ValueError, 'font pin'):
                generator.generate_png(p)
            with patch.object(generator, 'PINS', {'generate.py': '0' * 64}):
                with self.assertRaisesRegex(ValueError, 'source pin'):
                    generator.generate_png(p)

    def test_owned_output_and_previous_bytes_are_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / generator.MARKER).write_bytes(b'wrong marker')
            with self.assertRaisesRegex(ValueError, 'marker mismatch'):
                generator.write_owned(root, self.pdf, {})
            (root / generator.MARKER).write_bytes(generator.MARKER_BYTES)
            generator.write_owned(root, self.pdf, {'fictional': '図研'})
            receipt = (root / 'receipt.json').read_bytes()
            self.assertIn('図研'.encode('utf-8'), receipt)
            self.assertNotIn(b'\r\n', receipt)
            with self.assertRaisesRegex(ValueError, 'overwrite'):
                generator.write_owned(root, b'wrong', {})
            self.assertEqual((root / 'unlabeled-consumed-development.pdf').read_bytes(), self.pdf)
            self.assertEqual((root / 'receipt.json').read_bytes(), receipt)

    def test_pdf_contains_only_one_image_no_hidden_text_or_fonts(self):
        with fitz.open(stream=self.pdf, filetype='pdf') as document:
            page = document[0]
            self.assertEqual(page.get_text(), '')
            self.assertEqual(page.get_fonts(), [])
            self.assertEqual(len(page.get_images()), 1)
            self.assertEqual(tuple(page.mediabox), tuple(page.cropbox))

    def test_partial_receipt_failure_removes_only_created_owned_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            marker = root / generator.MARKER
            marker.write_bytes(generator.MARKER_BYTES)
            other = root / 'other-owner.txt'
            other.write_bytes(b'keep')
            def failing_dump(value, stream, **kwargs):
                stream.write('{"partial":')
                raise OSError('simulated ENOSPC')
            with patch.object(generator.json, 'dump', side_effect=failing_dump):
                with self.assertRaisesRegex(OSError, 'ENOSPC'):
                    generator.write_owned(root, self.pdf, {})
            self.assertFalse((root / 'receipt.json').exists())
            self.assertFalse((root / 'unlabeled-consumed-development.pdf').exists())
            self.assertEqual(marker.read_bytes(), generator.MARKER_BYTES)
            self.assertEqual(other.read_bytes(), b'keep')

    def test_partial_pdf_write_failure_is_rolled_back(self):
        original_open = Path.open
        class FailingWriter:
            def __init__(self, stream):
                self.stream = stream
            def __enter__(self):
                return self
            def __exit__(self, *args):
                self.stream.close()
            def write(self, data):
                self.stream.write(data[:12])
                raise OSError('simulated PDF ENOSPC')
        def open_with_failure(path, *args, **kwargs):
            stream = original_open(path, *args, **kwargs)
            return FailingWriter(stream) if path.suffix == '.pdf' else stream
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / generator.MARKER).write_bytes(generator.MARKER_BYTES)
            with patch.object(Path, 'open', open_with_failure):
                with self.assertRaisesRegex(OSError, 'PDF ENOSPC'):
                    generator.write_owned(root, self.pdf, {})
            self.assertEqual(list(root.iterdir()), [root / generator.MARKER])


if __name__ == '__main__':
    unittest.main()
