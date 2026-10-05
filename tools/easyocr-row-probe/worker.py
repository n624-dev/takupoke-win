"""ONE uniform original-row EasyOCR recognizer arm. No gold, repair or adoption."""
import argparse, json, sys, time, platform
from pathlib import Path
from contracts import sha, verify_crop, verify_ids

ROOT = Path(__file__).resolve().parent
def emit(value): print(json.dumps(value, ensure_ascii=False, allow_nan=False, default=lambda v: v.item()), flush=True)

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--owned', type=Path, required=True); args = parser.parse_args()
    owned = args.owned.resolve()
    if not (owned/'.owned-easyocr-row-probe').is_file(): raise ValueError('Owned marker absent')
    recipe = json.loads((ROOT/'recipe.json').read_text())
    for pin in recipe['sourcePins']:
        data = (ROOT/pin['path']).read_bytes()
        if len(data) != pin['bytes'] or sha(data) != pin['sha256']: raise ValueError('Source pin changed: '+pin['path'])
    png = (owned/'original.png').read_bytes()
    if len(png) != recipe['inputPNG']['bytes'] or sha(png) != recipe['inputPNG']['sha256']: raise ValueError('Original PNG changed')
    import numpy as np
    from PIL import Image
    image = Image.open(owned/'original.png'); image.load()
    rows = json.loads((ROOT/'rows-before-native.json').read_text())['rows']; verify_ids(rows)
    crops = [np.asarray(verify_crop(image, row)).copy() for row in rows]
    weight = owned/'models/japanese_g2.pth'; pin = json.loads((ROOT/'artifact-pin.json').read_text())['files'][0]
    if sha(weight.read_bytes()) != pin['sha256']: raise ValueError('Weight pin changed')
    sys.path.insert(0, str(ROOT/'upstream'))
    import torch, torchvision, easyocr
    from easyocr.config import recognition_models, imgH
    from easyocr.utils import reformat_input, get_image_list
    from easyocr.recognition import get_text
    torch.set_num_threads(2)
    reader = easyocr.Reader(['ja','en'], gpu=False, detector=False, recognizer=True, recog_network='japanese_g2',
                            quantize=False, download_enabled=False, model_storage_directory=str(owned/'models'),
                            user_network_directory=str(owned/'user-network'), verbose=False)
    expected_characters = recognition_models['gen2']['japanese_g2']['characters']
    if reader.character != expected_characters or imgH != 64: raise ValueError('Official public charset/input-height changed')
    observations = []; forward_count = 0
    def before_forward(model, values):
        nonlocal forward_count
        forward_count += 1
        array = values[0].detach().cpu().contiguous().numpy()
        observations.append({'shape':list(array.shape), 'dtype':str(array.dtype), 'sha256':sha(array.tobytes()),
                             'minimum':float(array.min()), 'maximum':float(array.max()), 'allFinite':bool(np.isfinite(array).all())})
    hook = reader.recognizer.register_forward_pre_hook(before_forward)
    emit({'type':'runtime','recipe':recipe['name'],'torch':torch.__version__,'torchvision':torchvision.__version__,
          'numpy':np.__version__,'Python':platform.python_version(),'OS':platform.platform(), 'sourceRevision':recipe['upstreamRevision'],
          'modelSHA256':pin['sha256'], 'publicCharsetCharacters':len(expected_characters),'settings':recipe['settings'],
          'inputPNG':recipe['inputPNG'],'plannedRows':170,'detectorCalls':0,'nativeConfidenceCalibration':'unavailable',
          'geometryScope':'Original measured pixel-support row ROI; no native predicted word/character boxes'})
    units = []; errors = []; returned = 0; serialized = 0; empty = []
    try:
        for row, rgb in zip(rows, crops):
            start = time.monotonic(); prior = forward_count; obs_start = len(observations); native = None
            try:
                _, grey = reformat_input(rgb)
                images, max_width = get_image_list([[0,rgb.shape[1],0,rgb.shape[0]]], [], grey, model_height=64)
                if len(images) != 1: raise ValueError('Expected exactly one declared original-row ROI')
                # This is the public get_text recognizer API. Full model charset,
                # no language/role/digit filter; contrast retry disabled uniformly.
                native = get_text(reader.character, 64, int(max_width), reader.recognizer, reader.converter, images,
                                  ignore_char='', decoder='greedy', beamWidth=5, batch_size=1, contrast_ths=0,
                                  adjust_contrast=0.5, filter_ths=0.003, workers=0, device='cpu')
                returned += 1
                emit({'type':'native-row-output','rowID':row['id'],'recognizerAPIReadReturned':True,'rawResults':native,
                      'originalRGBSHA256':row['originalRGBSHA256'],'greyShape':list(grey.shape),'greySHA256':sha(grey.tobytes()),
                      'officialImageListShape':list(images[0][1].shape),'modelWidth':int(max_width),
                      'actualForwardCalls':forward_count-prior,'actualModelInputs':observations[obs_start:]})
                if forward_count-prior != 1 or len(native) != 1: raise ValueError('Uniform one-forward/output contract changed')
                local_box, text, score = native[0]
                expected_box = [[0,0],[rgb.shape[1],0],[rgb.shape[1],rgb.shape[0]],[0,rgb.shape[0]]]
                if local_box != expected_box or not isinstance(text,str) or len(text.encode('utf-8')) > 16384 or not np.isfinite(score):
                    raise ValueError('Unexpected recognizer result contract')
                if not text: empty.append(row['id'])
                x1,y1,x2,y2 = row['box']
                unit = {'id':row['id'],'originalPixelRowID':row['id'],'text':text,'boundingBox':[[x1,y1],[x2,y1],[x2,y2],[x1,y2]],
                        'confidence':None,'recognizerSequenceConfidence':float(score),
                        'confidenceScope':'Official EasyOCR custom_mean, uncalibrated sequence diagnostic; not Paddle .8',
                        'geometryScope':'Original measured occupied-row pixel support ROI, not native predicted word/character box'}
                units.append(unit); serialized += 1
                emit({'type':'row-result','rowID':row['id'],'returned':True,'serializationCompleted':True,
                      'milliseconds':1000*(time.monotonic()-start),'actualInputRGBSHA256':row['originalRGBSHA256'],'unit':unit})
            except Exception as e:
                error = {'rowID':row['id'],'phase':'native' if native is None else 'post-native-serialization','errorType':type(e).__name__,
                         'error':str(e)[:1024],'recognizerAPIReadReturned':native is not None,'rawResults':native,
                         'actualForwardCalls':forward_count-prior,'actualModelInputs':observations[obs_start:]}
                errors.append(error); emit({'type':'row-error',**error})
    finally: hook.remove()
    emit({'type':'result','id':'unlabeled-full40','returned':returned==170,'plannedRows':170,'recognizerAPIReadReturnedRows':returned,
          'serializationCompletedRows':serialized,'runtimeErrors':errors,'emptyTranscriptionsOnPrintedInk':empty,
          'detectorCalls':0,'actualRecognizerForwardCalls':forward_count,'result':{'json_lines':units},
          'sourceAdoptionEligible':not errors and not empty and serialized==170,
          'scope':'Original row geometry/output availability only; confidence/host formal independent. No automatic adoption or best-per-field selection.',
          'fullFormal':'unassessed'})
    return 1 if errors or forward_count!=170 else 0

if __name__ == '__main__': raise SystemExit(main())
