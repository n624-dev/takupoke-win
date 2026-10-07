# OCR improvement log

## Direct font-source redraw: confidence improves, complete PDF quality unassessed

A new independent fictional 40-cell table contains 120 unlabelled body fields. A fixed Linux .NET comparison calls the unchanged production PP-OCRv5 OCR implementation and ONNX Runtime 1.23.2 four times: Noto Sans 11 px at native 1x/2x font-source rendering, and different Noto Serif 10 px literals at 1x/2x. The larger image is drawn directly from the font and vector geometry; it is not a resized low-resolution image. Model files, confidence floor, source-padding guards, connected-ink ownership, and adoption policy are unchanged.

| Render condition | Exact independently owned fields | Correct fields below the 0.8 floor | Incorrect fields above that floor | Read transaction |
| --- | ---: | ---: | ---: | ---: |
| Sans 1x, 940 × 680 | 120 / 120 | 7 | 0 | 4.462 s |
| Sans 2x, 1,880 × 1,360 | 120 / 120 | 2 | 0 | 4.572 s |
| Serif 1x, 940 × 680 | 120 / 120 | 32 | 0 | 4.103 s |
| Serif 2x, 1,880 × 1,360 | 120 / 120 | 14 | 0 | 4.225 s |

All four complete captures preserve 120 uniquely assigned fields and cover original non-rule ink. The output-only audit checks every native crop against independent original painted glyphs and the complete physical cell. It makes zero additional inference calls. Each 2x rendering has the same literal content as its paired 1x rendering; these are not four independent documents. Peak sampled process working set was 461,336,576 bytes. Cold session initialization and rendering size differ, so transaction times are not isolated model latency comparisons. Raw log SHA-256: `2124b5159bf131e7a712f19ad798744ec075d3d345f5a7f6c7dbdbaeb78f4dbe`.

After that fixed comparison closed, a separately recorded development experiment rendered only the same Serif content at 3x/4x and made two new calls. Both retain 120/120 exact owned fields with no incorrect field above the floor, but nine and four correct fields remain below it. Transactions were 5.223/5.257 seconds; peak sampled working set was 550,580,224 bytes. This is tuning on previously measured content, not unseen holdout evidence. Raw log SHA-256: `f3f0a30e1c95fa40b7a966c3b981379404bf57e2abc523c37dc4d1747d5b9b98`.

The 3x/4x whole images are 2,820 × 2,040 and 3,760 × 2,720: valid for this OCR component but beyond the current Windows PDF renderer's 2,400-pixel width limit. The experiment does not implement native original-PDF region rendering or prove that production PDF capture can supply these pixels. It also has no date/class/period/clock headers, merges, or parallel lessons. No formal Builder, Validator, or adoption is executed; no downloadable model is qualified. Four low-confidence fields remain above the document-wide maximum of three manual corrections, so that condition is not called a recovery success. Images, derived dictionaries, and build outputs are owned temporary data and removed after each experiment.

## Rejected: larger Tesseract Japanese model on fixed physical cells

A separate Linux component probe used the already available 14,330,109-byte `tessdata_best` Japanese model at revision `e12c65a915945e4c28e237a9b52bc4a8f39a0cec`, SHA-256 `36bdf9ac823f5911e624c30d0553e890b8abc7c31a65b3ef14da943658c40b79`, with Tesseract 5.5.0, OEM 1 and PSM 6. Before inference, a hash of physical cell ordinals selected 32 cells per previously frozen fictional cohort. Every selected cell retained its three unlabelled body lines and all interior source pixels, with two pixels excluded at each ruled boundary. There was no scaling, answer-dependent selection, retry, new model download, or new Paddle inference.

Exactly 64 calls completed with exit code zero in a 5.217-second transaction using at most four child processes and one OpenMP thread each. Each call retained the warning about the absent implicitly requested `jpn_vert` model. This is a dependency limitation, not a successful runtime qualification. Maximum single-child RSS was 105,268 KiB, not the combined process memory or a Windows device RAM requirement.

