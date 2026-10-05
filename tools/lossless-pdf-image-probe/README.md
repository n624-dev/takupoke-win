Lossless single-image PDF source acquisition probe

This research tool preserves original RGB samples only when the entire page is provably one opaque, untransformed image. It has no application caller. The source extraction and raw syntax gate are identical to the frozen Stage 0 prototype; only the command-line controls entry point makes the consumed development PDF optional.

Run the standalone controls from public source:

```sh
dotnet run --project tools/lossless-pdf-image-probe/LosslessImageProbe.csproj -- --controls
```

All 57 controls generate fictional PDFs in memory. They check sample orientation, bounded decompression, duplicate raw dictionary keys, paint completeness, masks, annotations, transforms, unsupported syntax, sample limits and cancellation. Build: .NET 10.0.0, zero warnings/errors; controls: 57 passed, zero failures and zero OCR calls. PdfPig 0.1.16 and SharpZipLib 1.4.2 are pinned research dependencies.

An optional second argument checks the known consumed fictional PDF against its post-extraction identity; that PDF is not included. Running that optional check produced 58 total passes. The actual consumed PDF produced exactly the prior original 3740×800 RGB image, including all 170 prior occupied-row inputs. This establishes causal input identity, without new OCR, correctness calibration, formal recovery, freshness or production adoption. No PDF, image, font, OCR model, binary pixels or base64 image is committed.

[DESIGN.md](DESIGN.md) describes the closed eligibility contract and synchronous PdfPig parsing limitation. [artifact-pins.json](artifact-pins.json) records exact extraction source hashes, test counts and dependency versions. Unsupported input returns `Unsupported`; production rendering, Schema 2, Validator 5, OCR confidence handling and application behavior are unchanged.
