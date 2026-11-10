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
        modelBuilder.Entity<ProfileSettingsEntity>(b =>
        {
            b.ToTable("ProfileSettings");
            b.HasKey(x => x.Id);
            b.Property(x => x.MoodleBaseUrl).HasMaxLength(2048).IsRequired();
            b.Property(x => x.ProductivityStateJson).IsRequired();
        });

        modelBuilder.Entity<CourseEntity>(b =>
        {
            b.ToTable("Courses");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired();
            b.Property(x => x.Code).HasDefaultValue(string.Empty).IsRequired();
            b.HasIndex(x => x.MoodleCourseId);
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

        modelBuilder.Entity<LegacyImportEntity>(b =>
        {
            b.ToTable("LegacyImports");
            b.HasKey(x => x.Key);
            b.Property(x => x.SourceHash).IsRequired();
        });
    }
}
