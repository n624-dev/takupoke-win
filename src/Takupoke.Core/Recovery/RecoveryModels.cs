using System.Text.Json.Serialization;

namespace Takupoke.Core.Recovery;

// The wire meanings are shared with the independently implemented iOS/Android contracts.
public enum RecoveryDocumentKind { Timetable, Exam, Return }
public enum RecoveryValueState { Present, Empty, Unreadable, Missing, Ambiguous }
public enum RecoveryInputState { Complete, Partial, RasterOnly }
public enum RecoveryJobState { Pending, Preparing, AwaitingModel, Running, AwaitingConfirmation, Adopted, Failed, Superseded, AwaitingManualCorrection }
public enum LocalProviderState { Ready, NotReady, Disabled, Unsupported, DownloadRequired, InsufficientMemory }
public sealed record RecoveryBox(double X, double Y, double Width, double Height)
{
    public bool Valid => new[] { X, Y, Width, Height, X + Width, Y + Height }.All(double.IsFinite) && X >= 0 && Y >= 0 && Width > 0 && Height > 0;
    public bool Contains(RecoveryBox other) => Valid && other.Valid && other.X >= X && other.Y >= Y && other.X + other.Width <= X + Width && other.Y + other.Height <= Y + Height;
}
public sealed record RecoverySource(string Id, string CellId, int Page, string Text, RecoveryBox Box, bool FromOcr = false, int? SourceLine = null, int? SourceOrder = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? NativeConfidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryFontEvidence? FontEvidence { get; init; }
}
public sealed record RecoveryFontEvidence(string Resource, string FontHash, string CidMapHash,
    int Code, int Cid, int GlyphId, int Scalar, int ReaderVersion = 1);
public sealed record RecoveryField(RecoveryValueState State, string Value, IReadOnlyList<string> Evidence);
public sealed record RecoverySlot(string ClassName, string Day, int Period);
public enum RecoveryHeaderAxis { Above, Left }
public sealed record RecoveryHeaderRegion(int Page, RecoveryBox Box, RecoveryHeaderAxis Axis);
public sealed record RecoveryClockBinding(int Page, RecoveryBox Box, string Day, int SpanStart, int SpanEnd,
    IReadOnlyList<string> DayHeaderIds, RecoveryHeaderRegion? DayRegion, IReadOnlyList<string> PeriodHeaderIds, RecoveryHeaderRegion PeriodRegion)
{ public bool CommonScope { get; init; } }
public enum RecoveryBindingMode { Fixed, RoleProposal }
public enum RecoveryFieldRole { Subject, Teacher, Room }
public enum RecoveryRoleProof { InlineLabel, ColumnHeader }
public sealed record RecoveryRoleScope(int LessonIndex, RecoveryFieldRole Role, int Page, RecoveryBox Box,
    IReadOnlyList<string> LabelSourceIds, RecoveryHeaderRegion LabelRegion, RecoveryRoleProof Proof, bool EmptyVerified);
