using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record IcsParseResult(
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Parses the reliable subset of RFC 5545 used by university calendars:
/// VEVENT, DTSTART/DTEND, all-day values, TZID/Z, RRULE
/// DAILY/WEEKLY/MONTHLY/YEARLY with INTERVAL/COUNT/UNTIL/BYDAY and EXDATE.
/// Named TZID recurrences preserve local wall time across DST; nonexistent
/// occurrences are skipped and ambiguous times use the first occurrence.
/// Recurrences are materialized inside an explicit window; VTIMEZONE rules,
/// BYSETPOS, RDATE and floating-time DST edge cases remain unsupported.
/// </summary>
public sealed class IcsCalendarParser
{
    public const int MaximumContentCharacters = 10 * 1024 * 1024;
    public const int MaximumEvents = 1000;
    private const int MaximumRecurrenceIterations = 20000;

    internal static (DateTimeOffset From, DateTimeOffset To) ResolveWindow(
        DateTimeOffset? windowStart,
        DateTimeOffset? windowEnd)
    {
        var from = windowStart ?? new DateTimeOffset(DateTime.Today.AddYears(-1));
        var to = windowEnd ?? from.AddYears(3);
        return (from, to);
    }

    public IcsParseResult Parse(
        string content,
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null,
        int maxEvents = 1000)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new IcsParseResult(Array.Empty<CalendarEvent>(), new[] { "Il file ICS è vuoto." });
        if (content.Length > MaximumContentCharacters)
            throw new InvalidDataException("Il contenuto ICS supera il limite consentito di 10 MB.");
        if (maxEvents is < 1 or > MaximumEvents)
            throw new ArgumentOutOfRangeException(nameof(maxEvents), $"Il numero massimo di eventi deve essere compreso tra 1 e {MaximumEvents}.");

        var (from, to) = ResolveWindow(windowStart, windowEnd);
        if (to <= from) throw new ArgumentOutOfRangeException(nameof(windowEnd));

        var warnings = new List<string>();
        var events = new List<CalendarEvent>();
        var lines = Unfold(content);
        var block = new List<string>();
        var inEvent = false;
        foreach (var line in lines)
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                block.Clear();
                inEvent = true;
                continue;
            }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (inEvent)
                {
                    try
                    {
                        foreach (var item in ParseEvent(block, from, to, warnings, maxEvents - events.Count))
                        {
                            events.Add(item);
                            if (events.Count >= maxEvents) break;
                        }
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                    {
                        warnings.Add($"Evento ICS ignorato: {ex.Message}");
                    }
                }
                inEvent = false;
                block.Clear();
                continue;
            }
            if (inEvent) block.Add(line);
        }

        if (events.Count >= maxEvents)
            warnings.Add($"Importazione limitata a {maxEvents} occorrenze.");
        return new IcsParseResult(events, warnings);
    }

    private static IEnumerable<CalendarEvent> ParseEvent(
        IReadOnlyList<string> lines,
        DateTimeOffset from,
        DateTimeOffset to,
        List<string> warnings,
        int remaining)
    {
        if (remaining <= 0) yield break;
        var props = new Dictionary<string, List<IcsProperty>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var property = ParseProperty(line);
            if (property is null) continue;
            if (!props.TryGetValue(property.Name, out var values))
                props[property.Name] = values = new List<IcsProperty>();
            values.Add(property);
        }
        if (!props.TryGetValue("DTSTART", out var startProperties) || startProperties.Count == 0)
        {
            warnings.Add("Evento ICS senza DTSTART ignorato.");
            yield break;
        }

        var startProperty = startProperties[0];
        var uid = Get(props, "UID");
        var summary = Sanitize(Unescape(Get(props, "SUMMARY")), 400);
        if (string.IsNullOrWhiteSpace(summary)) summary = "Evento importato";
        var allDay = IsDateOnly(startProperty.Value);
        var start = ParseDate(startProperty, allDay);
        var end = props.TryGetValue("DTEND", out var endProps) && endProps.Count > 0
            ? ParseDate(endProps[0], allDay)
            : allDay ? start.AddDays(1) : start.AddHours(1);
        if (end <= start) end = allDay ? start.AddDays(1) : start.AddHours(1);
        var sequence = long.TryParse(Get(props, "SEQUENCE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq) ? seq : 0;
        var cancelled = string.Equals(Get(props, "STATUS"), "CANCELLED", StringComparison.OrdinalIgnoreCase);
        var baseUid = string.IsNullOrWhiteSpace(uid)
            ? "generated:" + Hash($"{summary}|{start:O}|{end:O}")
            : uid.Trim();
        var exdates = ParseExDates(props, warnings);
        var rule = Get(props, "RRULE");
        var recurrenceRule = rule ?? string.Empty;
        var recurrenceZone = allDay
            ? null
            : ResolveTimeZone(startProperty.Parameters.GetValueOrDefault("TZID"));
        var occurrences = Expand(start, end, allDay, rule, exdates, from, to, remaining, warnings, recurrenceZone);
        var index = 0;
        foreach (var (occurrenceStart, occurrenceEnd) in occurrences)
        {
            index++;
            if (occurrenceStart >= to && rule is not null) break;
            if (occurrenceEnd <= from || occurrenceStart >= to) continue;
            yield return new CalendarEvent
            {
                Id = "ics:" + Hash(baseUid + "#" + occurrenceStart.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                Title = summary,
                Category = MapCategory(Get(props, "CATEGORIES"), summary),
                StartsAt = occurrenceStart,
                EndsAt = occurrenceEnd,
                IsAllDay = allDay,
                CourseId = null,
                CourseName = Sanitize(Unescape(Get(props, "X-UNITO-COURSE")), 200),
                Location = Sanitize(Unescape(Get(props, "LOCATION")), 400),
                Notes = Sanitize(Unescape(Get(props, "DESCRIPTION")), 12000),
                Source = CalendarEventSource.Ics,
                Verification = CalendarEventVerification.Unverified,
                TimeZoneId = startProperty.Parameters.TryGetValue("TZID", out var tzid) && !string.IsNullOrWhiteSpace(tzid)
                    ? tzid
                    : TimeZoneInfo.Local.Id,
                ExternalId = rule is null ? baseUid : $"{baseUid}#{occurrenceStart.ToUniversalTime():O}",
                ExternalSequence = sequence,
                IsCancelled = cancelled,
                RecurrenceRule = recurrenceRule,
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now
            };
        }
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Expand(
        DateTimeOffset start,
        DateTimeOffset end,
        bool allDay,
        string? rule,
        IReadOnlySet<DateOnly> exdates,
        DateTimeOffset from,
        DateTimeOffset to,
        int max,
        List<string> warnings,
        TimeZoneInfo? recurrenceZone)
    {
        var duration = end - start;
        if (string.IsNullOrWhiteSpace(rule))
        {
            if (start < to && end > from && !exdates.Contains(DateOnly.FromDateTime(start.DateTime)))
                yield return (start, end);
            yield break;
        }

        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0].ToUpperInvariant(), x => x[1], StringComparer.OrdinalIgnoreCase);
        if (!parts.TryGetValue("FREQ", out var frequency))
        {
            warnings.Add("RRULE senza FREQ ignorata.");
            yield break;
        }
        var interval = parts.TryGetValue("INTERVAL", out var intervalText) &&
                       int.TryParse(intervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInterval) &&
                       parsedInterval > 0
            ? parsedInterval
            : 1;
        var count = parts.TryGetValue("COUNT", out var countText) &&
                    int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount) &&
                    parsedCount > 0
            ? parsedCount
            : (int?)null;
        DateTimeOffset? until = null;
        if (parts.TryGetValue("UNTIL", out var untilText))
        {
            try { until = ParseDateValue(untilText, null, allDay); }
            catch (FormatException) { warnings.Add("UNTIL non valido ignorato."); }
        }
        var byDays = parts.TryGetValue("BYDAY", out var byDayText)
            ? ParseByDays(byDayText)
            : Array.Empty<DayOfWeek>();
        var emitted = 0;
        var skipped = 0;
        var cursor = start;
        var localCursor = DateTime.SpecifyKind(start.DateTime, DateTimeKind.Unspecified);
        var guard = 0;
        while (guard++ < MaximumRecurrenceIterations && emitted < max)
        {
            if (count is int maxCount && skipped >= maxCount) yield break;

            // RFC 5545 recurrence dates in a TZID are wall-clock values. Rebuild
            // each occurrence with the zone's offset for that date instead of
            // carrying DTSTART's original offset through a daylight-saving change.
            var occurrenceDateTime = recurrenceZone is null ? cursor.DateTime : localCursor;
            var occurrenceStart = cursor;
            var invalidLocalTime = false;
            if (recurrenceZone is not null)
            {
                invalidLocalTime = recurrenceZone.IsInvalidTime(localCursor);
                var offset = invalidLocalTime
                    ? recurrenceZone.BaseUtcOffset
                    : GetZoneOffset(recurrenceZone, localCursor);
                occurrenceStart = new DateTimeOffset(localCursor, offset);
            }

            if (until is not null && occurrenceStart > until.Value) yield break;
            if (occurrenceStart >= to) yield break;

            var include = frequency.ToUpperInvariant() switch
            {
                "DAILY" => true,
                "WEEKLY" => byDays.Count == 0 || byDays.Contains(occurrenceDateTime.DayOfWeek),
                "MONTHLY" => true,
                "YEARLY" => true,
                _ => false
            };
            if (frequency.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase) && byDays.Count > 0)
            {
                var weekStart = CalendarService.StartOfWeek(start.DateTime.Date);
                var cursorWeek = CalendarService.StartOfWeek(occurrenceDateTime.Date);
                var weeks = (cursorWeek - weekStart).Days / 7;
                include &= weeks % interval == 0;
            }
            if (include)
            {
                skipped++;
                var occurrenceEnd = occurrenceStart + duration;
                if (!exdates.Contains(DateOnly.FromDateTime(occurrenceDateTime)) && occurrenceEnd > from)
                {
                    if (invalidLocalTime)
                    {
                        warnings.Add(
                            $"Ricorrenza saltata: l'orario locale {localCursor:yyyyMMdd'T'HHmmss} non esiste in {recurrenceZone!.Id} a causa del cambio dell'ora.");
                    }
                    else
                    {
                        emitted++;
                        yield return (occurrenceStart, occurrenceEnd);
                    }
                }
            }

            if (recurrenceZone is null)
            {
                cursor = frequency.ToUpperInvariant() switch
                {
                    "DAILY" => cursor.AddDays(interval),
                    "WEEKLY" when byDays.Count > 0 => cursor.AddDays(1),
                    "WEEKLY" => cursor.AddDays(7 * interval),
                    "MONTHLY" => cursor.AddMonths(interval),
                    "YEARLY" => cursor.AddYears(interval),
                    _ => cursor.AddDays(1)
                };
            }
            else
            {
                localCursor = frequency.ToUpperInvariant() switch
                {
                    "DAILY" => localCursor.AddDays(interval),
                    "WEEKLY" when byDays.Count > 0 => localCursor.AddDays(1),
                    "WEEKLY" => localCursor.AddDays(7 * interval),
                    "MONTHLY" => localCursor.AddMonths(interval),
                    "YEARLY" => localCursor.AddYears(interval),
                    _ => localCursor.AddDays(1)
                };
            }
        }
        if (guard >= MaximumRecurrenceIterations)
            warnings.Add($"Ricorrenza interrotta dopo {MaximumRecurrenceIterations} iterazioni.");
    }

    private static HashSet<DateOnly> ParseExDates(
        IReadOnlyDictionary<string, List<IcsProperty>> props,
        List<string> warnings)
    {
        var result = new HashSet<DateOnly>();
        if (!props.TryGetValue("EXDATE", out var values)) return result;
        foreach (var property in values)
        {
            foreach (var raw in property.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var parsed = ParseDateValue(raw.Trim(), property.Parameters.GetValueOrDefault("TZID"), raw.Length == 8);
                    result.Add(DateOnly.FromDateTime(parsed.DateTime));
                }
                catch (FormatException) { warnings.Add($"EXDATE non valido ignorato: {raw}"); }
            }
        }
        return result;
    }

    private static IReadOnlyList<DayOfWeek> ParseByDays(string value)
    {
        var map = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
        {
            ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday,
            ["TH"] = DayOfWeek.Thursday, ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday,
            ["SU"] = DayOfWeek.Sunday
        };
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().Length >= 2 ? x.Trim()[^2..] : string.Empty)
            .Where(map.ContainsKey)
            .Select(x => map[x])
            .Distinct()
            .ToList();
    }

    private static DateTimeOffset ParseDate(IcsProperty property, bool allDay) =>
        ParseDateValue(property.Value, property.Parameters.GetValueOrDefault("TZID"), allDay);

    private static DateTimeOffset ParseDateValue(string value, string? tzid, bool allDay)
    {
        value = value.Trim();
        if (allDay || value.Length == 8)
        {
            var date = DateTime.ParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture);
            var offset = TimeZoneInfo.Local.GetUtcOffset(date);
            return new DateTimeOffset(date, offset);
        }

        var utc = value.EndsWith('Z');
        var text = utc ? value[..^1] : value;
        var parsed = DateTime.ParseExact(text, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        if (utc) return new DateTimeOffset(parsed, TimeSpan.Zero);
        var zone = ResolveTimeZone(tzid);
        if (zone is not null)
            return new DateTimeOffset(parsed, GetZoneOffset(zone, parsed));
        return new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed));
    }

    private static TimeZoneInfo? ResolveTimeZone(string? tzid)
    {
        if (string.IsNullOrWhiteSpace(tzid)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tzid);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new FormatException($"Fuso orario ICS sconosciuto: {tzid}.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new FormatException($"Fuso orario ICS non valido: {tzid}.");
        }
    }

    private static TimeSpan GetZoneOffset(TimeZoneInfo zone, DateTime localDateTime)
    {
        var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            throw new FormatException($"Orario locale ICS inesistente in {zone.Id}: {local:O}.");
        // During the autumn overlap RFC 5545 resolves an ambiguous local time
        // to its first occurrence, which is the numerically larger UTC offset.
        if (zone.IsAmbiguousTime(local))
            return zone.GetAmbiguousTimeOffsets(local).Max();
        return zone.GetUtcOffset(local);
    }

    private static IcsProperty? ParseProperty(string line)
    {
        var colon = FindColon(line);
        if (colon <= 0) return null;
        var left = line[..colon];
        var value = line[(colon + 1)..];
        var segments = left.Split(';');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < segments.Length; i++)
        {
            var pair = segments[i].Split('=', 2);
            if (pair.Length == 2) parameters[pair[0]] = pair[1].Trim('"');
        }
        return new IcsProperty(segments[0].ToUpperInvariant(), parameters, value);
    }

    private static int FindColon(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            if (line[i] == ':' && !quoted) return i;
        }
        return -1;
    }

    private static List<string> Unfold(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var result = new List<string>();
        foreach (var raw in normalized.Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && result.Count > 0)
                result[^1] += raw[1..];
            else if (!string.IsNullOrWhiteSpace(raw))
                result.Add(raw);
        }
        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, List<IcsProperty>> props, string name) =>
        props.TryGetValue(name, out var values) && values.Count > 0 ? values[0].Value : null;

    private static string Unescape(string? value) => value?
        .Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
        .Replace("\\N", "\n", StringComparison.Ordinal)
        .Replace("\\,", ",", StringComparison.Ordinal)
        .Replace("\\;", ";", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal) ?? string.Empty;

    private static bool IsDateOnly(string value) => value.Length == 8 && value.All(char.IsDigit);

    /// <summary>
    /// Imported calendar text is untrusted: control characters are removed and
    /// each field is capped to the length the persistence validator accepts, so
    /// a hostile ICS file cannot inject terminal/newline sequences or fail the
    /// whole import on one overlong field.
    /// </summary>
    private static string Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        foreach (var character in value)
        {
            if (builder.Length >= maximumLength) break;
            if (character is '\n' or '\r' or '\t' || !char.IsControl(character)) builder.Append(character);
        }
        return builder.ToString().Trim();
    }

    private static string MapCategory(string? categories, string summary)
    {
        var text = $"{categories} {summary}".ToUpperInvariant();
        if (text.Contains("ESAME") || text.Contains("EXAM") || text.Contains("APPELLO")) return CalendarEventCategory.Exam;
        if (text.Contains("LEZIONE") || text.Contains("LECTURE")) return CalendarEventCategory.Lecture;
        if (text.Contains("SCADENZA") || text.Contains("DEADLINE")) return CalendarEventCategory.Deadline;
        if (text.Contains("RIPASSO") || text.Contains("REVIEW")) return CalendarEventCategory.Review;
        if (text.Contains("STUDIO") || text.Contains("STUDY")) return CalendarEventCategory.Study;
        return CalendarEventCategory.Personal;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();

    private sealed record IcsProperty(string Name, IReadOnlyDictionary<string, string> Parameters, string Value);
}

