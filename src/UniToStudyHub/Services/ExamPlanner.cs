using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record ExamPlanningBlackout(DateOnly From, DateOnly To, string Label = "periodo escluso");

public sealed record ExamSessionAssignment(
    string ExamId,
    string CourseId,
    string ExamName,
    string SessionName,
    DateTime SessionDate,
    int Cfu,
    int EstimatedStudyHours);

public sealed record ExamSessionLoad(
    string SessionName,
    DateTime SessionDate,
    int AssignedCfu,
    int MaxCfu,
    int EstimatedStudyHours,
    int WeeksAvailable,
    decimal AverageWeeklyStudyHours,
    int MaxWeeklyStudyHours);

public sealed record ExamPlanningIssue(string ExamId, string CourseId, string ExamName, int Cfu, string Reason);

public sealed record ExamPlanningResult(
    DateTime ReferenceDate,
    DateTime TargetDate,
    IReadOnlyList<ExamSessionAssignment> Assignments,
    IReadOnlyList<ExamSessionLoad> SessionLoads,
    IReadOnlyList<ExamPlanningIssue> UnscheduledExams,
    IReadOnlyList<string> Warnings)
{
    public bool IsFeasible => UnscheduledExams.Count == 0;
    public int ScheduledCfu => Assignments.Sum(x => x.Cfu);
    public int UnscheduledCfu => UnscheduledExams.Sum(x => Math.Max(0, x.Cfu));
    public int ScheduledStudyHours => Assignments.Sum(x => x.EstimatedStudyHours);
}

public sealed record ExamPlanningAlternatives(
    ExamPlanningResult Conservative,
    ExamPlanningResult Balanced,
    ExamPlanningResult Intensive);

public sealed class ExamPlanningOptions
{
    public DateTime? TargetDate { get; init; }
    public int MaxCfuPerSession { get; init; } = 24;
    public int MaxWeeklyStudyHours { get; init; } = 20;
    public IReadOnlyList<ExamPlanningBlackout> ExcludedPeriods { get; init; } =
        Array.Empty<ExamPlanningBlackout>();
}

/// <summary>
/// Deterministic, constraint-aware assignment of unfinished exams to indicative
/// university sessions. Planning is read-only; applying a result is explicit.
/// </summary>
public static class ExamPlanner
{
    private const int MaximumExamCount = 500;
    private const int MaximumCourseCount = 2_000;
    private const int MaximumPrerequisiteEdges = 2_000;
    private const int MaximumBlackoutPeriods = 64;
    private const int MaximumPlanningHorizonYears = 10;
    private const int MaximumExamCfu = 60;
    private const int MaximumEstimatedStudyHours = 2_000;

    private sealed record SessionTarget(string Name, DateTime Date);

    private sealed class SessionLoad
    {
        public SessionTarget Target { get; }
        public int Cfu { get; set; }
        public int StudyHours { get; set; }

        public SessionLoad(SessionTarget target) => Target = target;
    }

    /// <summary>
    /// Creates a preview from the shared StudyData snapshot. Mandatory
    /// prerequisites and already-passed courses are taken from the same source
    /// of truth used by the rest of the application.
    /// </summary>
    public static ExamPlanningResult CreatePlan(
        StudyData data,
        ExamPlanningOptions? options = null,
        DateTime? referenceDate = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Exams is null || data.Courses is null || data.CoursePrerequisites is null)
            throw new InvalidDataException("I dati del pianificatore esami non sono inizializzati.");
        if (data.Exams.Count > MaximumExamCount)
            throw new InvalidOperationException($"Il pianificatore supporta al massimo {MaximumExamCount} esami per volta.");
        if (data.Courses.Count > MaximumCourseCount)
            throw new InvalidOperationException($"Il pianificatore supporta al massimo {MaximumCourseCount} corsi per volta.");
        if (data.CoursePrerequisites.Count > MaximumPrerequisiteEdges)
            throw new InvalidOperationException(
                $"Il numero di propedeuticità supera il limite di {MaximumPrerequisiteEdges}.");

        if (data.Exams.Any(x => x is null) || data.Courses.Any(x => x is null))
            throw new InvalidDataException("Gli esami o i corsi contengono una voce non valida.");
        if (data.CoursePrerequisites.Any(x => x is null))
            throw new InvalidDataException("L'elenco delle propedeuticità contiene una voce non valida.");

