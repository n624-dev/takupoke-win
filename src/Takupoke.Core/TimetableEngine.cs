namespace Takupoke.Core;

public abstract record BlockContent
{
    public abstract LessonNames Names { get; }
}
public sealed record NormalContent(NormalLesson Lesson) : BlockContent
{
    public override LessonNames Names => Lesson.Names;
}
public sealed record SpecialContent(MaterialKind Kind, SpecialLesson Lesson, TimeRange? Time) : BlockContent
{
    public override LessonNames Names => Lesson.Names;
}
public sealed record ChangeContent(ScheduleChange Change) : BlockContent
{
    public override LessonNames Names => new(Change.AfterSubject, Change.Teacher, Change.Room);
}
public sealed record ScheduleBlock(int StartPeriod, int EndPeriod, BlockContent Content);
public sealed record PositionedBlock(ScheduleBlock Block, int Lane);
public sealed record ScheduleSlot(IReadOnlyList<NormalLesson> BaseLessons, IReadOnlyList<SpecialContent> SpecialLessons,
    IReadOnlyList<ScheduleChange> Changes);
public sealed record DayPlan(IReadOnlyList<SchoolEvent> Events, bool IsNoClass, bool IsSupplementary,
    int? WeekdayOverride, bool ApiNoClass, bool ApiTest, bool ApiTestReturn, IReadOnlyList<string> WeekendEventLabels, bool WeekdayOverrideConflict = false)
{
    public string? WeekdayOverrideWarning => WeekdayOverrideConflict ? "曜日振替を確定できません" : null;
    public IReadOnlyList<string> FullDayLabels => Events.Where(e =>
        (!e.NeedsReview && e.Classification is EventClassification.NoClass or EventClassification.SchoolEventNoClass) || e.Tag == "補講日")
        .Select(e => e.Title.Trim()).Where(t => t.Length > 0).Distinct().Order(StringComparer.Ordinal).ToArray();
    public IEnumerable<SchoolEvent> HeaderEvents(bool hasFullDayCard) => Events.Where(e => e.Tag != "行事メモ"
        && (!hasFullDayCard || !FullDayLabels.Contains(e.Title.Trim())));
}

