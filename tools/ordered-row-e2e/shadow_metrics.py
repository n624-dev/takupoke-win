"""Post-return diagnostics only. No values or confidence reach adoption."""
from collections import Counter


def summarize(shadow, gold):
    expected = Counter(lesson[role] for slot in gold["slots"]
                       for lesson in slot["lessons"] for role in ("subject", "teacher", "room"))
    found, uncertain_spaces = Counter(), Counter()
    rows = [row for page in shadow["pages"] for row in page["rows"]]
    low_space = low_body = empty = unsupported = 0
    for row in rows:
        pieces = row["pieces"]
        raw = "".join(piece["Text"] for piece in pieces)
        space = any(piece["Text"].isspace() and piece["Confidence"] < .8 for piece in pieces)
        body = any(not piece["Text"].isspace() and piece["Confidence"] < .8 for piece in pieces)
        low_space += space
        low_body += body
        empty += not pieces
        unsupported += not row["paddingSupported"]
        if raw in expected:
            found[raw] += 1
            if space and not body and row["paddingSupported"]:
                uncertain_spaces[raw] += 1
    return {"pagesCompleted": len(shadow["pages"]), "error": shadow["error"],
            "recognizerCallsOnCompletedPages": len(rows), "lowWhitespaceRows": low_space,
            "lowNonWhitespaceRows": low_body, "zeroPieceRows": empty,
            "unsupportedPaddingRows": unsupported,
            "exactBodyLiteralOccurrences": sum((found & expected).values()),
            "exactBodyLiteralsWithOnlyUncertainWhitespace": sum((uncertain_spaces & expected).values()),
            "documentBodyObligations": sum(expected.values()), "nonAdoptable": True,
            "scope": "Literal occurrences only; no field ownership or complete document correctness"}
