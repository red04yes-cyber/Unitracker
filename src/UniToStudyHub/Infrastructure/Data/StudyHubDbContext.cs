using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data.Entities;

namespace UniToStudyHub.Infrastructure.Data;

public sealed class StudyHubDbContext(DbContextOptions<StudyHubDbContext> options) : DbContext(options)
{
    public DbSet<ProfileSettingsEntity> ProfileSettings => Set<ProfileSettingsEntity>();
    public DbSet<CourseEntity> Courses => Set<CourseEntity>();
    public DbSet<GradeEntity> Grades => Set<GradeEntity>();
    public DbSet<PlannedExamEntity> PlannedExams => Set<PlannedExamEntity>();
    public DbSet<AnnouncementEntity> Announcements => Set<AnnouncementEntity>();
    public DbSet<CareerExamEntity> CareerExams => Set<CareerExamEntity>();
    public DbSet<ExamAttemptEntity> ExamAttempts => Set<ExamAttemptEntity>();
    public DbSet<CoursePrerequisiteEntity> CoursePrerequisites => Set<CoursePrerequisiteEntity>();
    public DbSet<SyllabusTopicEntity> SyllabusTopics => Set<SyllabusTopicEntity>();
    public DbSet<QuestionBankItemEntity> QuestionBankItems => Set<QuestionBankItemEntity>();
    public DbSet<CalendarEventEntity> CalendarEvents => Set<CalendarEventEntity>();
    public DbSet<StudySessionEntity> StudySessions => Set<StudySessionEntity>();
    public DbSet<StudySessionSegmentEntity> StudySessionSegments => Set<StudySessionSegmentEntity>();
    public DbSet<StudyTaskEntity> StudyTasks => Set<StudyTaskEntity>();
    public DbSet<ErrorJournalEntryEntity> ErrorJournalEntries => Set<ErrorJournalEntryEntity>();
    public DbSet<FlashcardEntity> Flashcards => Set<FlashcardEntity>();
    public DbSet<FlashcardReviewEntity> FlashcardReviews => Set<FlashcardReviewEntity>();
    public DbSet<DistractionEntity> Distractions => Set<DistractionEntity>();
    public DbSet<EnergyCheckInEntity> EnergyCheckIns => Set<EnergyCheckInEntity>();
    public DbSet<StudyActivityEntity> StudyActivities => Set<StudyActivityEntity>();
    public DbSet<ActivityRevisionEntity> ActivityRevisions => Set<ActivityRevisionEntity>();
    public DbSet<StudyMissionEntity> StudyMissions => Set<StudyMissionEntity>();
    public DbSet<MissionStepEntity> MissionSteps => Set<MissionStepEntity>();
    public DbSet<PdfDocumentEntity> PdfDocuments => Set<PdfDocumentEntity>();
    public DbSet<LegacyImportEntity> LegacyImports => Set<LegacyImportEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProfileSettingsEntity>(b =>
        {
            b.ToTable("ProfileSettings");
            b.HasKey(x => x.Id);
            b.Property(x => x.MoodleBaseUrl).HasMaxLength(2048).IsRequired();
            b.Property(x => x.DegreeProgramName).HasMaxLength(200).IsRequired();
            b.Property(x => x.DegreeProgramCode).HasMaxLength(80).IsRequired();
            b.Property(x => x.StudyPreferences).HasMaxLength(4000).IsRequired();
            b.Property(x => x.DegreeTotalCfu).HasDefaultValue(0);
            b.Property(x => x.WeeklyStudyHours).HasDefaultValue(20);
            b.Property(x => x.MaxDailyStudyMinutes).HasDefaultValue(240);
        });

