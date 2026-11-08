using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class AcademicPersistenceTests
{
    [Fact]
    public async Task AcademicProfileCoursesPrerequisitesSyllabusAndQuestionsRoundTrip()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var data = new StudyData
        {
            DegreeProgramName = "Informatica",
            DegreeProgramCode = "L-31",
            EnrollmentYear = 2024,
            CurrentAcademicYear = 2,
            DegreeTotalCfu = 180,
            WeeklyStudyHours = 12,
            MaxDailyStudyMinutes = 180,
            StudyPreferences = "Sessione mattutina",
            Courses = new ObservableCollection<Course>
            {
                new()
                {
                    Id = "analisi",
                    Name = "Analisi",
                    Code = "MAT1",
                    Cfu = 9,
                    Teacher = "Prof. Rossi",
                    AcademicYear = "2025/26",
                    Session = "Invernale 2027",
                    EstimatedStudyHours = 80,
                    AppealDate = new DateTime(2027, 1, 20),
                    SyllabusSummary = "Limiti, derivate e integrali",
                    SyllabusUrl = "https://example.edu/analisi",
                    Notes = "Portare formulario",
                    Source = "manuale",
                    Status = CourseStatus.InProgress
                },
                new()
                {
                    Id = "fisica",
                    Name = "Fisica",
                    Code = "FIS1",
                    Cfu = 9,
                    Status = CourseStatus.NotStarted
                }
            },
            CoursePrerequisites = new ObservableCollection<CoursePrerequisiteData>
            {
                new() { CourseId = "fisica", PrerequisiteCourseId = "analisi" }
            },
            SyllabusTopics = new ObservableCollection<SyllabusTopicData>
            {
                new()
                {
                    Id = "topic-1",
                    CourseId = "analisi",
                    Title = "Limiti",
                    Description = "Definizione e calcolo",
                    Status = SyllabusTopicStatus.Completed,
                    EstimatedMinutes = 120,
                    SourceUrl = "https://example.edu/analisi#limiti",
                    LastStudiedAt = new DateTime(2026, 10, 1),
                    UpdatedAt = new DateTime(2026, 10, 1)
                }
            },
            Questions = new ObservableCollection<QuestionBankItemData>
            {
                new()
                {
                    Id = "question-1",
                    CourseId = "analisi",
                    SyllabusTopicId = "topic-1",
                    Text = "Qual è la derivata di x^2?",
                    Type = QuestionType.Closed,
                    Difficulty = "Facile",
                    Options = new ObservableCollection<string> { "2x", "x", "x^2" },
                    CorrectAnswer = "2x",
                    Explanation = "Regola della potenza.",
                    Source = "Manuale",
                    IsOfficial = false,
                    CreatedAt = new DateTime(2026, 10, 1),
                    UpdatedAt = new DateTime(2026, 10, 1)
                }
            },
            CareerExams = new ObservableCollection<CareerExamData>
            {
                new()
                {
                    Id = "career-analisi",
                    CourseId = "analisi",
                    Name = "Analisi",
                    Cfu = 9,
                    CountsTowardAverage = true,
                    Attempts = new ObservableCollection<ExamAttemptData>
                    {
                        new()
                        {
                            Id = "attempt-1",
                            AttemptedAt = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
                            Status = nameof(AttemptStatus.Completed),
                            Outcome = nameof(ExamOutcome.Passed),
                            GradeDecision = nameof(GradeDecision.Accepted),
                            NumericGrade = 28,
                            RecordedAt = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero)
                        }
                    }
                }
            }
        };

        await store.SaveAsync(data);
        var loaded = await store.LoadAsync();

        Assert.Equal("Informatica", loaded.DegreeProgramName);
        Assert.Equal("L-31", loaded.DegreeProgramCode);
        Assert.Equal(180, loaded.DegreeTotalCfu);
        Assert.Equal(12, loaded.WeeklyStudyHours);
        Assert.Equal("Sessione mattutina", loaded.StudyPreferences);
        Assert.Equal(2, loaded.Courses.Count);
        Assert.Equal("Prof. Rossi", loaded.Courses.Single(x => x.Id == "analisi").Teacher);
        Assert.Equal(CourseStatus.InProgress, loaded.Courses.Single(x => x.Id == "analisi").Status);
        Assert.Single(loaded.CoursePrerequisites);
        Assert.Single(loaded.SyllabusTopics);
        Assert.Equal(SyllabusTopicStatus.Completed, loaded.SyllabusTopics[0].Status);
        Assert.Single(loaded.Questions);
        Assert.Equal(new[] { "2x", "x", "x^2" }, loaded.Questions[0].Options);
        Assert.Single(loaded.CareerExams);
        Assert.Equal(28, loaded.CareerExams[0].Attempts.Single().NumericGrade);
    }

    [Fact]
    public void LegacyGradeIsProjectedIntoSingleCareerExamOnlyOnce()
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "course-1", Name = "Analisi", Cfu = 6, Completed = true }
            },
            Grades = new ObservableCollection<Grade>
            {
                new()
                {
                    Id = "grade-1",
                    CourseId = "course-1",
                    CourseName = "Analisi",
                    Cfu = 6,
                    Value = 27,
                    Date = new DateTime(2026, 1, 10)
                }
            }
        };

        StudyDataEntityMapper.NormalizeAndValidate(data);
        StudyDataEntityMapper.NormalizeAndValidate(data);

        var careerExam = Assert.Single(data.CareerExams);
        var attempt = Assert.Single(careerExam.Attempts);
        Assert.Equal(27, attempt.NumericGrade);
        Assert.Equal(6, careerExam.Cfu);
        Assert.Equal(CourseStatus.Passed, data.Courses[0].Status);
        Assert.True(data.Courses[0].Completed);
    }
}
