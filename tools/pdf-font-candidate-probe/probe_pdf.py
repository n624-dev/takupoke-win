"""Invented PDF probe, pinned public font only; no PDF/model upload or file."""
import hashlib
from io import BytesIO
import json
import re
import urllib.request

import fitz
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from candidate import read_candidates, Rejected


def indirect(doc, xref, name):
    kind, value = doc.xref_get_key(xref, name)
    if kind != "xref":
        raise Rejected("missing indirect " + name)
    return int(value.split()[0])


def run():
    url = "https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/sourcesans3/SourceSans3%5Bwght%5D.ttf"
    original = urllib.request.urlopen(url, timeout=45).read(652633)
    if len(original) != 652632 or hashlib.sha256(original).hexdigest() != "8b95ef0061a8eb29ec83589c30e9c4cea279590782ac58963ab5edfca9a51493":
        raise Rejected("public font hash differs")
    f = instantiateVariableFont(TTFont(BytesIO(original)), {"wght": 400}, inplace=True)
    f.recalcTimestamp = False
    buffer = BytesIO(); f.save(buffer)
    records = []
    for subset in (False, True):
        with fitz.open() as doc:
            page = doc.new_page()
            page.insert_font(fontname="IndependentSans", fontbuffer=buffer.getvalue())
            page.insert_text((60, 60), "AI_1 Al_1 A1_1", fontname="IndependentSans", fontsize=16)
            if subset:
                doc.subset_fonts()
            font_xref = page.get_fonts()[0][0]
            assert doc.xref_get_key(font_xref, "Subtype") == ("name", "/Type0")
            assert doc.xref_get_key(font_xref, "Encoding") == ("name", "/Identity-H")
            descendants = doc.xref_get_key(font_xref, "DescendantFonts")
            if descendants[0] != "array" or not re.fullmatch(r"\[\s*\d+ 0 R\s*\]", descendants[1]):
                raise Rejected("unexpected descendants")
            child = int(descendants[1].strip("[] ").split()[0])
            assert doc.xref_get_key(child, "Subtype") == ("name", "/CIDFontType2")
            descriptor = indirect(doc, child, "FontDescriptor")
            embedded = doc.xref_stream(indirect(doc, descriptor, "FontFile2"))
            map_kind, map_value = doc.xref_get_key(child, "CIDToGIDMap")
            if (map_kind, map_value) in (("null", "null"), ("name", "/Identity")):
                cid_map = "Identity"  # Absent CIDFontType2 map defaults to Identity.
            elif map_kind == "xref":
                cid_map = doc.xref_stream(int(map_value.split()[0]))
            else:
                raise Rejected("unsupported CIDToGIDMap")
            # Source codes come from PDF operators, not the expected literal.
            content = page.read_contents()
            chunks = re.findall(rb"\[<([0-9a-fA-F]+)>\]TJ", content)
            if len(chunks) != 1:
                raise Rejected("fixture text operator shape changed")
            codes = bytes.fromhex(chunks[0].decode("ascii"))
            if len(codes) % 2:
                raise Rejected("odd Identity-H code stream")
            codes = [int.from_bytes(codes[i:i + 2], "big") for i in range(0, len(codes), 2)]
            doc.xref_set_key(font_xref, "ToUnicode", "null")
            assert doc.xref_get_key(font_xref, "ToUnicode") == ("null", "null")
            try:
                result = read_candidates(embedded, codes, cid_map, visible=True, partial=True)
                literal = "".join(v["candidate"] for v in result["values"]) if result["completeMetadata"] else None
                reason = None
            except Rejected as error:
                literal, reason = None, str(error)
            records.append({"subset": subset, "fontBytes": len(embedded),
                            "fontSHA256": hashlib.sha256(embedded).hexdigest(),
                            "sourceOperatorSHA256": hashlib.sha256(content).hexdigest(),
                            "candidate": literal, "refusal": reason,
                            "metadataValues": result["values"] if reason is None else [],
                            "qualified": False, "adoptable": False})
    # Evaluate only after both candidate attempts; PDF layout/visibility recovery
    # is outside this probe and is not replaced by the fixture's visible flag.
    for record in records:
        record["metadataExact"] = record["candidate"] == "AI_1 Al_1 A1_1"
    full, subset = records
    expected = "AI_1 Al_1 A1_1"
    assert len(full["metadataValues"]) == len(expected)
    for item, character in zip(full["metadataValues"], expected):
        if character == " ":
            assert item["metadataState"] == "AMBIGUOUS_METADATA"
            assert item["candidate"] is None and item["scalarCandidates"] == [32, 160]
        else:
            assert item["metadataState"] == "UNIQUE_METADATA" and item["candidate"] == character
    assert subset["refusal"] == "required TrueType tables absent" and not subset["metadataValues"]
    assert all(not r["qualified"] and not r["adoptable"] and not r["metadataExact"] for r in records)
    return {"scope": "font-metadata candidates, not OCR or full-document success", "records": records}


if __name__ == "__main__":
    print(json.dumps(run(), ensure_ascii=False))
