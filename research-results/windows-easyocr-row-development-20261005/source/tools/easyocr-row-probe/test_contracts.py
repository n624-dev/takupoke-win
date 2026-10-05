import unittest
from PIL import Image
from contracts import verify_crop, verify_ids, sha

class OriginalInputTests(unittest.TestCase):
    def test_equal_shape_different_pixels_rejected(self):
        image = Image.new('RGB',(3,2),'white')
        row = {'box':[0,0,3,2],'shape':[2,3,3],'originalRGBSHA256':sha(image.tobytes())}
        self.assertEqual(verify_crop(image,row).tobytes(),image.tobytes())
        image.putpixel((0,0),(0,0,0))
        with self.assertRaisesRegex(ValueError,'bytes changed'): verify_crop(image,row)

    def test_outside_and_noninteger_boxes_rejected_without_clip(self):
        image = Image.new('RGB',(3,2),'white')
        for box in [[-1,0,3,2],[0,0,4,2],[0,0,2.5,2]]:
            with self.assertRaises(ValueError): verify_crop(image,{'box':box,'shape':[2,3,3],'originalRGBSHA256':sha(image.tobytes())})

    def test_missing_duplicate_reordered_rows_rejected(self):
        rows = [{'id':i} for i in range(170)]; verify_ids(rows)
        for bad in [rows[:-1],rows[::-1],rows[:-1]+[{'id':0}]]:
            with self.assertRaises(ValueError): verify_ids(bad)

if __name__ == '__main__': unittest.main()