/// <summary>Read-only projection shared by the home page, timetable and details.</summary>
public sealed class TimetableEngine(ScheduleData data, bool includesChanges = true, bool international = false,
    Func<string, string, int?, bool>? internationalRule = null)
{
    private IEnumerable<SpecialAnalysis> Specials => data.Specials ?? [];
    public DayPlan Plan(DateOnly day)
    {
        var events = (data.Events ?? []).Where(e => e.Applies(day)).ToArray();
        var reliable = events.Where(e => !e.NeedsReview).ToArray();
        var overrides = reliable.Where(e => e.Classification == EventClassification.WeekdayOverride)
            .Select(e => e.ScheduleDay).Where(d => d is not null).Distinct().ToArray();
        return new(events, reliable.Any(e => e.Classification is EventClassification.NoClass or EventClassification.SchoolEventNoClass),
            reliable.Any(e => e.Classification == EventClassification.Supplementary), overrides.Length == 1 ? overrides[0] : null,
            events.Any(e => e.Tag is "授業なし" or "行事（授業なし）"), events.Any(e => e.Tag == "テスト"), events.Any(e => e.Tag == "テスト返却"),
            day.SchoolWeekday() > 5 ? events.Where(e => e.Tag is "行事（授業なし）" or "補講日").Select(e => e.Title).Distinct().Order(StringComparer.Ordinal).ToArray() : [], overrides.Length > 1);
    }

    public ScheduleSlot Slot(DateOnly day, int period, string className)
    {
        if (period is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(period));
        var plan = Plan(day);
        if (plan.IsNoClass && !plan.ApiNoClass) return new([], [], []);
        var applies = Specials.Any(s => s.Applies(day, className));
        var specials = Specials.SelectMany(s => s.Lessons.Where(l => l.Date == day.Iso() && l.ClassName == className && l.Period == period)
            .Select(l => new SpecialContent(s.Kind, l, s.TimeFor(l)))).ToArray();
        var baseLessons = !(plan.IsNoClass || plan.IsSupplementary || plan.ApiTest || plan.ApiTestReturn || applies)
            && data.Timetable?.ApplicableRange?.Contains(day) == true
            ? data.Timetable.Lessons.Where(l => l.ClassName == className && l.Weekday == (plan.WeekdayOverride ?? day.SchoolWeekday()) && l.Period == period).ToArray()
            : [];
        var changes = includesChanges ? ChangesOn(day, className).Where(c => c.GridPeriods?.Contains(period) == true).ToArray() : [];
        return new(baseLessons, specials, changes);
    }

    public IEnumerable<ScheduleChange> ChangesOn(DateOnly day, string className) =>
        (data.Changes ?? []).Where(c => c.ChangeDate == day.Iso() && c.DisplayClassName == className);

    public IEnumerable<ScheduleChange> Changes(IReadOnlySet<string> classes, ChangeRange range, DateOnly today, DateOnly weekStart) =>
        (data.Changes ?? []).Where(c => classes.Contains(c.DisplayClassName) && SchoolDate.TryParse(c.ChangeDate, out var day)
            && Visible(c)
            && (range == ChangeRange.All || range == ChangeRange.Today && day >= today || range == ChangeRange.Week && day >= weekStart && day < weekStart.AddDays(7)))
            .OrderBy(c => c.ChangeDate, StringComparer.Ordinal).ThenBy(c => c.Period, StringComparer.Ordinal).ThenBy(c => c.DisplayClassName, StringComparer.Ordinal);

    public static ScheduleChange? EffectiveChange(IEnumerable<ScheduleChange> changes)
    {
        var rows = changes.ToArray();
        return rows.LastOrDefault(c => c.IsMakeup) ?? rows.LastOrDefault();
    }

    private bool Visible(string className, params string[] subjects) => international ||
        !subjects.Any(s => DisplayText.IsInternational(s) || internationalRule?.Invoke(s, className, null) == true);
    private bool Visible(ScheduleChange change)
    {
        int? year = SchoolDate.TryParse(change.ChangeDate, out var day) ? day.SchoolYear() : null;
        return international || !new[] { change.BeforeSubject, change.AfterSubject }.Any(s =>
            DisplayText.IsInternational(s) || internationalRule?.Invoke(s, change.DisplayClassName, year) == true);
    }

    public IReadOnlyList<ScheduleBlock> Blocks(DateOnly day, string className)
    {
        var result = new List<ScheduleBlock>();
        for (var period = 1; period <= 8; period++)
        {
            var slot = Slot(day, period, className);
            var visibleChanges = slot.Changes.Where(Visible).ToArray();
            if (visibleChanges.Length == 0)
            {
                foreach (var lesson in slot.BaseLessons.Where(l => Visible(className, l.Names.Subject, l.Names.SubjectFullName ?? "")))
                {
                    var index = result.FindLastIndex(block => block.EndPeriod == period - 1 && block.Content is NormalContent old
                        && SameNames(old.Names, lesson.Names));
                    if (index >= 0) result[index] = result[index] with { EndPeriod = period };
                    else result.Add(new(period, period, new NormalContent(lesson)));
                }
                foreach (var special in slot.SpecialLessons.Where(l => Visible(className, l.Names.Subject)))
                {
                    var index = result.FindLastIndex(block => block.EndPeriod == period - 1 && block.Content is SpecialContent old
                        && old.Kind == special.Kind && SameNames(old.Names, special.Names));
                    if (index >= 0) result[index] = result[index] with { EndPeriod = period };
                    else result.Add(new(period, period, special));
                }
            }
            var change = EffectiveChange(visibleChanges);
            if (change is not null)
            {
                var index = result.FindLastIndex(block => block.EndPeriod == period - 1 && block.Content is ChangeContent old && old.Change == change);
                if (index >= 0) result[index] = result[index] with { EndPeriod = period };
                else result.Add(new(period, period, new ChangeContent(change)));
            }
        }
        return result;
    }
    private static bool SameNames(LessonNames a, LessonNames b) => a.Subject == b.Subject && a.Teacher == b.Teacher && a.Room == b.Room;

    public static IReadOnlyList<PositionedBlock> Positioned(IEnumerable<ScheduleBlock> blocks)
    {
        var result = new List<PositionedBlock>();
        var ends = new List<int>();
        foreach (var block in blocks.OrderBy(b => b.StartPeriod))
        {
            var lane = ends.FindIndex(end => end < block.StartPeriod);
            if (lane < 0) { lane = ends.Count; ends.Add(block.EndPeriod); }
            else ends[lane] = block.EndPeriod;
            result.Add(new(block, lane));
        }
        return result;
    }

    public IReadOnlyList<DateOnly> DisplayedDays(DateOnly weekStart, IReadOnlyList<string> classes) => Enumerable.Range(0, 7)
        .Select(weekStart.AddDays).Where(day => day.SchoolWeekday() <= 5 || Plan(day).WeekendEventLabels.Count > 0
            || classes.Any(c => Blocks(day, c).Count > 0)).ToArray();

    public string? FullDayEventTitle(DateOnly day, IReadOnlyList<string> classes)
    {
        var plan = Plan(day);
        if ((!plan.IsNoClass && plan.FullDayLabels.Count == 0) || classes.Any(c => Blocks(day, c).Count > 0)) return null;
        return plan.FullDayLabels.Count == 0 ? "授業なし" : string.Join("・", plan.FullDayLabels);
    }

    public TimeRange? SlotTime(DateOnly day, string className, int period)
    {
        var slot = Slot(day, period, className);
        var changed = slot.Changes.Any(Visible);
        if (changed && !HasRequiredSpecialSources(day, className)) return null;
        var visibleSpecials = changed ? slot.SpecialLessons.ToArray()
            : slot.SpecialLessons.Where(item => Visible(className, item.Names.Subject)).ToArray();
        if (visibleSpecials.Length > 0) return UniqueTime(visibleSpecials.Select(item =>
            UniqueTime(Specials.Where(s => s.Kind == item.Kind && s.Lessons.Contains(item.Lesson))
                .Select(s => s.DisplayTime(item.Lesson, period, period)))));
        var sourceTimes = Specials.SelectMany(s => s.Lessons.Where(l => l.Date == day.Iso() && l.ClassName == className && l.Period == period)
            .Select(l => s.DisplayTime(l, period, period))).OfType<TimeRange>().Distinct().ToArray();
        if (sourceTimes.Length > 0) return sourceTimes.Length == 1 ? sourceTimes[0] : null;
        var applicable = Specials.Where(s => s.Applies(day, className)).ToArray();
        if (applicable.Length > 0) return UniqueTime(applicable.Select(s => s.PeriodTime(day, period)));
        var plan = Plan(day);
        return plan.ApiTest || plan.ApiTestReturn ? null : data.Times?.Time(day, period) ?? ScheduleTimes.Normal.GetValueOrDefault(period);
    }
    private static TimeRange? UniqueTime(IEnumerable<TimeRange?> values)
    {
        var times = values.ToArray();
        var distinct = times.OfType<TimeRange>().Distinct().ToArray();
        return times.All(t => t is not null) && distinct.Length == 1 ? distinct[0] : null;
    }

    private bool HasRequiredSpecialSources(DateOnly day, string className)
    {
        var plan = Plan(day);
        return (!plan.ApiTest || Specials.Any(source => source.Kind == MaterialKind.Exam && source.Applies(day, className)))
            && (!plan.ApiTestReturn || Specials.Any(source => source.Kind == MaterialKind.ExamReturn && source.Applies(day, className)));
    }

    public TimeRange? CardTime(DateOnly day, string className, ScheduleBlock block)
    {
        if (block.Content is SpecialContent item)
        {
            if (block.StartPeriod == block.EndPeriod && item.Lesson.SpanStart == item.Lesson.SpanEnd) return item.Time;
            var ranges = Specials.Where(s => s.Kind == item.Kind && s.Lessons.Contains(item.Lesson)).Select(source =>
            {
                var clocks = Enumerable.Range(block.StartPeriod, block.EndPeriod - block.StartPeriod + 1).Select(period =>
                    UniqueTime(source.Lessons.Where(l => l.Date == day.Iso() && l.ClassName == className && l.Period == period
                        && SameNames(l.Names, item.Names)).Select(l => source.DisplayTime(l, block.StartPeriod, block.EndPeriod)))).ToArray();
                return clocks.Length > 0 && clocks.All(t => t is not null) ? new TimeRange(clocks[0]!.Start, clocks[^1]!.End) : null;
            });
            return UniqueTime(ranges);
        }
        if (block.Content is ChangeContent)
        {
            if (!HasRequiredSpecialSources(day, className)) return null;
            var applicable = Specials.Where(source => source.Applies(day, className)).ToArray();
            if (applicable.Length > 0) return UniqueTime(applicable.Select(source =>
            {
                var clocks = Enumerable.Range(block.StartPeriod, block.EndPeriod - block.StartPeriod + 1).Select(period =>
                {
                    var lessons = source.Lessons.Where(lesson => lesson.Date == day.Iso() && lesson.ClassName == className && lesson.Period == period).ToArray();
                    return lessons.Length == 0 ? source.PeriodTime(day, period)
                        : UniqueTime(lessons.Select(lesson => source.DisplayTime(lesson, block.StartPeriod, block.EndPeriod)));
                }).ToArray();
                return clocks.All(clock => clock is not null) ? new TimeRange(clocks[0]!.Start, clocks[^1]!.End) : null;
            }));
        }
        var first = SlotTime(day, className, block.StartPeriod);
        var last = SlotTime(day, className, block.EndPeriod);
        return first is not null && last is not null ? new(first.Start, last.End) : null;
    }
    public TimeRange? CommonPeriodTime(int period, IReadOnlyList<DateOnly> days, IReadOnlyList<string> classes)
    {
        var times = new List<TimeRange?>();
        foreach (var day in days)
            foreach (var className in classes)
                if (Blocks(day, className).Any(b => b.StartPeriod <= period && b.EndPeriod >= period)) times.Add(SlotTime(day, className, period));
        return UniqueTime(times);
    }
    public IReadOnlyList<TimeRange?> ChangeTimes(ScheduleChange change)
    {
        if (!SchoolDate.TryParse(change.ChangeDate, out var day) || change.DetailPeriods is not { } periods) return [];
        var ranges = new List<(int Start, int End)>();
        foreach (var period in periods)
        {
            if (ranges.Count > 0 && ranges[^1].End + 1 == period) ranges[^1] = (ranges[^1].Start, period);
            else ranges.Add((period, period));
        }
        return ranges.Select(range => CardTime(day, change.DisplayClassName, new(range.Start, range.End, new ChangeContent(change)))).ToArray();
    }
    private IReadOnlyList<TimeRange?> ActivityTimes(DateOnly day, string className, ScheduleBlock block)
    {
        var item = block.Content as SpecialContent;
        var sources = (item is null ? Specials.Where(source => source.Applies(day, className))
            : Specials.Where(source => source.Kind == item.Kind && source.Lessons.Contains(item.Lesson))).ToArray();
        if (sources.Length == 0) return item is not null ? [] :
            Enumerable.Range(block.StartPeriod, block.EndPeriod - block.StartPeriod + 1)
                .Select(period => data.Times?.Time(day, period) ?? ScheduleTimes.Normal.GetValueOrDefault(period)).ToArray();
        HashSet<TimeRange>? agreed = null;
        foreach (var source in sources)
        {
            var intervals = new HashSet<TimeRange>();
            foreach (var period in Enumerable.Range(block.StartPeriod, block.EndPeriod - block.StartPeriod + 1))
            {
                var lessons = source.Lessons.Where(lesson => lesson.Date == day.Iso() && lesson.ClassName == className && lesson.Period == period
                    && (item is null || SameNames(lesson.Names, item.Names))).ToArray();
                var values = lessons.Length == 0 && item is null ? new[] { source.PeriodTime(day, period) }
                    : lessons.Select(lesson => lesson.RecordedTime is null ? source.PeriodTime(day, period)
                        : source.DisplayTime(lesson, block.StartPeriod, block.EndPeriod)).ToArray();
                if (values.Length == 0 || values.Any(value => value is null)) return [];
                intervals.UnionWith(values.OfType<TimeRange>());
            }
            if (agreed is not null && !agreed.SetEquals(intervals)) return [];
            agreed = intervals;
        }
        return agreed?.Cast<TimeRange?>().ToArray() ?? [];
    }

    public bool IsInProgress(DateOnly day, string className, ScheduleBlock block, DateTimeOffset now)
    {
        if (block.Content is ChangeContent { Change.IsCancellation: true } || day != SchoolDate.InJapan(now)) return false;
        var clocks = block.Content is NormalContent
            ? Enumerable.Range(block.StartPeriod, block.EndPeriod - block.StartPeriod + 1)
                .Select(period => data.Times?.Time(day, period) ?? ScheduleTimes.Normal.GetValueOrDefault(period))
            : CardTime(day, className, block) is null ? [] : ActivityTimes(day, className, block);
        var japan = now.ToOffset(TimeSpan.FromHours(9));
        var minute = japan.Hour * 60 + japan.Minute;
        return clocks.Any(time => time is not null && TimeRange.TryMinutes(time.Start, out var start)
            && TimeRange.TryMinutes(time.End, out var end) && start < end && start <= minute && minute < end);
    }

    public IReadOnlyList<string> MissingMessages(DateOnly day, string className)
    {
        var plan = Plan(day);
        var result = new List<string>();
        if (plan.ApiTest && !Specials.Any(s => s.Kind == MaterialKind.Exam && s.Applies(day, className))) result.Add("試験時間割：未公開または未解析です");
        if (plan.ApiTestReturn && !Specials.Any(s => s.Kind == MaterialKind.ExamReturn && s.Applies(day, className))) result.Add("試験返却時間割：未公開または未解析です");
        if (!(plan.IsNoClass || plan.IsSupplementary || plan.ApiTest || plan.ApiTestReturn || Specials.Any(s => s.Applies(day, className))))
        {
            if (data.Timetable is null) result.Add("通常時間割の解析結果がありません。");
            else if (data.Timetable.ApplicableRange is not { } range) result.Add("通常時間割の学期を確認できません。再解析してください。");
            else if (!range.Contains(day)) result.Add("この日に適用できる通常時間割がありません。");
            else if (!data.Timetable.Lessons.Any(l => l.ClassName == className)) result.Add("このクラスの通常時間割がありません。");
        }
        return result;
    }
    public (DateOnly Lower, DateOnly Upper) ReachableWeeks(DateOnly reference, IReadOnlyList<string> classes)
    {
        var first = reference.Month is >= 4 and <= 9;
        var year = first ? reference.Year : reference.Month >= 10 ? reference.Year : reference.Year - 1;
        var start = new DateOnly(year, first ? 4 : 10, 1);
        var end = new DateOnly(year + (first ? 0 : 1), first ? 10 : 4, 1).AddDays(-1);
        var upper = end.Monday() > reference.DisplayWeekStart() ? end.Monday() : reference.DisplayWeekStart();
        while (upper.Year < 9999 && upper <= DateOnly.MaxValue.AddDays(-13) && HasWeekData(upper.AddDays(7), classes)) upper = upper.AddDays(7);
        return (start.Monday(), upper);
    }
    private bool HasWeekData(DateOnly start, IReadOnlyList<string> classes) => classes.Count > 0 && Enumerable.Range(0, 7).Select(start.AddDays)
        .Any(day => Plan(day).Events.Count > 0 || classes.Any(c => Enumerable.Range(1, 8).Any(p =>
        {
            var slot = Slot(day, p, c);
            return slot.Changes.Count > 0 || slot.SpecialLessons.Count > 0 || slot.BaseLessons.Count > 0;
        })));
}