public sealed record IcsImportResult(
    int Added,
    int Updated,
    int Removed,
    int Skipped,
    IReadOnlyList<string> Warnings);

public sealed class IcsImportService
{
    public const long MaximumFileBytes = 10 * 1024 * 1024;

    public async Task<IcsImportResult> ImportFileAsync(
        string path,
        StudyData data,
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("File ICS non trovato.", path);
        var info = new FileInfo(path);
        if (info.Length == 0) throw new InvalidDataException("Il file ICS è vuoto.");
        if (info.Length > MaximumFileBytes) throw new InvalidDataException("Il file ICS supera il limite di 10 MB.");
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return ImportText(content, data, windowStart, windowEnd);
    }

    public IcsImportResult ImportText(
        string content,
        StudyData data,
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (content.Length > MaximumFileBytes)
            throw new InvalidDataException("Il contenuto ICS supera il limite di 10 MB.");
        var (windowFrom, windowTo) = IcsCalendarParser.ResolveWindow(windowStart, windowEnd);
        var parsed = new IcsCalendarParser().Parse(content, windowFrom, windowTo);
        var warnings = parsed.Warnings.ToList();
        var added = 0;
        var updated = 0;
        var skipped = 0;
        var incomingUids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var incoming in parsed.Events)
        {
            var uid = BaseUid(incoming.ExternalId);
            if (uid is not null) incomingUids.Add(uid);
            var existing = data.CalendarEvents.FirstOrDefault(x =>
                string.Equals(x.Source, CalendarEventSource.Ics, StringComparison.Ordinal) &&
                string.Equals(x.ExternalId, incoming.ExternalId, StringComparison.Ordinal));
            if (existing is null)
            {
                if (incoming.IsCancelled) { skipped++; continue; }
                data.CalendarEvents.Add(incoming);
                added++;
                continue;
            }
            if (incoming.ExternalSequence < existing.ExternalSequence)
            {
                skipped++;
                continue;
            }
            if (incoming.IsCancelled)
            {
                existing.IsCancelled = true;
                existing.ExternalSequence = incoming.ExternalSequence;
                existing.UpdatedAt = DateTimeOffset.Now;
                updated++;
                continue;
            }
            existing.Title = incoming.Title;
            existing.Category = incoming.Category;
            existing.StartsAt = incoming.StartsAt;
            existing.EndsAt = incoming.EndsAt;
            existing.IsAllDay = incoming.IsAllDay;
            existing.Location = incoming.Location;
            existing.Notes = incoming.Notes;
            existing.TimeZoneId = incoming.TimeZoneId;
            existing.ExternalSequence = incoming.ExternalSequence;
            existing.RecurrenceRule = incoming.RecurrenceRule;
            existing.IsCancelled = false;
            existing.UpdatedAt = DateTimeOffset.Now;
            updated++;
        }

