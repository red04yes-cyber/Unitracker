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
    public DbSet<LegacyImportEntity> LegacyImports => Set<LegacyImportEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProfileSettingsEntity>().ToTable("ProfileSettings").HasKey(x => x.Id);
        modelBuilder.Entity<ProfileSettingsEntity>().Property(x => x.MoodleBaseUrl).HasMaxLength(2048).IsRequired();

        modelBuilder.Entity<CourseEntity>().ToTable("Courses").HasKey(x => x.Id);
        modelBuilder.Entity<CourseEntity>().Property(x => x.Name).IsRequired();
        modelBuilder.Entity<CourseEntity>().Property(x => x.Code).HasDefaultValue(string.Empty).IsRequired();
        modelBuilder.Entity<CourseEntity>().HasIndex(x => x.MoodleCourseId);

        modelBuilder.Entity<GradeEntity>().ToTable("Grades").HasKey(x => x.Id);
        modelBuilder.Entity<GradeEntity>().HasIndex(x => x.CourseId);

        modelBuilder.Entity<PlannedExamEntity>().ToTable("PlannedExams").HasKey(x => x.Id);
        modelBuilder.Entity<PlannedExamEntity>().HasIndex(x => x.CourseId);

        modelBuilder.Entity<AnnouncementEntity>().ToTable("Announcements").HasKey(x => x.Id);
        modelBuilder.Entity<AnnouncementEntity>().HasIndex(x => x.SourceKey);

        modelBuilder.Entity<CareerExamEntity>().ToTable("CareerExams").HasKey(x => x.Id);
        modelBuilder.Entity<CareerExamEntity>().HasMany(x => x.Attempts)
            .WithOne().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExamAttemptEntity>().ToTable("ExamAttempts").HasKey(x => x.Id);
        modelBuilder.Entity<ExamAttemptEntity>().HasIndex(x => new { x.ExamId, x.AttemptedAt });

        modelBuilder.Entity<LegacyImportEntity>().ToTable("LegacyImports").HasKey(x => x.Key);
    }
}
