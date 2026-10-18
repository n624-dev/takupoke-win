"""CPU training inputs mirror the application's bounded original-pixel resize."""
import math
import numpy as np


def tensor(image):
    rgb = np.asarray(image.convert('RGB'), dtype=np.uint8)
    height, width = rgb.shape[:2]
    input_width = math.floor(48 * max(320 / 48, width / height))
    if input_width > 960: raise ValueError('Source exceeds application width bound')
    valid_width = min(input_width, math.ceil(48 * width / height))
    xs = np.clip((np.arange(valid_width)+.5)*width/valid_width-.5, 0, width-1)
    ys = np.clip((np.arange(48)+.5)*height/48-.5, 0, height-1)
    x0 = np.floor(xs).astype(int); x1 = np.minimum(x0+1, width-1); fx = xs-x0
    y0 = np.floor(ys).astype(int); y1 = np.minimum(y0+1, height-1); fy = ys-y0
    pixels = rgb[:, :, ::-1].astype(np.float64)
    values = ((pixels[y0[:, None], x0[None, :]]*(1-fx)[None, :, None]
        + pixels[y0[:, None], x1[None, :]]*fx[None, :, None])*(1-fy)[:, None, None]
        + (pixels[y1[:, None], x0[None, :]]*(1-fx)[None, :, None]
        + pixels[y1[:, None], x1[None, :]]*fx[None, :, None])*fy[:, None, None])
    values = np.floor(values+.5).astype(np.float32)
    values = (values/np.float32(255)-np.float32(.5))/np.float32(.5)
    output = np.zeros((3, 48, input_width), dtype=np.float32)
    output[:, :, :valid_width] = values.transpose(2, 0, 1)
    return output, valid_width


def greedy(probabilities, dictionary):
    if probabilities.ndim != 2 or probabilities.shape[1] != len(dictionary): raise ValueError('CTC shape mismatch')
    if not np.isfinite(probabilities).all() or (probabilities<0).any() or (probabilities>1).any(): raise ValueError('Invalid CTC probability')
    pieces = []; previous = -1
    for step, index in enumerate(probabilities.argmax(axis=1)):
        score = float(probabilities[step, index])
        if index and index != previous: pieces.append({'Text':dictionary[index], 'Start':step, 'End':step+1, 'Confidence':score})
        elif index and pieces:
            pieces[-1]['End'] = step+1; pieces[-1]['Confidence'] = max(pieces[-1]['Confidence'], score)
        previous = index
    return pieces
