using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class CourseRemovalSafetyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static StudyData Data() => new()
    {
        DegreeTotalCfu = 180,
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "course-1", Name = "Analisi", Cfu = 6 },
            new() { Id = "course-2", Name = "Fisica", Cfu = 6 }
        }
    };

    private static StudyData WithAllDependents()
    {
        var data = Data();
        data.Grades.Add(new Grade
        {
            Id = "grade-1", CourseId = "course-1", CourseName = "Analisi",
            Value = 24, Cfu = 6, Date = Now.UtcDateTime
        });
        data.Exams.Add(new Exam
        {
            Id = "exam-1", CourseId = "course-1", CourseName = "Analisi",
            Name = "Appello", Cfu = 6
        });
        data.CareerExams.Add(new CareerExamData
        {
            Id = "career-1", CourseId = "course-1", Name = "Analisi", Cfu = 6,
            Attempts = new ObservableCollection<ExamAttemptData>
            {
                new()
                {
                    Id = "attempt-1", AttemptedAt = Now, Status = "Scheduled",
                    Outcome = "Pending", GradeDecision = "Pending"
                }
            }
        });
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "topic-1", CourseId = "course-1", Title = "Limiti"
        });
        data.CoursePrerequisites.Add(new CoursePrerequisiteData
        {
            CourseId = "course-2", PrerequisiteCourseId = "course-1"
        });
        data.Questions.Add(new QuestionBankItemData
        {
            Id = "question-1", CourseId = "course-1", SyllabusTopicId = "topic-1",
            Text = "Definisci un limite."
        });
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-1", CourseId = "course-1", State = StudySessionState.Completed,
            StartedAt = Now, EndedAt = Now.AddMinutes(25), LastResumedAt = null,
            AccumulatedFocusSeconds = 1500,
            Segments = new ObservableCollection<StudySessionSegmentData>
            {
                new()
                {
                    Id = "segment-1", Type = StudySessionSegmentType.Focus,
                    StartedAt = Now, EndedAt = Now.AddMinutes(25)
                }
            }
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-1", CourseId = "course-1", Title = "Esercizi"
        });
        data.StudyMissions.Add(new StudyMissionData
        {
            Id = "mission-1", CourseId = "course-1", SyllabusTopicId = "topic-1",
            Title = "Ripasso limiti",
            Steps = new ObservableCollection<MissionStepData>
            {
                new() { Id = "step-1", Title = "Rivedi la teoria" }
            }
        });
        // This activity has no CourseId: it is still dependent through its
        // topic, task, mission, exam and focus-session references.
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "activity-1", SyllabusTopicId = "topic-1", TaskId = "task-1",
            MissionId = "mission-1", ExamId = "exam-1", SessionId = "session-1",
            Title = "Sessione collegata", StartedAt = Now,
            Status = StudyActivityStatus.Cancelled
        });
        data.ActivityRevisions.Add(new ActivityRevisionData
        {
            Id = "revision-1", ActivityId = "activity-1",
            Action = ActivityRevisionAction.Updated, Reason = "Correzione",
            BeforeJson = "{}", AfterJson = "{}", RevisedAt = Now
        });
        data.Flashcards.Add(new FlashcardData
        {
            Id = "card-1", CourseId = "course-1", SyllabusTopicId = "topic-1",
            Front = "Domanda", Back = "Risposta"
        });
        data.FlashcardReviews.Add(new FlashcardReviewData
        {
            Id = "review-1", FlashcardId = "card-1"
        });
        data.ErrorJournalEntries.Add(new ErrorJournalEntryData
        {
            Id = "error-1", CourseId = "course-1", SyllabusTopicId = "topic-1",
            Question = "Domanda", CorrectAnswer = "Risposta"
        });
        data.Distractions.Add(new DistractionData
        {
            Id = "distraction-1", SessionId = "session-1", Text = "Controllare il telefono"
        });
        data.PomodoroCycles.Add(new PomodoroCycleData
        {
            Id = "cycle-1", SessionId = "session-1", CycleNumber = 1,
            FocusStartedAt = Now, FocusEndedAt = Now.AddMinutes(25), FocusSeconds = 1500
        });
        data.PomodoroTimer = new PomodoroTimerData
        {
            Id = "timer-1", SessionId = "session-1", Status = PomodoroTimerStatus.Completed,
            Phase = PomodoroPhase.Focus, PhaseWindowStartedAt = Now,
            PhaseStartedAt = Now, ActiveSeconds = 1500
        };
        data.PdfDocuments.Add(new PdfDocumentData
        {
            Id = "pdf-1", CourseId = "course-1", FileName = "appunti.pdf",
            Pages = new ObservableCollection<PdfPageData>
            {
                new() { PageNumber = 1, Text = "Definizione di limite." }
            }
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "calendar-1", CourseId = "course-1", CourseName = "Analisi",
            Title = "Appello", StartsAt = Now, EndsAt = Now.AddHours(1)
        });
        return data;
    }

    [Fact]
    public void ImpactPreviewCountsAcademicStudyAndNestedDependents()
    {
        var data = WithAllDependents();
        var impact = new AcademicProfileService().GetCourseRemovalImpact(data, "course-1");

        Assert.Equal(1, impact.GradeRecords);
        Assert.Equal(1, impact.PlannedExams);
        Assert.Equal(1, impact.CareerExams);
        Assert.Equal(1, impact.CareerExamAttempts);
        Assert.Equal(1, impact.SyllabusTopics);
        Assert.Equal(1, impact.PrerequisiteLinks);
        Assert.Equal(1, impact.Questions);
        Assert.Equal(1, impact.StudySessions);
        Assert.Equal(1, impact.StudySessionSegments);
        Assert.Equal(1, impact.StudyTasks);
        Assert.Equal(1, impact.StudyActivities);
        Assert.Equal(1, impact.StudyMissions);
        Assert.Equal(1, impact.MissionSteps);
        Assert.Equal(1, impact.Flashcards);
        Assert.Equal(1, impact.FlashcardReviews);
        Assert.Equal(1, impact.ErrorJournalEntries);
        Assert.Equal(1, impact.Distractions);
        Assert.Equal(1, impact.PomodoroCycles);
        Assert.Equal(1, impact.PdfDocuments);
        Assert.Equal(1, impact.PdfPages);
        Assert.Equal(1, impact.CalendarEventsToDetach);
        Assert.Equal(1, impact.ActivityRevisions);
        Assert.True(impact.HasAcademicHistory);
        Assert.True(impact.HasStudyHistory);
        Assert.False(impact.HasActiveWork);
        Assert.False(impact.CanRemoveWithoutConfirmation);
    }

    [Fact]
    public void AcademicHistoryRequiresItsOwnConfirmationWithoutPartialMutation()
    {
        var data = Data();
        data.Grades.Add(new Grade { Id = "grade-1", CourseId = "course-1", Value = 24, Cfu = 6 });
        data.CoursePrerequisites.Add(new CoursePrerequisiteData
        {
            CourseId = "course-2", PrerequisiteCourseId = "course-1"
        });

        Assert.Throws<InvalidOperationException>(() =>
            new AcademicProfileService().RemoveCourse(data, "course-1"));

        Assert.Contains(data.Courses, x => x.Id == "course-1");
        Assert.Single(data.Grades);
        Assert.Single(data.CoursePrerequisites);
    }

    [Fact]
    public void StudyLabHistoryRequiresSeparateConfirmationEvenAfterCareerConfirmation()
    {
        var data = Data();
        data.Questions.Add(new QuestionBankItemData
        {
            Id = "question-1", CourseId = "course-1", Text = "Domanda"
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-1", CourseId = "course-1", Title = "Esercizi"
        });

        Assert.Throws<InvalidOperationException>(() =>
            new AcademicProfileService().RemoveCourse(
                data, "course-1", removeCareerHistory: true));

        Assert.Contains(data.Courses, x => x.Id == "course-1");
        Assert.Single(data.Questions);
        Assert.Single(data.StudyTasks);
    }

    [Fact]
    public void CalendarEventsRequireExplicitDetachmentAndArePreserved()
    {
        var data = Data();
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "calendar-1", CourseId = "course-1", Title = "Appello",
            StartsAt = Now, EndsAt = Now.AddHours(1)
        });
        var service = new AcademicProfileService();

        Assert.Throws<InvalidOperationException>(() => service.RemoveCourse(data, "course-1"));
        Assert.Equal("course-1", data.CalendarEvents[0].CourseId);

        service.RemoveCourse(data, "course-1", detachCalendarEvents: true);

        Assert.DoesNotContain(data.Courses, x => x.Id == "course-1");
        Assert.Single(data.CalendarEvents);
        Assert.Null(data.CalendarEvents[0].CourseId);
        Assert.Equal("Analisi", data.CalendarEvents[0].CourseName);
    }

    [Fact]
    public void ConfirmedRemovalCleansEveryDependentAndLeavesValidData()
    {
        var data = WithAllDependents();
        var service = new AcademicProfileService();

        service.RemoveCourse(
            data,
            "course-1",
            removeCareerHistory: true,
            removeStudyHistory: true,
            detachCalendarEvents: true);

        Assert.DoesNotContain(data.Courses, x => x.Id == "course-1");
        Assert.Contains(data.Courses, x => x.Id == "course-2");
        Assert.Empty(data.Grades);
        Assert.Empty(data.Exams);
        Assert.Empty(data.CareerExams);
        Assert.Empty(data.SyllabusTopics);
        Assert.Empty(data.CoursePrerequisites);
        Assert.Empty(data.Questions);
        Assert.Empty(data.StudySessions);
        Assert.Empty(data.StudyTasks);
        Assert.Empty(data.StudyActivities);
        Assert.Empty(data.StudyMissions);
        Assert.Empty(data.ActivityRevisions);
        Assert.Empty(data.Flashcards);
        Assert.Empty(data.FlashcardReviews);
        Assert.Empty(data.ErrorJournalEntries);
        Assert.Empty(data.Distractions);
        Assert.Empty(data.PomodoroCycles);
        Assert.Empty(data.PdfDocuments);
        Assert.Null(data.PomodoroTimer);
        Assert.Single(data.CalendarEvents);
        Assert.Null(data.CalendarEvents[0].CourseId);
        Assert.Equal("Analisi", data.CalendarEvents[0].CourseName);

        StudyDataEntityMapper.NormalizeAndValidate(data);
    }

    [Fact]
    public void RunningFocusSessionBlocksRemovalEvenWithAllConfirmations()
    {
        var data = Data();
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-1", CourseId = "course-1",
            State = StudySessionState.Running, StartedAt = Now, LastResumedAt = Now
        });

        Assert.Throws<InvalidOperationException>(() =>
            new AcademicProfileService().RemoveCourse(
                data, "course-1", true, true, true));

        Assert.Contains(data.Courses, x => x.Id == "course-1");
        Assert.Single(data.StudySessions);
    }

    [Fact]
    public void PausedFocusSessionAlsoBlocksRemoval()
    {
        var data = Data();
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-1", CourseId = "course-1",
            State = StudySessionState.Paused, StartedAt = Now,
            PausedAt = Now.AddMinutes(10), LastResumedAt = null
        });

        Assert.Throws<InvalidOperationException>(() =>
            new AcademicProfileService().RemoveCourse(
                data, "course-1", true, true, true));

        Assert.Contains(data.Courses, x => x.Id == "course-1");
        Assert.Single(data.StudySessions);
    }

    [Fact]
    public void ActivePomodoroBlocksRemovalEvenWhenFocusSessionIsCompleted()
    {
        var data = Data();
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-1", CourseId = "course-1",
            State = StudySessionState.Completed, StartedAt = Now,
            EndedAt = Now.AddMinutes(25)
        });
        data.PomodoroTimer = new PomodoroTimerData
        {
            Id = "timer-1", SessionId = "session-1",
            Status = PomodoroTimerStatus.Running, Phase = PomodoroPhase.Focus,
            PhaseWindowStartedAt = Now, PhaseStartedAt = Now
        };

        Assert.Throws<InvalidOperationException>(() =>
            new AcademicProfileService().RemoveCourse(
                data, "course-1", true, true, true));

        Assert.Contains(data.Courses, x => x.Id == "course-1");
        Assert.Single(data.StudySessions);
        Assert.NotNull(data.PomodoroTimer);
    }

    [Fact]
    public void UnrelatedCourseRecordsAreNotIncludedOrRemoved()
    {
        var data = Data();
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-2", CourseId = "course-2", Title = "Fisica"
        });
        var service = new AcademicProfileService();

        var impact = service.GetCourseRemovalImpact(data, "course-1");
        service.RemoveCourse(data, "course-1");

        Assert.Equal(0, impact.StudyTasks);
        Assert.True(impact.CanRemoveWithoutConfirmation);
        Assert.Single(data.StudyTasks);
        Assert.Equal("course-2", data.StudyTasks[0].CourseId);
    }

    [Fact]
    public void ImpactPreviewIsStableAndDoesNotMutateTheSnapshot()
    {
        var data = WithAllDependents();
        var service = new AcademicProfileService();
        var courseCount = data.Courses.Count;
        var activityCount = data.StudyActivities.Count;
        var first = service.GetCourseRemovalImpact(data, "course-1");
        var second = service.GetCourseRemovalImpact(data, "course-1");

        Assert.Equal(first, second);
        Assert.Equal(courseCount, data.Courses.Count);
        Assert.Equal(activityCount, data.StudyActivities.Count);
        Assert.Equal("course-1", data.CalendarEvents[0].CourseId);
    }

    [Fact]
    public void MissingCourseIsRejectedByPreviewAndRemoval()
    {
        var data = Data();
        var service = new AcademicProfileService();

        Assert.Throws<ArgumentException>(() => service.GetCourseRemovalImpact(data, "missing"));
        Assert.Throws<ArgumentException>(() => service.RemoveCourse(data, "missing"));
        Assert.Equal(2, data.Courses.Count);
    }

    [Fact]
    public void AllConfirmationsAreCheckedBeforeAnyCollectionChanges()
    {
        var data = Data();
        data.Grades.Add(new Grade { Id = "grade-1", CourseId = "course-1", Value = 24, Cfu = 6 });
        data.Questions.Add(new QuestionBankItemData
        {
            Id = "question-1", CourseId = "course-1", Text = "Domanda"
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "calendar-1", CourseId = "course-1", Title = "Appello",
            StartsAt = Now, EndsAt = Now.AddHours(1)
        });

        Assert.Throws<InvalidOperationException>(() =>
            new AcademicProfileService().RemoveCourse(
                data, "course-1",
                removeCareerHistory: true,
                removeStudyHistory: true,
                detachCalendarEvents: false));

        Assert.Contains(data.Courses, x => x.Id == "course-1");
        Assert.Single(data.Grades);
        Assert.Single(data.Questions);
        Assert.Equal("course-1", data.CalendarEvents[0].CourseId);
    }

    [Fact]
    public void ActiveTimerForAnotherCourseDoesNotBlockThisRemoval()
    {
        var data = Data();
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-2", CourseId = "course-2",
            State = StudySessionState.Completed, StartedAt = Now,
            EndedAt = Now.AddMinutes(25)
        });
        data.PomodoroTimer = new PomodoroTimerData
        {
            Id = "timer-1", SessionId = "session-2",
            Status = PomodoroTimerStatus.Running, Phase = PomodoroPhase.Focus,
            PhaseWindowStartedAt = Now, PhaseStartedAt = Now
        };

        new AcademicProfileService().RemoveCourse(data, "course-1");

        Assert.DoesNotContain(data.Courses, x => x.Id == "course-1");
        Assert.Contains(data.Courses, x => x.Id == "course-2");
        Assert.NotNull(data.PomodoroTimer);
        Assert.Equal("session-2", data.PomodoroTimer!.SessionId);
    }
}
