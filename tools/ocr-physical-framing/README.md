# Physical input framing research

This is an unfinished, paired diagnostic on two **consumed** fictional five-page
drawings from the pinned public iOS reference fixture. It is not a fresh final
qualification, real-school benchmark, device release or enabled app OCR change.
The iOS repository is read only. No school materials enter this experiment.

`run.py` verifies the original RLE/pixel identity and the production V5 detector,
recognizer and alphabet before starting the native reader. Its model manifest
contains pixels and artifact identity, never oracle strings or annotated boxes.
The baseline is the production manual-capture reader. The research arm first
applies the same connected-ink ownership checks, then restricts the recognizer
**input** to the physical grid cell only when every original text pixel remains.
Only white or independently verified rule pixels may be removed. Returned CTC
boxes and strings are not clipped, corrected or selected by expected values.
Native confidence, padding semantics and the 0.8 floor remain unchanged.

`Replay.cs` is a zero-inference source-contract diagnosis of closed observations.
It verifies original pixels, rechecks coverage and reuses the unchanged strings,
positions and native scores. It does not supply model outputs or count as a new
recognition measurement. The initial as-run source snapshots and raw observations
must remain immutable when later source-contract changes are evaluated.

`run_windows.py` uses SHA-pinned public drawing pixels and production V5 assets.
`prepare_pdf.py` makes new, lossless image-only PDF containers without a text
layer; these containers add **zero independent document credit**. The native
Windows executable calls the real PDF reader/Strict eligibility check and OS PDF
renderer, using the production dimensions and pixel-format choices. Actual
rendered pixel hashes are retained even when they differ from the original.
The shared reader then runs both conditions through Builder, Engine, Validator
and the formal timetable converter. Only after it terminates does `score.py`
read the oracle and check every slot, literal lesson name and verified blank.
Positive refusals are failures; missing/execution failures stay unassessed.

The scoped Windows workflow logs bounded compressed evidence consisting only of
synthetic research JSON/text observations. It cleans all owned models, source
pixels, PDFs and package caches unconditionally and uploads no Actions artifacts.
Neither this tool nor a successful workflow qualifies a production model.
`--acquisition-only` reacquires native synthetic pixels for diagnosis without
repeating detector/recognizer calls. That mode logs the SHA-verified public
synthetic pixel assets as well, never models, PDFs or school data.

```sh
python -B -m unittest discover -s tools/ocr-physical-framing -p 'test_*.py'
# Windows with .NET 10:
python -B tools/ocr-physical-framing/run_windows.py --output <new-owned-directory>
```

The prototype currently creates a grid/work context per connected crop. Before
any production integration it needs a shared page budget, fresh whole-document
positive/refusal tests, native Windows measurements and any needed header model
qualification. It is deliberately not wired into the application.

The original-image acquisition alternative is `OriginalImageRead.cs`. It checks
the real native PDF operations and source resource against the independent
native image event. Only one upright, opaque, full-page DeviceRGB8 image with
raw/Flate samples is supported. Additional paint, clipping, duplicate images,
masks, decode substitutions, colour defaults, alternate page state and unknown
resource/encoding paths refuse. Decompression is bounded by declared dimensions;
original sample/channel identity is preserved. `--original-source` runs the
paired closed-document experiment through that acquisition rather than OS
resampling. It adds no new independent accuracy cases for reused drawings.

`OwnershipInspect.cs` is a read-only connected-component diagnosis on actual
Windows-rendered synthetic pixels, with zero recognizer calls. It shows faint
unmasked rule edges without changing ink, ownership or acceptance.
`prepare_development.py` prepares new invented ShipporiMincho/MPLUSRounded1c
drawings: all17 classes,680 slots,2040 body values, plus missing-day and printed
Al_1 controls. Those are development cases, not a final set. The first condition
fails in both fonts; the negative pairs also have unrelated recognition failures
and therefore receive no attributed refusal qualification.

`InkCropFrame.cs` is a separately fixed research condition that retains every
non-rule original pixel and restricts recognizer input to the whole-line ink
bounds plus2 original pixels. It uses one page work allowance, never expected
text or CTC output to choose a crop. Unknown-space failures and overlapping
native line boxes motivate the condition; those failures remain immutable.
The crop changes are not enabled in the application. Source acquisition and
ink-frame contracts have dedicated original-sample/refusal/budget tests:

```sh
dotnet test tools/ocr-physical-framing/source-image-tests/SourceImageTests.csproj -c Release
```
