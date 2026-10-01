using System.Text;

namespace Takupoke.Core;

public sealed record MappingRule(string Alias, string FullName, IReadOnlyList<string>? Classes = null, bool? InternationalStudent = null);
public sealed record TeacherContextRule(string Alias, string FullName, string Subject, string ClassName, int SchoolYear);
public sealed record ChangePresentation(LessonNames Before, LessonNames After);
public sealed record MappingRules(IReadOnlyList<MappingRule> Subjects, IReadOnlyList<MappingRule> Teachers,
    IReadOnlyList<MappingRule> Rooms, IReadOnlyList<TeacherContextRule>? TeacherContexts = null)
{
    public static string Comparable(string value) => value.Normalize(NormalizationForm.FormKC).Trim();
    public LessonNames Apply(LessonNames names, string className) => names with
    {
        SubjectFullName = Match(names.Subject, Subjects, className) ?? names.SubjectFullName,
        TeacherFullName = MetadataName(names.Teacher, Teachers) ?? names.TeacherFullName,
        RoomFullName = MetadataName(names.Room, Rooms) ?? names.RoomFullName
    };
    public string? CanonicalSubject(string source, string className)
    {
        var text = Comparable(source);
        if (text.Length == 0) return null;
        var candidates = Subjects.Where(r => (r.Classes is null || r.Classes.Contains(className))
            && (Comparable(r.Alias) == text || Comparable(r.FullName) == text)).ToArray();
        var specific = candidates.Where(r => r.Classes?.Contains(className) == true).ToArray();
        return Unique((specific.Length > 0 ? specific : candidates).Select(r => Comparable(r.FullName)));
    }
    public bool IsInternational(string source, string className)
    {
        MappingRule? Preferred(IEnumerable<MappingRule> rules) => rules.FirstOrDefault(r => r.Classes?.Contains(className) == true)
            ?? rules.FirstOrDefault(r => r.Classes is null);
        var exact = Preferred(Subjects.Where(r => r.Alias == source));
        if (exact is not null) return exact.InternationalStudent == true;
        return Preferred(Subjects.Where(r => r.InternationalStudent == true && r.Alias.StartsWith("留 ", StringComparison.Ordinal)
            && r.Alias[2..] == source))?.InternationalStudent == true;
    }
    private static string? Match(string source, IReadOnlyList<MappingRule> rules, string? className = null)
    {
        if (source.Length == 0) return null;
        var matches = rules.Where(r => (r.Classes is null || className is not null && r.Classes.Contains(className))
            && Comparable(r.Alias) == Comparable(source)).ToArray();
        var specific = matches.Where(r => r.Classes is not null).ToArray();
        var preferred = specific.Length > 0 ? specific : matches;
        var exact = preferred.Where(r => r.Alias == source).ToArray();
        return Unique((exact.Length > 0 ? exact : preferred).Select(r => r.FullName));
    }
    private static string? Unique(IEnumerable<string> values)
    {
        var names = values.Distinct(StringComparer.Ordinal).ToArray();
        return names.Length == 1 ? names[0] : null;
    }
    private static IReadOnlyList<(string Value, string Separator)> MetadataFields(string source)
    {
        var fields = new List<(string, string)>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] is '(' or '（') depth++;
            else if (source[i] is ')' or '）') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && source[i] is ',' or '，' or '、')
            { fields.Add((source[start..i], source[i].ToString())); start = i + 1; }
        }
        fields.Add((source[start..], ""));
        return fields;
    }
    private static string? MetadataName(string source, IReadOnlyList<MappingRule> rules, Func<string, string?>? context = null)
    {
        string? Resolve(string value) => context?.Invoke(value.Trim()) ?? Match(value, rules);
        if (Resolve(source) is { } name) return name;
        var fields = MetadataFields(source);
        if (fields.Count < 2) return null;
        var resolved = false;
        var result = string.Concat(fields.Select(field =>
        {
            var full = Resolve(field.Value);
            if (full is null) return field.Value + field.Separator;
            resolved = true;
            return new string(field.Value.TakeWhile(char.IsWhiteSpace).ToArray()) + full
                + new string(field.Value.Reverse().TakeWhile(char.IsWhiteSpace).Reverse().ToArray()) + field.Separator;
        }));
        return resolved ? result : null;
    }
    private static bool Confirms(string source, IReadOnlyList<MappingRule> rules, Func<string, string?>? context = null)
    {
        bool Confirmed(string value) => context?.Invoke(value.Trim()) is not null || Match(value, rules) is not null;
        if (Confirmed(source)) return true;
        var fields = MetadataFields(source);
        return fields.Count > 1 && fields.All(f => Confirmed(f.Value));
    }
    private string? ContextualTeacher(string alias, string subject, string className, int? year)
    {
        if (year is null || CanonicalSubject(subject, className) is not { } canonical) return null;
        return Unique((TeacherContexts ?? []).Where(r => r.Alias == alias && r.ClassName == className && r.SchoolYear == year
            && Comparable(r.Subject) == canonical).Select(r => r.FullName));
    }
    public LessonNames SeparateChangeField(string source, string? className = null, int? year = null)
    {
        var remaining = source.Trim();
        var teacher = "";
        var room = "";
        while (remaining.Length > 0 && remaining[^1] is ')' or '）')
        {
            var closing = remaining[^1];
            var opening = closing == ')' ? '(' : '（';
            var depth = 0;
            var openingIndex = -1;
            for (var i = remaining.Length - 1; i >= 0; i--)
            {
                if (remaining[i] == closing) depth++;
                else if (remaining[i] == opening && --depth == 0) { openingIndex = i; break; }
            }
            if (openingIndex < 0 || depth != 0) break;
            var token = remaining[(openingIndex + 1)..^1].Trim();
            var subject = remaining[..openingIndex].Trim();
            var isTeacher = Confirms(token, Teachers, alias => className is null ? null : ContextualTeacher(alias, subject, className, year));
            var isRoom = Confirms(token, Rooms);
            if (isTeacher == isRoom) break;
            if (isTeacher) { if (teacher.Length > 0) break; teacher = token; }
            else { if (room.Length > 0) break; room = token; }
            remaining = subject;
        }
        return new(remaining, teacher, room);
    }
    public ChangePresentation Present(ScheduleChange change)
    {
        var cls = change.DisplayClassName;
        int? year = SchoolDate.TryParse(change.ChangeDate, out var day) ? day.SchoolYear() : null;
        var before = SeparateChangeField(change.BeforeSubject, cls, year);
        var after = SeparateChangeField(change.AfterSubject, cls, year);
        var conflict = change.Teacher.Length > 0 && after.Teacher.Length > 0 && change.Teacher != after.Teacher
            || change.Room.Length > 0 && after.Room.Length > 0 && change.Room != after.Room;
        after = conflict ? new(change.AfterSubject, change.Teacher, change.Room) : after with
        { Teacher = change.Teacher.Length > 0 ? change.Teacher : after.Teacher, Room = change.Room.Length > 0 ? change.Room : after.Room };
        LessonNames PresentNames(LessonNames names)
        {
            var standard = Apply(names, cls);
            return standard with { TeacherFullName = MetadataName(names.Teacher, Teachers, alias => ContextualTeacher(alias, names.Subject, cls, year)) ?? standard.TeacherFullName };
        }
        return new(PresentNames(before), PresentNames(after));
    }
    public string? ShortSubject(ScheduleChange change, IReadOnlyList<NormalLesson> lessons)
    {
        int? year = SchoolDate.TryParse(change.ChangeDate, out var day) ? day.SchoolYear() : null;
        var subject = SeparateChangeField(change.AfterSubject, change.DisplayClassName, year).Subject;
        var canonical = CanonicalSubject(subject, change.DisplayClassName);
        if (canonical is null) return null;
        return Unique(lessons.Where(l => l.ClassName == change.DisplayClassName && CanonicalSubject(l.Names.Subject, l.ClassName) == canonical)
            .Select(l => l.Names.Subject.Trim()));
    }
}