| Frozen fictional cohort | Selected fields | Tesseract fields with complete unique original-glyph ownership | Tesseract exact owned word concatenations | Frozen tiled V5 exact fields |
| --- | ---: | ---: | ---: | ---: |
| Noto Sans 11 px | 96 | 94 | 62 | 91 |
| Noto Serif 10 px, different literals | 96 | 72 | 0 | 68 |

The first output-only scoring used fixed 30-pixel field bands and reported unassessed fields because Tesseract's complete returned line boxes extended beyond those bands. That original output remains unchanged. A separately identified audit made zero inference calls and checked each complete box against all independently painted glyph support and its physical cell, without clipping boxes or changing source text. It left two and 24 fields unassessed respectively. The exact comparison above concatenates returned native words without adding separators; native TXT and TSV are retained separately, including Tesseract's inserted spaces. Neither representation is treated as a production Evidence value.

The second reader fixed none of the selected frozen V5 discrepancies and lost 29 and 68 previously correct fields, including unresolved ownership. It is not activated as a replacement or agreement-only acceptance policy. Confidence is retained on its native word scale and not compared numerically with Paddle's CTC scores. Complete document recovery, native Windows execution, and model qualification remain unassessed. Images and child outputs were owned scratch data and removed; shared pre-existing weights were preserved. Raw result SHA-256: `1c48badd4e1cbfba4e39518091d77bda7f75bb4549241d37d4e3c190ef3e1957`.

All observations below use independently fictional timetable content. The uploaded school PDF is not an input, fixture, or repository asset. No PDF, image, model weight, or school subject/teacher/room text is included in this log.

The first frozen recognizer comparisons in the following table are bounded Linux x64 CPU tests, not physical Windows qualification or application PDF-acquisition tests. The confidence floor remains 0.8. Literal comparisons preserve Unicode and do not normalize punctuation. Each of those comparisons has one invocation, no retry, and an independently reviewed complete inventory of 4,016 recognizer results for 4,015 source rows. Later component experiments have their separately stated inventories and limits.

| Condition | Exact source rows | Regressions / fixes versus its baseline | Wrong rows passing the recognizer guard | Wall time | Peak process RSS | Decision |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| PP-OCRv5 mobile, official minimum width 320, initial typography | 3,605 / 4,015 | Baseline | 25 | 172.483 s | 400,482,304 bytes | Not qualified |
| Same model, minimum width 32 | 3,286 / 4,015 | 321 / 2 | 242 | 47.328 s | 427,433,984 bytes | Reject: faster but substantially worse |
| Same model, corrected common font baseline in the fictional fixture | 3,715 / 4,015 | 0 / 110 | 0 | 113.460 s | 418,082,816 bytes | Not qualified; fixture typography correction, not a production OCR improvement |
| Same corrected fixture and model, uniformly expanded original white context | 3,548 / 4,015 | 193 / 26 | 127 | 132.643 s | 483,897,344 bytes | Reject universal context expansion |
| Same tight corrected fixture, PP-OCRv6 medium and its matching official dictionary | 3,953 / 4,015 | 0 / 238 | 56 | 415.961 s | 708,157,440 bytes | Improved literal recognition; not qualified |

The initial fictional generator aligned each glyph's top independently. A common font baseline corrected that fixture defect. The two fixture versions remain distinct; their old measurements have not been overwritten. Comparisons use original source-row identities rather than numeric row IDs, which changed when typography changed.

Uniform white context fixed all 15 previously misread period-7 labels, but introduced 193 regressions. Its shadow research paths produced three incorrect timetable proposals. No production adoption was attempted. A return-timetable builder capacity error is an operational error with unassessed output, not a correct refusal.

V6's model is 76,554,979 bytes; V5 mobile is 16,534,782 bytes. V6 requires its own 18,710-class dictionary. Reusing V5's dictionary would produce incorrect decoding. The V6 condition has 11 incorrect body-field painting literals: six parallel-separator substitutions and five O/0 substitutions. Six incorrect body literals pass the recognizer guard. The 51 assessed header literal differences concern time-range/note punctuation, not incorrect date/class digits. Raw literal discrepancies remain recorded even when a future independent semantic time parser might accept an equivalent range.

