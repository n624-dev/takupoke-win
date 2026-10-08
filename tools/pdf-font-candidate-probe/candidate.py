"""Local-only research parser. Font metadata proposes candidates, never adoption.

Only sfnt TrueType, Unicode cmap formats 4/12, Identity-H Type0/CIDFontType2
and explicit Identity/byte-stream CIDToGIDMap are in scope. No font download.
"""
from hashlib import sha256
import struct


class Rejected(ValueError):
    pass


def read_candidates(font, encoded_codes, cid_to_gid, *, visible, subtype="CIDFontType2", encoding="Identity-H", partial=False):
    if not visible or subtype != "CIDFontType2" or encoding != "Identity-H":
        raise Rejected("unsupported or unverified drawing")
    if not isinstance(font, bytes) or not 12 <= len(font) <= 20_000_000:
        raise Rejected("font size")
    def bounded(data, pos, length):
        if pos < 0 or length < 0 or pos > len(data) - length:
            raise Rejected("truncated table")
        return data[pos:pos + length]
    def u16(data, pos):
        return int.from_bytes(bounded(data, pos, 2), "big")
    def u32(data, pos):
        return int.from_bytes(bounded(data, pos, 4), "big")
    if font[:4] != b"\x00\x01\x00\x00":
        raise Rejected("not standalone TrueType")
    count = u16(font, 4)
    if not 1 <= count <= 256:
        raise Rejected("table count")
    directory_end = 12 + count * 16
    bounded(font, 12, count * 16)
    tables, extents = {}, []
    for index in range(count):
        pos = 12 + index * 16
        tag = bounded(font, pos, 4)
        start, size = u32(font, pos + 8), u32(font, pos + 12)
        if tag in tables or start < directory_end:
            raise Rejected("duplicate or overlapping directory")
        tables[tag] = bounded(font, start, size)
        if size:
            extents.append((start, start + size))
    extents.sort()
    if any(a[1] > b[0] for a, b in zip(extents, extents[1:])):
        raise Rejected("overlapping tables")
    if not all(tag in tables for tag in (b"cmap", b"maxp", b"glyf", b"loca")):
        raise Rejected("required TrueType tables absent")
    glyph_count = u16(tables[b"maxp"], 4)
    if not 1 <= glyph_count <= 65535:
        raise Rejected("glyph count")
    codes = list(encoded_codes)
    if not 1 <= len(codes) <= 65536 or any(type(c) is not int or not 0 <= c <= 65535 for c in codes):
        raise Rejected("code domain")
    if cid_to_gid != "Identity" and (not isinstance(cid_to_gid, bytes) or len(cid_to_gid) % 2 or len(cid_to_gid) > 131072):
        raise Rejected("CIDToGIDMap")
    code_gids = {}
    for code in codes:
        gid = code if cid_to_gid == "Identity" else u16(cid_to_gid, code * 2)
        if not 0 < gid < glyph_count:
            raise Rejected("missing or invalid glyph")
        code_gids[code] = gid
    requested = set(code_gids.values())
    cmap = tables[b"cmap"]
    if u16(cmap, 0) != 0:
        raise Rejected("cmap version")
    count = u16(cmap, 2)
    if not 1 <= count <= 64:
        raise Rejected("cmap count")
    bounded(cmap, 4, count * 8)
    mappings, work, offsets = [], 0, set()
    def consume(amount):
        nonlocal work
        work += amount
        if work > 1_200_000:
            raise Rejected("mapping work limit")
    def scalar(cp):
        return 0 <= cp <= 0x10FFFF and not 0xD800 <= cp <= 0xDFFF
    for index in range(count):
        pos = 4 + index * 8
        platform, enc, off = u16(cmap, pos), u16(cmap, pos + 2), u32(cmap, pos + 4)
        if not (platform == 0 or platform == 3 and enc in (1, 10)):
            continue
        if off in offsets:
            continue
        offsets.add(off)
        if off < 4 + count * 8:
            raise Rejected("subtable overlaps records")
        format_id = u16(cmap, off)
        inverse = {gid: set() for gid in requested}
        if format_id == 4:
            sub = bounded(cmap, off, u16(cmap, off + 2))
            twice = u16(sub, 6)
            if twice == 0 or twice % 2 or twice > 8192:
                raise Rejected("format4 segment count")
            n = twice // 2
            end_pos, start_pos, delta_pos, range_pos = 14, 16 + n * 2, 16 + n * 4, 16 + n * 6
            bounded(sub, 0, 16 + n * 8)
            if u16(sub, 14 + n * 2) != 0:
                raise Rejected("format4 reserved padding")
            previous = -1
            for seg in range(n):
                end, start = u16(sub, end_pos + seg * 2), u16(sub, start_pos + seg * 2)
                delta, ro = u16(sub, delta_pos + seg * 2), u16(sub, range_pos + seg * 2)
                if start > end or start <= previous or ro % 2:
                    raise Rejected("format4 segment order")
                previous = end
                consume(end - start + 1)
                for cp in range(start, end + 1):
                    if ro:
                        ptr = range_pos + seg * 2 + ro + 2 * (cp - start)
                        if ptr < 16 + n * 8:
                            raise Rejected("glyph array points into metadata")
                        gid = u16(sub, ptr)
                        if gid:
                            gid = (gid + delta) & 65535
                    else:
                        gid = (cp + delta) & 65535
                    if gid >= glyph_count:
                        raise Rejected("invalid cmap glyph")
                    if gid in inverse:
                        if not scalar(cp):
                            raise Rejected("invalid Unicode scalar")
                        inverse[gid].add(cp)
            if previous != 65535:
                raise Rejected("format4 sentinel absent")
        elif format_id == 12:
            sub = bounded(cmap, off, u32(cmap, off + 4))
            if u16(sub, 2) != 0:
                raise Rejected("format12 reserved")
            groups = u32(sub, 12)
            if groups > 65536 or len(sub) != 16 + groups * 12:
                raise Rejected("format12 groups")
            previous = -1
            for group in range(groups):
                pos = 16 + group * 12
                start, end, first_gid = u32(sub, pos), u32(sub, pos + 4), u32(sub, pos + 8)
                if start > end or start <= previous or end > 0x10FFFF or start <= 0xDFFF and end >= 0xD800 or first_gid + end - start >= glyph_count:
                    raise Rejected("format12 group domain")
                previous = end
                consume(len(requested))
                for gid in requested:
                    if first_gid <= gid <= first_gid + end - start:
                        inverse[gid].add(start + gid - first_gid)
        else:
            # A further Unicode cmap might contradict the supported maps.
            raise Rejected("unsupported Unicode cmap")
        mappings.append(inverse)
    if not mappings:
        raise Rejected("no supported Unicode cmap")
    selected = {}
    for gid in requested:
        union = set().union(*(m[gid] for m in mappings))
        if len(union) != 1 and not partial:
            raise Rejected("absent or ambiguous Unicode mapping")
        # Metadata uniqueness does not assert that original text is readable.
        # No replacement whitespace, best-guess scalar, or concatenation is
        # provided for ambiguous glyphs, even if the candidates look similar.
        selected[gid] = {"metadataState": "UNIQUE_METADATA" if len(union) == 1 else
                         "AMBIGUOUS_METADATA" if union else "MISSING_METADATA",
                         "candidate": chr(next(iter(union))) if len(union) == 1 else None,
                         "scalarCandidates": sorted(union)}
    return {"fontSHA256": sha256(font).hexdigest(), "candidateOnly": True,
            "qualified": False, "adoptable": False,
            "completeMetadata": all(x["metadataState"] == "UNIQUE_METADATA" for x in selected.values()),
            "values": [{"code": c, "gid": code_gids[c], **selected[code_gids[c]]} for c in codes]}
