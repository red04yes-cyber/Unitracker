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

        modelBuilder.Entity<LegacyImportEntity>(b =>
        {
            b.ToTable("LegacyImports");
            b.HasKey(x => x.Key);
            b.Property(x => x.SourceHash).IsRequired();
        });
    }
}
