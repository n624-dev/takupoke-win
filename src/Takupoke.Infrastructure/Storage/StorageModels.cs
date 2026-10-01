using Takupoke.Core;

namespace Takupoke.Infrastructure.Storage;

public readonly record struct SchoolLease(long Generation, SchoolDataPeriod Period);
public sealed record SourceRecord(string Id, MaterialKind Kind, string Path, string FileIdentity, string OriginalName,
    string Digest, long ByteCount, DateTimeOffset AcquiredAt, DateTimeOffset LastCheckedAt, DateTimeOffset? SourceModifiedAt);
public sealed record MaterialAnalysis(string OriginalId, MaterialKind Kind, int ParserVersion, string SourceDigest,
    string SourceName, DateTimeOffset ParsedAt, int SchoolYear, TimetableAnalysis? Timetable = null,
    IReadOnlyList<ScheduleChange>? Changes = null, SpecialAnalysis? Special = null);
public sealed record MaterialAttempt(DateTimeOffset At, string? Failure, bool Parsing, string? SourceDigest = null,
    int? SchoolYear = null, ChangeErrorCode? ChangeError = null);
public sealed record RetentionMarker(int SchemaVersion, int SchoolYear, int Half);
public interface IKeyProtector
{
    byte[] Protect(byte[] key);
    byte[] Unprotect(byte[] wrapped);
}