V6 produced no strict or recovery-builder proposal in the frozen comparison. Consequently all 1,270 formal timetable slots and 70 clocks remain unassessed. Zero incorrect formal proposals when no proposal exists is not evidence of safe recovery. Row accuracy, confidence, and absence of execution errors do not establish whole-document correctness.

Recognition and extraction are assessed separately. In retained native output, five exam date strings were correct, but estimated CTC glyph gaps caused extraction to split them into fragments. A weekday had the same problem. A separate source-row fix requires explicit OCR provenance, complete original row identity/order, shared vertical support, valid geometry, and no foreign text or crossing ruling. It retains raw evidence and leaves ordinary vector parsing unchanged. Its source tests do not replace full-document recovery evaluation.

There were no LLM calls in these OCR comparisons, so they provide no evidence for or against a prompt change. No downloadable generative model is qualified or enabled by these results. Future tests must keep all required classes, dates, periods, clocks, blanks, merged lessons, and parallel lessons in the completion check. Human correction remains limited to at most three subject/teacher/room fields per document; header correction and inferred missing content remain prohibited.

## Immutable execution identities

| Condition | Raw log SHA-256 | Independent result review SHA-256 |
| --- | --- | --- |
| Corrected V5 baseline | `10c9272d7b1009943fc37765772b3bf9cafd66ad2e61d4e3780399897408f767` | `985ca4cd2947d94fda08e0c18b4e549144f36cbc20d8cd430d66f07eca13fe3e` |
| Uniform white context | `e118e9b2c31d581f94bc052fcd76cdd8e1f2c931e756c7e2eae3dee90234d2ec` | `87358ab0cdad08e861fb119d4fc4b82a83e737cceb4d671ee3fd6c8ca7117cab` |
| V6 medium | `9037139835a464e17773a798a1a636de3ebb0e52a486a9b33ed22ec1e3d11c38` | `23de0bfa1a135eed31d6f128609cf87b3312320c6dcd5e81717a47f428dd4714` |

Closed failures remain part of the record; they must not be rerun silently, replaced with favorable subsets, or reported as quality successes.

## Separate source-only replay with current header extraction

After the frozen V6 comparison closed, one separately identified replay reused its immutable recognizer output with the reviewed source-row header extraction. It made zero recognizer or provider calls. This is a different source condition, not a replacement for the frozen V6 comparison above.

The normal recovery builder was exact on 76 of 80 slots. Four slots were incorrect: three affected by collapsing parallel lessons into one lesson and one O/0 substitution. All 510 exam slots remained unassessed: the strict parser stopped on a page-six header contract, and the recovery builder refused the source role contract. The return strict parser was exact on 678 of 680 slots, with two O/0 substitutions; its 40 semantic clocks were exact. The return recovery builder exceeded the existing comparison-work budget, leaving all 680 slots unassessed.

All three native acquisition protection gates were false. The outputs were shadow research proposals; no production adoption was attempted. Formal builder coverage was 80 assessed slots and 1,190 unassessed slots, with all 70 formal clocks unassessed. Two wrong shadow proposals mean this condition failed quality. It does not qualify the OCR model or any downloadable generative model.

The required pre-start disk reserve also failed: 1,727,135,744 bytes were available against a 2 GiB requirement. The operator incorrectly continued. This execution deviation is retained explicitly; the run receives no qualification credit and is not silently retried. It completed in 5,256 ms with 462,127,104 bytes peak process RSS.

Validator version 9 now refuses an original Latin middle dot in known-OCR body evidence rather than assuming a parallel or single lesson. Builder failure classification and cached historical-result certification use the same policy. Literal source text and acceptance history are preserved; no punctuation replacement, confidence-floor reduction, or inferred empty teacher is applied. The separate compatibility tests cover historical audits; this safety rejection is not a recognition improvement.

Closed replay review SHA-256: `4a37134522b485a6823dbf6219ed36c55394ff213ba03a39c431a78c8ac6ae5b`. Independent complete-result review SHA-256: `64671f0b1c3723ef798861d5099961dc0af1ba5f16619d519d1e775d94a7f5bf`.

