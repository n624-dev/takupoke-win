using Takupoke.Core;

namespace Takupoke.Infrastructure.Storage;

public sealed record ChangeRowSkipConsent(string SourceId, string Digest, int SchoolYear,
    int ParserVersion, IReadOnlyList<int> Rows)
{
    public bool Identifies(SourceRecord source, int year, int version) =>
        source.Kind == MaterialKind.Changes && SourceId == source.Id && Digest == source.Digest
        && SchoolYear == year && ParserVersion >= 1 && ParserVersion <= version && Rows is { Count: > 0 }
        && Rows.All(row => row > 1 && row <= ChangeNormalizer.MaximumRows)
        && Rows.SequenceEqual(Rows.Distinct().Order());

    public bool ValidFor(SourceRecord source, int year, int version) =>
        ParserVersion == version && Identifies(source, year, version);

    public bool SameAs(ChangeRowSkipConsent? other) => other is not null
        && SourceId == other.SourceId && Digest == other.Digest && SchoolYear == other.SchoolYear
        && ParserVersion == other.ParserVersion && Rows is not null && other.Rows is not null
        && Rows.SequenceEqual(other.Rows);
}
