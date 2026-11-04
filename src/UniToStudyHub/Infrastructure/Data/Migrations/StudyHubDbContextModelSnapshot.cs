using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using UniToStudyHub.Infrastructure.Data.Entities;

namespace UniToStudyHub.Infrastructure.Data.Migrations;

[DbContext(typeof(StudyHubDbContext))]
public sealed class StudyHubDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 64);

        modelBuilder.Entity<ProfileSettingsEntity>(b =>
        {
            b.ToTable("ProfileSettings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("INTEGER");
            b.Property(x => x.GraduationTarget).HasColumnType("TEXT");
            b.Property(x => x.MoodleBaseUrl).HasMaxLength(2048).IsRequired();
        });
        modelBuilder.Entity<CourseEntity>(b =>
        {
            b.ToTable("Courses");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("TEXT");
            b.Property(x => x.Name).IsRequired();
            b.Property(x => x.Code).IsRequired().HasDefaultValue("");
            b.HasIndex(x => x.MoodleCourseId);
        });
        modelBuilder.Entity<GradeEntity>(b =>
        {
            b.ToTable("Grades");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("TEXT");
            b.HasIndex(x => x.CourseId);
        });
        modelBuilder.Entity<PlannedExamEntity>(b =>
        {
            b.ToTable("PlannedExams");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("TEXT");
            b.HasIndex(x => x.CourseId);
        });
        modelBuilder.Entity<AnnouncementEntity>(b =>
        {
            b.ToTable("Announcements");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("TEXT");
            b.HasIndex(x => x.SourceKey);
        });
        modelBuilder.Entity<CareerExamEntity>(b =>
        {
            b.ToTable("CareerExams");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("TEXT");
            b.HasMany(x => x.Attempts).WithOne().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ExamAttemptEntity>(b =>
        {
            b.ToTable("ExamAttempts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("TEXT");
            b.HasIndex(x => new { x.ExamId, x.AttemptedAt });
        });
        modelBuilder.Entity<LegacyImportEntity>(b =>
        {
            b.ToTable("LegacyImports");
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasColumnType("TEXT");
            b.Property(x => x.SourceHash).IsRequired();
        });
    }
}