## Native raster geometry and a rejected endpoint change

The fixed fictional image-only PDF was rendered with the actual Windows PDF API in run 37610655287 (source b76f15c580ed4d336203e28d7ad53d0ee4c0b1ba). The 1,311 × 1,920 raster produced zero certified rules. No detector, recognizer, or model session was invoked. This was a topology refusal, not evidence of an OCR model failure.

A separate model-free comparison in run 37611030055 (source f0492088698a1c902f05583b39e7b06ff63dbdd9) retained observed spans between perpendicular intersections. The same raster SHA-256 was preserved; 29 rules and 145 closed interior candidates were obtained. This only measures physical acquisition. Selection by nonwhite ink can also select antialiased border residue, so the selected region is not proof of a readable body field. No whole-document quality credit or model qualification is assigned.

An initial production trial applied intersection trimming to every graph. Four existing endpoint-mask tests failed: it altered the exact endpoints of already valid thick borders. That trial was rejected. The implemented fallback preserves an already certified graph byte for byte and trims only when the entire original graph is refused. The complete Windows integration suite then passed 777 tests. Unsupported tails remain unread ink; isolated or H-shaped strokes do not certify borders. The iOS implementation follows the same fallback rule, with its existing pixel thresholds and work limits.

## Fixed native original-PDF ROI detector comparison

Run 37617262139 used source `56ef0701c4fdcf301296bcc91ad06333fe93187a` and packet `624fcc80e04a4105033c8bf650b06878e0f0b6b96b7f2755d3fd95be23304ac4`. It retained the same fictional PDF, existing PP-OCRv5 mobile detector, and pinned WindowsNuGet CPU runtime. Exactly three detector calls and zero recognizer calls were made. The complete capture was read back and hash-verified; SHA-256 `e99f5d2299a45da3307af0b7914fbde70dbe642ba32cf18c1a5d1be19ba818ab`.

To avoid selecting only antialiased border residue, candidate selection additionally required observed non-rule nonwhite pixels at least three pixels inside the cell. This restriction applied only to selection: the complete cell and all edge ink remained in the detector inputs and coverage denominator. A fixed geometry hash selected one eligible cell without inspecting recognized text, expected answers, or model outputs. There was no alternate-cell retry.

| Fixed condition | Detector candidates | Selected original raster ink spatially covered | Source closure |
| --- | ---: | ---: | --- |
| Whole product raster | 49 | 402 / 402 | Refused: crop assignment not unique |
| Selected cell from the product raster | 1 | 400 / 402 | Whole page deliberately unassessed |
| Same cell rendered directly from the original PDF | 2 | 336 / 402 | Refused: crop assignment not unique |

The native redraw produced overlapping detector boxes. These observations concern detection geometry, not literal recognition or timetable correctness. Pixels from distinct renderings do not establish identical glyph identities. Native redraw is not automatically an improvement; no arm qualified source roles or the whole document. No production model, confidence floor, or adoption gate was changed. The transaction took 1.907 seconds with a sampled peak working set of 362,758,144 bytes, and its owned scratch files were removed. All 32 model-free geometry controls passed.

## Independent lightweight second-reader component probe

A new fixed probe used 40 invented, unlabelled literals in paired 24 px / 16 px conditions: 80 cropped rows, not independent documents. The model/recipe were fixed before inference. The recognizer was the existing 16,534,782-byte PP-OCRv5 mobile model; the second reader was Tesseract 5.5.0 with the 2,471,260-byte Japanese `tessdata_fast` model at commit `87416418657359cb625c412a48b6e1d6d41c29bd`, SHA-256 `1f5de9236d2e85f5fdf4b3c500f2d4926f8d9449f28f5394472d9e8d83b91b4d`. It ran on Linux CPU, using a diagnostic Python preprocessing port with ONNX Runtime 1.30.0. It is separate from the production Windows 1.23.2 callable and the earlier frozen full-page/4,015-row comparisons.

