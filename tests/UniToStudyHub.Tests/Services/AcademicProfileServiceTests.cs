using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class AcademicProfileServiceTests
{
    private static StudyData Data() => new()
    {
        GraduationTarget = new DateTime(2027, 10, 1),
        DegreeTotalCfu = 180,
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "course-1", Name = "Analisi", Code = "MAT1", Cfu = 6 },
            new() { Id = "course-2", Name = "Fisica", Code = "FIS1", Cfu = 9 }
        }
    };

    [Fact]
    public void UpdateNormalizesAndValidatesProfile()
    {
        var data = Data();
        var service = new AcademicProfileService();

        service.Update(data, new AcademicProfileUpdate(
            "  Informatica  ", " L-31 ", 2024, 2, 180,
            new DateTime(2028, 7, 1), "  Studiare la mattina  ", 18, 180));

        Assert.Equal("Informatica", data.DegreeProgramName);
        Assert.Equal("L-31", data.DegreeProgramCode);
        Assert.Equal(2024, data.EnrollmentYear);
        Assert.Equal(2, data.CurrentAcademicYear);
        Assert.Equal(180, data.DegreeTotalCfu);
        Assert.Equal("Studiare la mattina", data.StudyPreferences);
        Assert.Equal(18, data.WeeklyStudyHours);
        Assert.Equal(180, data.MaxDailyStudyMinutes);
    }

    [Fact]
    public void UpdateRejectsInvalidCapacityAndCfu()
    {
        var service = new AcademicProfileService();
        var data = Data();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Update(data, new AcademicProfileUpdate(
            "Informatica", "", 2024, 2, 180, DateTime.Today, "", 169, 180)));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Update(data, new AcademicProfileUpdate(
            "Informatica", "", 2024, 2, 10, DateTime.Today, "", 20, 240)));
    }

    [Fact]
    public void CourseCanBeArchivedAndRestoredWithoutLosingPassedState()
    {
        var data = Data();
        var service = new AcademicProfileService();
        service.SetStatus(data, "course-1", CourseStatus.Passed);

        service.Archive(data, "course-1");
        Assert.True(data.Courses[0].Archived);
        Assert.True(data.Courses[0].Completed);
        Assert.Equal(CourseStatus.Archived, data.Courses[0].Status);

        service.Restore(data, "course-1");
        Assert.False(data.Courses[0].Archived);
        Assert.Equal(CourseStatus.Passed, data.Courses[0].Status);
    }

    [Fact]
    public void CourseWithCareerHistoryRequiresExplicitDeletionConfirmation()
    {
        var data = Data();
        data.CareerExams.Add(new CareerExamData
        {
            Id = "career-1",
            CourseId = "course-1",
            Name = "Analisi",
            Cfu = 6
        });
        var service = new AcademicProfileService();

        Assert.Throws<InvalidOperationException>(() => service.RemoveCourse(data, "course-1"));

        service.RemoveCourse(data, "course-1", removeCareerHistory: true);
        Assert.DoesNotContain(data.Courses, x => x.Id == "course-1");
        Assert.DoesNotContain(data.CareerExams, x => x.CourseId == "course-1");
    }

    [Fact]
    public void CourseCodeMustRemainUnique()
    {
        var data = Data();
        var service = new AcademicProfileService();

        Assert.Throws<ArgumentException>(() => service.AddCourse(data, new Course
        {
            Name = "Altro",
            Code = "MAT1",
            Cfu = 6
        }));
    }
}
