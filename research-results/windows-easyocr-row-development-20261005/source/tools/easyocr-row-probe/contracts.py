"""Pixel/source contracts shared by preparation and the observation-only worker."""
import hashlib

def sha(data):
    return hashlib.sha256(data).hexdigest()

def verify_crop(image, row):
    box = row['box']
    if len(box) != 4 or any(type(x) is not int for x in box):
        raise ValueError('Invalid measured integer ROI')
    x1, y1, x2, y2 = box
    if not (0 <= x1 < x2 <= image.width and 0 <= y1 < y2 <= image.height):
        raise ValueError('Measured ROI outside original raster')
    crop = image.crop(box)
    if crop.mode != 'RGB' or [crop.height, crop.width, 3] != row['shape']:
        raise ValueError('Original RGB shape changed')
    if sha(crop.tobytes()) != row['originalRGBSHA256']:
        raise ValueError('Original RGB bytes changed')
    return crop

def verify_ids(rows):
    if len(rows) != 170 or [r['id'] for r in rows] != list(range(170)):
        raise ValueError('Expected identical original ordered 170-row list')