Paddle had 65/80 exact literal rows; Tesseract had 59/80. There were 18 disagreements and six identical wrong literal outputs. Eight Paddle literal discrepancies passed its fixed 0.8 minimum-character-confidence guard: six O/0 substitutions in room strings and two time-range separator substitutions. The latter two may be semantically equivalent under the independent time parser; literal mismatch is not automatically an incorrect timetable clock. Of the six meaningful O/0 substitutions, the second reader disagreed on only one. It also disagreed with nine correct Paddle readings. An agreement-only policy would therefore retain errors while rejecting additional correct input.

Exactly 80 recognizer and 80 Tesseract calls completed in 9.617 seconds, without retries or source corrections. Model weights and generated images owned by this probe were deleted. Complete result SHA-256: `e4e8a04728ec150f7f2469369df3c615535222ac7a36293995572dab7d4b440b`. Full-document recovery, source-role certification, and model qualification remain unassessed. This candidate is not added to the production runtime or distribution catalog.

## Reduce repeated cell candidate scans

The Windows builder now indexes original atom and label origins per page and uses exact bounding-box containment to select candidates. It preserves array order and duplicate occurrences; it does not change any text, role labels, blank-field proof, confidence floor, work limit, or adoption predicate. Sources extending across a cell boundary remain excluded from that cell, with the existing full-document coverage validator still responsible for rejecting unassigned content.

Two new model-free tests compare the index with complete scans on 3,004 source occurrences and 207 query rectangles, including bucket edges, duplicate references, fractional bounds, and extreme finite coordinates. A local query into 20,000 sources charges fewer than 1,500 comparisons, with cancellation and budget failures retained. The complete Linux integration suite passed 779 tests, including the existing five-page 680-slot return fixture and the 77,929-atom dense layout. These are algorithmic regression checks using fictional source data, not new native OCR recognition, complete raster-document quality, or model qualification. The earlier frozen V6 result remains unchanged.

## Exact-white-margin preprocessing component comparison

A separate fixed Linux .NET probe used the actual 1.23.2 ONNX Runtime package, the pinned PP-OCRv5 mobile models, and 40 newly painted unlabelled cells with three fields each. It compared the unchanged production OCR callable with a research copy that removed only exact-white outer crop margins while retaining one pixel of available margin. Original pixels and all original containment/component-ownership guards were preserved; thresholds were unchanged. Three model-free controls covered source preservation, shared-ink rejection, and cancellation.

Both conditions completed 120 recognition rows and covered the original ink. An output-only audit checked every crop against the independent original glyph-paint support and its physical cell; all 120 fields had unique complete physical ownership in both conditions. This audit did not execute Builder, Validator, or formal adoption.

| Observation | Production baseline | White-margin candidate |
| --- | ---: | ---: |
| Exact owned literal fields | 107 / 120 | 104 / 120 |
| Exact owned fields above the unchanged 0.8 floor | 100 | 100 |
| Incorrect owned fields above that floor | 2 | 0 |
| Fields below the floor | 18 | 20 |
| Read transaction time | 6.904 s | 4.186 s |

The first transaction includes cold model initialization, so these times do not establish a causal speed improvement. The candidate lowers raw literal accuracy while reducing high-confidence errors on this small sample; it is not promoted as an automatic production change or a qualified model. Both conditions exceed the document-wide three-field manual-correction limit. Further evaluation requires independent held-out input, especially overlapping margins, rather than reusing this sample to select a threshold. The complete native output SHA-256 is `9b23c592bc990f9195574fca98aededcf5e1cac7888036b7a624fb57fe615cba`. The initial literal counter was corrected to count repeated room-name occurrences once each; native inference was not rerun. Generated rasters, the derived dictionary, and probe build files were removed at exit. No school PDF, cloud inference, or additional model download was used.

A separate held-out comparison kept the same trimming algorithm, runtime, models, and thresholds but used new literal contents and Noto Serif CJK at 16 px. Both conditions again had 120 uniquely owned fields and full ink coverage. The production baseline had 103 exact fields, the candidate 104; each had 101 exact fields and **11 incorrect fields** above the 0.8 floor, with eight fields below it. The first sample’s reduction in high-confidence errors did not generalize. This candidate is rejected for production activation. Raw output SHA-256: `309a4efd2e77b0d970fa19ce15632ace2a640d5c29dfd68c4f33c5a05e6a446c`. The two cohorts remain separately reported; neither established full-document adoption or model qualification.