        // A full import of a UID replaces its materialized occurrence window;
        // occurrences that disappeared are cancellations, not silent duplicates.
        var removed = 0;
        foreach (var uid in incomingUids)
        {
            var existingForUid = data.CalendarEvents
                .Where(x => string.Equals(x.Source, CalendarEventSource.Ics, StringComparison.Ordinal) &&
                            string.Equals(BaseUid(x.ExternalId), uid, StringComparison.Ordinal))
                .ToList();
            if (existingForUid.Count == 0) continue;
            var incomingStarts = parsed.Events
                .Where(x => string.Equals(BaseUid(x.ExternalId), uid, StringComparison.Ordinal))
                .Select(x => x.StartsAt)
                .ToHashSet();
            // Only reconcile the occurrence window represented by this import.
            // Existing materialized occurrences before or after the selected range
            // belong to other calendar views and must not be deleted by a partial import.
            foreach (var obsolete in existingForUid.Where(x =>
                         x.StartsAt < windowTo &&
                         x.EndsAt > windowFrom &&
                         !incomingStarts.Contains(x.StartsAt)))
            {
                data.CalendarEvents.Remove(obsolete);
                removed++;
            }
        }
        return new IcsImportResult(added, updated, removed, skipped, warnings);
    }

    private static string? BaseUid(string? externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return null;
        var index = externalId.IndexOf('#');
        return index < 0 ? externalId : externalId[..index];
    }
}
