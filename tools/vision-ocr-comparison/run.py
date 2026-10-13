#!/usr/bin/env python3
"""Local image-only reader comparison on frozen invented development pixels."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import urllib.request
import html_blocks

ROOT = Path(__file__).resolve().parent
PIXEL_SHA = "6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff"
MODELS = [
    ("glm-ocr-q8", "GLM-OCR-Q8_0.gguf", "45bc244a6446aff850521dc41f18bc8d7105ad5f0c2c8c28af04e7cc4f4d50b1",
     "mmproj-GLM-OCR-Q8_0.gguf", "9c4b58e33e316ed142eb5dcb41abec3844d3e6e5dc361ffb782c3fa9d175141f", "Text Recognition:"),
    ("qwen3-vl-2b-q8", "Qwen3VL-2B-Instruct-Q8_0.gguf", "1e8db19207c8ce0733ddd78c2eff8a9e22c27c82f4443df94c25792ed8fe04f2",
     "mmproj-Qwen3VL-2B-Instruct-Q8_0.gguf", "f9a68fabba69c3b81e153367b2c7521030b0fa8bb0de400c9599c8e6725f9c82",
     "Read only the visible text in this image. Transcribe it exactly, preserving spaces, punctuation and case. Return plain text only, with no explanation or added formatting. Do not guess unclear text. If the image has no text, return an empty string."),
]
JAPANESE_MODEL = (
    "qwen35-ocr-jp-f16", "qwen35ocr-jp-2b-f16.gguf", "302b7d71c0755e703831fc6606822121ed9788ea86479ff04adc284043d667f7",
    "qwen35ocr-jp-2b-mmproj-f16.gguf", "e445e9802c73621fa802a2f92771dfac1ed25eddd5ea405bbcb8f9443baaa435",
    "OCR this image as HTML layout blocks with bbox and label.")


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        while b := f.read(1024 * 1024): h.update(b)
    return h.hexdigest()


def local_http(port, route, payload=None, timeout=300):
    # The only inference destination is this task's loopback server.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    request = urllib.request.Request(f"http://127.0.0.1:{port}/{route}",
        data=None if payload is None else json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"})
    with opener.open(request, timeout=timeout) as response:
        data = response.read(1024 * 1024 + 1)
        if len(data) > 1024 * 1024: raise ValueError("Response exceeds bound")
        return json.loads(data)


def rss(pid):
    data = Path(f"/proc/{pid}/status").read_text()
    return {line.split(':')[0]: int(line.split()[1]) * 1024 for line in data.splitlines() if line.startswith(('VmRSS:', 'VmHWM:'))}


def main():
    p = argparse.ArgumentParser(); p.add_argument("--server", required=True, type=Path)
    p.add_argument("--models", required=True, type=Path); p.add_argument("--output", required=True, type=Path)
    p.add_argument("--model-set", choices=['base', 'japanese-html'], default='base')
    args = p.parse_args(); args.output.mkdir(parents=True, exist_ok=False)
    selected_models = MODELS if args.model_set == 'base' else [JAPANESE_MODEL]
    expected_calls = 192 * len(selected_models)
    maximum_tokens = 128 if args.model_set == 'base' else 512
    top_p = 1 if args.model_set == 'base' else .1
    if digest(args.server) != "67cc62bf21713881160b8ca69856d3899273ebe8924ae9aa8c99c2802967ee21":
        raise ValueError("Pinned llama.cpp b11371 Linux CPU server mismatch")
    if digest(ROOT / "fictional-pixels.json") != PIXEL_SHA: raise ValueError("Fictional pixel packet mismatch")
    packet = json.loads((ROOT / "fictional-pixels.json").read_text(encoding="utf-8"))
    rows = packet["rows"]
    if len(rows) != 192: raise ValueError("Frozen row count changed")
    for row in rows:
        image = base64.b64decode(row["pngBase64"], validate=True)
        if hashlib.sha256(image).hexdigest() != row["pngSha256"]: raise ValueError("Image mismatch")
    assets = []
    for name, model, model_sha, projector, projector_sha, prompt in selected_models:
        if digest(args.models / model) != model_sha or digest(args.models / projector) != projector_sha:
            raise ValueError("Model identity mismatch")
        assets.append({"id": name, "model": model, "modelSHA": model_sha, "projector": projector, "projectorSHA": projector_sha, "prompt": prompt})
    freeze = {"recipe": "frozen-image-only-vision-ocr-v1" if args.model_set == 'base' else 'frozen-japanese-html-ocr-v1', "pixelsSHA": PIXEL_SHA, "serverSHA": digest(args.server),
        "sourceSHA": digest(Path(__file__)), "models": assets, "callsPlanned": expected_calls, "cpuThreads": 4,
        "contextTokens": 4096, "maximumOutputTokens": maximum_tokens, "temperature": 0, "topK": 1, "topP": top_p,
        "seed": 17, "oracleSHA": digest(ROOT / "oracle.json"), "qualification": "unassessed"}
    (args.output / "freeze.json").write_text(json.dumps(freeze, indent=2) + "\n")
    print(json.dumps({"freezeSHA": digest(args.output / 'freeze.json'), "callsPlanned": expected_calls}), flush=True)
    # Download/setup is complete; no remote endpoint or asset URL enters either
    # server. Strip hosted account tokens from the inference child environment.
    env = {k: v for k, v in os.environ.items() if not any(s in k.upper() for s in ("TOKEN", "SECRET", "PASSWORD", "CREDENTIAL"))}
    completed = 0; resources = []
    for ordinal, (name, model, model_sha, projector, projector_sha, prompt) in enumerate(selected_models):
        port = 18790 + ordinal
        command = [str(args.server), '-m', str(args.models / model), '--mmproj', str(args.models / projector),
            '--host', '127.0.0.1', '--port', str(port), '-ngl', '0', '--no-mmproj-offload', '-c', '4096',
            '-t', '4', '-tb', '4', '-np', '1', '-fa', 'off', '-fit', 'off', '--no-warmup']
        with (args.output / (name + '-server.txt')).open('wb') as log:
            process = subprocess.Popen(command, env=env, stdout=log, stderr=log, start_new_session=True)
            try:
                ready = False
                for _ in range(600):
                    if process.poll() is not None: raise RuntimeError("Local image reader failed during loading")
                    try:
                        if local_http(port, 'health', timeout=1).get('status') == 'ok': ready = True; break
                    except Exception: pass
                    time.sleep(.25)
                if not ready: raise TimeoutError("Reader loading deadline exceeded")
                with (args.output / (name + '-raw.jsonl')).open('x', encoding='utf-8', newline='\n') as output:
                    for row in rows:
                        payload = {"messages": [{"role": "user", "content": [
                            {"type": "image_url", "image_url": {"url": "data:image/png;base64," + row["pngBase64"]}},
                            {"type": "text", "text": prompt}]}], "max_tokens": maximum_tokens, "temperature": 0,
                            "top_k": 1, "top_p": top_p, "seed": 17, "cache_prompt": False, "stream": False}
                        if args.model_set == 'japanese-html': payload['chat_template_kwargs'] = {'enable_thinking': False}
                        started = time.monotonic(); response = local_http(port, 'v1/chat/completions', payload)
                        choices = response.get('choices', [])
                        if len(choices) != 1: raise ValueError("Unexpected native choice count")
                        text = choices[0]['message'].get('content')
                        if not isinstance(text, str) or len(text.encode()) > 16384: raise ValueError("Native text bound failed")
                        record = {"id": row['id'], "model": name, "pngSHA": row['pngSha256'], "text": text,
                            "finishReason": choices[0].get('finish_reason'), "seconds": time.monotonic() - started,
                            "response": response, "confidence": "uncalibrated/unassessed", "productionAdoption": False}
                        output.write(json.dumps(record, ensure_ascii=False) + '\n'); output.flush(); os.fsync(output.fileno())
                        if rss(process.pid)['VmRSS'] > 12 * 1024**3: raise MemoryError("CPU16GB reader memory budget exceeded")
                        completed += 1
                        if completed % 24 == 0: print(json.dumps({"completed": completed, "model": name}), flush=True)
                resources.append({"model": name, **rss(process.pid), "rawSHA": digest(args.output / (name + '-raw.jsonl'))})
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGTERM)
                    try: process.wait(timeout=10)
                    except subprocess.TimeoutExpired: os.killpg(process.pid, signal.SIGKILL); process.wait(timeout=10)
        print(json.dumps({"model": name, "readerStopped": True}), flush=True)
    # Raw responses are closed and frozen before the literal oracle is opened.
    if completed != expected_calls: raise ValueError("Missing calls remain unassessed")
    if digest(ROOT / 'oracle.json') != freeze['oracleSHA']: raise ValueError("Oracle changed")
    gold = {r['id']: r for r in json.loads((ROOT / 'oracle.json').read_text(encoding='utf-8'))}
    summary = {}
    for name, *_ in selected_models:
        for line in (args.output / (name + '-raw.jsonl')).read_text(encoding='utf-8').splitlines():
            row = json.loads(line); g = gold[row['id']]; key = (name, g['cohort'], g['kind'])
            s = summary.setdefault(key, {'rows': 0, 'rawLiteralExact': 0, 'singleFinalNewlineDiagnosticExact': 0, 'truncated': 0, 'seconds': 0,
                'htmlProtocolParsed': 0, 'htmlProtocolExact': 0, 'htmlProtocolRefused': 0})
            s['rows'] += 1; s['rawLiteralExact'] += row['text'] == g['literal']; s['seconds'] += row['seconds']
            s['truncated'] += row['finishReason'] != 'stop'
            s['singleFinalNewlineDiagnosticExact'] += row['text'].removesuffix('\n') == g['literal']
            if args.model_set == 'japanese-html':
                try:
                    interpreted = html_blocks.decode(row['text']); s['htmlProtocolParsed'] += 1
                    s['htmlProtocolExact'] += interpreted['text'] == g['literal'] and row['finishReason'] == 'stop'
                except html_blocks.Refusal: s['htmlProtocolRefused'] += 1
    report = {**freeze, 'callsCompleted': completed, 'resources': resources,
        'aggregates': [{'model': k[0], 'cohort': k[1], 'kind': k[2], **v} for k, v in sorted(summary.items())],
        'scope': 'Consumed development recognition components; no role/ink certification, PDF Validator, qualification or adoption'}
    (args.output / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False), flush=True)


if __name__ == '__main__': main()
