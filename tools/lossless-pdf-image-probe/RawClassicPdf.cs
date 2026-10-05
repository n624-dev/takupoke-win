using System.Globalization;
using System.Text;
namespace LosslessImageResearch;

// A bounded syntax gate, NOT a replacement PDF graphics interpreter. Only one
// classic xref revision, sequential object ids/gen0 and direct stream Length.
// Binary stream data is skipped by its exact declared length, never tokenized.
public sealed class RawClassicPdf
{
    sealed record Value(string Kind,string Text="",Dictionary<string,Value>? Dict=null,List<Value>? Array=null);
    sealed record Obj(int Offset,Value Value,int DataStart=-1,int DataLength=0);
    readonly byte[] bytes; readonly CancellationToken token; int p;long work;readonly List<Obj> objects=[];
    RawClassicPdf(byte[] data,CancellationToken cancellation){bytes=data;token=cancellation;}
    static void Need(bool ok,string why){if(!ok)throw new Unsupported("Raw PDF: "+why);}
    void Tick(int amount=1){work+=amount;Need(work<=64_000_000,"syntax work limit");if(work%128==0||amount>1)token.ThrowIfCancellationRequested();}
    static bool White(byte b)=>b is 0 or 9 or 10 or 12 or 13 or 32;
    static bool Delim(byte b)=>White(b)||b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'/' or (byte)'%';
    void Whites(){while(p<bytes.Length&&White(bytes[p])){Tick();p++;}}
    void Space(){Whites();while(p<bytes.Length&&bytes[p]=='%'){while(p<bytes.Length&&bytes[p]!=10&&bytes[p]!=13){Tick();p++;}Whites();}}
    bool At(string word)=>p+word.Length<=bytes.Length&&bytes.AsSpan(p,word.Length).SequenceEqual(Encoding.ASCII.GetBytes(word));
    string Word(){Space();int start=p;while(p<bytes.Length&&!Delim(bytes[p])){Tick();p++;Need(p-start<=64,"token length");}Need(p>start,"token expected");return Encoding.Latin1.GetString(bytes,start,p-start);}
    int Integer(){var word=Word();Need(word.Length<=10&&word.All(char.IsAsciiDigit)&&int.TryParse(word,NumberStyles.None,CultureInfo.InvariantCulture,out _),"unsigned integer");return int.Parse(word,CultureInfo.InvariantCulture);}
    void Expect(string word){Need(Word()==word,"expected "+word);}
    string Name()
    {
        Space();Need(p<bytes.Length&&bytes[p++]=='/',"name");var raw=new List<byte>();
        while(p<bytes.Length&&!Delim(bytes[p]))
        {
            Tick();byte b=bytes[p++];
            if(b=='#'){Need(p+2<=bytes.Length,"name escape");int H(byte c)=>c is >=(byte)'0' and <=(byte)'9'?c-'0':c is >=(byte)'A' and <=(byte)'F'?c-'A'+10:c is >=(byte)'a' and <=(byte)'f'?c-'a'+10:-1;var a=H(bytes[p++]);var z=H(bytes[p++]);Need(a>=0&&z>=0,"name escape");b=(byte)(a*16+z);}
            raw.Add(b);Need(raw.Count<=128,"name limit");
        }
        Need(raw.Count>0,"empty name");return Encoding.Latin1.GetString(raw.ToArray());
    }
    Value Parse(int depth=0)
    {
        Tick();Need(depth<16,"nesting limit");Space();Need(p<bytes.Length,"missing value");
        if(At("<<"))
        {
            p+=2;var dict=new Dictionary<string,Value>(StringComparer.Ordinal);
            while(true){Space();if(At(">>")){p+=2;return new("dict",Dict:dict);}Need(dict.Count<128,"dictionary capacity");var key=Name();Need(!dict.ContainsKey(key),"duplicate key including decoded name escapes");dict.Add(key,Parse(depth+1));}
        }
        if(bytes[p]=='[')
        {p++;var list=new List<Value>();while(true){Space();Need(p<bytes.Length,"unterminated array");if(bytes[p]==']'){p++;return new("array",Array:list);}Need(list.Count<128,"array capacity");list.Add(Parse(depth+1));}}
        if(bytes[p]=='/')return new("name",Name());
        if(bytes[p]=='(')
        {
            p++;int start=p,level=1;
            while(level>0){Need(p<bytes.Length&&p-start<=4096,"literal string limit/end");Tick();var c=bytes[p++];if(c=='\\'){Need(p<bytes.Length,"string escape");var e=bytes[p++];if(e==13&&p<bytes.Length&&bytes[p]==10)p++;}else if(c=='('){Need(++level<=16,"string nesting");}else if(c==')')level--;}
            return new("string");
        }
        if(bytes[p]=='<')
        {p++;int start=p;while(true){Need(p<bytes.Length&&p-start<=4096,"hex string limit/end");Tick();var c=bytes[p++];if(c=='>')break;Need(White(c)||c is >=(byte)'0' and <=(byte)'9' or >=(byte)'A' and <=(byte)'F' or >=(byte)'a' and <=(byte)'f',"hex string character");}return new("hex");}
        var text=Word();if(text is "true" or "false" or "null")return new(text);
        Need(text.Length<=32&&text.All(c=>char.IsAsciiDigit(c)||c is '+' or '-' or '.')&&double.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number),"unsupported value/operator");
        if(text.All(char.IsAsciiDigit))
        {
            int save=p;Space();if(p<bytes.Length&&bytes[p] is >=(byte)'0' and <=(byte)'9')
            {var second=Word();Space();if(At("R")&&(p+1==bytes.Length||Delim(bytes[p+1]))){p++;Need(second=="0"&&int.TryParse(text,out var id)&&id is >0 and <128,"reference range/generation");return new("ref",text);}}
            p=save;
        }
        return new("number",text);
    }
    Obj Resolve(Value value){Need(value.Kind=="ref"&&int.TryParse(value.Text,out var id)&&id>0&&id<=objects.Count,"invalid object reference");return objects[int.Parse(value.Text)-1];}
    static Value Key(Value dict,string name){Need(dict.Kind=="dict"&&dict.Dict!.TryGetValue(name,out _),"missing dictionary key");return dict.Dict![name];}
    void CheckSimpleTree(Value trailer)
    {
        var catalog=Resolve(Key(trailer,"Root")).Value;Need(Key(catalog,"Type").Text=="Catalog","catalog type");var pagesRef=Key(catalog,"Pages");var pages=Resolve(pagesRef).Value;
        Need(Key(pages,"Type").Text=="Pages"&&Key(pages,"Count") is {Kind:"number",Text:"1"},"single page tree");var kids=Key(pages,"Kids");Need(kids.Kind=="array"&&kids.Array!.Count==1,"single page kid");var leaf=Resolve(kids.Array![0]).Value;
        Need(Key(leaf,"Type").Text=="Page"&&Key(leaf,"Parent")==pagesRef,"single leaf parent/type");
    }
    void Run()
    {
        token.ThrowIfCancellationRequested();Need(bytes.Length is >0 and <=50*1024*1024,"input capacity");
        Need(At("%PDF-1.")&&bytes.Length>8&&bytes[7] is >=(byte)'0' and <=(byte)'7'&&White(bytes[8]),"classic supported header");
        while(p<bytes.Length&&bytes[p]!=10&&bytes[p]!=13){Tick();p++;}Space();int xrefOffset;
        while(true)
        {
            Space();xrefOffset=p;if(At("xref"))break;
            Need(objects.Count<127,"object capacity");int offset=p,id=Integer();Need(id==objects.Count+1&&Integer()==0,"nonsequential/repeated object/generation");Expect("obj");var value=Parse();Space();int dataStart=-1,dataLength=0;
            if(At("stream"))
            {
                p+=6;Need(value.Kind=="dict"&&p<bytes.Length&&(bytes[p]==10||bytes[p]==13),"stream dictionary/EOL");if(bytes[p++]==13&&p<bytes.Length&&bytes[p]==10)p++;
                var length=Key(value,"Length");Need(length.Kind=="number"&&length.Text.All(char.IsAsciiDigit)&&int.TryParse(length.Text,out dataLength)&&dataLength is >=0 and <=50*1024*1024,"direct stream Length");
                dataStart=p;Need((long)p+dataLength<=bytes.Length,"stream data boundary");Tick(dataLength);p+=dataLength;
                // Only the optional stream separator EOL is allowed after data.
                if(p<bytes.Length&&bytes[p]==13){p++;if(p<bytes.Length&&bytes[p]==10)p++;}else if(p<bytes.Length&&bytes[p]==10)p++;
                Need(At("endstream")&&(p+9==bytes.Length||Delim(bytes[p+9])),"exact stream length/endstream");p+=9;
            }
            Expect("endobj");objects.Add(new(offset,value,dataStart,dataLength));
        }
        Expect("xref");Need(Integer()==0,"one xref subsection");var count=Integer();Need(count==objects.Count+1&&count<=128,"xref count");
        Need(Integer()==0&&Integer()==65535&&Word()=="f","xref null entry");
        for(int id=1;id<count;id++)Need(Integer()==objects[id-1].Offset&&Integer()==0&&Word()=="n","xref offset/generation/object mismatch");
        Expect("trailer");var trailer=Parse();Need(trailer.Kind=="dict"&&trailer.Dict!.Keys.All(k=>k is "Size" or "Root" or "Info" or "ID"),"unsupported trailer/revision/encryption");Need(Key(trailer,"Size") is {Kind:"number"} size&&size.Text==count.ToString(CultureInfo.InvariantCulture),"trailer Size");
        Expect("startxref");Need(Integer()==xrefOffset,"startxref offset");Whites();Need(At("%%EOF"),"EOF marker");p+=5;Whites();Need(p==bytes.Length,"trailing revision/data");CheckSimpleTree(trailer);
    }
    public static RawClassicPdf Verify(byte[] bytes,CancellationToken token){var gate=new RawClassicPdf(bytes,token);gate.Run();return gate;}
    public void MatchStream(ReadOnlySpan<byte> data)
    {
        // Full binary identity, never a scan for slash/name/dictionary tokens.
        foreach(var o in objects)if(o.DataStart>=0&&o.DataLength==data.Length){Tick(o.DataLength);if(bytes.AsSpan(o.DataStart,o.DataLength).SequenceEqual(data)){token.ThrowIfCancellationRequested();return;}}
        throw new Unsupported("Raw PDF: parsed stream does not match a verified raw stream");
    }
}