public sealed record RecoveryLessonBinding(IReadOnlyList<string> Subject, IReadOnlyList<string> Teacher, IReadOnlyList<string> Room);
// Original character geometry for a bounded, app-certified three-row family.
// Never part of a model response, and null preserves historical audit JSON.
public sealed record RecoveryOrderedRowPiece(string Text, RecoveryBox Box, int? SourceLine, int? SourceOrder);
public sealed record RecoveryOrderedRowProof(int Version, IReadOnlyList<string> SourceIds, IReadOnlyList<IReadOnlyList<RecoveryOrderedRowPiece>> Rows)
{
    public static bool SingleRow(IReadOnlyList<RecoveryOrderedRowPiece> pieces)
    {
        if (pieces.Count is < 1 or > 256 || pieces.Any(p => p.Text.Length != 1 || !p.Box.Valid || p.SourceLine is null or < 0 || p.SourceOrder is null or < 0) ||
            pieces.Select(p=>p.SourceLine).Distinct().Count()!=1 ||
            pieces.Zip(pieces.Skip(1)).Any(p=>p.First.SourceOrder>=p.Second.SourceOrder || p.First.Box.X+p.First.Box.Width>p.Second.Box.X+0.1)) return false;
        var ink=pieces.Where(p=>!string.IsNullOrWhiteSpace(p.Text)).ToArray();
        return ink.Length>0 && ink.Zip(ink.Skip(1)).All(p=>p.Second.Box.X-p.First.Box.X-p.First.Box.Width<=Math.Max(p.First.Box.Height,p.Second.Box.Height)*1.5);
    }
}
public sealed record RecoveryCell(string Id, int Page, RecoveryBox Box, RecoveryInputState InputState,
    IReadOnlyList<RecoverySlot> Slots, IReadOnlyList<string> SourceIds, IReadOnlyList<string> BlankFields,
    bool ConfirmedEmpty = false, int ParallelCount = 1)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryOrderedRowProof? OrderedRowProof { get; init; }
    // App-generated original separator proof, never a model response field.
    // Null preserves the historical V4/V5 document JSON and scope fingerprint.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? ParallelSeparators { get; init; }
    public RecoveryBindingMode BindingMode { get; init; } = RecoveryBindingMode.Fixed;
    public IReadOnlyList<RecoveryRoleScope> RoleScopes { get; init; } = [];
    public IReadOnlyList<string> ClassHeaderIds { get; init; } = [];
    public IReadOnlyList<string> DayHeaderIds { get; init; } = [];
    public IReadOnlyList<string> PeriodHeaderIds { get; init; } = [];
    public IReadOnlyList<RecoveryLessonBinding> LessonBindings { get; init; } = [];
    public RecoveryHeaderRegion? ClassRegion { get; init; }
    public RecoveryHeaderRegion? DayRegion { get; init; }
    public IReadOnlyDictionary<string, RecoveryHeaderRegion> PeriodRegions { get; init; } = new Dictionary<string, RecoveryHeaderRegion>();
}
public sealed record RecoveryDocument(string PdfHash, RecoveryDocumentKind Kind, int SchoolYear, string? Term,
    IReadOnlyList<string> Classes, IReadOnlyList<string> Days, IReadOnlyList<RecoverySlot> RequiredSlots,
    IReadOnlyList<RecoveryCell> Cells, IReadOnlyList<RecoverySource> Sources, bool Complete,
    IReadOnlyList<string> YearEvidence, IReadOnlyList<string> TermEvidence,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DayEvidence,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ClassEvidence,
    IReadOnlyDictionary<string, IReadOnlyList<string>> PeriodEvidence,
    IReadOnlyDictionary<string, string> Times, IReadOnlyList<string> TimeEvidence,
    IReadOnlyList<string> NormalTimeNoteEvidence)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryCaptureReceipt? Capture { get; init; }
    public RecoveryMetadata? StructureMetadata { get; init; }
    public IReadOnlyList<string> CommonClockEvidence { get; init; } = [];
    public IReadOnlyDictionary<string, RecoveryHeaderRegion> CommonClockRegions { get; init; } = new Dictionary<string, RecoveryHeaderRegion>();
    public IReadOnlyList<string> DocumentTitleEvidence { get; init; } = [];
    public IReadOnlyDictionary<string, RecoveryClockBinding> ClockBindings { get; init; } = new Dictionary<string, RecoveryClockBinding>();
    public IReadOnlyDictionary<string, IReadOnlyList<RecoveryClockBinding>> ClockReplicas { get; init; } = new Dictionary<string, IReadOnlyList<RecoveryClockBinding>>();
    public IReadOnlyDictionary<string, string> SpanTimes { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ClockEvidence { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
}
public sealed record RecoveryLesson(RecoveryField Subject, RecoveryField Teacher, RecoveryField Room,
    IReadOnlyList<string> DateEvidence, IReadOnlyList<string> PeriodEvidence);
public sealed record RecoveredCell(string CellId, RecoveryValueState State, IReadOnlyList<RecoveryLesson> Lessons);
public sealed record RecoveryMetadata(string Provider, string ModelId, string ModelVersion, string RuntimeVersion,
    string PromptVersion, int RecoverySchemaVersion, int ValidatorVersion, string OsVersion, string RecoveryVersion = "2");
public sealed record RecoveryResult(string PdfHash, RecoveryDocumentKind Kind, int SchoolYear, string? Term,
    IReadOnlyList<RecoveredCell> Cells, RecoveryMetadata Metadata)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RecoveryHumanCorrection>? HumanCorrections { get; init; }
}
public sealed record RecoveryJob(string PdfHash, RecoveryDocumentKind Kind, RecoveryJobState State,
    DateTimeOffset CreatedAt, string? ResultHash = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryManualPlan? ManualPlan { get; init; }
}
// Audit-level certification retains historical objects and explicit acceptance
// unchanged while binding current semantic validation and a bounded predecessor.
public sealed record RecoverySemanticCertification(int RecoverySchemaVersion, int ValidatorVersion,
    string ScopeHash, string ResultHash, string AcceptanceHash, string PreviousAcceptanceHash)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousCertificationHash { get; init; }
}
public sealed record RecoveryAcceptance(string PdfHash, string ResultHash, string ScopeHash, RecoveryMetadata Metadata, DateTimeOffset AcceptedAt);
public sealed record RecoveryValidation(IReadOnlyList<string> Errors)
{
    public bool CanAdopt => Errors.Count == 0;
}

// OCR, rule recovery and AI require opt-in; ordinary Strict parsing/monitoring stays available.
public sealed class AiFeaturePermission
{
    private readonly object _gate = new();
    private bool _enabled;
    private long _generation;
    private CancellationTokenSource _lifetime = new();
    public bool Enabled { get { lock (_gate) return _enabled; } }
    public (long Generation, CancellationToken Token) Capture() { lock (_gate) return (_generation, _lifetime.Token); }
    public void SetEnabled(bool enabled)
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            if (_enabled == enabled) return;
            _enabled = enabled; _generation++; old = _lifetime; _lifetime = new();
        }
        old.Cancel(); old.Dispose();
    }
    public void Check(long generation, bool requireEnabled = true)
    {
        lock (_gate) if (_generation != generation || requireEnabled && !_enabled) throw new OperationCanceledException("読み取り設定が変わりました。");
    }
    public static bool UsesAi(RecoveryDocument document, RecoveryResult result) => result.Metadata.Provider != "rule" || document.StructureMetadata is { Provider: not "rule" };
}