        modelBuilder.Entity<CourseEntity>(b =>
        {
            b.ToTable("Courses");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(300).IsRequired();
            b.Property(x => x.Code).HasDefaultValue(string.Empty).HasMaxLength(40).IsRequired();
            b.Property(x => x.Teacher).HasDefaultValue(string.Empty).HasMaxLength(200).IsRequired();
            b.Property(x => x.AcademicYear).HasDefaultValue(string.Empty).HasMaxLength(20).IsRequired();
            b.Property(x => x.Session).HasDefaultValue(string.Empty).HasMaxLength(80).IsRequired();
            b.Property(x => x.SyllabusUrl).HasDefaultValue(string.Empty).HasMaxLength(2048).IsRequired();
            b.Property(x => x.SyllabusSummary).HasDefaultValue(string.Empty).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Notes).HasDefaultValue(string.Empty).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Source).HasDefaultValue("manual").HasMaxLength(40).IsRequired();
            b.Property(x => x.Status).HasDefaultValue(0);
            b.Property(x => x.Archived).HasDefaultValue(false);
            b.Property(x => x.EstimatedStudyHours).HasDefaultValue(0);
            b.HasIndex(x => x.MoodleCourseId);
            b.HasIndex(x => x.Code);
        });

        modelBuilder.Entity<GradeEntity>(b =>
        {
            b.ToTable("Grades");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.CourseId);
            b.HasOne<CourseEntity>().WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlannedExamEntity>(b =>
        {
            b.ToTable("PlannedExams");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.CourseId);
            b.HasOne<CourseEntity>().WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AnnouncementEntity>(b =>
        {
            b.ToTable("Announcements");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.SourceKey);
        });

        modelBuilder.Entity<CareerExamEntity>(b =>
        {
            b.ToTable("CareerExams");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.CourseId).IsUnique();
            b.HasOne<CourseEntity>().WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Attempts)
                .WithOne()
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExamAttemptEntity>(b =>
        {
            b.ToTable("ExamAttempts");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.ExamId, x.AttemptedAt });
        });

        modelBuilder.Entity<CoursePrerequisiteEntity>(b =>
        {
            b.ToTable("CoursePrerequisites");
            b.HasKey(x => new { x.CourseId, x.PrerequisiteCourseId });
            b.HasIndex(x => x.PrerequisiteCourseId);
            b.HasOne<CourseEntity>().WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<CourseEntity>().WithMany()
                .HasForeignKey(x => x.PrerequisiteCourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SyllabusTopicEntity>(b =>
        {
            b.ToTable("SyllabusTopics");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.CourseId, x.Status });
            b.Property(x => x.Title).HasMaxLength(400).IsRequired();
            b.Property(x => x.Description).HasMaxLength(12000).IsRequired();
            b.Property(x => x.SourceUrl).HasMaxLength(2048).IsRequired();
            b.Property(x => x.Status).HasDefaultValue(0);
            b.HasOne<CourseEntity>().WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuestionBankItemEntity>(b =>
        {
            b.ToTable("QuestionBankItems");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.CourseId, x.SyllabusTopicId });
            b.Property(x => x.Text).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Difficulty).HasMaxLength(40).IsRequired();
            b.Property(x => x.OptionsJson).IsRequired();
            b.Property(x => x.CorrectAnswer).IsRequired();
            b.Property(x => x.Explanation).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Source).HasMaxLength(200).IsRequired();
            b.HasOne<CourseEntity>().WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CalendarEventEntity>(b =>
        {
            b.ToTable("CalendarEvents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(400).IsRequired();
            b.Property(x => x.Category).HasMaxLength(40).IsRequired();
            b.Property(x => x.CourseName).HasMaxLength(300).IsRequired();
            b.Property(x => x.Location).HasMaxLength(400).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Source).HasMaxLength(40).IsRequired();
            b.Property(x => x.Verification).HasMaxLength(40).IsRequired();
            b.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            b.Property(x => x.ExternalId).HasMaxLength(400);
            b.Property(x => x.RecurrenceRule).HasMaxLength(2000).IsRequired();
            b.HasIndex(x => x.StartsAt);
            b.HasIndex(x => new { x.Source, x.ExternalId });
        });

        modelBuilder.Entity<StudySessionEntity>(b =>
        {
            b.ToTable("StudySessions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Topic).HasMaxLength(400).IsRequired();
            b.Property(x => x.Outcome).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(12000).IsRequired();
            b.HasIndex(x => x.StartedAt);
            b.HasIndex(x => new { x.State, x.StartedAt });
            b.HasMany(x => x.Segments)
                .WithOne()
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StudySessionSegmentEntity>(b =>
        {
            b.ToTable("StudySessionSegments");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.SessionId, x.StartedAt });
        });

        modelBuilder.Entity<StudyTaskEntity>(b =>
        {
            b.ToTable("StudyTasks");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(400).IsRequired();
            b.Property(x => x.Description).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Source).HasMaxLength(40).IsRequired();
            b.HasIndex(x => new { x.Status, x.DueAt });
        });

        modelBuilder.Entity<ErrorJournalEntryEntity>(b =>
        {
            b.ToTable("ErrorJournalEntries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Topic).HasMaxLength(400).IsRequired();
            b.Property(x => x.Question).HasMaxLength(12000).IsRequired();
            b.Property(x => x.IncorrectAnswer).HasMaxLength(12000).IsRequired();
            b.Property(x => x.CorrectAnswer).HasMaxLength(12000).IsRequired();
            b.Property(x => x.ErrorType).HasMaxLength(80).IsRequired();
            b.Property(x => x.RootCause).HasMaxLength(4000).IsRequired();
            b.Property(x => x.Source).HasMaxLength(200).IsRequired();
            b.HasIndex(x => new { x.Status, x.NextReviewAt });
        });

        modelBuilder.Entity<FlashcardEntity>(b =>
        {
            b.ToTable("Flashcards");
            b.HasKey(x => x.Id);
            b.Property(x => x.Front).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Back).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Source).HasMaxLength(200).IsRequired();
            b.Property(x => x.EaseFactor).HasConversion<double>().HasDefaultValue(2.5);
            b.HasIndex(x => new { x.IsSuspended, x.DueAt });
        });

        modelBuilder.Entity<FlashcardReviewEntity>(b =>
        {
            b.ToTable("FlashcardReviews");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.FlashcardId, x.ReviewedAt });
        });

        modelBuilder.Entity<DistractionEntity>(b =>
        {
            b.ToTable("Distractions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Text).HasMaxLength(2000).IsRequired();
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<EnergyCheckInEntity>(b =>
        {
            b.ToTable("EnergyCheckIns");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.RecordedAt);
        });

        modelBuilder.Entity<StudyActivityEntity>(b =>
        {
            b.ToTable("StudyActivities");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(400).IsRequired();
            b.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            b.Property(x => x.Result).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Note).HasMaxLength(12000).IsRequired();
            b.Property(x => x.TagsJson).IsRequired();
            b.HasIndex(x => x.StartedAt);
            b.HasIndex(x => new { x.CourseId, x.StartedAt });
            b.HasIndex(x => x.SessionId);
            b.HasIndex(x => x.DeletedAt);
        });

        modelBuilder.Entity<ActivityRevisionEntity>(b =>
        {
            b.ToTable("ActivityRevisions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(400).IsRequired();
            b.Property(x => x.BeforeJson).IsRequired();
            b.Property(x => x.AfterJson).IsRequired();
            b.HasIndex(x => new { x.ActivityId, x.RevisedAt });
        });

        modelBuilder.Entity<StudyMissionEntity>(b =>
        {
            b.ToTable("StudyMissions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(400).IsRequired();
            b.Property(x => x.Description).HasMaxLength(12000).IsRequired();
            b.Property(x => x.Reward).HasMaxLength(400).IsRequired();
            b.Property(x => x.Source).HasMaxLength(40).IsRequired();
            b.HasIndex(x => new { x.Period, x.StartsAt });
            b.HasIndex(x => new { x.Status, x.DueAt });
            b.HasMany(x => x.Steps)
                .WithOne()
                .HasForeignKey(x => x.MissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MissionStepEntity>(b =>
        {
            b.ToTable("MissionSteps");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(400).IsRequired();
            b.HasIndex(x => new { x.MissionId, x.Position });
        });

        modelBuilder.Entity<PdfDocumentEntity>(b =>
        {
            b.ToTable("PdfDocuments");
            b.HasKey(x => x.Id);
            b.Property(x => x.FileName).HasMaxLength(512).IsRequired();
            b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            b.Property(x => x.CourseId).HasMaxLength(64).IsRequired();
            b.Property(x => x.PagesJson).IsRequired();
            b.HasIndex(x => x.Sha256).IsUnique();
        });

        modelBuilder.Entity<LegacyImportEntity>(b =>
        {
            b.ToTable("LegacyImports");
            b.HasKey(x => x.Key);
            b.Property(x => x.SourceHash).IsRequired();
        });
    }
}
