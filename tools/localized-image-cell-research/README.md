# Fictional localized image cell comparison

This isolated Linux CPU research entry executes actual local image inference, after exact source approval. It does not load a model on the current development host. The one existing independent fictional cell is 96 × 94 pixels (9,024 pixels), retained byte for byte from the existing consumed development source. No school PDF, school names, uploaded original, new drawing, new model identity, OCR invocation, redraw or crop expansion is used.

The pinned public Gemma 4 E2B LiteRT model (revision b3ca0d2f076785a8f4b2219ddbd2bdb99954eae1; 2,588,147,712 bytes; SHA256 181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c) is fetched on the hosted research VM together with the already pinned LiteRT-LM API 0.17.1 wheel (SHA256 4e52fa3a57bd54c399302d4237dd7d5b2235d92040922acc6e259f55b8732979). This is local CPU inference on that machine, with no inference API or cloud fallback. It does not qualify Windows, iOS or a phone. The runtime's bundle capability must report image support and the pinned 280 image token budget before Engine loading. Actual image routing, recognition, cache footprint and memory remain unassessed until execution.

The worker makes at most two fresh-conversation calls under one Engine:

1. An image-only transcription receives the original small PNG plus a neutral instruction and JSON schema. It receives no OCR text, baseline model answer, task field, context, expected answer or teacher/room vocabulary. The complete native response is persisted, fsynced, and frozen by SHA256.
2. Only after that freeze, the worker opens retained OCR source IDs, unchanged strings, estimated CTC coordinates and acquisition rows. It groups unchanged native acquisition rows into three short candidate rows and requests one subject row candidateID or UNKNOWN using the shared row protocol, with the frozen blind transcription as unverified comparison data. A caller field question is not a role certificate. The selected row expands deterministically to its unchanged original OCR IDs in source order. Values are reconstructed only from those original OCR IDs; blind image literals cannot repair OCR or create IDs.

No usable image (UNKNOWN/NONE/malformed/unsupported/runtime failure) leaves comparison unassessed. There is no fallback text call and no retry. The existing same-cell Gemma text baseline from run 37383946385 is retained unchanged for the post-worker protocol reporter and never shown to either new model call. The retained current OCR, previous text proposal and image plus short-row comparison are shown together, with literal agreement and ID changes separate. The previous baseline used a longer atom/coordinate prompt, so this probe does not isolate an image-only causal improvement. The reporter reads no oracle and assigns no correctness credit: OCR and image are both candidate evidence.

Limits are fixed at one 9,024-pixel crop, zero additional crops/renders/OCR calls, two calls, one Engine, CPU2 for text and vision, 8,192 context tokens, 256 output tokens, 60 seconds per call, 180 seconds for native loading, 360 seconds total process, sampled process-group RSS 8 GiB, and 9.5 GiB conservative memory headroom before model loading. The guarded runner retains the existing 2 GiB disk reserve, 1 GiB owned cache bound, 16 MiB diagnostics bound, socket/io_uring network denial, owned process cleanup, and source/runtime/model hash checks. The hosted VM is at most 16 GiB; the local development host's approximately 14.5 GiB use and 2.28 GB free disk prohibit this Engine here. A tiny crop may be insufficient; it is not expanded adaptively.

Source/role/original ink certificates remain absent. Existing OCR confidence thresholds are unchanged. Native deterministic whole-document validation and adoption remain separate and are not invoked here. The application limit of at most three BODY manual corrections document-wide remains; this research adds no class/header/structure correction route. Formal 1,270 slots and 70 clocks remain unassessed. Catalog activation, qualified model claims and direct adoption are absent.

Registration push runs source validation/tests only. Dispatch requires the exact packet and recipe hashes, exact research branch, GitHub event/checkout SHA equality, and attempt 1. No push/dispatch authority is implied by source preparation. Controller downloads only the two pinned public assets before staging fictional input. It strips hosted tokens from runner/worker environments, records bounded fictional responses and execution/cleanup receipts to logs, and removes its owned scratch after diagnostics and proved cleanup. Artifact uploads and model cache uploads are zero; incomplete cleanup retains the marked owned scratch rather than deleting under a possibly live process.

Prepare/freeze and validate without native imports:

```sh
python tools/localized-image-cell-research/prepare.py
python tools/localized-image-cell-research/bootstrap_ci.py --validate-only
python -m unittest discover -s tools/localized-image-cell-research -p 'test_*.py' -v
```

The preparation script pins this source; any source changes require a new exact root review. This consumed cell is a bounded mechanism probe, not an independent holdout or Japanese timetable qualification.
