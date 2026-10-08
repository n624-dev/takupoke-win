"""Invented font metadata tests; these are not OCR or full-PDF quality tests."""
from io import BytesIO
import struct
import unittest
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib.tables._c_m_a_p import CmapSubtable
from candidate import Rejected, read_candidates


def font_fixture(mapping=None, conflict=None, supplementary=False):
    builder = FontBuilder(1000, isTTF=True)
    order = [".notdef", "shapeA", "shapeB", "shapeC"]
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap(mapping or {65: "shapeA", 73: "shapeB", 108: "shapeC"})
    glyphs = {}
    for index, name in enumerate(order):
        pen = TTGlyphPen(None)
        pen.moveTo((50, 0)); pen.lineTo((200 + index * 50, 0))
        pen.lineTo((200 + index * 50, 700)); pen.lineTo((50, 700)); pen.closePath()
        glyphs[name] = pen.glyph()
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics({name: (600, 50) for name in order})
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable({"familyName": "InventedMetadataFixture", "styleName": "Regular"})
    builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    builder.setupPost(); builder.setupMaxp()
    if conflict is not None or supplementary:
        table = CmapSubtable.newSubtable(12)
        table.platformID, table.platEncID, table.language = 3, 10, 0
        table.cmap = conflict or {65: "shapeA", 73: "shapeB", 108: "shapeC", 0x10001: "shapeC"}
        builder.font["cmap"].tables.append(table)
    output = BytesIO(); builder.font.save(output)
    return output.getvalue()


def map_stream(*gids):
    return b"".join(struct.pack(">H", gid) for gid in gids)


class CandidateTests(unittest.TestCase):
    def test_identity_metadata_candidates_preserve_input_order_and_duplicates(self):
        r = read_candidates(font_fixture(), [3, 1, 2, 1], "Identity", visible=True)
        self.assertEqual("lAIA", "".join(v["candidate"] for v in r["values"]))
        self.assertEqual([3, 1, 2, 1], [v["code"] for v in r["values"]])
        self.assertTrue(r["candidateOnly"])
        self.assertFalse(r["qualified"]); self.assertFalse(r["adoptable"])

    def test_non_identity_cid_to_gid_does_not_treat_code_as_gid(self):
        r = read_candidates(font_fixture(), [1, 2, 3], map_stream(0, 3, 1, 2), visible=True)
        self.assertEqual("lAI", "".join(v["candidate"] for v in r["values"]))
        self.assertEqual([3, 1, 2], [v["gid"] for v in r["values"]])

    def test_duplicate_unicode_for_requested_glyph_refuses(self):
        with self.assertRaises(Rejected):
            read_candidates(font_fixture({65: "shapeA", 73: "shapeB", 108: "shapeB"}), [2], "Identity", visible=True)

    def test_partial_metadata_preserves_only_unique_candidates_and_order(self):
        r = read_candidates(font_fixture({65: "shapeA", 73: "shapeB", 108: "shapeB"}), [2, 1, 2], "Identity", visible=True, partial=True)
        self.assertEqual([None, "A", None], [v["candidate"] for v in r["values"]])
        self.assertEqual(["AMBIGUOUS_METADATA", "UNIQUE_METADATA", "AMBIGUOUS_METADATA"], [v["metadataState"] for v in r["values"]])
        self.assertEqual([73, 108], r["values"][0]["scalarCandidates"])
        self.assertFalse(r["completeMetadata"]); self.assertFalse(r["adoptable"])

    def test_partial_mode_never_bypasses_visibility_or_missing_bytes(self):
        for data, visible in ((font_fixture(), False), (font_fixture()[:40], True)):
            with self.subTest(visible=visible), self.assertRaises(Rejected):
                read_candidates(data, [1], "Identity", visible=visible, partial=True)

    def test_ambiguity_in_unrequested_glyph_does_not_invent_failure_elsewhere(self):
        r = read_candidates(font_fixture({65: "shapeA", 73: "shapeB", 108: "shapeB"}), [1], "Identity", visible=True)
        self.assertEqual("A", r["values"][0]["candidate"])

    def test_conflicting_unicode_subtables_refuse(self):
        with self.assertRaises(Rejected):
            read_candidates(font_fixture(conflict={65: "shapeB", 73: "shapeA", 108: "shapeC"}), [1], "Identity", visible=True)

    def test_supplementary_codepoint_is_not_truncated_to_bmp(self):
        # Both available maps agree where present; format4 lacks the astral glyph.
        data = font_fixture({65: "shapeA", 73: "shapeB", 0x10001: "shapeC"})
        r = read_candidates(data, [3], "Identity", visible=True)
        self.assertEqual(chr(0x10001), r["values"][0]["candidate"])

    def test_missing_glyphs_never_become_empty(self):
        for gid in (0, 4, 65535):
            with self.subTest(gid=gid), self.assertRaises(Rejected):
                read_candidates(font_fixture(), [1], map_stream(0, gid), visible=True)

    def test_truncated_or_unknown_cid_map_refuses(self):
        for mapping in (b"\x00", b"\x00\x00", "Unknown", None):
            with self.subTest(mapping=mapping), self.assertRaises(Rejected):
                read_candidates(font_fixture(), [1], mapping, visible=True)

    def test_visibility_and_encoding_are_required(self):
        for kwargs in ({"visible": False}, {"visible": True, "encoding": "Identity-V"}, {"visible": True, "subtype": "CIDFontType0"}):
            with self.subTest(kwargs=kwargs), self.assertRaises(Rejected):
                read_candidates(font_fixture(), [1], "Identity", **kwargs)

    def test_empty_and_invalid_codes_refuse(self):
        for codes in ([], [-1], [65536], [True], [1.0], [1] * 65537):
            with self.subTest(codes=str(codes[:2])), self.assertRaises(Rejected):
                read_candidates(font_fixture(), codes, "Identity", visible=True)

    def test_each_truncation_of_independent_font_refuses(self):
        data = font_fixture()
        count = int.from_bytes(data[4:6], "big")
        # Missing trailing alignment padding is not missing table content.
        required = max(int.from_bytes(data[20 + i * 16:24 + i * 16], "big") +
                       int.from_bytes(data[24 + i * 16:28 + i * 16], "big") for i in range(count))
        for length in range(required):
            with self.subTest(length=length), self.assertRaises(Rejected):
                read_candidates(data[:length], [1], "Identity", visible=True)

    def test_overlapping_tables_and_bad_offsets_refuse(self):
        data = font_fixture()
        for offset in (0, 12, len(data) + 1, 0xFFFFFFFF):
            modified = bytearray(data); modified[20:24] = struct.pack(">I", offset)
            with self.subTest(offset=offset), self.assertRaises(Rejected):
                read_candidates(bytes(modified), [1], "Identity", visible=True)


if __name__ == "__main__":
    unittest.main()