## Overlapping native-scale detector research

A separately fixed Linux .NET 1.23.2 probe compared the production 960-pixel whole-page detector with 640-pixel cores and 128-pixel overlap, preserving the same PP-OCRv5 models, pixel sampling, detector probability thresholds, recognition floor, and original component/ink guards. Each condition had one invocation. The original 1,920 × 2,048 raster contained an invented one-day, 17-class, eight-period table with unlabelled subject/teacher/room lines: 436 distinct literals including headings. There was one detector call in the baseline and 12 in the candidate. Three model-free controls verified bounded windows, unique geometric ownership, and cancellation. No PDF, LLM, or adoption operation was involved.

On Noto Sans CJK at 11 px, both conditions completed 436 rows with unique complete original glyph support and body-cell membership. Exact literals improved from 363/436 to 417/436. Incorrect literals passing the unchanged 0.8 floor fell from 67 to zero, while rows below it increased from 17 to 56. The read transactions took 14.143 and 16.708 seconds; the first includes cold initialization. Native output SHA-256: `ad5ca6f187bc130fae1e0c48d5c658d6daab89531988138edaaf0096e6c0e2d0`. Neither condition satisfies a document-wide three-field correction limit.

A separate held-out cohort kept the same recipe and algorithms but changed content, class order, term/day/year, and font to Noto Serif CJK at 10 px. The baseline stopped after 359/436 rows because a native CTC interval touched input padding; it had 313 exact observed literals, with 77 unassessed. The candidate completed all 436 uniquely owned rows but had only 307 exact literals, three incorrect literals above the floor, and 169 below it. In particular, the candidate recognized only 22/136 subject literals exactly, while the partial baseline recognized 111/112 observed subjects exactly. Complete and partial denominators are kept separate; the unobserved baseline rows are not guessed. Native output SHA-256: `96ca9701ff58d26558c30e8e85df9d7cec96657477075d1ee432dc0af6c1a28b`.

The first cohort's improvement did not establish a general preprocessing improvement. This tiling recipe is not activated in production. A changed detector crop can alter recognition even when every original glyph is spatially covered, so source coverage alone cannot certify correct text. Both native comparisons retained all returned rows and errors; images, dictionary, and owned build files were removed at exit. Whole-document Builder/Validator correctness and model qualification remain unassessed.

## Model and detector-crop interaction

A new bounded comparison reused the two raster recipes above with the already downloaded 76,554,979-byte PP-OCRv6 medium recognizer and its matching 18,710-class dictionary. The detector, original pixels, native 1.23.2 runtime, preprocessing, probability thresholds, confidence floor, and source-padding guard were retained. No additional download or production switch was made. Original raster hashes matched the V5 comparisons; these are paired model/preprocessing conditions, not new independent documents.

On the 11 px Sans cohort, both V6 detector conditions completed all 436 uniquely owned rows and had 366 exact literals. The whole-page condition had 65 incorrect literals above the floor, the overlapping condition 66. Thus the larger model did not reproduce the V5 overlapping condition's 417 exact literals and zero high-confidence literal errors. V6 read transactions took 47.680 and 34.286 seconds, including cold initialization in the first. Native output SHA-256: `6af7626eeb9f4ed69bfdffb1c67c27a449e40130abd590f6f084f29c80ba41fb`.

On the 10 px Serif cohort, V6 stopped on the unchanged CTC-padding/source-position guard after 12 rows in the whole-page condition and 242 in the overlapping condition. The observed exact counts were 12/12 and 241/242; the remaining 424 and 194 source literals are unassessed. These partial rates cannot be presented as complete reading success. Native output SHA-256: `c8b61273243ee83c2c0d9fc9e9c2e6aec42568aac14a0026838e924be91c29ed`. The model/crop combination is not qualified, and the padding guard is not bypassed to manufacture completion. Images and owned build/dictionary files were removed; the previously owned shared models were preserved.
