using Takupoke.Core;
using Takupoke.Core.Recovery;
using Takupoke.Infrastructure.Parsing;

namespace Takupoke.Infrastructure.Storage;

public readonly record struct SchoolLease(long Generation, SchoolDataPeriod Period);
public sealed record SourceRecord(string Id, MaterialKind Kind, string Path, string FileIdentity, string OriginalName,
    string Digest, long ByteCount, DateTimeOffset AcquiredAt, DateTimeOffset LastCheckedAt, DateTimeOffset? SourceModifiedAt)
{
    public ChangeWeekdayConsent? WeekdayConsent { get; init; }
}
public sealed record ChangeWeekdayConsent(string Digest, int SchoolYear, int ParserVersion)
{
    public bool Matches(SourceRecord source, int year, int version) => source.Kind == MaterialKind.Changes && Digest == source.Digest && SchoolYear == year && ParserVersion == version;
}
public sealed record MaterialAnalysis(string OriginalId, MaterialKind Kind, int ParserVersion, string SourceDigest,
    string SourceName, DateTimeOffset ParsedAt, int SchoolYear, TimetableAnalysis? Timetable = null,
    IReadOnlyList<ScheduleChange>? Changes = null, SpecialAnalysis? Special = null, RecoveryAudit? Recovery = null)
{
    public bool DateDerivedWeekdays { get; init; }
}
public sealed record RecoveryAudit(RecoveryDocument Document, RecoveryResult Result, RecoveryAcceptance Acceptance)
{
    public RecoveryAcceptance? PreviousAcceptance { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RecoverySemanticCertification? CurrentCertification { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RecoverySemanticCertification? PreviousCertification { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RecoverySemanticCertification>? CertificationHistory { get; init; }
}
public sealed record RecoveryPreview(string SourceId, SchoolLease Lease, RecoveryDocument Document, RecoveryResult Result, DateTimeOffset CreatedAt);
public sealed record RecoveryManualSession(string SourceId, SchoolLease Lease, RecoveryManualPlan Plan, DateTimeOffset CreatedAt);
public sealed record MaterialAttempt(DateTimeOffset At, string? Failure, bool Parsing, string? SourceDigest = null,
    int? SchoolYear = null, ChangeErrorCode? ChangeError = null, int? ParserVersion = null, int? Page = null, PdfFailurePosition? Cell = null, bool RecoveryPending = false);
public sealed record RetentionMarker(int SchemaVersion, int SchoolYear, int Half);
public interface IKeyProtector
{
    byte[] Protect(byte[] key);
    byte[] Unprotect(byte[] wrapped);
}
