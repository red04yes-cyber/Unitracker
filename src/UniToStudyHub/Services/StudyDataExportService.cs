using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public enum StudyExportFormat
{
    Csv,
    Xlsx,
    Json
}

public sealed record StudyExportRequest(
    DateOnly From,
    DateOnly To,
    string TimeZoneId = "",
    IReadOnlyCollection<string>? CourseIds = null,
    IReadOnlyCollection<StudyActivityType>? ActivityTypes = null,
    bool IncludePomodoroCycles = true,
    bool IncludePrivateNotes = false,
    bool IncludeNotes = true,
    bool IncludeRatings = true,
    bool IncludeTags = true,
    bool IncludeResult = true,
    bool IncludeDeleted = false);

public sealed record StudyExportFile(string FileName, string ContentType, byte[] Content);

public sealed record StudyExportBundle(
    StudyExportFormat Format,
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<StudyExportFile> Files);

/// <summary>
/// Builds deterministic exports from the persisted StudyData. Exports are data
/// selections, not a second database: the caller writes the returned bytes.
/// Private notes and secrets are removed before any serializer sees the rows.
/// </summary>
public sealed class StudyDataExportService
{
    public const string SchemaVersion = "1.0";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string JsonContentType = "application/json; charset=utf-8";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StudyExportBundle Export(
        StudyData data,
        StudyExportRequest request,
        StudyExportFormat format,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var generatedAt = now ?? DateTimeOffset.Now;
        var selection = Select(data, request, generatedAt);
        return format switch
        {
            StudyExportFormat.Csv => new StudyExportBundle(format, SchemaVersion, generatedAt, BuildCsv(selection, request)),
            StudyExportFormat.Xlsx => new StudyExportBundle(format, SchemaVersion, generatedAt,
                new[] { BuildXlsx(selection, request, generatedAt) }),
            StudyExportFormat.Json => new StudyExportBundle(format, SchemaVersion, generatedAt,
                new[] { BuildJson(selection, request, generatedAt) }),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static ExportSelection Select(StudyData data, StudyExportRequest request, DateTimeOffset now)
    {
        if (request.To < request.From)
            throw new ArgumentOutOfRangeException(nameof(request), "The end date must not precede the start date.");
        if (request.To.DayNumber - request.From.DayNumber > 3660)
            throw new ArgumentOutOfRangeException(nameof(request), "The export range is limited to 3661 days.");
        var zone = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? TimeZoneInfo.Local
            : ResolveTimeZone(request.TimeZoneId);
        var rangeStart = StartOfDay(request.From, zone);
        var rangeEnd = StartOfDay(request.To.AddDays(1), zone);
        var courses = request.CourseIds?.ToHashSet(StringComparer.Ordinal);
        var types = request.ActivityTypes?.Count > 0 ? request.ActivityTypes.ToHashSet() : null;

        var activities = data.StudyActivities
            .Where(x => request.IncludeDeleted || !x.IsDeleted)
            .Where(x => x.StartedAt < rangeEnd && (x.EndedAt ?? now) > rangeStart)
            .Where(x => courses is null || courses.Count == 0 ||
                        (!string.IsNullOrWhiteSpace(x.CourseId) && courses.Contains(x.CourseId)))
            .Where(x => types is null || types.Contains(x.Type))
            .OrderBy(x => x.StartedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var sessions = data.StudySessions
            .Where(x => x.StartedAt < rangeEnd && (x.EndedAt ?? now) > rangeStart)
            .Where(x => courses is null || courses.Count == 0 ||
                        (!string.IsNullOrWhiteSpace(x.CourseId) && courses.Contains(x.CourseId)))
            .OrderBy(x => x.StartedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
        var sessionIds = sessions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var activity in activities)
        {
            if (!string.IsNullOrWhiteSpace(activity.SessionId) && sessionIds.Add(activity.SessionId) &&
                data.StudySessions.FirstOrDefault(x => x.Id == activity.SessionId) is { } linked)
                sessions.Add(linked);
        }
        sessions = sessions.OrderBy(x => x.StartedAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();

        var cycles = request.IncludePomodoroCycles
            ? data.PomodoroCycles
                .Where(x => x.FocusStartedAt < rangeEnd && (x.CompletedAt ?? x.FocusEndedAt) > rangeStart)
                .Where(x => courses is null || courses.Count == 0 ||
                            data.StudySessions.FirstOrDefault(s => s.Id == x.SessionId) is { } session &&
                            !string.IsNullOrWhiteSpace(session.CourseId) && courses.Contains(session.CourseId))
                .OrderBy(x => x.FocusStartedAt)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList()
            : new List<PomodoroCycleData>();

        var scopedData = new StudyData
        {
            Courses = data.Courses,
            StudyActivities = new ObservableCollection<StudyActivityData>(activities),
            StudySessions = new ObservableCollection<StudySessionData>(sessions)
        };
        var ledger = new ActivityLedgerService();
        var summary = ledger.Summarize(scopedData, request.From, request.To, now, zone.Id);
        return new ExportSelection(activities, sessions, cycles, summary, zone, request, now);
    }

    private static IReadOnlyList<StudyExportFile> BuildCsv(ExportSelection selection, StudyExportRequest request)
    {
        var files = new List<StudyExportFile>
        {
            new("activities.csv", CsvContentType, CsvBytes(ActivityTable(selection, request))),
            new("sessions.csv", CsvContentType, CsvBytes(SessionTable(selection, request))),
            new("segments.csv", CsvContentType, CsvBytes(SegmentTable(selection))),
            new("summary.csv", CsvContentType, CsvBytes(SummaryTable(selection)))
        };
        if (request.IncludePomodoroCycles)
            files.Add(new("pomodoro-cycles.csv", CsvContentType, CsvBytes(PomodoroTable(selection))));
        return files;
    }

    private static StudyExportFile BuildJson(ExportSelection selection, StudyExportRequest request, DateTimeOffset generatedAt)
    {
        var document = new
        {
            schemaVersion = SchemaVersion,
            generatedAt,
            filters = FilterDocument(request),
            summary = new
            {
                recordedSeconds = selection.Summary.RecordedSeconds,
                overlapSeconds = selection.Summary.OverlapSeconds,
                activityCount = selection.Summary.ActivityCount,
                byType = selection.Summary.ByType.ToDictionary(x => x.Key.ToString(), x => x.Value),
                byCourse = selection.Summary.ByCourse,
                byTopic = selection.Summary.ByTopic
            },
            activities = selection.Activities.Select(x => new
            {
                x.Id,
                type = x.Type.ToString(),
                x.Title,
                x.CourseId,
                x.SyllabusTopicId,
                x.TaskId,
                x.MissionId,
                x.SessionId,
                x.StartedAt,
                x.EndedAt,
                x.TimeZoneId,
                x.EffectiveSeconds,
                origin = x.Origin.ToString(),
                status = x.Status.ToString(),
                result = request.IncludeResult ? x.Result : null,
                ratings = request.IncludeRatings
                    ? new { x.FocusRating, x.DifficultyRating, x.QualityRating, x.EnergyRating }
                    : null,
                note = NoteForExport(x, request),
                tags = request.IncludeTags ? x.Tags : null,
                x.IsPrivate,
                x.Revision,
                x.DeletedAt
            }),
            sessions = selection.Sessions.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.Topic,
                x.PlannedMinutes,
                x.StartedAt,
                x.EndedAt,
                state = x.State.ToString(),
                x.AccumulatedFocusSeconds,
                x.AccumulatedPauseSeconds,
                outcome = request.IncludeResult ? x.Outcome : null,
                notes = SessionNotesForExport(x, request),
                notesArePrivate = x.NotesArePrivate,
                segments = selection.Segments
                    .Where(s => s.SessionId == x.Id)
                    .Select(s => new
                    {
                        s.Segment.Id,
                        type = s.Segment.Type.ToString(),
                        s.Segment.StartedAt,
                        s.Segment.EndedAt,
                        durationSeconds = s.DurationSeconds
                    })
            }),
            segments = selection.Segments.Select(x => new
            {
                x.SessionId,
                x.Segment.Id,
                type = x.Segment.Type.ToString(),
                x.Segment.StartedAt,
                x.Segment.EndedAt,
                x.DurationSeconds
            }),
            pomodoroCycles = request.IncludePomodoroCycles
                ? selection.Cycles.Select(x => new
                {
                    x.Id,
                    x.SessionId,
                    x.CycleNumber,
                    x.FocusStartedAt,
                    x.FocusEndedAt,
                    x.FocusSeconds,
                    breakType = x.BreakType.ToString(),
                    x.BreakStartedAt,
                    x.BreakEndedAt,
                    x.BreakSeconds,
                    x.WasSkipped,
                    x.IsCompleted,
                    x.CompletedAt
                })
                : null
        };
        return new StudyExportFile("unitracker-export.json", JsonContentType,
            JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions));
    }

    private static StudyExportFile BuildXlsx(ExportSelection selection, StudyExportRequest request, DateTimeOffset generatedAt)
    {
        var sheets = new List<XlsxSheet>
        {
            SheetFromTable("Attività", ActivityTable(selection, request)),
            SheetFromTable("Sessioni", SessionTable(selection, request)),
            SheetFromTable("Segmenti", SegmentTable(selection)),
            new("Riepilogo", new[] { "Metrica", "Valore" }, new[]
            {
                new object?[] { "Schema", SchemaVersion },
                new object?[] { "Generato il", generatedAt },
                new object?[] { "Generato il (ISO)", generatedAt.ToString("O", CultureInfo.InvariantCulture) },
                new object?[] { "Dal", selection.Request.From },
                new object?[] { "Al", selection.Request.To },
                new object?[] { "Secondi unici", selection.Summary.RecordedSeconds },
                new object?[] { "Secondi sovrapposti", selection.Summary.OverlapSeconds },
                new object?[] { "Attività", selection.Summary.ActivityCount },
                new object?[] { "Minuti per tipo", string.Join("; ", selection.Summary.ByType.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value / 60}")) },
                new object?[] { "Minuti per materia", string.Join("; ", selection.Summary.ByCourse.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value / 60}")) },
                new object?[] { "Minuti per argomento", string.Join("; ", selection.Summary.ByTopic.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value / 60}")) }
            }, new[] { 0, 0 })
        };
        if (request.IncludePomodoroCycles)
            sheets.Add(SheetFromTable("Pomodoro", PomodoroTable(selection)));
        return new StudyExportFile("unitracker-export.xlsx", XlsxContentType, XlsxBytes(sheets));
    }

    private static XlsxSheet SheetFromTable(string name, ExportTable table)
    {
        if (table.InstantColumns.Count == 0)
            return new XlsxSheet(name, table.Headers, table.Rows, table.Styles);

        var headers = table.Headers
            .Concat(table.InstantColumns.Select(index => table.Headers[index] + " ISO"))
            .ToArray();
        var styles = table.Styles
            .Concat(table.InstantColumns.Select(_ => 0))
            .ToArray();
        var rows = table.Rows.Select(row => row
            .Concat(table.InstantColumns.Select(index => row[index] is DateTimeOffset instant
                ? (object?)instant.ToString("O", CultureInfo.InvariantCulture)
                : string.Empty))
            .ToArray()).ToList();
        return new XlsxSheet(name, headers, rows, styles);
    }

    private static ExportTable ActivityTable(ExportSelection selection, StudyExportRequest request)
    {
        var columns = new List<ExportColumn<StudyActivityData>>
        {
            new("ID", x => x.Id),
            new("Tipo", x => x.Type.ToString()),
            new("Titolo", x => x.Title),
            new("Materia", x => x.CourseId),
            new("Argomento", x => x.SyllabusTopicId),
            new("Task", x => x.TaskId),
            new("Missione", x => x.MissionId),
            new("Sessione", x => x.SessionId),
            new("Inizio", x => x.StartedAt, 1, IsInstant: true),
            new("Fine", x => x.EndedAt, 1, IsInstant: true),
            new("Secondi effettivi", x => x.EffectiveSeconds, 2),
            new("Origine", x => x.Origin.ToString()),
            new("Stato", x => x.Status.ToString())
        };
        if (request.IncludeResult) columns.Add(new("Risultato", x => x.Result));
        if (request.IncludeRatings)
        {
            columns.Add(new("Focus 1-5", x => x.FocusRating));
            columns.Add(new("Difficoltà 1-5", x => x.DifficultyRating));
            columns.Add(new("Qualità 1-5", x => x.QualityRating));
            columns.Add(new("Energia 1-5", x => x.EnergyRating));
        }
        if (request.IncludeNotes) columns.Add(new("Nota", x => NoteForExport(x, request)));
        if (request.IncludeTags) columns.Add(new("Tag", x => string.Join(", ", x.Tags)));
        columns.Add(new("Privata", x => x.IsPrivate));
        columns.Add(new("Revisione", x => x.Revision));
        columns.Add(new("Eliminata il", x => x.DeletedAt, 1, IsInstant: true));
        return Table(columns, selection.Activities);
    }

    private static ExportTable SessionTable(ExportSelection selection, StudyExportRequest request)
    {
        var columns = new List<ExportColumn<StudySessionData>>
        {
            new("ID", x => x.Id),
            new("Materia", x => x.CourseId),
            new("Argomento", x => x.Topic),
            new("Minuti pianificati", x => x.PlannedMinutes),
            new("Inizio", x => x.StartedAt, 1, IsInstant: true),
            new("Fine", x => x.EndedAt, 1, IsInstant: true),
            new("Stato", x => x.State.ToString()),
            new("Secondi focus", x => x.AccumulatedFocusSeconds, 2),
            new("Secondi pausa", x => x.AccumulatedPauseSeconds, 2)
        };
        if (request.IncludeResult) columns.Add(new("Esito", x => x.Outcome));
        if (request.IncludeNotes) columns.Add(new("Note", x => SessionNotesForExport(x, request)));
        columns.Add(new("Note private", x => x.NotesArePrivate));
        return Table(columns, selection.Sessions);
    }

    private static ExportTable SegmentTable(ExportSelection selection) =>
        Table(new[]
        {
            new ExportColumn<SegmentRow>("Sessione", x => x.SessionId),
            new ExportColumn<SegmentRow>("Segmento", x => x.Segment.Id),
            new ExportColumn<SegmentRow>("Tipo", x => x.Segment.Type.ToString()),
            new ExportColumn<SegmentRow>("Inizio", x => x.Segment.StartedAt, 1, IsInstant: true),
            new ExportColumn<SegmentRow>("Fine", x => x.Segment.EndedAt, 1, IsInstant: true),
            new ExportColumn<SegmentRow>("Secondi", x => x.DurationSeconds, 2)
        }, selection.Segments);

    private static ExportTable PomodoroTable(ExportSelection selection) =>
        Table(new[]
        {
            new ExportColumn<PomodoroCycleData>("ID", x => x.Id),
            new ExportColumn<PomodoroCycleData>("Sessione", x => x.SessionId),
            new ExportColumn<PomodoroCycleData>("Ciclo", x => x.CycleNumber),
            new ExportColumn<PomodoroCycleData>("Focus inizio", x => x.FocusStartedAt, 1, IsInstant: true),
            new ExportColumn<PomodoroCycleData>("Focus fine", x => x.FocusEndedAt, 1, IsInstant: true),
            new ExportColumn<PomodoroCycleData>("Secondi focus", x => x.FocusSeconds, 2),
            new ExportColumn<PomodoroCycleData>("Pausa", x => x.BreakType.ToString()),
            new ExportColumn<PomodoroCycleData>("Pausa inizio", x => x.BreakStartedAt, 1, IsInstant: true),
            new ExportColumn<PomodoroCycleData>("Pausa fine", x => x.BreakEndedAt, 1, IsInstant: true),
            new ExportColumn<PomodoroCycleData>("Secondi pausa", x => x.BreakSeconds, 2),
            new ExportColumn<PomodoroCycleData>("Saltato", x => x.WasSkipped),
            new ExportColumn<PomodoroCycleData>("Completato", x => x.IsCompleted),
            new ExportColumn<PomodoroCycleData>("Completato il", x => x.CompletedAt, 1, IsInstant: true)
        }, selection.Cycles);

    private static ExportTable SummaryTable(ExportSelection selection)
    {
        var rows = new List<object?[]>
        {
            new object?[] { "Secondi unici", selection.Summary.RecordedSeconds },
            new object?[] { "Secondi sovrapposti", selection.Summary.OverlapSeconds },
            new object?[] { "Attività", selection.Summary.ActivityCount }
        };
        foreach (var item in selection.Summary.ByType.OrderBy(x => x.Key))
            rows.Add(new object?[] { $"Tipo: {item.Key}", item.Value });
        foreach (var item in selection.Summary.ByCourse.OrderBy(x => x.Key))
            rows.Add(new object?[] { $"Materia: {item.Key}", item.Value });
        foreach (var item in selection.Summary.ByTopic.OrderBy(x => x.Key))
            rows.Add(new object?[] { $"Argomento: {item.Key}", item.Value });
        return new ExportTable(new[] { "Metrica", "Secondi" }, new[] { 0, 2 }, rows, Array.Empty<int>());
    }

    private static ExportTable Table<T>(IEnumerable<ExportColumn<T>> columns, IEnumerable<T> rows)
    {
        var list = columns.ToList();
        return new ExportTable(
            list.Select(x => x.Header).ToArray(),
            list.Select(x => x.Style).ToArray(),
            rows.Select(row => list.Select(column => column.Value(row)).ToArray()).ToList(),
            list.Select((column, index) => (column, index))
                .Where(x => x.column.IsInstant)
                .Select(x => x.index)
                .ToArray());
    }

    private static byte[] CsvBytes(ExportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', table.Headers.Select(EscapeCsv)));
        foreach (var row in table.Rows)
            builder.AppendLine(string.Join(',', row.Select(EscapeCsv)));
        // Encoding.GetBytes never emits the preamble: prepend the UTF-8 BOM
        // explicitly so Excel reads accents in the CSV files.
        var body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static string EscapeCsv(object? value)
    {
        if (value is null) return string.Empty;
        if (value is DateTimeOffset date)
            return QuoteCsv(date.ToString("O", CultureInfo.InvariantCulture));
        if (value is DateOnly dateOnly)
            return QuoteCsv(dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (value is bool boolean)
            return boolean ? "true" : "false";
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        return QuoteCsv(NeedsFormulaGuard(value.ToString() ?? string.Empty)
            ? "'" + value
            : value.ToString() ?? string.Empty);
    }

    private static string QuoteCsv(string text) =>
        "\"" + text.Replace("\"", "\"\"") + "\"";

    private static bool NeedsFormulaGuard(string text)
    {
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character)) continue;
            return character is '=' or '+' or '-' or '@';
        }
        return false;
    }

    private static byte[] XlsxBytes(IReadOnlyList<XlsxSheet> sheets)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", ContentTypes(sheets.Count));
            AddEntry(archive, "_rels/.rels", RootRelationships());
            AddEntry(archive, "xl/workbook.xml", Workbook(sheets));
            AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Count));
            AddEntry(archive, "xl/styles.xml", Styles());
            for (var index = 0; index < sheets.Count; index++)
                AddEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", Worksheet(sheets[index]));
        }
        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string Worksheet(XlsxSheet sheet)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        AppendRow(builder, 1, sheet.Headers.Select(x => new ExportCell(x, 0)));
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var row = sheet.Rows[index];
            AppendRow(builder, index + 2,
                row.Select((value, column) => new ExportCell(value, sheet.Styles[column])));
        }
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, int rowNumber, IEnumerable<ExportCell> cells)
    {
        builder.Append("<row r=\"").Append(rowNumber).Append("\">");
        var column = 0;
        foreach (var cell in cells)
        {
            builder.Append("<c r=\"").Append(ColumnName(column++)).Append(rowNumber).Append('"');
            if (cell.Style > 0) builder.Append(" s=\"").Append(cell.Style).Append('"');
            var value = cell.Value;
            if (value is null)
            {
                builder.Append("/>");
                continue;
            }
            if (value is string text)
            {
                builder.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                    .Append(Xml(text)).Append("</t></is></c>");
                continue;
            }
            if (value is DateTimeOffset date)
            {
                var serial = (date.DateTime - new DateTime(1899, 12, 30)).TotalDays;
                builder.Append("><v>").Append(serial.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                continue;
            }
            if (value is DateOnly dateOnly)
            {
                var serial = (dateOnly.ToDateTime(TimeOnly.MinValue) - new DateTime(1899, 12, 30)).TotalDays;
                builder.Append("><v>").Append(serial.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                continue;
            }
            if (value is bool boolean)
            {
                builder.Append(" t=\"b\"><v>").Append(boolean ? '1' : '0').Append("</v></c>");
                continue;
            }
            builder.Append("><v>").Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append("</v></c>");
        }
        builder.Append("</row>");
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        index++;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }

    private static string ContentTypes(int sheetCount)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (var index = 1; index <= sheetCount; index++)
            builder.Append("<Override PartName=\"/xl/worksheets/sheet").Append(index)
                .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        return builder.Append("</Types>").ToString();
    }

    private static string RootRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";

    private static string Workbook(IReadOnlyList<XlsxSheet> sheets)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (var index = 0; index < sheets.Count; index++)
            builder.Append("<sheet name=\"").Append(Xml(sheets[index].Name)).Append("\" sheetId=\"")
                .Append(index + 1).Append("\" r:id=\"rId").Append(index + 1).Append("\"/>");
        return builder.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRelationships(int sheetCount)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var index = 1; index <= sheetCount; index++)
            builder.Append("<Relationship Id=\"rId").Append(index)
                .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
                .Append(index).Append(".xml\"/>");
        builder.Append("<Relationship Id=\"rId").Append(sheetCount + 1)
            .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return builder.Append("</Relationships>").ToString();
    }

    private static string Styles() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd hh:mm\"/><numFmt numFmtId=\"165\" formatCode=\"[h]:mm:ss\"/></numFmts><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>";

    private static string Xml(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string? NoteForExport(StudyActivityData activity, StudyExportRequest request) =>
        request.IncludeNotes && (request.IncludePrivateNotes || !activity.IsPrivate)
            ? activity.Note
            : null;

    private static string? SessionNotesForExport(StudySessionData session, StudyExportRequest request) =>
        request.IncludeNotes && (request.IncludePrivateNotes || !session.NotesArePrivate)
            ? session.Notes
            : null;

    private static object FilterDocument(StudyExportRequest request) => new
    {
        request.From,
        request.To,
        request.TimeZoneId,
        request.CourseIds,
        activityTypes = request.ActivityTypes?.Select(x => x.ToString()).ToArray(),
        request.IncludePomodoroCycles,
        request.IncludePrivateNotes,
        request.IncludeNotes,
        request.IncludeRatings,
        request.IncludeTags,
        request.IncludeResult,
        request.IncludeDeleted
    };

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException ex) { throw new ArgumentException($"Unknown time zone '{id}'.", nameof(id), ex); }
        catch (InvalidTimeZoneException ex) { throw new ArgumentException($"Invalid time zone '{id}'.", nameof(id), ex); }
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        var offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private sealed record ExportColumn<T>(string Header, Func<T, object?> Value, int Style = 0, bool IsInstant = false);
    private sealed record ExportTable(
        string[] Headers,
        int[] Styles,
        IReadOnlyList<object?[]> Rows,
        IReadOnlyList<int> InstantColumns);
    private sealed record XlsxSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows, int[] Styles);
    private sealed record ExportCell(object? Value, int Style);
    private sealed record ExportSelection(
        IReadOnlyList<StudyActivityData> Activities,
        IReadOnlyList<StudySessionData> Sessions,
        IReadOnlyList<PomodoroCycleData> Cycles,
        ActivitySummary Summary,
        TimeZoneInfo Zone,
        StudyExportRequest Request,
        DateTimeOffset Now)
    {
        public IReadOnlyList<SegmentRow> Segments { get; } = Sessions
            .SelectMany(session => session.Segments.Select(segment => new SegmentRow(session.Id, segment, Now)))
            .OrderBy(x => x.Segment.StartedAt)
            .ToList();
    }

    private sealed record SegmentRow(string SessionId, StudySessionSegmentData Segment, DateTimeOffset Now)
    {
        public int DurationSeconds => Segment.EndedAt is { } end
            ? Math.Max(0, (int)Math.Floor((end - Segment.StartedAt).TotalSeconds))
            : Math.Max(0, (int)Math.Floor((Now - Segment.StartedAt).TotalSeconds));
    }
}
