import Foundation

// Completely fictional inputs. Compile with the pinned, unmodified iOS sources.
func change(_ date: String, period: String, note: String, subject: String = "架空科目B") -> ScheduleChange {
    ScheduleChange(change_date: date, class_name: "1_CN", period: period, before_subject: "架空科目A", after_subject: subject,
        teacher: "架空教員B", room: "", note: note, raw_text: "架空原文B", canonical_text: "架空正規化B")
}
func clock(_ value: String?) -> Any { value.map { $0.replacingOccurrences(of: "〜", with: "～") as Any } ?? NSNull() }
func changeJSON(_ row: ScheduleChange) -> [String: Any] {
    ["changeDate": row.change_date, "className": row.class_name, "period": row.period, "beforeSubject": row.before_subject,
     "afterSubject": row.after_subject, "teacher": row.teacher, "room": row.room, "note": row.note, "rawText": row.raw_text, "canonicalText": row.canonical_text]
}
func encoded<T: Encodable>(_ value: T) throws -> Any { try JSONSerialization.jsonObject(with: JSONEncoder().encode(value)) }
@main struct ExportParity {
    static func main() throws {
        let parsed = Date(timeIntervalSince1970: 0)
        var cases: [[String: Any]] = []
        let tags = ["", "授業なし", "補講日", "行事（授業なし）", "行事（授業あり）", "行事", "行事（時間割変更）", "テスト", "テスト返却", "行事メモ", "曜日振替"]
        for tag in tags {
            for include in [false, true] {
                for specialMode in 0...3 {
                    let day = SchoolDate(iso8601: "2032-04-05")!
                    let lessons = (1...5).flatMap { weekday in (1...3).map { period in
                        PDFLesson(className: "1_CN", weekday: weekday, period: period, names: .init(subject: period == 3 ? "留 架空科目C" : "架空科目A", teacher: "架空教員A", room: ""), sourceText: "架空原文A", page: 1)
                    }}
                    let normal = PDFAnalysis(kind: .timetable, sourceDigest: "fictional", sourceName: "fictional.pdf", parsedAt: parsed,
                        schoolYear: 2032, term: "前期", lessons: lessons, events: [], notices: [])
                    let rows = [change(day.iso8601, period: "1,2", note: "補講"), change(day.iso8601, period: "2", note: "休講", subject: ""),
                                change(day.iso8601, period: "1,3", note: "変更"), change(day.addingDays(5)!.iso8601, period: "4", note: "補講")]
                    let changes = ChangeAnalysis(sourceDigest: "fictional", sourceName: "fictional.xlsx", defaultYear: 2032, parsedAt: parsed, records: rows)
                    let title = tag == "曜日振替" ? "火曜日授業" : "架空行事A"
                    let classification = PDFEventClassification(type: tag == "授業なし" ? .noClass : tag == "補講日" ? .supplementary : tag == "行事（授業なし）" ? .schoolEventNoClass : tag == "曜日振替" ? .weekdayOverride : .special, scheduleDay: tag == "曜日振替" ? 2 : nil)
                    let eventRows = tag.isEmpty ? [] : [PDFSchoolEvent(date: day.iso8601, scope: "全クラス", title: title, page: 0, classification: classification, apiTag: tag)]
                    let events = PDFAnalysis(kind: .events, sourceDigest: "fictional", sourceName: "fictional", parsedAt: parsed, schoolYear: 2032, term: nil, lessons: [], events: eventRows, notices: [])
                    let kinds: [SpecialScheduleKind] = specialMode == 0 ? [] : specialMode == 1 ? [.exam] : specialMode == 2 ? [.examReturn] : [.exam, .examReturn]
                    let specials = kinds.map { kind in
                        SpecialScheduleAnalysis(kind: kind, sourceDigest: "fictional", sourceName: "fictional.pdf", parsedAt: parsed, schoolYear: 2032,
                            coveredDates: [day.iso8601, day.addingDays(1)!.iso8601], coveredClasses: ["1_CN"], periodTimes: [1: "09:10〜09:30", 2: kind == .exam ? "09:30〜09:50" : "09:40〜10:00"],
                            lessons: (1...2).map { SpecialScheduleLesson(date: day.iso8601, className: "1_CN", period: $0, spanStart: $0, spanEnd: $0, timeRange: nil, lines: ["架空特別科目A", "架空教員C", ""], page: 1) })
                    }
                    let schedule = TimetableDaySchedule(timetable: normal, changes: changes, events: events, specials: specials, includesChanges: include)
                    let positioned = TimetableSchedule.positioned(schedule.blocks(on: day, className: "1_CN", international: false))
                    let blocks: [[String: Any]] = positioned.map { item in
                        let block = item.block
                        let kind: String; let subject: String
                        switch block.content { case .normal(let lesson): kind = "normal"; subject = lesson.names.subject
                            case .special(let special): kind = special.kind.rawValue; subject = special.lesson.subject
                            case .change(let row): kind = "change"; subject = row.after_subject }
                        return ["start": block.startPeriod, "end": block.endPeriod, "lane": item.lane, "kind": kind, "subject": subject,
                                "time": clock(schedule.cardTime(block, on: day, className: "1_CN"))]
                    }
                    let bounds = TimetableSchedule.reachableWeekBounds(containing: day, classes: ["1_CN"], timetable: normal, changes: changes, events: events, includesChanges: include, specials: specials)
                    let displayed = TimetableSchedule.displayedDays(weekStart: day.monday, classes: ["1_CN"], timetable: normal, changes: changes, events: events, includesChanges: include, isInternationalStudent: false, specials: specials)
                    let specialJSON = specials.map { special -> [String: Any] in
                        let ranges = special.periodTimes.mapValues { value -> [String: String] in let parts = value.components(separatedBy: "〜"); return ["start": parts[0], "end": parts[1]] }
                        return ["kind": special.kind.rawValue, "schoolYear": special.schoolYear, "coveredDates": special.coveredDates, "coveredClasses": special.coveredClasses,
                            "periodTimes": Dictionary(uniqueKeysWithValues: ranges.map { (String($0.key), $0.value) }),
                            "lessons": special.lessons.map { lesson -> [String: Any] in ["date": lesson.date, "className": lesson.className, "period": lesson.period, "spanStart": lesson.spanStart, "spanEnd": lesson.spanEnd, "lines": lesson.lines, "page": lesson.page] }]
                    }
                    let eventJSON: [[String: Any]] = eventRows.map { row in ["date": row.date, "title": row.title, "tag": tag,
                        "classification": tag == "授業なし" ? "noClass" : tag == "補講日" ? "supplementary" : tag == "行事（授業なし）" ? "schoolEventNoClass" : tag == "曜日振替" ? "weekdayOverride" : "none", "scheduleDay": tag == "曜日振替" ? 2 : NSNull()] }
                    let input: [String: Any] = ["timetable": try encoded(normal), "changes": rows.map(changeJSON), "specials": specialJSON, "events": eventJSON]
                    let expected: [String: Any] = ["blocks": blocks, "lower": bounds.lowerBound.iso8601, "upper": bounds.upperBound.iso8601,
                        "days": displayed.map(\.iso8601), "missingCount": schedule.missingMessages(on: day, className: "1_CN").count,
                        "commonTimes": (1...8).map { clock(schedule.commonPeriodTime($0, days: displayed, classes: ["1_CN"], international: false)) },
                        "changeTimes": rows.map { schedule.changeTimeRanges($0).map(clock) }]
                    cases.append(["id": "tag-\(tag.isEmpty ? "empty" : tag)-changes-\(include)-special-\(specialMode)", "day": day.iso8601, "className": "1_CN", "includesChanges": include, "input": input, "expected": expected])
                }
            }
        }
        let strings = ["架空ｶﾅ・科目\nA", "（架空教員A）", "架空ガパA", "留 架空科目A", "架空科目A（内訳）"]
        let text = strings.map { ["source": $0, "continuous": TimetableDisplayText.continuous($0), "kana": TimetableDisplayText.kana($0), "compact": TimetableDisplayText.halfwidthKana($0)] }
        let queries = ["かくう", "kakuu", "ＫＡＫＵＵ", "架空", "unknown", "", "がっこう", "gakko", "しゃしん"]
        let terms = "架空|かくう|がっこう|しゃしん"
        let search = queries.map { ["query": $0, "terms": terms, "score": LinkSearch.score(terms: terms, query: $0), "normalize": LinkSearch.normalize($0), "romaji": LinkSearch.romaji($0)] as [String: Any] }
        let headers = ["学 年", "学科・クラス", "月日", "時限", "変更前", "変更後", "教員", "教室", "備考"]
        let changeInputs = [
            [headers, ["1", "CN", "4/5", "1", "架空科目A", "架空科目B", "架空教員A", "", "変更"]],
            [headers, ["1〜3", "CN、ES", "1/10", "1,3", "架空科目A", "", "", "", "休講"]],
            [headers, ["AI", "1", "2032/4/5", "2", "", "架空科目B", "", "", "補講"]],
            [headers, ["1", "AI", "4/5", "1", "", "架空科目B", "", "", "補講"]],
            [headers, ["3-1", "IT", "2032/4/5", "1~2", "", "架空科目B", "", "", "補講"]],
            [headers, ["1", "CN", "4/5", "1", "A", "B", "", "", ""], ["1", "ES", "4/5", "1", "A", "B", "", "", ""], ["1", "全", "4/6", "2", "A", "C", "", "", "変更"]],
            [headers, ["1", "全", "4/5", "1", "A", "B", "", "", ""]],
            [headers, ["0", "CN", "4/5", "1", "A", "B", "", "", ""]],
            [headers, ["1", "CN", "2/30", "1", "A", "B", "", "", ""]],
            [headers, ["1", "CN", "4/5", "1", "A", "B", "", "", "", "余分なセル"]],
            [headers]
        ]
        let normalizer = changeInputs.enumerated().map { index, rows -> [String: Any] in
            let expected: [String: Any]
            do { expected = ["records": try ChangeNormalizer.parse(rows, defaultYear: 2032).map(changeJSON)] }
            catch let error as ChangeParseError { expected = ["error": error.code.rawValue, "row": error.row.map { $0 as Any } ?? NSNull()] }
            catch { fatalError("Unexpected reference error") }
            return ["id": "changes-\(index)", "rows": rows, "defaultYear": 2032, "expected": expected]
        }
        let subjects = [["架空科目A", "架空教員A", ""], ["科A・科B", "教A・教B", "室A・"], ["科A・科B", "教A・教B", "室A"], ["・科B", "教A・教B", "室A・室B"], ["科A", "教A", "（室A）"]]
        let pdf = subjects.enumerated().map { index, cells -> [String: Any] in
            var glyphs = [PDFGlyph(text: "令和14年度前期時間割", x: 30, y: 20, width: 100, height: 10, sourceLine: 0, sourceOrder: 100)]
            for i in 0..<40 { glyphs.append(PDFGlyph(text: String(i % 8 + 1), x: Double(42 + i * 10), y: 70, width: 5, height: 10, sourceLine: 1, sourceOrder: i)) }
            glyphs.append(contentsOf: [PDFGlyph(text: "CN", x: 25, y: 110, width: 8, height: 10, sourceLine: 2, sourceOrder: 40), PDFGlyph(text: "1", x: 5, y: 110, width: 8, height: 10, sourceLine: 3, sourceOrder: 41)])
            for i in 0..<3 { glyphs.append(PDFGlyph(text: cells[i], x: 41, y: Double(96 + i * 12), width: 8, height: 8, sourceLine: 4 + i, sourceOrder: 42 + i)) }
            var rules = [PDFRule(x1: 0, y1: 60, x2: 440, y2: 60), PDFRule(x1: 0, y1: 90, x2: 440, y2: 90), PDFRule(x1: 0, y1: 140, x2: 440, y2: 140), PDFRule(x1: 0, y1: 60, x2: 0, y2: 140), PDFRule(x1: 20, y1: 60, x2: 20, y2: 140)]
            for i in 0...40 { rules.append(PDFRule(x1: Double(40 + i * 10), y1: 60, x2: Double(40 + i * 10), y2: 140)) }
            let page = PDFPageLayout(width: 500, height: 500, glyphs: glyphs, lines: rules)
            let expected: [String: Any]
            do { let parsed = try PDFSchoolParser.parse([page], kind: .timetable, digest: "fictional", name: "fictional.pdf"); expected = ["schoolYear": parsed.schoolYear, "term": parsed.term!, "lessons": try encoded(parsed.lessons)] }
            catch let error as PDFParseError { expected = ["error": error.stage!.label.components(separatedBy: "（")[1].components(separatedBy: "）")[0]] }
            catch { fatalError("Unexpected reference error") }
            return ["id": "pdf-\(index)", "pages": try! encoded([page]), "expected": expected]
        }
        let result: [String: Any] = ["schemaVersion": 1, "iosCommit": "bbd2bd7b7606e2135279d4b90bb61fd4e80fe501", "schedule": cases, "changeNormalizer": normalizer, "pdf": pdf, "text": text, "search": search,
            "classes": TimetableSchedule.selectableClasses.sorted(), "colors": MainColor.allCases.map { ["key": $0.rawValue, "label": $0.title] }]
        let data = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys, .prettyPrinted, .withoutEscapingSlashes])
        try data.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
    }
}