        var edges = data.CoursePrerequisites
            .Where(x => x.IsMandatory)
            .Select(x => (x.CourseId, x.PrerequisiteCourseId))
            .ToArray();

        var completedCourseIds = data.Courses
            .Where(x => x.Completed || x.Status == CourseStatus.Passed)
            .Select(x => x.Id)
            .Concat(data.Exams.Where(x => x.Completed).Select(x => x.CourseId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        return CreatePlanCore(data.Exams, options, referenceDate, edges, completedCourseIds);
    }

    /// <summary>
    /// Creates a preview when only the legacy exam list is available. This
    /// overload preserves the existing MainViewModel call site.
    /// </summary>
    public static ExamPlanningResult CreatePlan(
        IEnumerable<Exam> exams,
        ExamPlanningOptions? options = null,
        DateTime? referenceDate = null) =>
        CreatePlanCore(exams, options, referenceDate, Array.Empty<(string CourseId, string PrerequisiteCourseId)>(),
            new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Generates three explicit workload alternatives without changing the
    /// persisted exam dates.
    /// </summary>
    public static ExamPlanningAlternatives CreateAlternatives(
        StudyData data,
        DateTime? referenceDate = null,
        DateTime? targetDate = null,
        IReadOnlyList<ExamPlanningBlackout>? excludedPeriods = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var blackouts = excludedPeriods ?? Array.Empty<ExamPlanningBlackout>();
        return new ExamPlanningAlternatives(
            CreatePlan(data, new ExamPlanningOptions
            {
                TargetDate = targetDate,
                MaxCfuPerSession = 18,
                MaxWeeklyStudyHours = 10,
                ExcludedPeriods = blackouts
            }, referenceDate),
            CreatePlan(data, new ExamPlanningOptions
            {
                TargetDate = targetDate,
                MaxCfuPerSession = 24,
                MaxWeeklyStudyHours = 20,
                ExcludedPeriods = blackouts
            }, referenceDate),
            CreatePlan(data, new ExamPlanningOptions
            {
                TargetDate = targetDate,
                MaxCfuPerSession = 30,
                MaxWeeklyStudyHours = 30,
                ExcludedPeriods = blackouts
            }, referenceDate));
    }

    /// <summary>
    /// Applies a preview to the existing exam DTOs. Completed exams are never
    /// changed; unfinished exams without a feasible assignment are explicitly
    /// returned to the "Da pianificare" state.
    /// </summary>
    public static void ApplyPlan(IList<Exam> exams, ExamPlanningResult result)
    {
        ArgumentNullException.ThrowIfNull(exams);
        ArgumentNullException.ThrowIfNull(result);
        if (exams.Count > MaximumExamCount)
            throw new InvalidOperationException($"Il pianificatore supporta al massimo {MaximumExamCount} esami per volta.");

        var byId = new Dictionary<string, Exam>(StringComparer.Ordinal);
        foreach (var exam in exams)
        {
            if (exam is null)
                throw new InvalidDataException("L'elenco degli esami contiene una voce nulla.");
            if (string.IsNullOrWhiteSpace(exam.Id)) continue;
            if (!byId.TryAdd(exam.Id, exam))
                throw new InvalidDataException("Gli identificativi degli esami devono essere univoci.");
        }

        if (result.Assignments is null)
            throw new InvalidOperationException("La preview non contiene un elenco di assegnazioni valido.");
        if (result.Assignments.Count > MaximumExamCount)
            throw new InvalidOperationException("La preview contiene troppe assegnazioni.");

        var assignments = new Dictionary<string, ExamSessionAssignment>(StringComparer.Ordinal);
        foreach (var assignment in result.Assignments)
        {
            if (assignment is null || string.IsNullOrWhiteSpace(assignment.ExamId) ||
                !byId.TryGetValue(assignment.ExamId, out var exam) || exam.Completed ||
                !string.Equals(assignment.CourseId, exam.CourseId, StringComparison.Ordinal) ||
                assignment.Cfu != exam.Cfu ||
                assignment.EstimatedStudyHours != exam.EstimatedStudyHours ||
                string.IsNullOrWhiteSpace(assignment.SessionName) ||
                assignment.SessionDate == default ||
                !assignments.TryAdd(assignment.ExamId, assignment))
                throw new InvalidOperationException("La preview contiene un'assegnazione non valida o duplicata.");
            if (assignment.SessionDate.Date < result.ReferenceDate.Date ||
                assignment.SessionDate.Date > result.TargetDate.Date)
                throw new InvalidOperationException("La preview contiene una data fuori dall'intervallo richiesto.");
        }

        foreach (var exam in exams.Where(x => !x.Completed))
        {
            if (!string.IsNullOrWhiteSpace(exam.Id) && assignments.TryGetValue(exam.Id, out var assignment))
            {
                exam.PlannedSession = assignment.SessionName;
                exam.PlannedDate = assignment.SessionDate;
            }
            else
            {
                exam.PlannedSession = "Da pianificare";
                exam.PlannedDate = null;
            }
        }
    }

    /// <summary>
    /// Backward-compatible entry point used by the existing WPF command. The
    /// returned result is additive: callers that ignore it continue to compile.
    /// </summary>
    public static ExamPlanningResult AssignIndicativeSessions(
        IList<Exam> exams,
        DateTime? referenceDate = null,
        int maxCfuPerSession = 24,
        DateTime? targetDate = null)
    {
        ArgumentNullException.ThrowIfNull(exams);
        var result = CreatePlan(exams, new ExamPlanningOptions
        {
            TargetDate = targetDate,
            MaxCfuPerSession = maxCfuPerSession
        }, referenceDate);
        ApplyPlan(exams, result);
        return result;
    }

    /// <summary>Applies a prerequisite-aware preview to the shared StudyData snapshot.</summary>
    public static ExamPlanningResult AssignIndicativeSessions(
        StudyData data,
        ExamPlanningOptions? options = null,
        DateTime? referenceDate = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var result = CreatePlan(data, options, referenceDate);
        ApplyPlan(data.Exams, result);
        return result;
    }

    private static ExamPlanningResult CreatePlanCore(
        IEnumerable<Exam> exams,
        ExamPlanningOptions? options,
        DateTime? referenceDate,
        IReadOnlyCollection<(string CourseId, string PrerequisiteCourseId)> prerequisiteEdges,
        IReadOnlySet<string> completedCourseIds)
    {
        ArgumentNullException.ThrowIfNull(exams);
        ArgumentNullException.ThrowIfNull(prerequisiteEdges);
        ArgumentNullException.ThrowIfNull(completedCourseIds);

        var input = new List<Exam>(MaximumExamCount);
        foreach (var exam in exams)
        {
            if (input.Count == MaximumExamCount)
                throw new InvalidOperationException($"Il pianificatore supporta al massimo {MaximumExamCount} esami per volta.");
            input.Add(exam);
        }
        if (input.Any(x => x is null))
            throw new InvalidDataException("L'elenco degli esami contiene una voce nulla.");

        var validatedOptions = ValidateOptions(options ?? new ExamPlanningOptions());
        var today = (referenceDate ?? DateTime.Today).Date;
        var target = (validatedOptions.TargetDate ?? SafeAddYears(today, 2)).Date;
        var maximumTarget = SafeAddYears(today, MaximumPlanningHorizonYears);
        if (target > maximumTarget)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"L'orizzonte del pianificatore non può superare {MaximumPlanningHorizonYears} anni.");

        var warnings = new List<string>
        {
            "Le date proposte sono indicative e non rappresentano appelli ufficiali."
        };
        var issues = new List<ExamPlanningIssue>();
        if (target < today)
        {
            warnings.Add("L'obiettivo di laurea è già trascorso: nessuna sessione viene spostata automaticamente in avanti.");
            return new ExamPlanningResult(today, target, Array.Empty<ExamSessionAssignment>(),
                Array.Empty<ExamSessionLoad>(), input.Where(x => !x.Completed)
                    .Select(x => Issue(x, "L'obiettivo configurato è precedente alla data di pianificazione.")).ToArray(),
                warnings);
        }

        var allIds = new HashSet<string>(StringComparer.Ordinal);
        var courseIds = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<Exam>();
        foreach (var exam in input)
        {
            if (string.IsNullOrWhiteSpace(exam.Id))
            {
                if (!exam.Completed)
                    issues.Add(Issue(exam, "L'esame non ha un identificativo valido."));
                continue;
            }
            if (!allIds.Add(exam.Id))
                throw new InvalidDataException($"L'identificativo esame '{exam.Id}' è duplicato.");
            if (exam.Completed) continue;
            if (string.IsNullOrWhiteSpace(exam.CourseId))
            {
                issues.Add(Issue(exam, "L'esame non ha un corso valido."));
                continue;
            }
            if (!courseIds.Add(exam.CourseId))
                throw new InvalidDataException($"Il corso '{exam.CourseId}' è associato a più esami aperti.");
            if (exam.Cfu is < 1 or > MaximumExamCfu)
            {
                issues.Add(Issue(exam, $"I CFU devono essere compresi tra 1 e {MaximumExamCfu}."));
                continue;
            }
            if (exam.EstimatedStudyHours is < 0 or > MaximumEstimatedStudyHours)
            {
                issues.Add(Issue(exam,
                    $"Le ore stimate devono essere comprese tra 0 e {MaximumEstimatedStudyHours}."));
                continue;
            }
            candidates.Add(exam);
        }

        var graph = new PrerequisiteGraphService(prerequisiteEdges);
        var topologicalOrder = graph.TopologicalOrder(candidates.Select(x => x.CourseId));
        var orderIndex = topologicalOrder
            .Select((courseId, index) => (courseId, index))
            .ToDictionary(x => x.courseId, x => x.index, StringComparer.Ordinal);

        var sessions = BuildUpcomingSessions(today, target)
            .Where(session => !IsExcluded(session.Date, validatedOptions.ExcludedPeriods))
            .Select(session => new SessionLoad(session))
            .ToArray();
        if (sessions.Length == 0)
            warnings.Add("Non ci sono sessioni disponibili entro l'obiettivo configurato e al di fuori dei periodi esclusi.");

        var assignments = new List<ExamSessionAssignment>(candidates.Count);
        var assignmentsByCourse = new Dictionary<string, ExamSessionAssignment>(StringComparer.Ordinal);
        var issuesByExamId = issues.Select(x => x.ExamId).ToHashSet(StringComparer.Ordinal);

        foreach (var exam in candidates
                     .OrderBy(x => orderIndex.GetValueOrDefault(x.CourseId, int.MaxValue))
                     .ThenByDescending(x => x.Cfu)
                     .ThenByDescending(x => x.EstimatedStudyHours)
                     .ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            if (issuesByExamId.Contains(exam.Id)) continue;

            var missingPrerequisite = graph.DirectPrerequisites(exam.CourseId)
                .FirstOrDefault(id => !completedCourseIds.Contains(id) && !assignmentsByCourse.ContainsKey(id));
            if (missingPrerequisite is not null)
            {
                issues.Add(Issue(exam,
                    $"La propedeuticità '{missingPrerequisite}' non è superata né pianificabile prima di questo esame."));
                issuesByExamId.Add(exam.Id);
                continue;
            }

            var prerequisiteDate = graph.DirectPrerequisites(exam.CourseId)
                .Where(assignmentsByCourse.ContainsKey)
                .Select(id => assignmentsByCourse[id].SessionDate)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            if (exam.Cfu > validatedOptions.MaxCfuPerSession)
            {
                issues.Add(Issue(exam,
                    $"L'esame richiede {exam.Cfu} CFU, oltre il limite di {validatedOptions.MaxCfuPerSession} CFU per sessione."));
                continue;
            }

            var eligible = sessions.Where(load =>
                    load.Target.Date > prerequisiteDate &&
                    load.Cfu + exam.Cfu <= validatedOptions.MaxCfuPerSession)
                .ToArray();
            if (eligible.Length == 0)
            {
                issues.Add(Issue(exam, prerequisiteDate != DateTime.MinValue
                    ? "Non esiste una sessione successiva disponibile che rispetti i vincoli di CFU."
                    : "Nessuna sessione disponibile rispetta il limite di CFU per sessione."));
                continue;
            }

            var weeksBySession = eligible.ToDictionary(
                load => load.Target.Name,
                load => WeeksAvailable(today, load.Target.Date),
                StringComparer.Ordinal);
            var weeklyEligible = eligible.FirstOrDefault(load =>
                (load.StudyHours + exam.EstimatedStudyHours) / (decimal)weeksBySession[load.Target.Name]
                <= validatedOptions.MaxWeeklyStudyHours);
            if (weeklyEligible is null)
            {
                issues.Add(Issue(exam,
                    $"Le ore stimate superano il limite medio di {validatedOptions.MaxWeeklyStudyHours} ore settimanali in tutte le sessioni disponibili."));
                continue;
            }

            weeklyEligible.Cfu += exam.Cfu;
            weeklyEligible.StudyHours += exam.EstimatedStudyHours;
            var assignment = new ExamSessionAssignment(
                exam.Id,
                exam.CourseId,
                string.IsNullOrWhiteSpace(exam.Name) ? exam.CourseName : exam.Name,
                weeklyEligible.Target.Name,
                weeklyEligible.Target.Date,
                exam.Cfu,
                exam.EstimatedStudyHours);
            assignments.Add(assignment);
            assignmentsByCourse.Add(exam.CourseId, assignment);
        }

        var loads = sessions.Select(load =>
        {
            var weeks = WeeksAvailable(today, load.Target.Date);
            return new ExamSessionLoad(
                load.Target.Name,
                load.Target.Date,
                load.Cfu,
                validatedOptions.MaxCfuPerSession,
                load.StudyHours,
                weeks,
                Math.Round(load.StudyHours / (decimal)weeks, 2, MidpointRounding.AwayFromZero),
                validatedOptions.MaxWeeklyStudyHours);
        }).ToArray();

        return new ExamPlanningResult(today, target, assignments, loads, issues, warnings);
    }

    private static ExamPlanningOptions ValidateOptions(ExamPlanningOptions options)
    {
        if (options.MaxCfuPerSession is < 1 or > MaximumExamCfu)
            throw new ArgumentOutOfRangeException(nameof(options.MaxCfuPerSession),
                $"Il limite CFU deve essere compreso tra 1 e {MaximumExamCfu}.");
        if (options.MaxWeeklyStudyHours is < 1 or > 168)
            throw new ArgumentOutOfRangeException(nameof(options.MaxWeeklyStudyHours),
                "Il limite settimanale deve essere compreso tra 1 e 168 ore.");
        if (options.ExcludedPeriods is null)
            throw new ArgumentException("L'elenco dei periodi esclusi non può essere nullo.", nameof(options));
        if (options.ExcludedPeriods.Count > MaximumBlackoutPeriods)
            throw new ArgumentOutOfRangeException(nameof(options.ExcludedPeriods),
                $"Sono consentiti al massimo {MaximumBlackoutPeriods} periodi esclusi.");

        foreach (var period in options.ExcludedPeriods)
        {
            if (period is null || period.From > period.To)
                throw new ArgumentException("Ogni periodo escluso deve avere date valide e ordinate.", nameof(options));
            if (period.Label is null || period.Label.Length > 100)
                throw new ArgumentException("L'etichetta del periodo escluso non può superare 100 caratteri.", nameof(options));
        }

        return options;
    }

    private static IReadOnlyList<SessionTarget> BuildUpcomingSessions(DateTime today, DateTime target)
    {
        var sessions = new List<SessionTarget>();
        for (var year = today.Year; year <= target.Year; year++)
        {
            AddIfFuture(sessions, "Invernale", new DateTime(year, 1, 20), today, target);
            AddIfFuture(sessions, "Estiva", new DateTime(year, 6, 20), today, target);
            AddIfFuture(sessions, "Autunnale", new DateTime(year, 9, 15), today, target);
        }
        return sessions.OrderBy(x => x.Date).ToArray();
    }

    private static void AddIfFuture(
        ICollection<SessionTarget> sessions,
        string name,
        DateTime date,
        DateTime today,
        DateTime target)
    {
        if (date >= today && date <= target)
            sessions.Add(new SessionTarget($"{name} {date.Year}", date));
    }

    private static bool IsExcluded(DateTime date, IReadOnlyList<ExamPlanningBlackout> periods) =>
        periods.Any(period => date >= period.From.ToDateTime(TimeOnly.MinValue) &&
                              date <= period.To.ToDateTime(TimeOnly.MaxValue));

    private static int WeeksAvailable(DateTime from, DateTime to) =>
        Math.Max(1, (int)Math.Ceiling((to.Date - from.Date).TotalDays / 7d));

    private static ExamPlanningIssue Issue(Exam exam, string reason) =>
        new(exam.Id ?? string.Empty, exam.CourseId ?? string.Empty,
            string.IsNullOrWhiteSpace(exam.Name) ? exam.CourseName ?? string.Empty : exam.Name,
            Math.Max(0, exam.Cfu), reason);

    private static DateTime SafeAddYears(DateTime date, int years)
    {
        try
        {
            return date.AddYears(years);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MaxValue.Date;
        }
    }
}
