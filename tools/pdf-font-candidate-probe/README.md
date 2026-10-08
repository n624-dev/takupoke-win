# Font metadata candidates

This research code is separate from app parsing and OCR. It supports bounded
standalone TrueType cmap4/12, Type0/Identity-H/CIDFontType2 and Identity or byte
CIDToGIDMap. It preserves source-code order, repeated glyphs and each used GID's
Unicode ambiguity. Partial metadata never supplies a replacement character.

`python -B -m unittest -v` creates invented font metadata entirely in memory.
`python -B probe_pdf.py` downloads only a pinned public SourceSans3 font, creates
two independent in-memory PDFs, reads their actual TJ source codes, removes
ToUnicode and compares full/subset embedded-font candidates. No expected text
selects input GIDs; literal evaluation follows both candidate attempts. Neither
PDF, embedded font, raster nor OCR data is written or uploaded.

UNIQUE_METADATA is not the RecoveryResult PRESENT state. The candidates do not
prove visual legibility, intended Unicode, visibility, cell ownership or complete
document semantics. The fixture's known visible drawing is not a general PDF
visibility interpreter. Every result remains candidateOnly=true, qualified=false,
adoptable=false. No production runtime, threshold, model catalog or adoption
condition uses this code. Fonts without cmap and conflicting maps stay unknown.

Dependencies: fonttools4.61.1 and PyMuPDF1.26.6. The public font's revision,
original SHA-256 and byte length are fixed in the script; transformed font hashes
identify observed bytes, not new independent accuracy cases. The source font is
under OFL1.1; no font binary is distributed with this probe. CI stores no cache
or artifact and passes only invented source information to its console.
