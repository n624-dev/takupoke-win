#!/usr/bin/env python3
"""Official GLM-OCR FP32 CPU condition; separate from closed GGUF Q8 results."""
import argparse
import base64
import hashlib
import importlib.metadata
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
REVISION = '2e85a62840ccac27daa451df36c736c4636b8628'
MODEL_SHA = 'a16eb0de98d199293371c560f95f83130d2a2c9612449df16839f08ff9498815'


def sha(p):
    digest = hashlib.sha256()
    with p.open('rb') as f:
        for block in iter(lambda: f.read(8*1024*1024), b''):
            digest.update(block)
    return digest.hexdigest()


def write(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')


def worker(a):
    import torch
    from PIL import Image
    from transformers import AutoProcessor, GlmOcrForConditionalGeneration
    freeze = json.loads((a.output/'freeze.json').read_text())
    if sha(a.packet) != freeze['packetSha256']:
        raise ValueError('Original packet changed')
    for name, r in freeze['assets'].items():
        p = a.assets/name
        if p.stat().st_size != r['bytes'] or sha(p) != r['sha256']:
            raise ValueError('Pinned model/processor/tokenizer changed')
    torch.set_num_threads(4)
    torch.set_num_interop_threads(1)
    torch.manual_seed(17)
    started = time.monotonic()
    processor = AutoProcessor.from_pretrained(a.assets, local_files_only=True, trust_remote_code=False)
    model = GlmOcrForConditionalGeneration.from_pretrained(a.assets, local_files_only=True,
                trust_remote_code=False, dtype=torch.float32, attn_implementation='eager').eval()
    if any(p.device.type != 'cpu' or p.dtype != torch.float32 for p in model.parameters()):
        raise ValueError('Expected CPU-only FP32 parameters')
    removed_ids = set()
    for key in ['bos_token_id', 'eos_token_id', 'pad_token_id']:
        value = getattr(model.generation_config, key, None)
        if isinstance(value, int):
            removed_ids.add(value)
        elif isinstance(value, list):
            removed_ids.update(value)
    write(a.output/'model-load-report.json', {'seconds': time.monotonic()-started,
          'parameterCount': sum(p.numel() for p in model.parameters()), 'parameterDtype': 'float32',
          'removedBosEosPadIds': sorted(removed_ids), 'unknownAndOtherSpecialTokensPreserved': True})

    def deadline(*_):
        raise TimeoutError('Single inference exceeded fixed 180-second deadline')

    signal.signal(signal.SIGALRM, deadline)
    with (a.output/'observations.jsonl').open('x', encoding='utf-8', newline='\n') as stream:
        for number, row in enumerate(json.loads(a.packet.read_text())['rows'], 1):
            png = base64.b64decode(row['pngBase64'], validate=True)
            if hashlib.sha256(png).hexdigest() != row['pngSha256']:
                raise ValueError('Original PNG changed')
            with Image.open(io.BytesIO(png)) as im:
                image = im.convert('RGB')
            messages = [{'role': 'user', 'content': [{'type': 'image', 'image': image},
                        {'type': 'text', 'text': 'Text Recognition:'}]}]
            inputs = processor.apply_chat_template(messages, tokenize=True, add_generation_prompt=True,
                         return_dict=True, return_tensors='pt')
            if any(v.device.type != 'cpu' for v in inputs.values() if torch.is_tensor(v)):
                raise ValueError('Expected CPU inputs')
            metadata = {}
            for key, value in inputs.items():
                if torch.is_tensor(value):
                    if value.is_floating_point() and value.dtype != torch.float32:
                        raise ValueError('Expected FP32 official image processor')
                    metadata[key] = {'shape': list(value.shape), 'dtype': str(value.dtype),
                         'sha256': hashlib.sha256(value.contiguous().numpy().tobytes()).hexdigest()}
            timer = time.monotonic()
            signal.alarm(180)
            with torch.inference_mode():
                output = model.generate(**inputs, max_new_tokens=128, do_sample=False, num_beams=1,
                             return_dict_in_generate=True, output_scores=True)
            signal.alarm(0)
            ids = output.sequences[0, inputs['input_ids'].shape[-1]:].tolist()
            literal_ids = [token for token in ids if token not in removed_ids]
            text = processor.tokenizer.decode(literal_ids, skip_special_tokens=False, clean_up_tokenization_spaces=False)
            full_decoded = processor.tokenizer.decode(ids, skip_special_tokens=False, clean_up_tokenization_spaces=False)
            token_scores = []
            for index, (token, logits) in enumerate(zip(ids, output.scores)):
                probability = float(torch.softmax(logits[0].float(), dim=-1)[token])
                token_scores.append({'index': index, 'tokenId': token, 'probability': probability,
                       'removedBosEosPad': token in removed_ids})
            values = [r['probability'] for r in token_scores if not r['removedBosEosPad']]
            minimum = min(values, default=None)
            truncated = len(ids) >= 128 and (not ids or ids[-1] not in removed_ids)
            record = {'id': row['id'], 'text': text, 'fullDecodedIncludingControls': full_decoded,
                      'generatedIds': ids, 'tokenProbabilities': token_scores, 'minimumTokenProbability': minimum,
                      'confidenceKind': 'Uncalibrated native autoregressive token probability, not CTC or field confidence',
                      'truncated': truncated, 'inputTensors': metadata,
                      'originalPngSha256': row['pngSha256'], 'inferenceSeconds': time.monotonic()-timer}
            stream.write(json.dumps(record, ensure_ascii=False, separators=(',', ':'))+'\n')
            stream.flush()
            del inputs, output
            if number % 16 == 0:
                print(json.dumps({'completed': number, 'planned': len(json.loads(a.packet.read_text())['rows'])}), flush=True)
    write(a.output/'worker-report.json', {'callsCompleted': len(json.loads(a.packet.read_text())['rows']), 'totalSeconds': time.monotonic()-started,
          'qualification': False, 'modelActivation': False})


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--assets', type=Path, required=True)
    p.add_argument('--packet', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--worker', action='store_true')
    a = p.parse_args()
    if a.worker:
        worker(a)
        return
    a.output.mkdir(parents=True, exist_ok=False)
    receipt = json.loads((a.assets/'assets.json').read_text())
    if receipt['revision'] != REVISION or receipt['assets']['model.safetensors']['sha256'] != MODEL_SHA:
        raise ValueError('Official pinned GLM-OCR assets required')
    oracle = a.packet.parent/'oracle.json'
    freeze = {'recipe': 'official-glm-ocr-fp32-cpu4-native-tails-v1', 'repo': receipt['repo'], 'revision': REVISION,
              'assets': receipt['assets'], 'packetSha256': sha(a.packet), 'oracleSha256': sha(oracle),
              'sourceSha256': sha(Path(__file__)),
              'runtime': {n: importlib.metadata.version(n) for n in ['torch', 'transformers', 'Pillow', 'numpy']},
              'condition': 'Official unquantized weights converted to CPU FP32, eager attention, official processor/template, Text Recognition:, greedy128tokens, seed17. Only known BOS/EOS/PAD removed, no Unicode/case/space/format repair.',
              'scope': 'All eligible original native fictional crops, consumed development only; not new full Windows/iPhone qualification.',
              'budgets': {'seconds': 2700, 'requestSeconds': 180, 'rssBytes': 10*1024**3},
              'qualification': False, 'modelActivation': False}
    write(a.output/'freeze.json', freeze)
    snapshot = a.output/'as-run-source'
    snapshot.mkdir()
    (snapshot/Path(__file__).name).write_bytes(Path(__file__).read_bytes())
    env = {**os.environ, 'HF_HUB_OFFLINE': '1', 'HF_HUB_DISABLE_TELEMETRY': '1',
           'TRANSFORMERS_OFFLINE': '1', 'TOKENIZERS_PARALLELISM': 'false'}
    args = [sys.executable, str(Path(__file__).resolve()), '--worker', '--assets', str(a.assets.resolve()),
            '--packet', str(a.packet.resolve()), '--output', str(a.output.resolve())]
    started = time.monotonic()
    peak = 0
    stop = None
    with (a.output/'worker-stdout.txt').open('w') as stdout, (a.output/'worker-stderr.txt').open('w') as stderr:
        child = subprocess.Popen(args, stdout=stdout, stderr=stderr, env=env)
        while child.poll() is None:
            try:
                for line in Path(f'/proc/{child.pid}/status').read_text().splitlines():
                    if line.startswith('VmRSS:'):
                        peak = max(peak, int(line.split()[1])*1024)
            except FileNotFoundError:
                pass
            if peak > freeze['budgets']['rssBytes'] or time.monotonic()-started > freeze['budgets']['seconds']:
                stop = 'RSS budget' if peak > freeze['budgets']['rssBytes'] else 'Elapsed budget'
                child.terminate()
                try:
                    child.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    child.kill()
                break
            time.sleep(.5)
        child.wait()
    raw = a.output/'observations.jsonl'
    write(a.output/'raw-receipt.json', {'exitCode': child.returncode, 'budgetStop': stop, 'peakRssBytes': peak,
          'sha256': sha(raw) if raw.exists() else None, 'bytes': raw.stat().st_size if raw.exists() else None})
    if child.returncode or stop:
        raise RuntimeError('Worker failed; retain partial raw/logs, no precision credit')
    if sha(oracle) != freeze['oracleSha256'] or sha(Path(__file__)) != freeze['sourceSha256']:
        raise ValueError('Source/oracle changed')
    print(json.dumps({'closed':True,'rawSha256':sha(raw),'peakRssBytes':peak}),flush=True)


if __name__ == '__main__':
    main()
