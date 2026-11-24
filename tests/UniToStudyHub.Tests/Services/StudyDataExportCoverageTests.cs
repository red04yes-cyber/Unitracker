using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class StudyDataExportCoverageTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private static readonly StudyExportRequest Request =
        new(new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 24), "UTC");

    private static StudyData CreateData()
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new()
                {
                    Id = "course-a",
                    Name = "Analisi",
                    Code = "MAT-01",
                    Cfu = 6,
                    Notes = "COURSE_PRIVATE_NOTE",
                    SyllabusSummary = "COURSE_SYLLABUS_SUMMARY",
                    UpdatedAt = Start.UtcDateTime
                },
                new()
                {
                    Id = "course-b",
                    Name = "Fisica",
                    Code = "FIS-01",
                    Cfu = 9
                }
            }
        };

        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-a",
            CourseId = "course-a",
            Title = "TASK_TITLE",
            Description = "TASK_DESCRIPTION",
            DueAt = Start.AddHours(2),
            EstimatedMinutes = 45,
            Priority = 4,
            Status = StudyTaskStatus.InProgress,
            CreatedAt = Start,
            UpdatedAt = Start
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-b",
            CourseId = "course-b",
            Title = "TASK_B",
            CreatedAt = Start,
            UpdatedAt = Start
        });

        data.StudyMissions.Add(new StudyMissionData
        {
            Id = "mission-a",
            CourseId = "course-a",
            SyllabusTopicId = "topic-a",
            Title = "MISSION_TITLE",
            Description = "MISSION_DESCRIPTION",
            Reward = "MISSION_REWARD",
            StartsAt = Start,
            DueAt = Start.AddHours(3),
            Difficulty = 3,
            Status = StudyMissionStatus.InProgress,
            CreatedAt = Start,
            UpdatedAt = Start,
            Steps = new ObservableCollection<MissionStepData>
            {
                new() { Id = "step-a", Title = "MISSION_STEP_TITLE", Position = 1, IsCompleted = true, CompletedAt = Start }
            }
        });
        data.StudyMissions.Add(new StudyMissionData
        {
            Id = "mission-b",
            CourseId = "course-b",
            Title = "MISSION_B",
            StartsAt = Start,
            CreatedAt = Start,
            UpdatedAt = Start
        });

        data.Flashcards.Add(new FlashcardData
        {
            Id = "card-a",
            CourseId = "course-a",
            SyllabusTopicId = "topic-a",
            Front = "FLASHCARD_FRONT",
            Back = "FLASHCARD_BACK",
            DueAt = Start.AddHours(1),
            LastReviewedAt = Start.AddMinutes(5),
            CreatedAt = Start,
            UpdatedAt = Start
        });
        data.Flashcards.Add(new FlashcardData
        {
            Id = "card-b",
            CourseId = "course-b",
            Front = "FLASHCARD_B",
            CreatedAt = Start,
            UpdatedAt = Start
        });
        data.FlashcardReviews.Add(new FlashcardReviewData
        {
            Id = "review-a",
            FlashcardId = "card-a",
            ReviewedAt = Start.AddMinutes(5),
            Rating = RecallRating.Good,
            PreviousIntervalDays = 1,
            NewIntervalDays = 6,
            EaseFactorAfter = 2.6m
        });
        data.FlashcardReviews.Add(new FlashcardReviewData
        {
            Id = "review-orphan",
            FlashcardId = "missing-card",
            ReviewedAt = Start,
            Rating = RecallRating.Again
        });

        data.ErrorJournalEntries.Add(new ErrorJournalEntryData
        {
            Id = "error-a",
            CourseId = "course-a",
            SyllabusTopicId = "topic-a",
            Topic = "Derivate",
            Question = "ERROR_QUESTION",
            IncorrectAnswer = "ERROR_INCORRECT",
            CorrectAnswer = "ERROR_CORRECT",
            ErrorType = "Calcolo",
            RootCause = "ERROR_ROOT_CAUSE",
            NextReviewAt = Start.AddDays(1),
            ReviewCount = 2,
            Status = ErrorReviewStatus.Reviewing,
            CreatedAt = Start,
            UpdatedAt = Start
        });
        data.ErrorJournalEntries.Add(new ErrorJournalEntryData
        {
            Id = "error-b",
            CourseId = "course-b",
            Topic = "Fisica",
            Question = "ERROR_B_QUESTION",
            CreatedAt = Start,
            UpdatedAt = Start
        });

        data.EnergyCheckIns.Add(new EnergyCheckInData
        {
            Id = "energy-a",
            RecordedAt = Start.AddMinutes(10),
            EnergyLevel = 3,
            FocusLevel = 4,
            Note = "ENERGY_PRIVATE_NOTE"
        });
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "topic-a",
            CourseId = "course-a",
            Title = "Limiti",
            Description = "SYLLABUS_DESCRIPTION",
            Status = SyllabusTopicStatus.Completed,
            EstimatedMinutes = 60,
            SourceUrl = "https://example.invalid/syllabus",
            LastStudiedAt = Start.UtcDateTime,
            UpdatedAt = Start.UtcDateTime
        });
        data.Questions.Add(new QuestionBankItemData
        {
            Id = "question-a",
            CourseId = "course-a",
            SyllabusTopicId = "topic-a",
            Text = "QUESTION_TEXT",
            Type = QuestionType.Closed,
            Difficulty = "Media",
            Options = new ObservableCollection<string> { "QUESTION_OPTION_A", "QUESTION_OPTION_B" },
            CorrectAnswer = "QUESTION_CORRECT_ANSWER",
            Explanation = "QUESTION_EXPLANATION",
            Source = "Manuale",
            CreatedAt = Start.UtcDateTime,
            UpdatedAt = Start.UtcDateTime
        });
        data.Questions.Add(new QuestionBankItemData
        {
            Id = "question-b",
            CourseId = "course-b",
            Text = "QUESTION_B",
            CreatedAt = Start.UtcDateTime,
            UpdatedAt = Start.UtcDateTime
        });

        data.Grades.Add(new Grade
        {
            Id = "grade-a",
            CourseId = "course-a",
            CourseName = "Analisi",
            Value = 30,
            Honors = true,
            Cfu = 6,
            Date = Start.UtcDateTime,
            CountsTowardAverage = true
        });
        data.Grades.Add(new Grade
        {
            Id = "grade-b",
            CourseId = "course-b",
            CourseName = "Fisica",
            Value = 27,
            Cfu = 9,
            Date = Start.UtcDateTime
        });
        data.CareerExams.Add(new CareerExamData
        {
            Id = "career-a",
            CourseId = "course-a",
            Name = "Analisi",
            Cfu = 6,
            Attempts = new ObservableCollection<ExamAttemptData>
            {
                new()
                {
                    Id = "attempt-a",
                    AttemptedAt = Start.AddHours(1),
                    Status = "Recorded",
                    Outcome = "Passed",
                    GradeDecision = "Accepted",
                    NumericGrade = 30,
                    IsHonors = true,
                    RecordedAt = Start.AddHours(2)
                }
            }
        });
        data.Exams.Add(new Exam
        {
            Id = "exam-a",
            CourseId = "course-a",
            CourseName = "Analisi",
            Name = "Appello invernale",
            Cfu = 6,
            EstimatedStudyHours = 20,
            PlannedDate = Start.UtcDateTime,
            PlannedSession = "Invernale"
        });

        return data;
    }

    private static StudyExportBundle Export(
        StudyData data,
        StudyExportFormat format,
        StudyExportRequest? request = null) =>
        new StudyDataExportService().Export(data, request ?? Request, format, Start.AddDays(1));

    private static string Csv(StudyExportBundle bundle, string fileName) =>
        Encoding.UTF8.GetString(Assert.Single(bundle.Files.Where(x => x.FileName == fileName)).Content);

    private static string WorkbookXml(StudyExportFile file)
    {
        using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/workbook.xml")!.Open());
        return reader.ReadToEnd();
    }

    private static string WorksheetXml(StudyExportFile file)
    {
        using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        return string.Join(Environment.NewLine, archive.Entries
            .Where(x => x.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal))
            .Select(entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }));
    }

    [Fact]
    public void CsvBundleIncludesStudyLabAndAcademicRecords()
    {
        var bundle = Export(CreateData(), StudyExportFormat.Csv);
        var names = bundle.Files.Select(x => x.FileName).ToHashSet(StringComparer.Ordinal);

        foreach (var name in new[]
        {
            "tasks.csv", "missions.csv", "mission-steps.csv", "flashcards.csv",
            "flashcard-reviews.csv", "errors.csv", "energy-checkins.csv",
            "syllabus.csv", "questions.csv", "grades.csv", "career-attempts.csv",
            "exams.csv", "courses.csv"
        })
            Assert.Contains(name, names);

        Assert.Contains("TASK_TITLE", Csv(bundle, "tasks.csv"));
        Assert.Contains("MISSION_STEP_TITLE", Csv(bundle, "mission-steps.csv"));
        Assert.Contains("FLASHCARD_FRONT", Csv(bundle, "flashcards.csv"));
        Assert.Contains("ERROR_QUESTION", Csv(bundle, "errors.csv"));
        Assert.Contains("QUESTION_TEXT", Csv(bundle, "questions.csv"));
        Assert.Contains("30", Csv(bundle, "grades.csv"));
    }

    [Fact]
    public void XlsxContainsDedicatedSheetsForStudyLabAndAcademicRecords()
    {
        var file = Assert.Single(Export(CreateData(), StudyExportFormat.Xlsx).Files);
        var workbook = WorkbookXml(file);

        foreach (var sheet in new[]
        {
            "Task", "Missioni", "Passi missioni", "Flashcard", "Ripassi", "Errori",
            "Check-in", "Syllabus", "Domande", "Voti", "Tentativi", "Appelli planner", "Materie"
        })
            Assert.Contains($"name=\"{sheet}\"", workbook, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonExportIncludesStudyLabAndAcademicCollections()
    {
        using var document = JsonDocument.Parse(
            Export(CreateData(), StudyExportFormat.Json).Files.Single().Content);
        var root = document.RootElement;

        Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(2, root.GetProperty("tasks").GetArrayLength());
        Assert.Equal(2, root.GetProperty("missions").GetArrayLength());
        Assert.Equal(2, root.GetProperty("flashcards").GetArrayLength());
        Assert.Single(root.GetProperty("flashcardReviews").EnumerateArray());
        Assert.Equal(2, root.GetProperty("errors").GetArrayLength());
        Assert.Single(root.GetProperty("energyCheckIns").EnumerateArray());
        Assert.Single(root.GetProperty("syllabusTopics").EnumerateArray());
        Assert.Equal(2, root.GetProperty("questions").GetArrayLength());
        Assert.Equal(2, root.GetProperty("grades").GetArrayLength());
        Assert.Single(root.GetProperty("careerAttempts").EnumerateArray());
        Assert.Single(root.GetProperty("exams").EnumerateArray());
        Assert.Equal(2, root.GetProperty("courses").GetArrayLength());
    }

    [Fact]
    public void CourseFilterExcludesOtherCoursesAndGlobalCheckIns()
    {
        var request = Request with { CourseIds = new[] { "course-a" } };
        var bundle = Export(CreateData(), StudyExportFormat.Csv, request);

        Assert.DoesNotContain("TASK_B", Csv(bundle, "tasks.csv"), StringComparison.Ordinal);
        Assert.DoesNotContain("MISSION_B", Csv(bundle, "missions.csv"), StringComparison.Ordinal);
        Assert.DoesNotContain("FLASHCARD_B", Csv(bundle, "flashcards.csv"), StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR_B_QUESTION", Csv(bundle, "errors.csv"), StringComparison.Ordinal);
        Assert.DoesNotContain("QUESTION_B", Csv(bundle, "questions.csv"), StringComparison.Ordinal);
        Assert.DoesNotContain("Fisica", Csv(bundle, "grades.csv"), StringComparison.Ordinal);
        Assert.DoesNotContain("Fisica", Csv(bundle, "courses.csv"), StringComparison.Ordinal);
        Assert.DoesNotContain("ENERGY_PRIVATE_NOTE", Csv(bundle, "energy-checkins.csv"), StringComparison.Ordinal);
    }

    [Fact]
    public void DateRangeIncludesTasksDueInWindowButExcludesUnrelatedOldTasks()
    {
        var data = CreateData();
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-due-in-range",
            CourseId = "course-a",
            Title = "TASK_DUE_IN_RANGE",
            CreatedAt = Start.AddDays(-10),
            UpdatedAt = Start.AddDays(-10),
            DueAt = Start.AddMinutes(1)
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-outside",
            CourseId = "course-a",
            Title = "TASK_OUTSIDE_RANGE",
            CreatedAt = Start.AddDays(-10),
            UpdatedAt = Start.AddDays(-10),
            DueAt = Start.AddDays(-9)
        });

        var csv = Csv(Export(data, StudyExportFormat.Csv), "tasks.csv");

        Assert.Contains("TASK_DUE_IN_RANGE", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("TASK_OUTSIDE_RANGE", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludeNotesFalseOmitsStudyTextAndFreeFormNotes()
    {
        using var document = JsonDocument.Parse(
            Export(CreateData(), StudyExportFormat.Json, Request with { IncludeNotes = false })
                .Files.Single().Content);
        var json = document.RootElement.GetRawText();

        foreach (var privateText in new[]
        {
            "TASK_DESCRIPTION", "MISSION_DESCRIPTION", "MISSION_REWARD", "MISSION_STEP_TITLE",
            "FLASHCARD_FRONT", "FLASHCARD_BACK", "ERROR_QUESTION", "ERROR_INCORRECT",
            "ERROR_CORRECT", "ERROR_ROOT_CAUSE", "ENERGY_PRIVATE_NOTE", "SYLLABUS_DESCRIPTION",
            "QUESTION_TEXT", "QUESTION_OPTION_A", "QUESTION_EXPLANATION", "COURSE_PRIVATE_NOTE",
            "COURSE_SYLLABUS_SUMMARY"
        })
            Assert.DoesNotContain(privateText, json, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludeResultFalseOmitsGradesAndCorrectAnswers()
    {
        using var document = JsonDocument.Parse(
            Export(CreateData(), StudyExportFormat.Json, Request with { IncludeResult = false })
                .Files.Single().Content);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("grades")[0].GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("grades")[0].GetProperty("honors").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("careerAttempts")[0].GetProperty("numericGrade").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("careerAttempts")[0].GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("questions")[0].GetProperty("correctAnswer").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("errors")[0].GetProperty("correctAnswer").ValueKind);
    }

    [Fact]
    public void CsvFormulaGuardProtectsNewUserSuppliedFields()
    {
        var data = CreateData();
        data.StudyTasks[0].Title = "=HYPERLINK(\"https://example.invalid\",\"open\")";
        data.StudyMissions[0].Title = "@SUM(1,1)";
        data.Flashcards[0].Front = "+SUM(1,1)";
        data.ErrorJournalEntries[0].Topic = "-2+2";
        data.EnergyCheckIns[0].Note = "=cmd|' /C calc'!A0";
        data.Questions[0].Text = "=WEBSERVICE(\"https://example.invalid\")";

        var bundle = Export(data, StudyExportFormat.Csv, Request with { IncludePrivateNotes = true });

        Assert.Contains("'=HYPERLINK", Csv(bundle, "tasks.csv"), StringComparison.Ordinal);
        Assert.Contains("'@SUM", Csv(bundle, "missions.csv"), StringComparison.Ordinal);
        Assert.Contains("'+SUM", Csv(bundle, "flashcards.csv"), StringComparison.Ordinal);
        Assert.Contains("'-2+2", Csv(bundle, "errors.csv"), StringComparison.Ordinal);
        Assert.Contains("'=cmd|", Csv(bundle, "energy-checkins.csv"), StringComparison.Ordinal);
        Assert.Contains("'=WEBSERVICE", Csv(bundle, "questions.csv"), StringComparison.Ordinal);
    }

    [Fact]
    public void XlsxStoresUserSuppliedTextAsInlineStringsNotFormulas()
    {
        var data = CreateData();
        data.StudyTasks[0].Title = "=HYPERLINK(\"https://example.invalid\",\"open\")";
        var file = Assert.Single(Export(data, StudyExportFormat.Xlsx).Files);
        var worksheets = WorksheetXml(file);

        Assert.Contains("=HYPERLINK", worksheets, StringComparison.Ordinal);
        Assert.Contains("t=\"inlineStr\"", worksheets, StringComparison.Ordinal);
        Assert.DoesNotContain("<f>", worksheets, StringComparison.Ordinal);
    }

    [Fact]
    public void OrphanFlashcardReviewsAreNotExported()
    {
        using var document = JsonDocument.Parse(
            Export(CreateData(), StudyExportFormat.Json).Files.Single().Content);

        var reviews = document.RootElement.GetProperty("flashcardReviews");
        Assert.Equal(1, reviews.GetArrayLength());
        Assert.Equal("review-a", reviews[0].GetProperty("id").GetString());
    }

    [Fact]
    public void EnergyCheckInNotesArePrivateUnlessExplicitlyIncluded()
    {
        var data = CreateData();
        var defaultExport = Encoding.UTF8.GetString(
            Export(data, StudyExportFormat.Json).Files.Single().Content);
        var optedInExport = Encoding.UTF8.GetString(
            Export(data, StudyExportFormat.Json, Request with { IncludePrivateNotes = true }).Files.Single().Content);

        Assert.DoesNotContain("ENERGY_PRIVATE_NOTE", defaultExport, StringComparison.Ordinal);
        Assert.Contains("ENERGY_PRIVATE_NOTE", optedInExport, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonExportDoesNotIncludePdfTextMoodleUrlOrAuditSnapshots()
    {
        var data = CreateData();
        data.MoodleBaseUrl = "https://moodle-secret.example.invalid";
        data.Courses[0].SyllabusUrl = "https://moodle-secret.example.invalid/course?token=course-token";
        data.SyllabusTopics[0].SourceUrl = "https://moodle-secret.example.invalid/topic?token=topic-token";
        data.PdfDocuments.Add(new PdfDocumentData
        {
            FileName = "personal-document.pdf",
            Pages = new ObservableCollection<PdfPageData>
            {
                new() { PageNumber = 1, Text = "PDF_PRIVATE_TEXT" }
            }
        });
        data.ActivityRevisions.Add(new ActivityRevisionData
        {
            Id = "revision-a",
            ActivityId = "activity-a",
            BeforeJson = "REVISION_PRIVATE_SNAPSHOT",
            AfterJson = "REVISION_PRIVATE_AFTER"
        });

        var json = Export(data, StudyExportFormat.Json).Files.Single().Content;
        var text = Encoding.UTF8.GetString(json);

        Assert.DoesNotContain("moodle-secret.example.invalid", text, StringComparison.Ordinal);
        Assert.DoesNotContain("course-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("topic-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PDF_PRIVATE_TEXT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("personal-document.pdf", text, StringComparison.Ordinal);
        Assert.DoesNotContain("REVISION_PRIVATE_SNAPSHOT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("REVISION_PRIVATE_AFTER", text, StringComparison.Ordinal);
    }
}
