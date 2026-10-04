using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Takupoke.Core.Recovery;

internal sealed record ProbeCase(string Id,string Task,string System,string Input,string? Grammar,string Expected);
internal static class Protocol
{
    internal const string Recipe = "compact-whole-header-shared-v2-win-q4-dev-v1";
    internal const string InstructionSha = "d24d3520e32dcd44d7a7440adc5ff20169580285d12190e04c2feb42329ad741";
    private static readonly QualificationCase Case = QualificationCorpus.Create().Single(c=>c.Id=="Timetable-folded-0");
    private static readonly RecoveryCell Cell = Case.Document.Cells.Single(c=>c.Id==Case.CellId);
    private static readonly RecoveryPromptCell Prompt = RecoveryStructure.Prompt(Case.Document,Cell);
    private static RecoveryResult? ExpectedResult;
    private static string? ExpectedFormal;
    private static readonly string[] Roles = ["subject","teacher","room"];
    private static string Instruction()
    {
        using var input = typeof(Protocol).Assembly.GetManifestResourceStream("CompactHeaderInstruction") ?? throw new InvalidDataException("Shared instruction missing.");
        using var output = new MemoryStream(); input.CopyTo(output); var bytes=output.ToArray();
        if(bytes.Length!=1099 || Convert.ToHexStringLower(SHA256.HashData(bytes))!=InstructionSha) throw new InvalidDataException("Shared instruction pin mismatch.");
        return new UTF8Encoding(false,true).GetString(bytes);
    }
    private static string Input(string role)
    {
        var field = role switch { "subject"=>RecoveryFieldRole.Subject,"teacher"=>RecoveryFieldRole.Teacher,"room"=>RecoveryFieldRole.Room,_=>throw new ArgumentException() };
        // Complete public alias list for the requested role, never selected IDs.
        var aliases = RecoveryRoleLabels.For(field).Select(v=>v+":").ToArray();
        return "targetRole: "+role+"\nallowedHeaders: "+JsonSerializer.Serialize(aliases,LabelSelectionProtocol.ReadableOptions)+
            "\nsources (original order):\n"+string.Join('\n',Prompt.Sources.Select(s=>JsonSerializer.Serialize(s.Id)+": "+JsonSerializer.Serialize(s.Text,LabelSelectionProtocol.ReadableOptions)));
    }
    internal static ProbeCase[] Cases()
    {
        var oracle = QualificationCorpus.Oracle(Prompt);
        var expected = new[]{oracle.Subject,oracle.Teacher,oracle.Room}.Select(f=>string.Join(',',f.Evidence.SkipLast(3))).ToArray();
        return Roles.Select((role,i)=>new ProbeCase(role,"compact-header",Instruction(),Input(role),SingleRoleProtocol.Format(Prompt).LarkGrammar,expected[i])).ToArray();
    }
    internal static string? Decode(string? raw,string task)
    {
        if(raw is null || raw.Length>2048) return null;
        try { return string.Join(',',SingleRoleProtocol.Decode(raw,Prompt)); }
        catch(InvalidRecoveryOutputException) { return null; }
    }
    internal static async Task<object> Preflight()
    {
        if(RecoveryValidator.Fingerprint(Case.Document)!="b0cc74a5748cdde17598602c3fbe8414fe51eb33d3eaa7637744e4383de1771e") throw new InvalidDataException("Consumed document differs from original frozen source.");
        var scopes=RecoveryStructure.Verify(Case.Document,Cell,Prompt,[QualificationCorpus.Oracle(Prompt)]);
        var rebuilt=Case.Document with { Cells=Case.Document.Cells.Select(c=>c.Id==Cell.Id ? c with {RoleScopes=scopes}:c).ToArray() };
        var expected=await RecoveryEngine.RunAsync(rebuilt,"windows",10,true,[],_=>null);
        if(expected.Result is null || !RecoveryValidator.Validate(rebuilt,expected.Result).CanAdopt) throw new InvalidDataException("Oracle cannot carry unchanged certificate/Validator.");
        ExpectedResult=expected.Result;ExpectedFormal=RecoveryValidator.Fingerprint(FormalComparison.Convert(rebuilt,expected.Result,CancellationToken.None));
        var rules=await RecoveryStructure.ResolveAsync(Case.Document,"windows",10,[],CancellationToken.None);
        var rulesRun=rules.Document is null ? null : await RecoveryEngine.RunAsync(rules.Document,"windows",10,true,[],_=>null);
        if(rulesRun?.Result is null || !RecoveryValidator.Validate(rules.Document!,rulesRun.Result).CanAdopt) throw new InvalidDataException("Original Rules-only control failed.");
        var boundaries=new[]{"{\"ids\":[\"demoA\"]}","{\"ids\":[\"g4\",\"g2\"]}","{\"ids\":[\"g2\",\"g2\"]}","{\"ids\":[],\"ids\":[\"g0\"]}","```json\n{\"ids\":[\"g0\"]}\n```"};
        if(boundaries.Any(s=>Decode(s,"compact-header") is not null) || Decode("{\"ids\":[\"g2\",\"g4\"]}","compact-header")!="g2,g4" || Decode("{\"ids\":[]}","compact-header")!="") throw new InvalidDataException("Strict decoder boundaries failed.");
        // Host-only scorer regression; these expected IDs never enter requests.
        var goldRows=Cases().Select(c=>{
            var raw=JsonSerializer.Serialize(new {ids=c.Expected.Split(',')});
            return new Observation(c.Id,c.Task,c.System,c.Input,c.Grammar,c.Expected,raw,raw.Length,false,c.Expected,true,true,null,null,0,0,0);
        }).ToArray();
        var gold=JsonSerializer.SerializeToElement(await AssessWhole(goldRows,CancellationToken.None));
        if(!gold.TryGetProperty("correctRecovery",out var correct) || !correct.GetBoolean()) throw new InvalidDataException("Full host scorer gold regression failed: "+gold);
        var emptyRows=goldRows.Select(o=>o with {Raw="{\"ids\":[]}",Decoded="",Exact=false}).ToArray();
        var empty=JsonSerializer.SerializeToElement(await AssessWhole(emptyRows,CancellationToken.None));
        if(empty.GetProperty("accepted").GetBoolean() || empty.GetProperty("assessedPositive").GetInt32()!=1) throw new InvalidDataException("Empty proposal received incorrect safety credit.");
        var partialRows=goldRows.Select((o,i)=>i==1 ? o with {Assessed=false,ErrorType="OperationCanceledException"}:o).ToArray();
        var partial=JsonSerializer.SerializeToElement(await AssessWhole(partialRows,CancellationToken.None));
        if(partial.GetProperty("assessedPositive").GetInt32()!=0 || partial.GetProperty("unassessedPositive").GetInt32()!=1) throw new InvalidDataException("Operational proposal received semantic credit.");
        var truncatedRows=goldRows.Select((o,i)=>i==1 ? o with {RawTruncated=true,RawLength=2049}:o).ToArray();
        var truncated=JsonSerializer.SerializeToElement(await AssessWhole(truncatedRows,CancellationToken.None));
        if(truncated.GetProperty("accepted").GetBoolean() || truncated.GetProperty("assessedPositive").GetInt32()!=1) throw new InvalidDataException("Valid JSON prefix received full-output credit.");
        return new { recipe=Recipe,planned=3,nativeCalls=0,sourceHash=RecoveryValidator.Fingerprint(Case.Document),instructionSha256=InstructionSha,
            validatorVersion=RecoveryValidator.Version,certificate=true,expectedFormalHash=ExpectedFormal,formalSlots=Case.Document.RequiredSlots.Count,
            rulesProviderCalls=0,usefulAiAssessed=0,strictBoundaryChecks=boundaries.Length+2,hostScorerChecks=4,cases=Cases() };
    }
    internal static async Task<object> AssessWhole(IReadOnlyList<Observation> observations,CancellationToken token)
    {
        var complete=observations.Count==3 && observations.All(o=>o.Assessed);
        if(!complete) return new {plannedPositive=1,assessedPositive=0,unassessedPositive=1,stage="native-incomplete",accepted=false,incorrectAcceptance=(bool?)null};
        if(observations.Any(o=>o.RawTruncated || o.Raw is null || o.RawLength is null or >2048 || o.RawLength!=o.Raw.Length || o.Decoded is null))
            return new {plannedPositive=1,assessedPositive=1,unassessedPositive=0,stage="returned-format-or-bound-rejection",accepted=false,incorrectAcceptance=false};
        var stage="per-role-strict-decode";
        try
        {
            var selections=observations.ToDictionary(o=>o.Id,o=>SingleRoleProtocol.Decode(o.Raw!,Prompt));
            stage="merged-ownership-decode";
            var merged=LabelSelectionProtocol.Decode(JsonSerializer.Serialize(selections),Prompt);
            stage="measured-cut-reconstruction";var proposal=LabelSelectionProtocol.Proposal(Prompt,merged);
            stage="production-certificate";var scopes=RecoveryStructure.Verify(Case.Document,Cell,Prompt,[proposal]);
            var metadata=new RecoveryMetadata("foundryLocal","qwen3.5-4b-generic-cpu:3","diagnostic-1","Foundry.Local.WinML:1.2.4","3",RecoveryValidator.SchemaVersion,RecoveryValidator.Version,Environment.OSVersion.VersionString);
            var rebuilt=Case.Document with {Cells=Case.Document.Cells.Select(c=>c.Id==Cell.Id ? c with {RoleScopes=scopes}:c).ToArray(),StructureMetadata=metadata};
            stage="Engine-Validator";var run=await RecoveryEngine.RunAsync(rebuilt,"windows",10,true,[],_=>null,token);
            if(run.Errors.Any(e=>e is "runtimeFailure" or "noLocalProvider") || run.State==RecoveryJobState.AwaitingModel)
                return new {plannedPositive=1,assessedPositive=0,unassessedPositive=1,stage,accepted=false,incorrectAcceptance=(bool?)null,errors=run.Errors};
            var accepted=run.Result is not null && RecoveryValidator.Validate(rebuilt,run.Result,token).CanAdopt;
            if(!accepted) return new {plannedPositive=1,assessedPositive=1,unassessedPositive=0,stage,accepted=false,incorrectAcceptance=false,errors=run.Errors};
            stage="full-formal-conversion";
            var formal=FormalComparison.Convert(rebuilt,run.Result!,token);var actualHash=RecoveryValidator.Fingerprint(formal);
            var resultExact=RecoveryValidator.Fingerprint(run.Result! with {Metadata=ExpectedResult!.Metadata})==RecoveryValidator.Fingerprint(ExpectedResult);
            var exact=resultExact && actualHash==ExpectedFormal;
            return new {plannedPositive=1,assessedPositive=1,unassessedPositive=0,stage,accepted=true,fullResultExact=resultExact,fullFormalExact=actualHash==ExpectedFormal,
                correctRecovery=exact,incorrectAcceptance=!exact,actualFormalHash=actualHash,expectedFormalHash=ExpectedFormal,requiredSlots=40,document=rebuilt,result=run.Result,formal};
        }
        catch(InvalidRecoveryOutputException e) {return new {plannedPositive=1,assessedPositive=1,unassessedPositive=0,stage,accepted=false,incorrectAcceptance=false,error=e.GetType().Name};}
        catch(Exception e) {return new {plannedPositive=1,assessedPositive=0,unassessedPositive=1,stage,accepted=false,incorrectAcceptance=(bool?)null,error=e.GetType().Name,message=e.Message[..Math.Min(e.Message.Length,1024)]};}
    }
}
