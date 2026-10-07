# OCR improvement log

All observations below use independently fictional timetable content. The uploaded school PDF is not an input, fixture, or repository asset. No PDF, image, model weight, or school subject/teacher/room text is included in this log.

These are bounded Linux x64 CPU recognizer comparisons, not physical Windows qualification or application PDF-acquisition tests. The confidence floor remains 0.8. Literal comparisons preserve Unicode and do not normalize punctuation. Each experiment has one invocation, no retry, and an independently reviewed complete inventory of 4,016 recognizer results for 4,015 source rows.

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
